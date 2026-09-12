#requires -Version 7.0
function Get-DevCheckPlan {
    param([ValidateSet('Quick','Full')][string]$Profile = 'Quick')
    @('restore','audit','build','selftest')
    if ($Profile -eq 'Full') { @('publish','exe-selftest','portable','package') }
}

function Assert-DevAudit {
    param([Parameter(Mandatory)][string]$Json)
    $audit = $Json | ConvertFrom-Json -ErrorAction Stop
    if ($audit.version -ne 1 -or @($audit.projects).Count -eq 0 -or $null -eq $audit.projects) { throw 'Incomplete NuGet audit: version/projects missing.' }
    if (@($audit.logs | Where-Object { $_.level -in 'error','warning' }).Count -gt 0) { throw 'NuGet audit reported an error/warning; no clean result is established.' }
    foreach ($project in $audit.projects) {
        if ($null -eq $project.frameworks -or @($project.frameworks).Count -eq 0) {
            # SDK 8 JSON format 1 deliberately emits only path for a project with
            # no vulnerable packages. Observed in the real Windows integration run.
            $filtered = $audit.parameters -match '(^|\s)--vulnerable(\s|$)' -and $audit.parameters -match '(^|\s)--include-transitive(\s|$)'
            if ($filtered -and -not [string]::IsNullOrWhiteSpace($project.path) -and $project.path.EndsWith('.csproj', [StringComparison]::OrdinalIgnoreCase)) { continue }
            throw 'Incomplete NuGet audit: no framework data or validated no-findings project.'
        }
        foreach ($framework in $project.frameworks) {
            if ([string]::IsNullOrWhiteSpace($framework.framework)) { throw 'Incomplete NuGet audit: framework identity missing.' }
            foreach ($package in @($framework.topLevelPackages) + @($framework.transitivePackages)) {
                if ($null -ne $package -and @($package.vulnerabilities).Count -gt 0 -and $null -ne $package.vulnerabilities) {
                    throw "NuGet audit found vulnerabilities in $($package.id). See the audit log."
                }
            }
        }
    }
}

function Invoke-DevNative {
    param([Parameter(Mandatory)][string]$FilePath, [AllowEmptyCollection()][string[]]$Arguments = @(), [Parameter(Mandatory)][string]$LogPrefix)
    $info = [Diagnostics.ProcessStartInfo]::new()
    $info.FileName = $FilePath
    $info.WorkingDirectory = (Get-Location).ProviderPath
    $info.UseShellExecute = $false
    $info.RedirectStandardOutput = $true; $info.RedirectStandardError = $true
    $info.StandardOutputEncoding = [Text.UTF8Encoding]::new($false)
    $info.StandardErrorEncoding = [Text.UTF8Encoding]::new($false)
    foreach ($argument in $Arguments) { $info.ArgumentList.Add($argument) }
    $process = [Diagnostics.Process]::new(); $process.StartInfo = $info
    try {
        [void]$process.Start()
        $stdoutTask = $process.StandardOutput.ReadToEndAsync()
        $stderrTask = $process.StandardError.ReadToEndAsync()
        $process.WaitForExit()
        $stdout = $stdoutTask.GetAwaiter().GetResult(); $stderr = $stderrTask.GetAwaiter().GetResult()
        [IO.File]::WriteAllText($LogPrefix + '.stdout.log', $stdout, [Text.UTF8Encoding]::new($false))
        [IO.File]::WriteAllText($LogPrefix + '.stderr.log', $stderr, [Text.UTF8Encoding]::new($false))
        if ($stdout.Length -gt 0) { Write-Host $stdout.TrimEnd() }
        if ($stderr.Length -gt 0) { Write-Host $stderr.TrimEnd() }
        if ($process.ExitCode -ne 0) {
            $failure = [InvalidOperationException]::new("Native command failed with exit code $($process.ExitCode); see stage logs.")
            $failure.Data['ExitCode'] = $process.ExitCode
            throw $failure
        }
        return [pscustomobject]@{ ExitCode = $process.ExitCode; Stdout = $stdout; Stderr = $stderr }
    }
    finally { $process.Dispose() }
}

function Invoke-DevSequence {
    param([Parameter(Mandatory)][ValidateNotNullOrEmpty()][string[]]$Steps, [Parameter(Mandatory)][scriptblock]$Execute,
        [Parameter(Mandatory)][string]$Directory, [hashtable]$Metadata = @{})
    if (@($Steps | Select-Object -Unique).Count -ne $Steps.Count -or @($Steps | Where-Object { $_ -notmatch '^[a-z][a-z0-9-]*$' }).Count -gt 0) { throw 'Invalid or duplicate stage identifiers.' }
    if (Test-Path -LiteralPath $Directory) { throw 'Result directory already exists. Use a new run directory.' }
    [void][IO.Directory]::CreateDirectory($Directory)
    $result = [pscustomobject][ordered]@{
        SchemaVersion = 1; State = 'Running'; ReleaseReady = $false; Metadata = $Metadata
        StartedAt = [DateTimeOffset]::UtcNow.ToString('O'); FinishedAt = $null
        Steps = @($Steps | ForEach-Object { [pscustomobject][ordered]@{ Id = $_; State = 'NotRun'; StartedAt = $null; FinishedAt = $null; Seconds = $null; ExitCode = $null; ErrorType = $null } })
    }
    $save = {
        $temp = Join-Path $Directory 'summary.json.tmp'; $path = Join-Path $Directory 'summary.json'
        [IO.File]::WriteAllText($temp, ($result | ConvertTo-Json -Depth 10), [Text.UTF8Encoding]::new($false))
        [IO.File]::Move($temp, $path, $true)
    }
    & $save
    foreach ($step in $result.Steps) {
        $step.State = 'Running'; $step.StartedAt = [DateTimeOffset]::UtcNow.ToString('O'); & $save
        $clock = [Diagnostics.Stopwatch]::StartNew()
        Write-Host "START $($step.Id)"
        try {
            & $Execute $step.Id (Join-Path $Directory $step.Id) | Out-Null
            $step.State = 'Passed'; $step.ExitCode = 0
        }
        catch {
            $step.State = 'Failed'; $step.ErrorType = $_.Exception.GetType().FullName
            $step.ExitCode = $_.Exception.Data['ExitCode']; $result.State = 'Failed'
            Write-Host "FAILED $($step.Id): $($_.Exception.Message)"
        }
        finally {
            $clock.Stop(); $step.Seconds = [Math]::Round($clock.Elapsed.TotalSeconds, 3)
            $step.FinishedAt = [DateTimeOffset]::UtcNow.ToString('O'); & $save
            Write-Host "$($step.State) $($step.Id) ($($step.Seconds) s)"
        }
        if ($result.State -eq 'Failed') { break }
    }
    if ($result.State -ne 'Failed') { $result.State = 'Passed' }
    $result.FinishedAt = [DateTimeOffset]::UtcNow.ToString('O'); & $save
    Write-Host "Results: $(Join-Path $Directory 'summary.json')"
    return $result
}
Export-ModuleMember -Function Get-DevCheckPlan, Assert-DevAudit, Invoke-DevNative, Invoke-DevSequence
