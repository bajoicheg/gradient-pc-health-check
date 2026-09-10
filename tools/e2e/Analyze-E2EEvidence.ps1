[CmdletBinding(DefaultParameterSetName = 'Zip')]
param(
    [Parameter(Mandatory = $true, ParameterSetName = 'Zip')]
    [string]$EvidenceZip,

    [Parameter(Mandatory = $true, ParameterSetName = 'Directory')]
    [string]$EvidenceDirectory,

    [string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Get-NestedValue {
    param(
        [object]$Object,
        [string[]]$Path
    )

    $current = $Object
    foreach ($name in $Path) {
        if ($null -eq $current) { return $null }
        $property = $current.PSObject.Properties[$name]
        if ($null -eq $property) { return $null }
        $current = $property.Value
    }
    return $current
}

function Add-Check {
    param(
        [System.Collections.Generic.List[object]]$List,
        [string]$Severity,
        [string]$Name,
        [string]$Expected,
        [string]$Actual,
        [string]$Details = ''
    )

    $List.Add([pscustomobject]@{
        severity = $Severity
        name = $Name
        expected = $Expected
        actual = $Actual
        details = $Details
    })
}

function Convert-ToMarkdownCell {
    param([object]$Value)
    if ($null -eq $Value) { return '—' }
    $text = [string]$Value
    $text = $text -replace '\|', '\|'
    $text = $text -replace "`r?`n", '<br>'
    if ([string]::IsNullOrWhiteSpace($text)) { return '—' }
    return $text
}

function Test-SafeZipEntryName {
    param([string]$Name)
    if ([string]::IsNullOrWhiteSpace($Name)) { return $true }
    $normalized = $Name.Replace('/', '\')
    if ($normalized.StartsWith('\')) { return $false }
    if ($normalized -match '^[A-Za-z]:') { return $false }
    foreach ($segment in $normalized.Split('\')) {
        if ($segment -eq '..') { return $false }
    }
    return $true
}

function Read-JsonFile {
    param([string]$Path)
    return (Get-Content -LiteralPath $Path -Raw -Encoding UTF8 | ConvertFrom-Json)
}

$tempRoot = $null
$bundleRoot = $null
$sourceDescription = $null

try {
    if ($PSCmdlet.ParameterSetName -eq 'Zip') {
        if (-not (Test-Path -LiteralPath $EvidenceZip -PathType Leaf)) {
            throw "Evidence ZIP not found: $EvidenceZip"
        }

        Add-Type -AssemblyName System.IO.Compression.FileSystem
        $archive = [System.IO.Compression.ZipFile]::OpenRead((Resolve-Path -LiteralPath $EvidenceZip).Path)
        try {
            foreach ($entry in $archive.Entries) {
                if (-not (Test-SafeZipEntryName -Name $entry.FullName)) {
                    throw "Unsafe ZIP entry rejected: $($entry.FullName)"
                }
            }
        }
        finally {
            $archive.Dispose()
        }

        $tempRoot = Join-Path $env:TEMP "GradientPcHealthCheck-Analyze-$([guid]::NewGuid().ToString('N'))"
        New-Item -ItemType Directory -Path $tempRoot -Force | Out-Null
        Expand-Archive -LiteralPath $EvidenceZip -DestinationPath $tempRoot -Force
        $bundleRoot = $tempRoot
        $sourceDescription = (Resolve-Path -LiteralPath $EvidenceZip).Path
    }
    else {
        if (-not (Test-Path -LiteralPath $EvidenceDirectory -PathType Container)) {
            throw "Evidence directory not found: $EvidenceDirectory"
        }
        $bundleRoot = (Resolve-Path -LiteralPath $EvidenceDirectory).Path
        $sourceDescription = $bundleRoot
    }

    $environmentFile = Get-ChildItem -LiteralPath $bundleRoot -Filter 'environment.json' -File -Recurse | Select-Object -First 1
    if ($null -eq $environmentFile) {
        throw 'environment.json not found in evidence bundle.'
    }

    $actualRoot = $environmentFile.Directory.FullName
    $environment = Read-JsonFile -Path $environmentFile.FullName
    $checks = [System.Collections.Generic.List[object]]::new()

    $schemaVersion = Get-NestedValue -Object $environment -Path @('schemaVersion')
    Add-Check -List $checks -Severity $(if ($schemaVersion -eq 1) { 'PASS' } else { 'WARN' }) `
        -Name 'Evidence schema' -Expected '1' -Actual ([string]$schemaVersion) `
        -Details 'Unknown schema versions may require manual review.'

    $osCaption = Get-NestedValue -Object $environment -Path @('operatingSystem','Caption')
    if ([string]::IsNullOrWhiteSpace([string]$osCaption)) {
        Add-Check -List $checks -Severity 'WARN' -Name 'Windows version' -Expected 'Windows 11' -Actual 'not collected'
    }
    elseif ([string]$osCaption -match 'Windows 11') {
        Add-Check -List $checks -Severity 'PASS' -Name 'Windows version' -Expected 'Windows 11' -Actual ([string]$osCaption)
    }
    else {
        Add-Check -List $checks -Severity 'WARN' -Name 'Windows version' -Expected 'Windows 11' -Actual ([string]$osCaption)
    }

    $sourceHash = Get-NestedValue -Object $environment -Path @('sourceExe','sha256')
    $installedHash = Get-NestedValue -Object $environment -Path @('installedExe','sha256')
    $hashMatch = Get-NestedValue -Object $environment -Path @('sourceAndInstalledSha256Match')
    if ($null -ne $sourceHash -and $null -ne $installedHash) {
        if ($hashMatch -eq $true -and [string]::Equals([string]$sourceHash, [string]$installedHash, [StringComparison]::OrdinalIgnoreCase)) {
            Add-Check -List $checks -Severity 'PASS' -Name 'EXE SHA-256' -Expected 'source = Program Files copy' -Actual ([string]$sourceHash)
        }
        else {
            Add-Check -List $checks -Severity 'FAIL' -Name 'EXE SHA-256' -Expected 'source = Program Files copy' -Actual "source=$sourceHash installed=$installedHash"
        }
    }
    else {
        Add-Check -List $checks -Severity 'WARN' -Name 'EXE SHA-256' -Expected 'both source and Program Files hashes collected' -Actual 'incomplete evidence'
    }

    $verificationPath = Join-Path $actualRoot 'e2e-verification.json'
    $verification = $null
    if (Test-Path -LiteralPath $verificationPath -PathType Leaf) {
        $verification = Read-JsonFile -Path $verificationPath
        $verificationPassed = Get-NestedValue -Object $verification -Path @('passed')
        Add-Check -List $checks -Severity $(if ($verificationPassed -eq $true) { 'PASS' } else { 'FAIL' }) `
            -Name 'CleanTemp sentinel verification' -Expected 'passed=true' -Actual ([string]$verificationPassed)
    }
    else {
        Add-Check -List $checks -Severity 'FAIL' -Name 'CleanTemp sentinel verification' -Expected 'e2e-verification.json present' -Actual 'missing'
    }

    $manifestPath = Join-Path $actualRoot 'e2e-manifest.json'
    if (Test-Path -LiteralPath $manifestPath -PathType Leaf) {
        $manifest = Read-JsonFile -Path $manifestPath
        $junctionEnabled = Get-NestedValue -Object $manifest -Path @('junctionTest','enabled')
        Add-Check -List $checks -Severity 'PASS' -Name 'E2E manifest' -Expected 'present' -Actual 'present'
        if ($junctionEnabled -eq $true) {
            Add-Check -List $checks -Severity 'PASS' -Name 'Junction scenario' -Expected 'enabled or explicitly optional' -Actual 'enabled'
        }
        else {
            Add-Check -List $checks -Severity 'INFO' -Name 'Junction scenario' -Expected 'optional' -Actual 'not enabled'
        }
    }
    else {
        Add-Check -List $checks -Severity 'FAIL' -Name 'E2E manifest' -Expected 'e2e-manifest.json present' -Actual 'missing'
    }

    $reportVerification = $null
    $reportFile = $null
    $reportsDirectory = Join-Path $actualRoot 'reports'
    if (Test-Path -LiteralPath $reportsDirectory -PathType Container) {
        $candidateFiles = Get-ChildItem -LiteralPath $reportsDirectory -Filter '*.json' -File | Sort-Object Name -Descending
        foreach ($candidate in $candidateFiles) {
            try {
                $candidateJson = Read-JsonFile -Path $candidate.FullName
                if ($null -ne (Get-NestedValue -Object $candidateJson -Path @('Before')) -and
                    $null -ne (Get-NestedValue -Object $candidateJson -Path @('After')) -and
                    $null -ne (Get-NestedValue -Object $candidateJson -Path @('Remediation'))) {
                    $reportVerification = $candidateJson
                    $reportFile = $candidate
                    break
                }
            }
            catch { }
        }
    }

    $summary = [ordered]@{
        machine = Get-NestedValue -Object $environment -Path @('machine')
        evidenceCollectedAt = Get-NestedValue -Object $environment -Path @('collectedAt')
        appVerificationReport = $null
        scoreBefore = $null
        scoreAfter = $null
        statusBefore = $null
        statusAfter = $null
        coverageBefore = $null
        coverageAfter = $null
        missingSignalsAfter = @()
        collectionWarningsAfter = @()
        cleanTempSuccess = $null
    }

    if ($null -eq $reportVerification) {
        Add-Check -List $checks -Severity 'FAIL' -Name 'Application verification report' -Expected 'before/after JSON present' -Actual 'not found'
    }
    else {
        $summary.appVerificationReport = $reportFile.Name
        $summary.scoreBefore = Get-NestedValue -Object $reportVerification -Path @('Before','Assessment','Score')
        $summary.scoreAfter = Get-NestedValue -Object $reportVerification -Path @('After','Assessment','Score')
        $summary.statusBefore = Get-NestedValue -Object $reportVerification -Path @('Before','Assessment','Status')
        $summary.statusAfter = Get-NestedValue -Object $reportVerification -Path @('After','Assessment','Status')
        $summary.coverageBefore = Get-NestedValue -Object $reportVerification -Path @('Before','Assessment','CoveragePercent')
        $summary.coverageAfter = Get-NestedValue -Object $reportVerification -Path @('After','Assessment','CoveragePercent')
        $summary.missingSignalsAfter = @(Get-NestedValue -Object $reportVerification -Path @('After','Assessment','MissingSignals'))
        $summary.collectionWarningsAfter = @(Get-NestedValue -Object $reportVerification -Path @('After','Data','CollectionWarnings'))

        $beforeMachine = Get-NestedValue -Object $reportVerification -Path @('Before','Data','System','ComputerName')
        $afterMachine = Get-NestedValue -Object $reportVerification -Path @('After','Data','System','ComputerName')
        if (-not [string]::IsNullOrWhiteSpace([string]$beforeMachine) -and [string]::Equals([string]$beforeMachine, [string]$afterMachine, [StringComparison]::OrdinalIgnoreCase)) {
            Add-Check -List $checks -Severity 'PASS' -Name 'Before/after machine identity' -Expected 'same computer' -Actual ([string]$afterMachine)
        }
        else {
            Add-Check -List $checks -Severity 'FAIL' -Name 'Before/after machine identity' -Expected 'same computer' -Actual "before=$beforeMachine after=$afterMachine"
        }

        $coverage = $summary.coverageAfter
        if ($null -eq $coverage) {
            Add-Check -List $checks -Severity 'FAIL' -Name 'Diagnostic coverage after remediation' -Expected '>= 85%' -Actual 'missing'
        }
        elseif ([double]$coverage -ge 85) {
            Add-Check -List $checks -Severity 'PASS' -Name 'Diagnostic coverage after remediation' -Expected '>= 85%' -Actual "$coverage%"
        }
        elseif ([double]$coverage -ge 65) {
            Add-Check -List $checks -Severity 'WARN' -Name 'Diagnostic coverage after remediation' -Expected '>= 85%' -Actual "$coverage%"
        }
        else {
            Add-Check -List $checks -Severity 'FAIL' -Name 'Diagnostic coverage after remediation' -Expected '>= 85%' -Actual "$coverage%"
        }

        if ($summary.collectionWarningsAfter.Count -gt 0) {
            Add-Check -List $checks -Severity 'WARN' -Name 'Collection warnings after remediation' -Expected '0' -Actual ($summary.collectionWarningsAfter -join '; ')
        }
        else {
            Add-Check -List $checks -Severity 'PASS' -Name 'Collection warnings after remediation' -Expected '0' -Actual '0'
        }

        $remediationActions = @(Get-NestedValue -Object $reportVerification -Path @('Remediation','Actions'))
        $cleanTemp = @($remediationActions | Where-Object { (Get-NestedValue -Object $_ -Path @('Id')) -eq 'CleanTemp' } | Select-Object -First 1)
        if ($cleanTemp.Count -eq 1) {
            $cleanTempSuccess = Get-NestedValue -Object $cleanTemp[0] -Path @('Success')
            $summary.cleanTempSuccess = $cleanTempSuccess
            Add-Check -List $checks -Severity $(if ($cleanTempSuccess -eq $true) { 'PASS' } else { 'FAIL' }) `
                -Name 'CleanTemp remediation result' -Expected 'Success=true' -Actual ([string]$cleanTempSuccess)
        }
        else {
            Add-Check -List $checks -Severity 'FAIL' -Name 'CleanTemp remediation result' -Expected 'CleanTemp action present' -Actual 'missing'
        }

        Add-Check -List $checks -Severity 'INFO' -Name 'Health result after remediation' `
            -Expected 'informational; may legitimately remain WARN/CRIT' `
            -Actual "score=$($summary.scoreAfter) status=$($summary.statusAfter)" `
            -Details 'A real workstation can remain unhealthy after a correctly executed remediation; this is not an E2E failure by itself.'
    }

    $failCount = @($checks | Where-Object { $_.severity -eq 'FAIL' }).Count
    $warnCount = @($checks | Where-Object { $_.severity -eq 'WARN' }).Count
    $verdict = if ($failCount -gt 0) { 'FAIL' } elseif ($warnCount -gt 0) { 'WARN' } else { 'PASS' }

    $result = [ordered]@{
        schemaVersion = 1
        analyzedAt = (Get-Date).ToString('o')
        source = $sourceDescription
        verdict = $verdict
        failCount = $failCount
        warnCount = $warnCount
        summary = $summary
        checks = @($checks)
    }

    if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
        $OutputDirectory = if ($PSCmdlet.ParameterSetName -eq 'Zip') {
            Split-Path (Resolve-Path -LiteralPath $EvidenceZip).Path -Parent
        }
        else {
            $bundleRoot
        }
    }
    New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null

    $stamp = Get-Date -Format 'yyyyMMdd_HHmmss'
    $jsonOutput = Join-Path $OutputDirectory "Gradient-PC-Health-Check-E2E-analysis-$stamp.json"
    $mdOutput = Join-Path $OutputDirectory "Gradient-PC-Health-Check-E2E-analysis-$stamp.md"
    $result | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $jsonOutput -Encoding UTF8

    $markdown = [System.Collections.Generic.List[string]]::new()
    $markdown.Add('# Gradient PC Health Check — E2E analysis')
    $markdown.Add('')
    $markdown.Add("**Verdict:** $verdict  ")
    $markdown.Add("**Source:** $(Convert-ToMarkdownCell $sourceDescription)  ")
    $markdown.Add("**Analyzed:** $((Get-Date).ToString('yyyy-MM-dd HH:mm:ss K'))")
    $markdown.Add('')
    $markdown.Add('## Automated checks')
    $markdown.Add('')
    $markdown.Add('| Severity | Check | Expected | Actual | Details |')
    $markdown.Add('|---|---|---|---|---|')
    foreach ($check in $checks) {
        $markdown.Add("| $(Convert-ToMarkdownCell $check.severity) | $(Convert-ToMarkdownCell $check.name) | $(Convert-ToMarkdownCell $check.expected) | $(Convert-ToMarkdownCell $check.actual) | $(Convert-ToMarkdownCell $check.details) |")
    }
    $markdown.Add('')
    $markdown.Add('## Diagnostic summary')
    $markdown.Add('')
    $markdown.Add("- Machine: $(Convert-ToMarkdownCell $summary.machine)")
    $markdown.Add("- Score: $(Convert-ToMarkdownCell $summary.scoreBefore) → $(Convert-ToMarkdownCell $summary.scoreAfter)")
    $markdown.Add("- Status: $(Convert-ToMarkdownCell $summary.statusBefore) → $(Convert-ToMarkdownCell $summary.statusAfter)")
    $markdown.Add("- Coverage: $(Convert-ToMarkdownCell $summary.coverageBefore)% → $(Convert-ToMarkdownCell $summary.coverageAfter)%")
    $markdown.Add("- Missing signals after: $(if ($summary.missingSignalsAfter.Count -gt 0) { Convert-ToMarkdownCell ($summary.missingSignalsAfter -join ', ') } else { 'none' })")
    $markdown.Add("- CleanTemp success: $(Convert-ToMarkdownCell $summary.cleanTempSuccess)")
    $markdown.Add('')
    $markdown.Add('Health Score/Status describe the workstation, not test success. A WARN/CRIT workstation can still produce a PASS E2E if the application measured it correctly and remediation behaved safely.')
    Set-Content -LiteralPath $mdOutput -Value $markdown -Encoding UTF8

    Write-Host ''
    Write-Host "E2E evidence analysis: $verdict" -ForegroundColor $(if ($verdict -eq 'PASS') { 'Green' } elseif ($verdict -eq 'WARN') { 'Yellow' } else { 'Red' })
    Write-Host "JSON: $jsonOutput"
    Write-Host "Markdown: $mdOutput"
    Write-Host ''

    Write-Output $jsonOutput
    Write-Output $mdOutput

    if ($verdict -eq 'FAIL') { exit 20 }
    if ($verdict -eq 'WARN') { exit 10 }
    exit 0
}
finally {
    if ($null -ne $tempRoot -and (Test-Path -LiteralPath $tempRoot)) {
        Remove-Item -LiteralPath $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}
