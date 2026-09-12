[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$ExePath,

    [Parameter(Mandatory = $true)]
    [string]$ChecksumPath,

    [Parameter(Mandatory = $true)]
    [string]$OutputDirectory,

    [string]$GitSha = $env:GITHUB_SHA
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Resolve-RequiredFile {
    param([string]$Path, [string]$Description)
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "$Description not found: $Path"
    }
    return (Resolve-Path -LiteralPath $Path).Path
}

$exe = Resolve-RequiredFile -Path $ExePath -Description 'EXE'
$checksum = Resolve-RequiredFile -Path $ChecksumPath -Description 'Checksum file'
$projectPath = Resolve-RequiredFile -Path '.\src\G.PcHealthCheck\G.PcHealthCheck.csproj' -Description 'Project file'

[xml]$project = Get-Content -LiteralPath $projectPath -Raw
$version = [string]$project.Project.PropertyGroup.Version
if ([string]::IsNullOrWhiteSpace($version) -or $version -notmatch '^\d+\.\d+\.\d+$') {
    throw "Unexpected project version: $version"
}

$checksumLine = (Get-Content -LiteralPath $checksum -Raw -Encoding ASCII).Trim()
if ($checksumLine -notmatch '^(?<hash>[0-9a-fA-F]{64})\s+\*?(?<name>[^\r\n]+)$') {
    throw 'Checksum file must contain: <64-hex-sha256><spaces><filename>.'
}
$expectedHash = $Matches['hash'].ToLowerInvariant()
$checksumName = $Matches['name'].Trim()
if ($checksumName -ne 'G-PC-Health-Check.exe') {
    throw "Unexpected filename in checksum: $checksumName"
}

$actualHash = (Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash.ToLowerInvariant()
if (-not [string]::Equals($actualHash, $expectedHash, [StringComparison]::Ordinal)) {
    throw "EXE SHA-256 mismatch. Expected=$expectedHash Actual=$actualHash"
}

$fileVersion = [Version][Diagnostics.FileVersionInfo]::GetVersionInfo($exe).FileVersion
$expectedFileVersion = [Version]::Parse("$version.0")
if ($fileVersion -ne $expectedFileVersion) {
    throw "EXE FileVersion does not match project version. Expected=$expectedFileVersion EXE=$fileVersion"
}

$requiredFiles = @(
    '.\docs\E2E-TEST-PLAN.md',
    '.\docs\EVIDENCE-ANALYSIS.md',
    '.\docs\SECURITY.md',
    '.\tools\e2e\Prepare-CleanTempScenario.ps1',
    '.\tools\e2e\Verify-CleanTempScenario.ps1',
    '.\tools\e2e\Collect-E2EEvidence.ps1',
    '.\tools\e2e\Cleanup-E2EScenario.ps1',
    '.\tools\e2e\Analyze-E2EEvidence.ps1'
)
foreach ($file in $requiredFiles) {
    [void](Resolve-RequiredFile -Path $file -Description 'Pilot bundle input')
}

New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$outputRoot = (Resolve-Path -LiteralPath $OutputDirectory).Path
$bundleName = "G-PC-Health-Check-$version-pilot"
$stage = Join-Path $outputRoot $bundleName
$zip = Join-Path $outputRoot "$bundleName.zip"

if (Test-Path -LiteralPath $stage) { Remove-Item -LiteralPath $stage -Recurse -Force }
if (Test-Path -LiteralPath $zip) { Remove-Item -LiteralPath $zip -Force }
New-Item -ItemType Directory -Force -Path $stage,(Join-Path $stage 'e2e'),(Join-Path $stage 'docs') | Out-Null

Copy-Item -LiteralPath $exe -Destination (Join-Path $stage 'G-PC-Health-Check.exe')
Copy-Item -LiteralPath $checksum -Destination (Join-Path $stage 'G-PC-Health-Check.exe.sha256')
Copy-Item -LiteralPath '.\docs\E2E-TEST-PLAN.md' -Destination (Join-Path $stage 'docs\E2E-TEST-PLAN.md')
Copy-Item -LiteralPath '.\docs\EVIDENCE-ANALYSIS.md' -Destination (Join-Path $stage 'docs\EVIDENCE-ANALYSIS.md')
Copy-Item -LiteralPath '.\docs\SECURITY.md' -Destination (Join-Path $stage 'docs\SECURITY.md')
Get-ChildItem -LiteralPath '.\tools\e2e' -Filter '*.ps1' -File | ForEach-Object {
    Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $stage ('e2e\' + $_.Name))
}

$manifest = [ordered]@{
    schemaVersion = 1
    product = 'G PC Health Check'
    version = $version
    fileVersion = $fileVersion.ToString()
    architecture = 'win-x64'
    sha256 = $actualHash
    gitSha = if ([string]::IsNullOrWhiteSpace($GitSha)) { $null } else { $GitSha }
    packagedAtUtc = [DateTime]::UtcNow.ToString('o')
    trustedRemediationPath = $null
    portableElevation = $true
    executableNameRestricted = $false
    privilegedActions = @('Dism','Sfc')
    cleanTempScope = '%LOCALAPPDATA%\Temp'
    cleanTempElevated = $false
    bootstrapFromDownloads = $false
    contents = [ordered]@{
        executable = 'G-PC-Health-Check.exe'
        checksum = 'G-PC-Health-Check.exe.sha256'
        e2eTools = @((Get-ChildItem -LiteralPath (Join-Path $stage 'e2e') -Filter '*.ps1' -File | Sort-Object Name).Name)
        documentation = @((Get-ChildItem -LiteralPath (Join-Path $stage 'docs') -File | Sort-Object Name).Name)
    }
}
$manifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $stage 'package-manifest.json') -Encoding UTF8

$startHere = @"
G PC Health Check $version — PILOT / E2E

1. Сверьте SHA-256:
   Get-FileHash .\G-PC-Health-Check.exe -Algorithm SHA256
   Get-Content .\G-PC-Health-Check.exe.sha256
   После переименования EXE укажите его новое имя в Get-FileHash; значение хэша не меняется.

2. Прочитайте:
   .\docs\E2E-TEST-PLAN.md

3. Диагностику можно запускать из этого каталога без прав администратора.
   CleanTemp также работает без elevation и удаляет только старые обычные файлы из %LOCALAPPDATA%\Temp текущего пользователя.
   Elevated worker CleanTemp не принимает.

4. DISM/SFC разрешены из любого расположения и с любым именем EXE.
   Перенос в Program Files и установка не требуются. Запустите приложение обычным двойным кликом.
   После выбора и подтверждения этих действий программа запросит UAC для той же копии EXE.
   Права доступа Windows, доступность сетевого пути и корпоративные политики запуска продолжают действовать.
   Не перемещайте и не переименовывайте файл во время работы приложения.

5. После E2E соберите ZIP через e2e\Collect-E2EEvidence.ps1, указав -SourceExe с фактическим путём, и проверьте его через e2e\Analyze-E2EEvidence.ps1.
   Сравнение с прежней Program Files копией в старом анализаторе — дополнительная проверка, а не требование установки.

Version: $version
FileVersion: $fileVersion
SHA-256: $actualHash
Git SHA: $(if ([string]::IsNullOrWhiteSpace($GitSha)) { 'not supplied' } else { $GitSha })
"@

# Explicit BOM avoids mojibake in Windows editors that still auto-detect this file as ANSI.
$startHerePath = Join-Path $stage 'START-HERE.txt'
[IO.File]::WriteAllText($startHerePath, $startHere, [Text.UTF8Encoding]::new($true, $true))

# Strict decoding rejects malformed UTF-8 instead of silently replacing invalid byte sequences.
$strictUtf8Bom = [Text.UTF8Encoding]::new($true, $true)
$roundTrip = [IO.File]::ReadAllText($startHerePath, $strictUtf8Bom)
if (-not $roundTrip.Contains('Сверьте SHA-256') -or -not $roundTrip.Contains('Диагностику можно запускать')) {
    throw 'START-HERE strict UTF-8 verification failed.'
}

# Verify the staged executable again before archiving.
$stagedExe = Join-Path $stage 'G-PC-Health-Check.exe'
$stagedHash = (Get-FileHash -LiteralPath $stagedExe -Algorithm SHA256).Hash.ToLowerInvariant()
if (-not [string]::Equals($stagedHash, $actualHash, [StringComparison]::Ordinal)) {
    throw 'Staged EXE hash changed before packaging.'
}

Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip -CompressionLevel Optimal
if (-not (Test-Path -LiteralPath $zip -PathType Leaf) -or (Get-Item -LiteralPath $zip).Length -le 0) {
    throw 'Pilot ZIP was not created.'
}

Write-Host "Pilot bundle: $zip"
Write-Host "Version: $version"
Write-Host "SHA-256: $actualHash"
Write-Output $zip
