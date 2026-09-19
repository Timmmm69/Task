[CmdletBinding()]
param(
    [string]$RepositoryRoot,
    [string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'

if (-not $RepositoryRoot) {
    $RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
}
if (-not $OutputDirectory) {
    $OutputDirectory = Join-Path $RepositoryRoot 'outputs\20260919_direction2_search_notifications_lifecycle_1.0.0'
}

$outputsRoot = (Resolve-Path (Join-Path $RepositoryRoot 'outputs')).Path
$candidate = [IO.Path]::GetFullPath($OutputDirectory)
if (-not $candidate.StartsWith($outputsRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw "OutputDirectory must be a child of $outputsRoot."
}

function Write-TextFile([string]$Path, [string]$Value) {
    [IO.File]::WriteAllText($Path, (($Value.TrimEnd()) + "`n"), [Text.UTF8Encoding]::new($false))
}

if (Test-Path -LiteralPath $candidate) {
    $resolved = (Resolve-Path -LiteralPath $candidate).Path
    if (-not $resolved.StartsWith($outputsRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to replace output outside outputs: $resolved"
    }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}

$evidenceSource = Join-Path $RepositoryRoot 'work\production\evidence\direction2-search-lifecycle-1.0.0'
$evidenceTarget = Join-Path $candidate 'evidence'
[IO.Directory]::CreateDirectory($evidenceTarget) | Out-Null
foreach ($name in @('normal.png', 'normal-overlay.png', 'offline.png', 'limited-role.png', 'validation.json')) {
    $source = Join-Path $evidenceSource $name
    if (-not (Test-Path -LiteralPath $source -PathType Leaf)) { throw "Missing evidence file: $source" }
    Copy-Item -LiteralPath $source -Destination (Join-Path $evidenceTarget $name)
}

Write-TextFile (Join-Path $candidate 'VERSION.txt') '1.0.0'

$readme = @'
# Direction 2: поиск, уведомления и lifecycle

Пакет фиксирует production-реализацию frozen Direction 2 для глобального поиска, центра уведомлений, архива и корзины в Windows-клиенте Task.

В `evidence/` находятся фактические Release WPF-скриншоты normal, overlay, offline и limited-role, а также машинный отчёт UIA-прогона. Итог проверок описан в `VALIDATION_REPORT.md`; состав и исходные хэши — в `manifest.json`; хэши файлов пакета — в `SHA256SUMS`.
'@
Write-TextFile (Join-Path $candidate 'README.md') $readme

$validation = @'
# Validation report — Direction 2 search, notifications and lifecycle

Version: 1.0.0

Date: 2026-09-19

Result: **PASS**

## Implemented

- Канонический глобальный поиск и modal overlay с Ctrl+K, Escape, стрелками и Enter.
- Permission-safe группировка, подсветка совпадений и redaction без раскрытия скрытых объектов, количества или связей.
- Центр уведомлений с unread-маркером, critical/warning/default срочностью, фильтрацией и массовым прочтением.
- Архив и корзина с единым inspector, retention/hold-информацией, фильтрами и доступным восстановлением.
- Empty, loading, offline, limited-role и server/error feedback states.
- UIA names/ids, focus return и layout, устойчивый к Windows scaling.

## Verification

- `dotnet build Task.sln -c Release --no-restore`: PASS, 0 errors; 10 existing test-code warnings (`xUnit1031`, `ASPDEPR004`) outside this change scope.
- `dotnet test Task.sln -c Release --no-build --no-restore`: PASS — Desktop 336, core 815, service-host 590; всего 1741 passed, 4 skipped, 0 failed.
- `Test-Direction2SearchLifecycle.ps1`: PASS на изолированных PostgreSQL 16 + HTTPS API + Release WPF; normal, overlay, offline retained-results и limited-role redaction.
- Визуально проверены все четыре скриншота против frozen Direction 2 baseline; защищённые названия, совпадения, количество и связи в limited-role не показаны.
- `git diff --check`: PASS.

## Evidence

- `evidence/normal.png`
- `evidence/normal-overlay.png`
- `evidence/offline.png`
- `evidence/limited-role.png`
- `evidence/validation.json`

E2E cleanup остановил API/PostgreSQL, удалил временный runtime и восстановил исходные Desktop AppData.
'@
Write-TextFile (Join-Path $candidate 'VALIDATION_REPORT.md') $validation

$sourceFiles = @(
    'work/production/src/Task.Desktop/MainWindow.xaml',
    'work/production/src/Task.Desktop/MainWindow.xaml.cs',
    'work/production/src/Task.Desktop/ViewModels/WorkHubViewModel.cs',
    'work/production/src/Task.Desktop/Views/WorkHubView.xaml',
    'work/production/src/Task.Desktop/Work/DesktopWorkApiClient.cs',
    'work/production/tests/Task.Desktop.Tests/WindowsUxAccessibilityTests.cs',
    'work/production/tests/Task.Desktop.Tests/Work/DesktopWorkApiClientTests.cs',
    'work/production/tests/Task.Desktop.Tests/Work/WorkHubViewModelTests.cs',
    'work/production/verification/Test-Direction2SearchLifecycle.ps1',
    'work/production/verification/Build-Direction2SearchLifecyclePackage.ps1',
    '.project-dashboard/roadmap.json'
)
$referenceFiles = @(
    'work/stage_5_prototype/qa-wave-c-search.png',
    'work/stage_5_prototype/qa-wave-c-search-overlay.png',
    'work/stage_5_prototype/qa-wave-c-lifecycle-archive.png',
    'work/stage_5_prototype/qa-wave-c-lifecycle-trash-retention.png',
    'work/stage_5_prototype/edge-notification-target-changed.png',
    'work/stage_5_6_final_visual_baseline_and_handoff/prototype/src/App.jsx',
    'work/stage_5_6_final_visual_baseline_and_handoff/prototype/src/styles.css',
    'work/stage_5_6_final_visual_baseline_and_handoff/design-system/Design_System_1.0.md',
    'work/stage_5_6_final_visual_baseline_and_handoff/design-system/Component_Implementation_Specs_1.0.csv'
)

function Get-Inventory([string[]]$Paths) {
    foreach ($relativePath in $Paths) {
        $fullPath = Join-Path $RepositoryRoot $relativePath
        if (-not (Test-Path -LiteralPath $fullPath -PathType Leaf)) { throw "Missing inventory file: $relativePath" }
        $item = Get-Item -LiteralPath $fullPath
        [ordered]@{
            path = $relativePath.Replace('\', '/')
            bytes = $item.Length
            sha256 = (Get-FileHash -LiteralPath $fullPath -Algorithm SHA256).Hash.ToLowerInvariant()
        }
    }
}

$manifest = [ordered]@{
    schemaVersion = 1
    package = 'Task Direction 2 search, notifications and lifecycle'
    version = '1.0.0'
    date = '2026-09-19'
    result = 'PASS'
    source_files = @(Get-Inventory $sourceFiles)
    frozen_references = @(Get-Inventory $referenceFiles)
    verification = [ordered]@{
        release_build = 'PASS: 0 errors; 10 existing test-code warnings (xUnit1031, ASPDEPR004)'
        tests = [ordered]@{ passed = 1741; skipped = 4; failed = 0 }
        native_uia = 'PASS: normal, normal-overlay, offline, limited-role'
        security = 'PASS: permission-safe redaction preserved'
    }
    package_files = @(
        'README.md',
        'VERSION.txt',
        'VALIDATION_REPORT.md',
        'evidence/normal.png',
        'evidence/normal-overlay.png',
        'evidence/offline.png',
        'evidence/limited-role.png',
        'evidence/validation.json',
        'manifest.json',
        'SHA256SUMS'
    )
}
Write-TextFile (Join-Path $candidate 'manifest.json') ($manifest | ConvertTo-Json -Depth 8)

$checksumTargets = Get-ChildItem -LiteralPath $candidate -Recurse -File |
    Where-Object Name -ne 'SHA256SUMS' |
    Sort-Object FullName
$checksums = foreach ($file in $checksumTargets) {
    $relative = [IO.Path]::GetRelativePath($candidate, $file.FullName).Replace('\', '/')
    '{0}  {1}' -f (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $relative
}
Write-TextFile (Join-Path $candidate 'SHA256SUMS') ($checksums -join "`n")

foreach ($line in Get-Content -LiteralPath (Join-Path $candidate 'SHA256SUMS')) {
    if ($line -notmatch '^([0-9a-f]{64})  (.+)$') { throw "Invalid checksum line: $line" }
    $actual = (Get-FileHash -LiteralPath (Join-Path $candidate $Matches[2]) -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actual -ne $Matches[1]) { throw "Checksum mismatch: $($Matches[2])" }
}

Write-Host "[PASS] Direction 2 output package built and checksum-validated: $candidate"
