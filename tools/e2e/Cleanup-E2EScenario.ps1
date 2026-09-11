[CmdletBinding(SupportsShouldProcess)]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$targets = @(
    (Join-Path $env:TEMP 'GPcHealthCheck-E2E'),
    (Join-Path $env:LOCALAPPDATA 'G\PCHealthCheck\E2E-Outside')
)

foreach ($target in $targets) {
    if (-not (Test-Path -LiteralPath $target)) { continue }

    $full = [IO.Path]::GetFullPath($target)
    $allowedTemp = [IO.Path]::GetFullPath((Join-Path $env:TEMP 'GPcHealthCheck-E2E')).TrimEnd('\')
    $allowedOutside = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'G\PCHealthCheck\E2E-Outside')).TrimEnd('\')
    $safe = [string]::Equals($full.TrimEnd('\'), $allowedTemp, [StringComparison]::OrdinalIgnoreCase) -or
            [string]::Equals($full.TrimEnd('\'), $allowedOutside, [StringComparison]::OrdinalIgnoreCase)
    if (-not $safe) { throw "Safety check failed for cleanup path: $full" }

    if ($PSCmdlet.ShouldProcess($full, 'Remove E2E test data')) {
        # Remove a junction itself without traversing its target.
        $item = Get-Item -LiteralPath $full -Force
        if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            Remove-Item -LiteralPath $full -Force
        }
        else {
            Remove-Item -LiteralPath $full -Recurse -Force
        }
        Write-Host "Removed: $full"
    }
}

Write-Host 'G PC Health Check E2E test data cleanup completed.' -ForegroundColor Green
