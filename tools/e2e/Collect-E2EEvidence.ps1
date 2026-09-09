[CmdletBinding()]
param(
    [string]$SourceExe,
    [int]$LatestReportFiles = 8,
    [string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if ($LatestReportFiles -lt 1 -or $LatestReportFiles -gt 30) {
    throw 'LatestReportFiles must be between 1 and 30.'
}

function Get-IsAdministrator {
    try {
        $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
        $principal = [Security.Principal.WindowsPrincipal]::new($identity)
        return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
    }
    catch { return $false }
}

function Get-FileEvidence {
    param([string]$Path)
    if ([string]::IsNullOrWhiteSpace($Path) -or -not (Test-Path -LiteralPath $Path -PathType Leaf)) { return $null }
    $item = Get-Item -LiteralPath $Path
    $version = [Diagnostics.FileVersionInfo]::GetVersionInfo($item.FullName)
    return [ordered]@{
        path = $item.FullName
        size = $item.Length
        sha256 = (Get-FileHash -LiteralPath $item.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        fileVersion = $version.FileVersion
        productVersion = $version.ProductVersion
        lastWriteTime = $item.LastWriteTime.ToString('o')
    }
}

$timestamp = Get-Date -Format 'yyyyMMdd_HHmmss'
$desktop = [Environment]::GetFolderPath([Environment+SpecialFolder]::DesktopDirectory)
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = if ([string]::IsNullOrWhiteSpace($desktop)) { $env:TEMP } else { $desktop }
}
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null

$work = Join-Path $env:TEMP "GradientPcHealthCheck-Evidence-$timestamp-$([guid]::NewGuid().ToString('N').Substring(0,8))"
New-Item -ItemType Directory -Path $work -Force | Out-Null

try {
    $installedExe = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::ProgramFiles)) 'Gradient\PCHealthCheck\Gradient-PC-Health-Check.exe'
    $reportsDir = Join-Path $env:LOCALAPPDATA 'Gradient\PCHealthCheck\Reports'
    $scenarioRoot = Join-Path $env:TEMP 'GradientPcHealthCheck-E2E'

    $os = $null
    try {
        $os = Get-CimInstance Win32_OperatingSystem | Select-Object Caption, Version, BuildNumber, OSArchitecture, LastBootUpTime
    }
    catch {
        $os = [pscustomobject]@{ error = $_.Exception.Message }
    }

    $computer = $null
    try {
        $computer = Get-CimInstance Win32_ComputerSystem | Select-Object Manufacturer, Model, Domain, UserName, TotalPhysicalMemory, NumberOfLogicalProcessors
    }
    catch {
        $computer = [pscustomobject]@{ error = $_.Exception.Message }
    }

    $sourceEvidence = Get-FileEvidence -Path $SourceExe
    $installedEvidence = Get-FileEvidence -Path $installedExe
    $hashMatch = $null
    if ($null -ne $sourceEvidence -and $null -ne $installedEvidence) {
        $hashMatch = [string]::Equals([string]$sourceEvidence.sha256, [string]$installedEvidence.sha256, [StringComparison]::OrdinalIgnoreCase)
    }

    $summary = [ordered]@{
        schemaVersion = 1
        collectedAt = (Get-Date).ToString('o')
        machine = $env:COMPUTERNAME
        currentUser = [Security.Principal.WindowsIdentity]::GetCurrent().Name
        processIsAdministrator = Get-IsAdministrator
        powershell = $PSVersionTable.PSVersion.ToString()
        operatingSystem = $os
        computerSystem = $computer
        sourceExe = $sourceEvidence
        installedExe = $installedEvidence
        sourceAndInstalledSha256Match = $hashMatch
        reportsDirectory = $reportsDir
    }
    $summary | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $work 'environment.json') -Encoding UTF8

    foreach ($name in @('e2e-manifest.json','e2e-verification.json')) {
        $path = Join-Path $scenarioRoot $name
        if (Test-Path -LiteralPath $path) { Copy-Item -LiteralPath $path -Destination (Join-Path $work $name) }
    }

    if (Test-Path -LiteralPath $reportsDir) {
        $reportsTarget = Join-Path $work 'reports'
        New-Item -ItemType Directory -Path $reportsTarget -Force | Out-Null
        Get-ChildItem -LiteralPath $reportsDir -File |
            Where-Object { $_.Extension -in '.json','.html','.zip' } |
            Sort-Object LastWriteTime -Descending |
            Select-Object -First $LatestReportFiles |
            ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $reportsTarget $_.Name) }
    }

    $readme = @"
Gradient PC Health Check — E2E evidence bundle
Generated: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss K')
Machine:   $env:COMPUTERNAME
User:      $([Security.Principal.WindowsIdentity]::GetCurrent().Name)

Contents:
- environment.json: OS / machine / EXE version and SHA-256 evidence
- e2e-manifest.json: deterministic CleanTemp test input, if prepared
- e2e-verification.json: automated CleanTemp verification, if executed
- reports/: latest PC Health Check HTML/JSON/ZIP reports

This bundle may contain workstation/user names and diagnostic information. Treat it as internal support evidence.
"@
    Set-Content -LiteralPath (Join-Path $work 'README.txt') -Value $readme -Encoding UTF8

    $safeMachine = ($env:COMPUTERNAME -replace '[^A-Za-z0-9_.-]','_')
    $zip = Join-Path $OutputDirectory "Gradient-PC-Health-Check-E2E-$safeMachine-$timestamp.zip"
    if (Test-Path -LiteralPath $zip) { Remove-Item -LiteralPath $zip -Force }
    Compress-Archive -Path (Join-Path $work '*') -DestinationPath $zip -CompressionLevel Optimal

    Write-Host ''
    Write-Host 'E2E evidence bundle created.' -ForegroundColor Green
    Write-Host "ZIP: $zip"
    if ($null -ne $hashMatch) {
        Write-Host "Source / Program Files SHA-256 match: $hashMatch"
    }
    Write-Host ''
    Write-Output $zip
}
finally {
    if (Test-Path -LiteralPath $work) { Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue }
}
