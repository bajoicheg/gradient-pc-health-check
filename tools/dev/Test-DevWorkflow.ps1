#requires -Version 7.0
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'DevWorkflow.psm1') -Force
$failures = [Collections.Generic.List[string]]::new()
$script:count = 0
function Test([string]$Name, [scriptblock]$Action) {
    $script:count++
    try { & $Action; Write-Host "PASS $Name" }
    catch { $failures.Add("${Name}: $($_.Exception.Message)"); Write-Host "FAIL $Name" }
}
function Require([bool]$Condition, [string]$Message) { if (-not $Condition) { throw $Message } }
function Expect-Error([scriptblock]$Action) { $caught = $false; try { & $Action | Out-Null } catch { $caught = $true }; Require $caught 'Expected an error.' }
$root = Join-Path ([IO.Path]::GetTempPath()) ('GpcDevTools-Tests-' + [guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($root)
try {
    Test 'Quick checks source without packaging' {
        Require ((@(Get-DevCheckPlan Quick) -join ',') -eq 'restore,audit,build,selftest') 'Quick plan changed or skips source tests.'
    }
    Test 'Full retains Quick and adds packaged/portable checks' {
        Require ((@(Get-DevCheckPlan Full) -join ',') -eq 'restore,audit,build,selftest,publish,exe-selftest,portable,package') 'Full plan is incomplete.'
    }
    Test 'Unknown profile cannot silently skip checks' { Expect-Error { Get-DevCheckPlan Surprise } }
    $clean = '{"version":1,"projects":[{"frameworks":[{"framework":"net8.0-windows"}]}]}'
    Test 'Complete clean audit accepted' { Assert-DevAudit $clean }
    Test 'Empty audit rejected' { Expect-Error { Assert-DevAudit '{}' } }
    Test 'Malformed audit rejected' { Expect-Error { Assert-DevAudit 'not json' } }
    Test 'No projects rejected' { Expect-Error { Assert-DevAudit '{"version":1,"projects":[]}' } }
    Test 'No frameworks rejected' { Expect-Error { Assert-DevAudit '{"version":1,"projects":[{}]}' } }
    Test 'Audit error log rejected' { Expect-Error { Assert-DevAudit '{"version":1,"logs":[{"level":"error","message":"synthetic"}],"projects":[{"frameworks":[{}]}]}' } }
    foreach ($kind in @('topLevelPackages', 'transitivePackages')) {
        Test "Reject vulnerabilities in $kind" {
            $audit = @{ version = 1; projects = @(@{ frameworks = @(@{ framework = 'synthetic'; $kind = @(@{ id = 'Synthetic'; vulnerabilities = @(@{ severity = 'High' }) }) }) }) } | ConvertTo-Json -Depth 10
            Expect-Error { Assert-DevAudit $audit }
        }
    }
    Test 'Successful sequence records exact metadata and all steps' {
        $dir = Join-Path $root 'success'
        $r = Invoke-DevSequence -Steps @('one','two') -Execute { param($id, $log) } -Directory $dir -Metadata @{ Sha = ('a' * 40); Dirty = $true; Profile = 'Quick' }
        $saved = Get-Content (Join-Path $dir 'summary.json') -Raw | ConvertFrom-Json
        Require ($r.State -eq 'Passed' -and $saved.State -eq 'Passed' -and $saved.Steps.Count -eq 2) 'Success not recorded.'
        Require ($saved.Metadata.Sha -eq ('a' * 40) -and $saved.Metadata.Dirty -and $saved.Metadata.Profile -eq 'Quick') 'Source provenance lost.'
        Require (@($saved.Steps | Where-Object State -ne 'Passed').Count -eq 0) 'Incorrect step states.'
        Require (-not $saved.ReleaseReady) 'Local result claims a release.'
    }
    Test 'Failure stops later steps and leaves evidence' {
        $calls = [Collections.Generic.List[string]]::new(); $dir = Join-Path $root 'failure'
        $r = Invoke-DevSequence -Steps @('one','two','three') -Execute { param($id, $log) $calls.Add($id); if ($id -eq 'two') { throw 'Synthetic failure' } } -Directory $dir -Metadata @{}
        Require ($r.State -eq 'Failed' -and ($calls -join ',') -eq 'one,two') 'Failure did not stop the sequence.'
        $saved = Get-Content (Join-Path $dir 'summary.json') -Raw | ConvertFrom-Json
        Require ($saved.Steps[0].State -eq 'Passed' -and $saved.Steps[1].State -eq 'Failed' -and $saved.Steps[2].State -eq 'NotRun') 'Failure presented as success or missing data.'
    }
    Test 'In-progress snapshot exists before executing a step' {
        $dir = Join-Path $root 'running'
        $r = Invoke-DevSequence -Steps @('one') -Execute { param($id, $log)
            $s = Get-Content (Join-Path $dir 'summary.json') -Raw | ConvertFrom-Json
            Require ($s.State -eq 'Running' -and $s.Steps[0].State -eq 'Running') 'No resumable in-progress evidence.'
        } -Directory $dir -Metadata @{}
        Require ($r.State -eq 'Passed') 'Progress check failed.'
    }
    Test 'Empty sequence cannot report success' { Expect-Error { Invoke-DevSequence -Steps @() -Execute {} -Directory (Join-Path $root 'empty') -Metadata @{} } }
    Test 'Existing result directory cannot be overwritten' {
        $dir = Join-Path $root 'existing'; [void][IO.Directory]::CreateDirectory($dir)
        Set-Content (Join-Path $dir 'sentinel') 'preserve'
        Expect-Error { Invoke-DevSequence -Steps @('one') -Execute {} -Directory $dir -Metadata @{} }
        Require ((Get-Content (Join-Path $dir 'sentinel') -Raw).Trim() -eq 'preserve') 'Existing result changed.'
    }
    $pwsh = (Get-Process -Id $PID).Path
    Test 'Native nonzero exit throws with exact code and saves log' {
        $prefix = Join-Path $root 'failed-native'; $code = $null
        try { Invoke-DevNative -FilePath $pwsh -Arguments @('-NoProfile','-Command','[Console]::Error.WriteLine("synthetic stderr"); exit 17') -LogPrefix $prefix | Out-Null }
        catch { $code = $_.Exception.Data['ExitCode'] }
        Require ($code -eq 17) 'Native error exit lost.'
        Require ((Get-Content ($prefix + '.stderr.log') -Raw).Contains('synthetic stderr')) 'Failure log missing.'
    }
    Test 'Native stderr with exit zero is not a false failure' {
        $r = Invoke-DevNative -FilePath $pwsh -Arguments @('-NoProfile','-Command','[Console]::Error.WriteLine("synthetic warning"); exit 0') -LogPrefix (Join-Path $root 'warning')
        Require ($r.ExitCode -eq 0) 'stderr alone became failure.'
    }
    Test 'Native arguments preserve Unicode spaces and metacharacters' {
        $script = Join-Path $root 'echo arguments.ps1'; Set-Content -LiteralPath $script -Value 'param([string]$Value) [Console]::Write($Value)' -Encoding utf8
        $value = 'Тест (1) & [x]'
        $r = Invoke-DevNative -FilePath $pwsh -Arguments @('-NoProfile','-File',$script,$value) -LogPrefix (Join-Path $root 'literal')
        Require ($r.Stdout -eq $value) 'Argument was reinterpreted by a shell.'
    }
    Test 'Launch failure is not a successful step' {
        $r = Invoke-DevSequence -Steps @('native') -Execute { param($id, $log) Invoke-DevNative -FilePath (Join-Path $root 'absent.exe') -Arguments @() -LogPrefix $log | Out-Null } -Directory (Join-Path $root 'missing-tool') -Metadata @{}
        Require ($r.State -eq 'Failed') 'Missing executable reported as passing.'
    }
}
finally { if (Test-Path -LiteralPath $root) { Remove-Item -LiteralPath $root -Recurse -Force } }
Write-Host ("Development workflow: {0}/{1} passed." -f ($script:count - $failures.Count), $script:count)
foreach ($failure in $failures) { Write-Host "FAIL: $failure" }
if ($failures.Count -gt 0) { throw "$($failures.Count) development workflow checks failed." }
