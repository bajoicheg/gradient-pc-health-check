#requires -Version 7.0
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'DevWorkflow.psm1') -Force
# Shape reproduced from SDK 8.0.425, JSON format 1, run 34710575090.
# Only the project path has been replaced with a synthetic fixture.
$fixture = '{"version":1,"parameters":"--vulnerable --include-transitive","sources":["https://api.nuget.org/v3/index.json"],"projects":[{"path":"C:/Synthetic/Test.csproj"}]}'
Assert-DevAudit $fixture
$passed = 1
foreach ($variant in @(
    '{"version":1,"parameters":"--vulnerable","projects":[{"path":"C:/Synthetic/Test.csproj"}]}',
    '{"version":1,"parameters":"--include-transitive","projects":[{"path":"C:/Synthetic/Test.csproj"}]}',
    '{"version":1,"parameters":"--vulnerable --include-transitive","projects":[{}]}',
    '{"version":1,"parameters":"--vulnerable --include-transitive","logs":[{"level":"warning","message":"Synthetic unavailable source"}],"projects":[{"path":"C:/Synthetic/Test.csproj"}]}',
    '{"version":1,"parameters":"--vulnerable --include-transitive","projects":[{"path":"C:/Synthetic/Test.csproj"},{"path":"C:/Synthetic/Second.csproj","frameworks":[{"framework":"net8.0","transitivePackages":[{"id":"Synthetic","vulnerabilities":[{"severity":"High"}]}]}]}]}'
)) {
    $rejected = $false
    try { Assert-DevAudit $variant } catch { $rejected = $true }
    if (-not $rejected) { throw 'Incomplete or vulnerable audit was accepted.' }
    $passed++
}
Write-Host "SDK audit contract: $passed/6 passed."
