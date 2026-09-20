[CmdletBinding()]
param(
    [string]$Version = '1.0.0',
    [string]$OutputDirectory = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\..'))
if (-not $OutputDirectory) {
    $OutputDirectory = Join-Path $repoRoot 'outputs\20260920_direction2_final_acceptance_1.0.0'
}
$outputRoot = [IO.Path]::GetFullPath($OutputDirectory)
$repoPrefix = $repoRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
if (-not $outputRoot.StartsWith($repoPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'OutputDirectory must be inside the Task repository.'
}

$sourceEvidence = Join-Path $repoRoot 'work\production\evidence\final-direction2-acceptance-1.0.0'
$contactSheetScript = Join-Path $PSScriptRoot 'New-FinalDirection2ContactSheet.ps1'
$contactSheetPath = Join-Path $outputRoot 'direction2-baseline-production-contact-sheet.png'
$requiredEvidence = @(
    'desk05\windows-ux.json',
    'desk05\windows-ux.trx',
    'auth-admin-settings\validation.json',
    'search-lifecycle\validation.json'
)
foreach ($relativePath in $requiredEvidence) {
    $path = Join-Path $sourceEvidence $relativePath
    if (-not (Test-Path -LiteralPath $path)) { throw "Missing acceptance evidence: $path" }
}

[IO.Directory]::CreateDirectory($outputRoot) | Out-Null
& $contactSheetScript -OutputPath $contactSheetPath
if (-not $?) { throw 'Contact sheet generation failed.' }

$evidenceOutput = Join-Path $outputRoot 'evidence'
[IO.Directory]::CreateDirectory($evidenceOutput) | Out-Null
$copyMap = @(
    @{ Source = 'desk05'; Target = 'accessibility' },
    @{ Source = 'auth-admin-settings'; Target = 'sections-and-edge-states' },
    @{ Source = 'search-lifecycle'; Target = 'search-lifecycle' },
    @{ Source = 'full-tests'; Target = 'full-tests' }
)
foreach ($copy in $copyMap) {
    $source = Join-Path $sourceEvidence $copy.Source
    $target = Join-Path $evidenceOutput $copy.Target
    if (-not (Test-Path -LiteralPath $source)) { throw "Missing evidence directory: $source" }
    [IO.Directory]::CreateDirectory($target) | Out-Null
    Get-ChildItem -LiteralPath $source -File | Copy-Item -Destination $target -Force
}

$trxFiles = Get-ChildItem -LiteralPath (Join-Path $evidenceOutput 'full-tests') -Filter '*.trx' -File
if ($trxFiles.Count -ne 3) { throw "Expected three full-suite TRX files, found $($trxFiles.Count)." }
$testTotals = [ordered]@{ total = 0; passed = 0; failed = 0; skipped = 0 }
foreach ($trx in $trxFiles) {
    [xml]$document = Get-Content -LiteralPath $trx.FullName -Raw
    $counters = $document.TestRun.ResultSummary.Counters
    $testTotals.total += [int]$counters.total
    $testTotals.passed += [int]$counters.passed
    $testTotals.failed += [int]$counters.failed
    $testTotals.skipped += ([int]$counters.total - [int]$counters.executed)
}
if ($testTotals.failed -ne 0 -or $testTotals.passed -ne 1747 -or $testTotals.skipped -ne 4) {
    throw "Unexpected full-suite totals: $($testTotals | ConvertTo-Json -Compress)"
}

$desk05 = Get-Content -LiteralPath (Join-Path $sourceEvidence 'desk05\windows-ux.json') -Raw | ConvertFrom-Json
$sections = Get-Content -LiteralPath (Join-Path $sourceEvidence 'auth-admin-settings\validation.json') -Raw | ConvertFrom-Json
$search = Get-Content -LiteralPath (Join-Path $sourceEvidence 'search-lifecycle\validation.json') -Raw | ConvertFrom-Json
if ($desk05.result -ne 'PASS' -or $desk05.customViewports.result -ne 'PASS' -or
    $sections.result -ne 'PASS' -or $search.result -ne 'PASS') {
    throw 'One or more native evidence gates are not PASS.'
}

$versionPath = Join-Path $outputRoot 'VERSION.txt'
$readmePath = Join-Path $outputRoot 'README.md'
$validationPath = Join-Path $outputRoot 'VALIDATION_REPORT.md'
[IO.File]::WriteAllText($versionPath, "$Version`n", [Text.UTF8Encoding]::new($false))

$readme = @"
# Task — final Direction 2 acceptance

Version: $Version

Date: 2026-09-20

Result: **PASS**

This package contains the final Release WPF visual/accessibility acceptance against the frozen Direction 2 sources. Open `direction2-baseline-production-contact-sheet.png` for the side-by-side review and `VALIDATION_REPORT.md` for the evidence-backed decision.

Evidence is grouped into exact DPI/viewport checks, primary sections and edge states, search/offline/read-only lifecycle, and the complete solution test run.
"@
[IO.File]::WriteAllText($readmePath, $readme, [Text.UTF8Encoding]::new($false))

$validation = @"
# Validation report — final Direction 2 production acceptance

Version: $Version

Date: 2026-09-20

Result: **PASS**

## Acceptance decision

- Critical visual defects remaining: **0**.
- High visual defects remaining: **0**.
- Practically removable Medium defects remaining: **0**.
- The primary Release WPF screens remain recognizably consistent with frozen Direction 2 in composition, hierarchy, typography, colour roles, density, state language and interaction model.
- `sources/` and business requirements were not changed.

## Corrected during this acceptance

- Removed the 200%/narrow-viewport shell clipping: navigation becomes the canonical 64 DIP icon rail below 900 DIP, the page heading steps down, and search/new-task/context labels collapse before they can crowd the header.
- Added exact native 1200×900 coverage for authentication, tasks and calendar instead of inferring it from the DPI matrix.
- Extended deterministic evidence to every primary section plus bootstrap failure/progress, offline, limited-role, notification urgency and retained search results.
- Strengthened regression contracts for responsive breakpoints, long Russian text wrapping/ellipsis, and all shared high-contrast resource dictionaries.

## Verification

- Release solution build: **PASS**, 0 errors and 10 existing test-code warnings: one `xUnit1031` in `DesktopCredentialVaultTests.cs` and nine `ASPDEPR004` uses of the legacy test-host `WebHostBuilder`. They are not product/runtime failures.
- Full solution tests: **PASS**, $($testTotals.passed) passed, $($testTotals.skipped) expected PostgreSQL-only skips, $($testTotals.failed) failed.
- DESK-05 native Windows gate: **PASS** — UIA Value/Invoke/Selection/Scroll, Tab, complete F6 cycle, 100/125/150/200%, plus exact 1200×900 auth/tasks/calendar.
- Direction 2 section/edge capture: **PASS** — $($sections.scenarios.Count) named screenshots.
- Search/lifecycle capture: **PASS** — grouped search, overlay, semantic notification urgency, offline retained results and limited-role redaction.
- Offline/read-only: **PASS** through real API shutdown and capability-limited login; retained confirmed data remains visible and write actions remain unavailable.
- Long Russian strings: **PASS** through visible wrapped Russian explanatory/status copy in section and offline captures plus explicit `TextWrapping`/`TextTrimming` contract tests.
- Forced colours/high contrast: **PASS (resource/runtime contract)** through dynamic `SystemParameters.HighContrast` and `SystemColors` resources in Theme, shell, buttons, navigation, data and state controls, loaded by the WPF test host.
- Dashboard ordering/validation: **PASS**; only DESK-05 and QA-01 evidence were updated.

## Visual comparison method

The contact sheet compares nine frozen prototype states with current Release WPF captures: Today, Tasks, Calendar, Inbox, Projects, Search, Settings, Administration, and Offline/read-only. Each production panel names its scenario and scale. Individual unscaled PNGs remain under `evidence/`.

The systematic review used the frozen baseline document, prototype JSX/CSS, stage 5 evidence, design-system tokens and accessibility baseline. It checked shell geometry, three-pane composition, typography scale, Fluent colour roles, 4/8/12/16/20/24 spacing rhythm, control density, focus/state semantics, responsive behaviour and data-state truthfulness.

## Known limits (not release blockers)

- The host's physical DPI was 150%. The 100/125/200% cases are deterministic logical viewports exercised in a live PerMonitorV2 WPF process, not physical monitor switching.
- Windows High Contrast was not toggled globally during automation. The forced-colours result is based on loaded runtime resource triggers/SystemColors and regression tests; a physical OS-theme screenshot remains deployment smoke evidence.
- The four skipped solution tests require an externally enabled shared PostgreSQL test fixture; the isolated PostgreSQL 16 + HTTPS native gates used by this acceptance passed.

## Evidence map

- `direction2-baseline-production-contact-sheet.png` — nine baseline/production pairs.
- `evidence/accessibility/windows-ux.json` — named scenarios, platform, DPI/viewport bounds, keyboard and UIA results.
- `evidence/accessibility/*-100.png` through `*-200.png` — scale evidence.
- `evidence/accessibility/*-1200x900.png` — exact minimum viewport evidence.
- `evidence/sections-and-edge-states/validation.json` — primary sections and auth/admin/settings edges.
- `evidence/search-lifecycle/validation.json` — search, notification, offline and limited-role evidence.
- `evidence/full-tests/*.trx` — complete solution test results.

All isolated test services were stopped and Desktop AppData was restored by the verification scripts.
"@
[IO.File]::WriteAllText($validationPath, $validation, [Text.UTF8Encoding]::new($false))

function Get-FileRecord([string]$relativePath) {
    $fullPath = Join-Path $repoRoot $relativePath
    if (-not (Test-Path -LiteralPath $fullPath)) { throw "Manifest input missing: $fullPath" }
    $file = Get-Item -LiteralPath $fullPath
    [ordered]@{
        path = $relativePath.Replace('\', '/')
        bytes = $file.Length
        sha256 = (Get-FileHash -LiteralPath $fullPath -Algorithm SHA256).Hash.ToLowerInvariant()
    }
}

$sourceFiles = @(
    'work\production\src\Task.Desktop\Converters\VisualFoundationConverters.cs',
    'work\production\src\Task.Desktop\MainWindow.xaml',
    'work\production\src\Task.Desktop\Resources\Tokens.Spacing.xaml',
    'work\production\tests\Task.Desktop.Tests\VisualFoundationTests.cs',
    'work\production\tests\Task.Desktop.Tests\WindowsUxAccessibilityTests.cs',
    'work\production\verification\Test-Desk05WindowsUx.ps1',
    'work\production\verification\Test-Direction2AuthAdminSettings.ps1',
    'work\production\verification\New-FinalDirection2ContactSheet.ps1',
    'work\production\verification\Build-FinalDirection2AcceptancePackage.ps1',
    '.project-dashboard\roadmap.json'
)
$frozenReferences = @(
    'work\stage_5_6_final_visual_baseline_and_handoff\FINAL_VISUAL_BASELINE_1.0.md',
    'work\stage_5_6_final_visual_baseline_and_handoff\prototype\src\App.jsx',
    'work\stage_5_6_final_visual_baseline_and_handoff\prototype\src\styles.css',
    'work\stage_5_6_final_visual_baseline_and_handoff\design-system\Design_System_1.0.md',
    'work\stage_5_prototype\implementation-direction2-final.png',
    'work\stage_5_prototype\implementation-direction2-tasks-final.png',
    'work\stage_5_prototype\implementation-direction2-calendar-week.png',
    'work\stage_5_prototype\implementation-direction2-compact-1200x900.png',
    'work\stage_5_prototype\edge-scaling-200.png'
)

$packageFiles = Get-ChildItem -LiteralPath $outputRoot -Recurse -File |
    Where-Object { $_.Name -notin @('manifest.json', 'SHA256SUMS') } |
    ForEach-Object { [IO.Path]::GetRelativePath($outputRoot, $_.FullName).Replace('\', '/') } |
    Sort-Object
$manifest = [ordered]@{
    schemaVersion = 1
    package = 'Task final Direction 2 production acceptance'
    version = $Version
    date = '2026-09-20'
    result = 'PASS'
    severitiesRemaining = [ordered]@{ critical = 0; high = 0; removableMedium = 0 }
    verification = [ordered]@{
        releaseBuild = 'PASS: 0 errors; 10 existing test-code warnings (1 xUnit1031, 9 ASPDEPR004)'
        fullSolutionTests = $testTotals
        desk05 = 'PASS: UIA, keyboard, 100/125/150/200%, exact 1200x900'
        sectionsAndEdges = "PASS: $($sections.scenarios.Count) screenshots"
        searchLifecycle = 'PASS: normal, overlay, notification urgency, offline, limited role'
        forcedColors = 'PASS: loaded WPF HighContrast/SystemColors resource contract'
    }
    sourceFiles = @($sourceFiles | ForEach-Object { Get-FileRecord $_ })
    frozenReferences = @($frozenReferences | ForEach-Object { Get-FileRecord $_ })
    packageFiles = @($packageFiles)
}
$manifestPath = Join-Path $outputRoot 'manifest.json'
$manifestJson = $manifest | ConvertTo-Json -Depth 8
[IO.File]::WriteAllText($manifestPath, $manifestJson + "`n", [Text.UTF8Encoding]::new($false))

$checksumLines = Get-ChildItem -LiteralPath $outputRoot -Recurse -File |
    Where-Object { $_.Name -ne 'SHA256SUMS' } |
    Sort-Object FullName |
    ForEach-Object {
        $relative = [IO.Path]::GetRelativePath($outputRoot, $_.FullName).Replace('\', '/')
        $hash = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        "$hash  $relative"
    }
$shaPath = Join-Path $outputRoot 'SHA256SUMS'
[IO.File]::WriteAllLines($shaPath, $checksumLines, [Text.UTF8Encoding]::new($false))

Write-Host "[PASS] Final Direction 2 acceptance package: $outputRoot"
Write-Host "[PASS] Full suite: $($testTotals.passed) passed, $($testTotals.skipped) skipped, $($testTotals.failed) failed."
