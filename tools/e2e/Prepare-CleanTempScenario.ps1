[CmdletBinding()]
param(
    [switch]$IncludeJunctionTest
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$root = Join-Path $env:TEMP 'GradientPcHealthCheck-E2E'
$expectedRoot = [IO.Path]::GetFullPath((Join-Path $env:TEMP 'GradientPcHealthCheck-E2E'))
$actualRoot = [IO.Path]::GetFullPath($root)
if (-not [string]::Equals($expectedRoot.TrimEnd('\'), $actualRoot.TrimEnd('\'), [StringComparison]::OrdinalIgnoreCase)) {
    throw "Safety check failed for E2E root: $actualRoot"
}

if (Test-Path -LiteralPath $root) {
    Remove-Item -LiteralPath $root -Recurse -Force
}
New-Item -ItemType Directory -Path $root -Force | Out-Null

$oldFile = Join-Path $root 'old-delete-me.txt'
$freshFile = Join-Path $root 'fresh-must-stay.txt'
$oldContent = "Gradient PC Health Check E2E OLD sentinel $([guid]::NewGuid())"
$freshContent = "Gradient PC Health Check E2E FRESH sentinel $([guid]::NewGuid())"

Set-Content -LiteralPath $oldFile -Value $oldContent -Encoding UTF8
Set-Content -LiteralPath $freshFile -Value $freshContent -Encoding UTF8
(Get-Item -LiteralPath $oldFile).LastWriteTime = (Get-Date).AddDays(-5)
(Get-Item -LiteralPath $freshFile).LastWriteTime = Get-Date

$junctionEnabled = $false
$junctionPath = $null
$outsideRoot = $null
$outsideFile = $null
$outsideHash = $null
$junctionError = $null

if ($IncludeJunctionTest) {
    try {
        $outsideRoot = Join-Path $env:LOCALAPPDATA 'Gradient\PCHealthCheck\E2E-Outside'
        New-Item -ItemType Directory -Path $outsideRoot -Force | Out-Null
        $outsideFile = Join-Path $outsideRoot 'outside-must-never-be-deleted.txt'
        Set-Content -LiteralPath $outsideFile -Value "OUTSIDE junction sentinel $([guid]::NewGuid())" -Encoding UTF8
        (Get-Item -LiteralPath $outsideFile).LastWriteTime = (Get-Date).AddDays(-5)
        $outsideHash = (Get-FileHash -LiteralPath $outsideFile -Algorithm SHA256).Hash.ToLowerInvariant()

        $junctionPath = Join-Path $root 'junction-must-be-skipped'
        if (Test-Path -LiteralPath $junctionPath) { Remove-Item -LiteralPath $junctionPath -Force }
        $output = & cmd.exe /d /c "mklink /J `"$junctionPath`" `"$outsideRoot`"" 2>&1
        if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $junctionPath)) {
            throw "mklink failed: $($output -join ' ')"
        }
        $attrs = (Get-Item -LiteralPath $junctionPath -Force).Attributes
        if (($attrs -band [IO.FileAttributes]::ReparsePoint) -eq 0) {
            throw 'Created directory is not reported as a reparse point.'
        }
        $junctionEnabled = $true
    }
    catch {
        $junctionError = $_.Exception.Message
        Write-Warning "Optional junction test was not prepared: $junctionError"
    }
}

$manifest = [ordered]@{
    schemaVersion = 1
    preparedAt = (Get-Date).ToString('o')
    machine = $env:COMPUTERNAME
    user = [Environment]::UserName
    tempRoot = $root
    cleanTempAgeDays = 3
    oldFile = [ordered]@{
        path = $oldFile
        lastWriteTime = (Get-Item -LiteralPath $oldFile).LastWriteTime.ToString('o')
        sha256 = (Get-FileHash -LiteralPath $oldFile -Algorithm SHA256).Hash.ToLowerInvariant()
        expectedAfterRemediation = 'ABSENT'
    }
    freshFile = [ordered]@{
        path = $freshFile
        lastWriteTime = (Get-Item -LiteralPath $freshFile).LastWriteTime.ToString('o')
        sha256 = (Get-FileHash -LiteralPath $freshFile -Algorithm SHA256).Hash.ToLowerInvariant()
        expectedAfterRemediation = 'PRESENT_UNCHANGED'
    }
    junctionTest = [ordered]@{
        requested = [bool]$IncludeJunctionTest
        enabled = $junctionEnabled
        junctionPath = $junctionPath
        outsideRoot = $outsideRoot
        outsideFile = $outsideFile
        outsideSha256 = $outsideHash
        expectedAfterRemediation = if ($junctionEnabled) { 'OUTSIDE_PRESENT_UNCHANGED' } else { 'NOT_APPLICABLE' }
        preparationError = $junctionError
    }
}

$manifestPath = Join-Path $root 'e2e-manifest.json'
$manifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $manifestPath -Encoding UTF8

Write-Host ''
Write-Host 'Gradient PC Health Check — CleanTemp E2E prepared' -ForegroundColor Green
Write-Host "Root:      $root"
Write-Host "OLD:       $oldFile  (must be deleted)"
Write-Host "FRESH:     $freshFile  (must remain unchanged)"
if ($junctionEnabled) {
    Write-Host "JUNCTION:  $junctionPath  (must be skipped)"
    Write-Host "OUTSIDE:   $outsideFile  (must remain unchanged)"
}
Write-Host "Manifest:  $manifestPath"
Write-Host ''
Write-Host 'Next: run Gradient-PC-Health-Check.exe as a normal user, select CleanTemp, confirm the action (no UAC), then run Verify-CleanTempScenario.ps1.'
