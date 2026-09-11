[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$root = Join-Path $env:TEMP 'GPcHealthCheck-E2E'
$manifestPath = Join-Path $root 'e2e-manifest.json'
if (-not (Test-Path -LiteralPath $manifestPath)) {
    throw "E2E manifest not found: $manifestPath. Run Prepare-CleanTempScenario.ps1 first."
}

$manifest = Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
$checks = [System.Collections.Generic.List[object]]::new()

function Add-Check {
    param([string]$Name, [bool]$Passed, [string]$Expected, [string]$Actual)
    $checks.Add([pscustomobject]@{
        name = $Name
        passed = $Passed
        expected = $Expected
        actual = $Actual
    })
}

$oldExists = Test-Path -LiteralPath $manifest.oldFile.path
Add-Check -Name 'Old Temp sentinel deleted' -Passed (-not $oldExists) -Expected 'ABSENT' -Actual $(if ($oldExists) { 'PRESENT' } else { 'ABSENT' })

$freshExists = Test-Path -LiteralPath $manifest.freshFile.path
$freshHash = if ($freshExists) { (Get-FileHash -LiteralPath $manifest.freshFile.path -Algorithm SHA256).Hash.ToLowerInvariant() } else { $null }
$freshOk = $freshExists -and [string]::Equals($freshHash, [string]$manifest.freshFile.sha256, [StringComparison]::OrdinalIgnoreCase)
Add-Check -Name 'Fresh Temp sentinel preserved' -Passed $freshOk -Expected "PRESENT sha256=$($manifest.freshFile.sha256)" -Actual $(if (-not $freshExists) { 'ABSENT' } else { "PRESENT sha256=$freshHash" })

if ([bool]$manifest.junctionTest.enabled) {
    $outsideExists = Test-Path -LiteralPath $manifest.junctionTest.outsideFile
    $outsideHash = if ($outsideExists) { (Get-FileHash -LiteralPath $manifest.junctionTest.outsideFile -Algorithm SHA256).Hash.ToLowerInvariant() } else { $null }
    $outsideOk = $outsideExists -and [string]::Equals($outsideHash, [string]$manifest.junctionTest.outsideSha256, [StringComparison]::OrdinalIgnoreCase)
    Add-Check -Name 'Reparse/junction target preserved' -Passed $outsideOk -Expected "PRESENT sha256=$($manifest.junctionTest.outsideSha256)" -Actual $(if (-not $outsideExists) { 'ABSENT' } else { "PRESENT sha256=$outsideHash" })
}

$failed = @($checks | Where-Object { -not $_.passed })
$result = [ordered]@{
    schemaVersion = 1
    verifiedAt = (Get-Date).ToString('o')
    machine = $env:COMPUTERNAME
    user = [Environment]::UserName
    passed = ($failed.Count -eq 0)
    checks = @($checks)
}

$resultPath = Join-Path $root 'e2e-verification.json'
$result | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $resultPath -Encoding UTF8

Write-Host ''
Write-Host 'G PC Health Check — CleanTemp E2E verification'
foreach ($check in $checks) {
    $mark = if ($check.passed) { 'PASS' } else { 'FAIL' }
    $color = if ($check.passed) { 'Green' } else { 'Red' }
    Write-Host ("[{0}] {1}" -f $mark, $check.name) -ForegroundColor $color
    if (-not $check.passed) {
        Write-Host "       expected: $($check.expected)"
        Write-Host "       actual:   $($check.actual)"
    }
}
Write-Host "Evidence: $resultPath"
Write-Host ''

if ($failed.Count -gt 0) { exit 10 }
exit 0
