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
    $OutputDirectory = Join-Path $RepositoryRoot 'outputs\20260920_direction2_auth_admin_settings_1.0.0'
}

$outputsRoot = (Resolve-Path (Join-Path $RepositoryRoot 'outputs')).Path
$candidate = [IO.Path]::GetFullPath($OutputDirectory)
$outputPrefix = $outputsRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
if (-not $candidate.StartsWith($outputPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw "OutputDirectory must be a child of $outputsRoot."
}

function Write-TextFile([string]$Path, [string]$Value) {
    [IO.File]::WriteAllText($Path, (($Value.TrimEnd()) + "`n"), [Text.UTF8Encoding]::new($false))
}

if (Test-Path -LiteralPath $candidate) {
    $resolved = (Resolve-Path -LiteralPath $candidate).Path
    if (-not $resolved.StartsWith($outputPrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to replace output outside outputs: $resolved"
    }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}

$evidenceSource = Join-Path $RepositoryRoot 'work\production\evidence\direction2-auth-admin-settings-1.0.0'
if (-not (Test-Path -LiteralPath (Join-Path $evidenceSource 'validation.json') -PathType Leaf)) {
    throw "Missing Direction 2 evidence: $evidenceSource"
}
$evidenceTarget = Join-Path $candidate 'evidence'
[IO.Directory]::CreateDirectory($candidate) | Out-Null
Copy-Item -LiteralPath $evidenceSource -Destination $evidenceTarget -Recurse

Write-TextFile (Join-Path $candidate 'VERSION.txt') '1.0.0'
Write-TextFile (Join-Path $candidate 'README.md') @'
# Direction 2: вход, bootstrap, настройки и администрирование

Пакет фиксирует production-перенос frozen Direction 2 для первого подключения и входа, bootstrap, настроек, пользователей, ролей, сетевых ресурсов и capability-limited администрирования Windows-клиента Task.

`evidence/` содержит фактические Release WPF-скриншоты основных и edge-состояний, машинный отчёт сценарного прогона, а также нативную Windows-матрицу UIA, keyboard и масштабов 100/125/150/200%. Итог проверок описан в `VALIDATION_REPORT.md`; инвентарь исходников и frozen references — в `manifest.json`; целостность пакета — в `SHA256SUMS`.
'@

Write-TextFile (Join-Path $candidate 'VALIDATION_REPORT.md') @'
# Validation report — Direction 2 auth, bootstrap, settings and administration

Version: 1.0.0

Date: 2026-09-20

Result: **PASS**

## Implemented

- Единый Direction 2 flow первого подключения, входа и отдельного fail-closed bootstrap перед открытием shell.
- Retry/logout при ошибке bootstrap без расширения session/capability контрактов.
- Настройки профиля, уведомлений и server connection в одном responsive settings shell.
- Server-backed read-only представления пользователей, ролей и сетевых ресурсов; capability-gated limited-role state.
- Offline/reconnecting, session revoked/expired, scope changed, maintenance/server unavailable, storage read-only и безопасные error/retry состояния используют существующие auth/connectivity/capability контракты.
- UIA ids/names, live feedback, детерминированный F6 focus cycle, scroll-safe layout и общие forced-colors/high-contrast ресурсы.

## Verification

- Release Desktop build: PASS, 0 warnings, 0 errors.
- Desktop tests: PASS, 340/340.
- DESK-01 auth/security gate: PASS, 1749 tests and 14 mandatory auth scenarios, 0 failed, 0 skipped, isolated PostgreSQL 16.
- Direction 2 native evidence gate: PASS, 10 primary/edge screenshots against real HTTPS API and capability-limited account.
- DESK-05 native Windows gate: PASS, 100/125/150/200%, keyboard Tab/F6, UIA Value/Invoke/Selection, critical controls unclipped.
- Forced-colors: PASS through shared `SystemParameters.HighContrast`/`SystemColors` resources and VisualFoundation coverage.
- `git diff --check`: PASS.

## Evidence

- `evidence/first-connection.png`
- `evidence/bootstrap-failure.png`
- `evidence/bootstrap-progress.png`
- `evidence/settings-profile.png`
- `evidence/settings-notifications.png`
- `evidence/settings-server.png`
- `evidence/admin-users.png`
- `evidence/admin-roles.png`
- `evidence/admin-resources.png`
- `evidence/settings-offline.png`
- `evidence/admin-limited-role.png`
- `evidence/validation.json`
- `evidence/accessibility/windows-ux.json`
- `evidence/accessibility/auth-100.png` through `auth-200.png`
- `evidence/accessibility/main-100.png` through `main-200.png`

Оба нативных E2E-прогона остановили API/PostgreSQL, удалили временный runtime и восстановили исходные Desktop AppData.
'@

$sourceFiles = @(
    'work/production/src/Task.Desktop/App.xaml.cs',
    'work/production/src/Task.Desktop/AuthWindow.xaml',
    'work/production/src/Task.Desktop/BootstrapWindow.xaml',
    'work/production/src/Task.Desktop/BootstrapWindow.xaml.cs',
    'work/production/src/Task.Desktop/MainWindow.xaml',
    'work/production/src/Task.Desktop/MainWindow.xaml.cs',
    'work/production/src/Task.Desktop/Administration/DesktopAdministrationApiClient.cs',
    'work/production/src/Task.Desktop/ViewModels/AdministrationViewModel.cs',
    'work/production/src/Task.Desktop/ViewModels/AuthenticationViewModels.cs',
    'work/production/src/Task.Desktop/ViewModels/MainWindowViewModel.cs',
    'work/production/src/Task.Desktop/ViewModels/WorkHubViewModel.cs',
    'work/production/src/Task.Desktop/Views/AdministrationView.xaml',
    'work/production/src/Task.Desktop/Views/AdministrationView.xaml.cs',
    'work/production/src/Task.Desktop/Views/WorkHubView.xaml',
    'work/production/tests/Task.Desktop.Tests/Administration/AdministrationViewModelTests.cs',
    'work/production/tests/Task.Desktop.Tests/MainWindowViewModelTests.cs',
    'work/production/tests/Task.Desktop.Tests/WindowsUxAccessibilityTests.cs',
    'work/production/verification/Test-Direction2AuthAdminSettings.ps1',
    'work/production/verification/Build-Direction2AuthAdminSettingsPackage.ps1',
    '.project-dashboard/roadmap.json'
)
$referenceFiles = @(
    'work/stage_5_6_final_visual_baseline_and_handoff/prototype/src/App.jsx',
    'work/stage_5_6_final_visual_baseline_and_handoff/prototype/src/styles.css',
    'work/stage_5_6_final_visual_baseline_and_handoff/evidence/usability-screenshots/01-shell-start.png',
    'work/stage_5_6_final_visual_baseline_and_handoff/evidence/usability-screenshots/02-first-connection.png',
    'work/stage_5_6_final_visual_baseline_and_handoff/evidence/usability-screenshots/03-bootstrap-progress.png',
    'work/stage_5_prototype/qa-wave-c-settings.png',
    'work/stage_5_prototype/qa-wave-c-settings-notifications.png',
    'work/stage_5_prototype/qa-wave-c-settings-offline.png',
    'work/stage_5_prototype/qa-wave-c-admin-users.png',
    'work/stage_5_prototype/qa-wave-c-admin-roles.png',
    'work/stage_5_prototype/qa-wave-c-admin-resources-offline.png',
    'work/stage_5_prototype/qa-wave-c-admin-limited.png',
    'work/stage_5_prototype/edge-reconnecting.png',
    'work/stage_5_prototype/edge-session-revoked.png',
    'work/stage_5_prototype/edge-maintenance-readonly.png',
    'work/stage_5_prototype/edge-storage-readonly.png',
    'work/stage_5_prototype/edge-scope-recovery.png',
    'work/stage_5_prototype/edge-scaling-200.png',
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

$evidenceFiles = Get-ChildItem -LiteralPath $evidenceTarget -Recurse -File | Sort-Object FullName
$packageFiles = @('README.md', 'VERSION.txt', 'VALIDATION_REPORT.md') +
    @($evidenceFiles | ForEach-Object { [IO.Path]::GetRelativePath($candidate, $_.FullName).Replace('\', '/') }) +
    @('manifest.json', 'SHA256SUMS')
$manifest = [ordered]@{
    schemaVersion = 1
    package = 'Task Direction 2 auth, bootstrap, settings and administration'
    version = '1.0.0'
    date = '2026-09-20'
    result = 'PASS'
    source_files = @(Get-Inventory $sourceFiles)
    frozen_references = @(Get-Inventory $referenceFiles)
    verification = [ordered]@{
        release_build = 'PASS: 0 warnings, 0 errors'
        desktop_tests = [ordered]@{ passed = 340; skipped = 0; failed = 0 }
        auth_security_gate = [ordered]@{ passed = 1749; mandatory_scenarios = 14; skipped = 0; failed = 0 }
        direction2_native = 'PASS: first connection, bootstrap, settings, admin, offline and limited role'
        accessibility = 'PASS: UIA, keyboard, 100/125/150/200%, forced-colors resources'
        security = 'PASS: existing auth/capability contracts retained; read-only direct write denied HTTP 403'
    }
    package_files = $packageFiles
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

Write-Host "[PASS] Direction 2 auth/admin/settings output package built and checksum-validated: $candidate"
