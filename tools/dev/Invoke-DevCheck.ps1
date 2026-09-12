#requires -Version 7.0
[CmdletBinding()]
param([ValidateSet('Quick','Full')][string]$Profile = 'Quick', [switch]$PlanOnly)
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'DevWorkflow.psm1') -Force
$steps = @('preflight','tool-tests') + @(Get-DevCheckPlan $Profile)
if ($PlanOnly) { [pscustomobject]@{ Profile = $Profile; Steps = $steps; ReleaseReady = $false } | ConvertTo-Json; return }
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$directory = Join-Path $root ('artifacts/dev/' + [DateTime]::UtcNow.ToString('yyyyMMdd_HHmmss') + '-' + [guid]::NewGuid().ToString('N'))
$project = 'src/G.PcHealthCheck/G.PcHealthCheck.csproj'
$publish = Join-Path $directory 'publish'
$exe = Join-Path $publish 'G-PC-Health-Check.exe'
$metadata = @{ Profile = $Profile; Sha = $null; Branch = $null; Dirty = $null; Sdk = $null; PowerShell = $PSVersionTable.PSVersion.ToString(); Platform = if ($IsWindows) { 'Windows' } else { 'Unsupported' } }
$tools = @{}
Push-Location $root
try {
    $result = Invoke-DevSequence -Steps $steps -Directory $directory -Metadata $metadata -Execute {
        param($id, $log)
        switch ($id) {
            'preflight' {
                if (-not $IsWindows) { throw 'Application checks require Windows. PlanOnly and isolated tool unit tests do not run the application.' }
                foreach ($name in @('git','dotnet','pwsh')) { $tools[$name] = (Get-Command $name -CommandType Application -ErrorAction Stop | Select-Object -First 1).Source }
                $metadata.Sha = (Invoke-DevNative $tools.git @('rev-parse','HEAD') ($log + '-sha')).Stdout.Trim()
                $metadata.Branch = (Invoke-DevNative $tools.git @('branch','--show-current') ($log + '-branch')).Stdout.Trim()
                $metadata.Dirty = -not [string]::IsNullOrWhiteSpace((Invoke-DevNative $tools.git @('status','--porcelain=v1','--untracked-files=normal') ($log + '-status')).Stdout)
                if ($metadata.Sha -notmatch '^[0-9a-f]{40}$') { throw 'Cannot bind results to a valid Git commit.' }
                $sdk = Get-Content (Join-Path $root 'global.json') -Raw | ConvertFrom-Json
                $metadata.Sdk = (Invoke-DevNative $tools.dotnet @('--version') ($log + '-sdk')).Stdout.Trim()
                if ($metadata.Sdk -ne $sdk.sdk.version) { throw "SDK mismatch. Install the version in global.json: $($sdk.sdk.version). No automatic installer is run." }
                foreach ($file in Get-ChildItem (Join-Path $root 'tools') -Recurse -File | Where-Object Extension -in '.ps1','.psm1') {
                    $tokens = $null; $errors = $null
                    [void][Management.Automation.Language.Parser]::ParseFile($file.FullName, [ref]$tokens, [ref]$errors)
                    if (@($errors).Count -gt 0) { throw "PowerShell parse failure in $($file.Name)." }
                }
            }
            'tool-tests' { Invoke-DevNative $tools.pwsh @('-NoLogo','-NoProfile','-NonInteractive','-File',(Join-Path $PSScriptRoot 'Test-DevWorkflow.ps1')) $log | Out-Null }
            'restore' { Invoke-DevNative $tools.dotnet @('restore',$project) $log | Out-Null }
            'audit' {
                $audit = Invoke-DevNative $tools.dotnet @('list',$project,'package','--vulnerable','--include-transitive','--format','json','--output-version','1') $log
                Assert-DevAudit $audit.Stdout
            }
            'build' { Invoke-DevNative $tools.dotnet @('build',$project,'-c','Release','--no-restore','-warnaserror') $log | Out-Null }
            'selftest' { Invoke-DevNative $tools.dotnet @('run','--project',$project,'-c','Release','--no-build','--','--selftest') $log | Out-Null }
            'publish' { Invoke-DevNative $tools.dotnet @('publish',$project,'-c','Release','-r','win-x64','--self-contained','true','--no-restore','-o',$publish) $log | Out-Null }
            'exe-selftest' { Invoke-DevNative $exe @('--selftest') $log | Out-Null }
            'portable' { Invoke-DevNative $tools.pwsh @('-NoLogo','-NoProfile','-NonInteractive','-File',(Join-Path $root 'tools/ci/Test-PortableWorker.ps1'),'-ExePath',$exe) $log | Out-Null }
            'package' {
                $files = @(Get-ChildItem $publish -File)
                if ($files.Count -ne 1 -or $files[0].Name -ne 'G-PC-Health-Check.exe') { throw 'Expected only the single-file EXE in the fresh publish directory.' }
                [xml]$xml = Get-Content $project -Raw
                $expected = [version]([string]$xml.Project.PropertyGroup.Version + '.0')
                $actual = [version][Diagnostics.FileVersionInfo]::GetVersionInfo($exe).FileVersion
                if ($actual -ne $expected) { throw "EXE version $actual does not match project $expected." }
                $hash = (Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash.ToLowerInvariant()
                $metadata.ExeSha256 = $hash; $metadata.FileVersion = $actual.ToString()
                [IO.File]::WriteAllText((Join-Path $directory 'G-PC-Health-Check.exe.sha256'), "$hash  G-PC-Health-Check.exe`n", [Text.Encoding]::ASCII)
            }
            default { throw "Unknown step $id" }
        }
    }
    if ($result.State -ne 'Passed') { throw 'Development check failed. See the recorded failing stage; later stages were not run.' }
    Write-Host "$Profile local checks passed. This is NOT a published release, pilot acceptance or artifact attestation."
}
finally { Pop-Location }
