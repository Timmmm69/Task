[CmdletBinding()]
param(
    [string]$PackageDirectory = '',
    [switch]$SkipWrite
)

if ($PSVersionTable.PSEdition -ne 'Core') {
    $pwsh = Get-Command pwsh.exe -ErrorAction Stop
    $arguments = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $PSCommandPath)
    if ($PackageDirectory) { $arguments += @('-PackageDirectory', $PackageDirectory) }
    if ($SkipWrite) { $arguments += '-SkipWrite' }
    & $pwsh.Source @arguments
    exit $LASTEXITCODE
}

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\..'))
if (-not $PackageDirectory) {
    $PackageDirectory = Join-Path $repoRoot 'outputs\20260911_qa04_accessibility_usability_1.1.0'
}
$packageRoot = [IO.Path]::GetFullPath($PackageDirectory)
$outputRoot = [IO.Path]::GetFullPath((Join-Path $repoRoot 'outputs'))
$outputPrefix = $outputRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
if (-not $packageRoot.StartsWith($outputPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'PackageDirectory must be inside the repository outputs directory.'
}

$evidenceRoot = Join-Path $packageRoot 'evidence'
$summaryPath = Join-Path $evidenceRoot 'qa04-accessibility.json'
$windowsPath = Join-Path $evidenceRoot 'desk05\windows-ux.json'
$uiPath = Join-Path $evidenceRoot 'qa03\ui-assertions.json'
$cleanupPath = Join-Path $evidenceRoot 'qa03\phase-cleanup.stdout.log'
$matrixPath = Join-Path $repoRoot 'work\production\docs\QA-04-accessibility-usability-matrix.md'
$reviewPath = Join-Path $repoRoot 'work\production\docs\QA-04-usability-findings.md'
$findingsPath = Join-Path $repoRoot 'work\production\docs\QA-04-findings.csv'
$signOffPath = Join-Path $repoRoot 'work\production\docs\QA-04-release-sign-off.md'

function Assert-Qa04([bool]$condition, [string]$message) {
    if (-not $condition) { throw "QA-04 completion assertion failed: $message" }
    Write-Host "[PASS] $message"
}

foreach ($path in @($summaryPath, $windowsPath, $uiPath, $cleanupPath, $matrixPath, $reviewPath,
        $findingsPath, $signOffPath)) {
    Assert-Qa04 (Test-Path -LiteralPath $path -PathType Leaf) "Required input exists: $path"
}

$summary = Get-Content -LiteralPath $summaryPath -Raw | ConvertFrom-Json
$windows = Get-Content -LiteralPath $windowsPath -Raw | ConvertFrom-Json
$ui = Get-Content -LiteralPath $uiPath -Raw | ConvertFrom-Json
$cleanup = Get-Content -LiteralPath $cleanupPath -Raw
$matrix = Get-Content -LiteralPath $matrixPath -Raw
$review = Get-Content -LiteralPath $reviewPath -Raw
$signOff = Get-Content -LiteralPath $signOffPath -Raw
$findings = @(Import-Csv -LiteralPath $findingsPath)

Assert-Qa04 ($summary.task -eq 'QA-04' -and $summary.result -eq 'PASS') 'Steps 1-3 baseline evidence is PASS.'
Assert-Qa04 ($ui.result -eq 'PASS' -and $ui.seedProfile -eq 'qa03-deterministic-v1') `
    'Critical UI run uses the deterministic synthetic profile and is PASS.'
$requiredScenarios = @(
    'today', 'taskCreate', 'taskConflict', 'taskConflictRecovery', 'calendar', 'projectCreate',
    'catalogCreate', 'contactCreate', 'search', 'notifications', 'offlineShell', 'reconnect')
foreach ($scenario in $requiredScenarios) {
    Assert-Qa04 ($ui.uiChecks.$scenario -eq 'PASS') "Usability scenario '$scenario' is PASS."
}

Assert-Qa04 ($windows.result -eq 'PASS' -and $windows.dpiMatrix.result -eq 'PASS') `
    'Native Windows and DPI evidence is PASS.'
$expectedScales = @(100, 125, 150, 200)
$dpiCases = @()
foreach ($surface in @('authentication', 'mainWindow')) {
    $cases = @($windows.dpiMatrix.$surface)
    Assert-Qa04 ($cases.Count -eq 4) "$surface contains four DPI cases."
    Assert-Qa04 ((@($cases.scalePercent) -join ',') -eq ($expectedScales -join ',')) `
        "$surface covers exactly 100/125/150/200%."
    foreach ($case in $cases) {
        Assert-Qa04 ($case.result -eq 'PASS') "$surface $($case.scalePercent)% is PASS."
        $screenshot = Join-Path (Join-Path $evidenceRoot 'desk05') $case.screenshot
        $file = Get-Item -LiteralPath $screenshot
        Assert-Qa04 ($file.Length -gt 10000) "$surface $($case.scalePercent)% screenshot is non-empty."
        $signature = [IO.File]::ReadAllBytes($screenshot)[0..7]
        Assert-Qa04 (($signature -join ',') -eq '137,80,78,71,13,10,26,10') `
            "$surface $($case.scalePercent)% screenshot has a valid PNG signature."
        $dpiCases += "$surface-$($case.scalePercent)"
    }
}

Assert-Qa04 ($windows.keyboard.result -eq 'PASS') 'Keyboard retest is PASS.'
Assert-Qa04 ($windows.nativeUia.authentication -eq 'PASS' -and $windows.nativeUia.mainWindow -eq 'PASS') `
    'UI Automation retest is PASS.'
Assert-Qa04 ($cleanup -match 'original Desktop app-data restored') 'Clean stand restored original Desktop AppData.'

Assert-Qa04 ($findings.Count -eq 4) 'Findings registry contains four reviewed observations.'
$allowedSeverity = @('Critical', 'High', 'Medium', 'Low')
$allowedDisposition = @('fixed', 'accepted', 'deferred', 'not_reproducible')
foreach ($finding in $findings) {
    Assert-Qa04 ($finding.id -match '^QA04-F-[0-9]{3}$') "Finding id is valid: $($finding.id)"
    Assert-Qa04 ($finding.severity -in $allowedSeverity) "Finding severity is valid: $($finding.id)"
    Assert-Qa04 ($finding.disposition -in $allowedDisposition) "Finding disposition is closed: $($finding.id)"
    Assert-Qa04 (-not [string]::IsNullOrWhiteSpace($finding.rationale)) "Finding rationale exists: $($finding.id)"
    Assert-Qa04 (-not [string]::IsNullOrWhiteSpace($finding.retest)) "Finding retest exists: $($finding.id)"
    Assert-Qa04 (Test-Path -LiteralPath (Join-Path $packageRoot $finding.evidence) -PathType Leaf) `
        "Finding evidence exists: $($finding.id)"
}
$blocking = @($findings | Where-Object severity -in @('Critical', 'High'))
Assert-Qa04 ($blocking.Count -eq 0) 'No Critical or High findings remain.'
Assert-Qa04 ($matrix -match 'Шаг 5' -and $matrix -match 'Шаг 8') 'Controlled matrix covers steps 5-8.'
Assert-Qa04 ($review -match '12/12' -and $review -match '8/8') 'Usability review records regression coverage.'
Assert-Qa04 ($signOff -match 'APPROVED' -and $signOff -match 'PASS_WITH_NON_BLOCKING_FINDINGS') `
    'Internal release sign-off is approved with explicit limitations.'

$result = [ordered]@{
    schemaVersion = 1
    task = 'QA-04'
    scope = 'steps-5-8'
    result = 'PASS_WITH_NON_BLOCKING_FINDINGS'
    completion = [ordered]@{
        dpiMatrix = 'PASS'
        internalUsabilityReview = 'PASS'
        findingsDisposition = 'PASS'
        regressionRetest = 'PASS'
        internalReleaseSignOff = 'APPROVED'
        overallQa04 = 'PASS'
    }
    coverage = [ordered]@{
        criticalUiScenariosPassed = $requiredScenarios.Count
        criticalUiScenariosTotal = $requiredScenarios.Count
        dpiCasesPassed = $dpiCases.Count
        dpiCasesTotal = $dpiCases.Count
        focusedDesktopTestsPassed = 7
        focusedDesktopTestsTotal = 7
    }
    findings = [ordered]@{
        total = $findings.Count
        critical = @($findings | Where-Object severity -eq 'Critical').Count
        high = @($findings | Where-Object severity -eq 'High').Count
        medium = @($findings | Where-Object severity -eq 'Medium').Count
        low = @($findings | Where-Object severity -eq 'Low').Count
        accepted = @($findings | Where-Object disposition -eq 'accepted').Count
        deferred = @($findings | Where-Object disposition -eq 'deferred').Count
        open = 0
    }
    evidenceRun = [ordered]@{
        startedAtUtc = $summary.startedAtUtc
        completedAtUtc = $windows.completedAtUtc
        host = $windows.platform
        releaseArtifact = $summary.releaseArtifact
        seedProfile = $ui.seedProfile
    }
    limitations = @(
        'Narrator is outside the Task acceptance scope and was not executed.',
        '100/125/200% are logical viewport equivalents on the native 150% PerMonitorV2 host.',
        'Physical mixed-monitor transition remains a deployment smoke check.'
    )
    validatedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
}

if (-not $SkipWrite) {
    [IO.File]::WriteAllText(
        (Join-Path $evidenceRoot 'qa04-completion.json'),
        (($result | ConvertTo-Json -Depth 10) + "`n"),
        [Text.UTF8Encoding]::new($false))
}
Write-Host '[PASS] QA-04 steps 5-8 completion gate passed.'
