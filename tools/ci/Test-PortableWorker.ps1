[CmdletBinding()]
param([Parameter(Mandatory = $true)][string]$ExePath)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$exe = (Resolve-Path -LiteralPath $ExePath).Path
$hash = (Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash
$root = Join-Path ([IO.Path]::GetTempPath()) ('GPcHealthCheck-Portable-' + [guid]::NewGuid().ToString('N'))
$script:caseCount = 0

function Assert-Exit {
    param([string]$File, [string[]]$Arguments, [int]$Expected, [string]$Label)
    $info = [Diagnostics.ProcessStartInfo]::new()
    $info.FileName = $File
    $info.UseShellExecute = $false
    $info.CreateNoWindow = $true
    foreach ($argument in $Arguments) { $info.ArgumentList.Add($argument) }
    $process = [Diagnostics.Process]::Start($info)
    try {
        if (-not $process.WaitForExit(60000)) {
            $process.Kill($true)
            $process.WaitForExit()
            throw "Timed out: $Label"
        }
        $actual = $process.ExitCode
        if ($actual -ne $Expected) { throw "$Label expected $Expected, got $actual" }
        $script:caseCount++
        Write-Host "[PASS] $Label (exit $actual)"
    }
    finally { $process.Dispose() }
}

try {
    $executables = [Collections.Generic.List[string]]::new()
    $executables.Add($exe)
    foreach ($variant in @(
        @{ Directory = 'Downloads'; Name = 'G-PC-Health-Check.exe' },
        @{ Directory = 'Downloads\Тестовая папка'; Name = 'Проверка ПК (1).exe' },
        @{ Directory = 'Other tools\A & B'; Name = 'health [test].exe' }
    )) {
        $directory = Join-Path $root $variant.Directory
        New-Item -ItemType Directory -Path $directory -Force | Out-Null
        $copy = Join-Path $directory $variant.Name
        Copy-Item -LiteralPath $exe -Destination $copy
        if ((Get-FileHash -LiteralPath $copy -Algorithm SHA256).Hash -ne $hash) { throw 'Portable test copy hash mismatch.' }
        $executables.Add($copy)
    }

    foreach ($file in $executables) {
        $session = [guid]::NewGuid().ToString('D')
        $pipe = "GPcHealthCheck-$session"
        $nonce = 'A' * 64
        $prefix = @('--worker', '--session', $session)
        $suffix = @('--temp-days', '3', '--pipe', $pipe, '--nonce', $nonce)
        Assert-Exit -File $file -Arguments @('--selftest') -Expected 0 -Label "Self-test: $file"
        Assert-Exit -File $file -Arguments @('--worker') -Expected 20 -Label "Missing session, not location refusal: $file"
        Assert-Exit -File $file -Arguments @('--bootstrap-worker') -Expected 48 -Label "Obsolete bootstrap disabled: $file"
        foreach ($action in @('DefinitelyNotAllowed', 'CleanTemp', 'Dism,DefinitelyNotAllowed')) {
            Assert-Exit -File $file -Arguments ($prefix + @('--actions', $action) + $suffix) -Expected 21 -Label "Reject $action from $file"
        }
        Assert-Exit -File $file -Arguments ($prefix + @('--actions','Dism','--pipe','invalid-pipe','--nonce',$nonce)) -Expected 24 -Label "Invalid pipe: $file"
        Assert-Exit -File $file -Arguments ($prefix + @('--actions','Sfc','--pipe',$pipe,'--nonce','bad-nonce')) -Expected 25 -Label "Invalid nonce: $file"
    }
    if ($script:caseCount -ne 32) { throw "Incomplete portable matrix: $script:caseCount/32" }
    Write-Host "Portable EXE/worker matrix: $script:caseCount/32 passed across 4 path/name layouts."
    Write-Host 'No DISM, SFC, DNS flush or Temp cleanup was executed by this matrix; interactive UAC remains a pilot check.'
}
finally {
    if (Test-Path -LiteralPath $root) { Remove-Item -LiteralPath $root -Recurse -Force }
}
