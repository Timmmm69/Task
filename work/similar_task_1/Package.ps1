$ErrorActionPreference = 'Stop'
$root = (Resolve-Path "$PSScriptRoot/../..").Path
$output = Join-Path $root 'outputs/20261008_task_similar_create_1.0.0'
[IO.Directory]::CreateDirectory($output) | Out-Null
[IO.File]::WriteAllText("$output/.gitattributes", "** -text -diff`n")
$files = @(
 'work/production/src/Task.Desktop/MainWindow.xaml',
 'work/production/src/Task.Desktop/ViewModels/TasksViewModel.cs',
 'work/production/src/Task.Desktop/ViewModels/TaskEditorViewModel.cs',
 'work/production/src/Task.Infrastructure/Persistence/PostgresProductApiTaskWorkspace.cs',
 'work/production/tests/Task.Desktop.Tests/Tasks/SimilarTaskTests.cs',
 'work/production/tests/Task.Desktop.Tests/Tasks/DesktopTasksApiClientTests.cs',
 'work/production/tests/Task.Desktop.Tests/Tasks/TasksViewModelTests.cs',
 'work/production/tests/Task.Desktop.Tests/WindowsUxAccessibilityTests.cs',
 'work/production/tests/Task.Tests/PostgresTaskCardWorkflowTests.cs',
 'work/production/docs/task-similar-create.md'
)
$inventory = @()
foreach ($relative in $files) {
    $source = Join-Path $root $relative
    $target = Join-Path $output "source/$relative"
    [IO.Directory]::CreateDirectory((Split-Path -Parent $target)) | Out-Null
    Copy-Item -LiteralPath $source -Destination $target
    $inventory += @{ path = $relative; sha256 = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash.ToLowerInvariant() }
}
$inventory | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath "$output/source-inventory.json" -Encoding UTF8
'1.0.0' | Set-Content -LiteralPath "$output/VERSION" -Encoding UTF8
$evidence = Join-Path $output 'evidence'
[IO.Directory]::CreateDirectory($evidence) | Out-Null
foreach ($name in @('build.log','tests.log','boundaries.log')) { Copy-Item -LiteralPath "$PSScriptRoot/database-gate/$name" -Destination $evidence }
Get-ChildItem -LiteralPath "$PSScriptRoot/database-gate" -Filter '*.trx' | Sort-Object LastWriteTime -Descending | Select-Object -First 3 | ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination $evidence }
$base = (& git -C $root rev-parse HEAD).Trim()
@"
# Validation report — Создать похожую задачу 1.0.0

Date: 2026-10-08 (Europe/Minsk). Base commit: $base. Source changes are local; no commit/push was performed.

- Release solution build: PASS.
- Full solution with disposable native PostgreSQL 16 on loopback: **2002 PASS, 0 FAIL, 0 SKIP** (Domain/Application/Infrastructure 825; Desktop 587; ServiceHosts 590).
- New feature coverage: safe allowlist, cleared schedule/time/due/parent/requester/recurrence identity, retained duration, completed/cancelled source, capability gating, re-read source access, inactive/unverified relations, project effective grant/deny and archived project, ordinary POST payload, no aggregate children writes, offline/revoked Task.Create, 403/422/503, idempotent retry and double Save.
- Archived/trashed source: actual PostgreSQL read boundary verified. Current Task GET only exposes active lifecycle; no broader source access or source restore was introduced.
- Keyboard/accessibility: automated XAML command/visibility/automation labels/tab-stop contracts and existing title focus/navigation checked. **Native keyboard/screen-reader walkthrough was not run.**
- Project boundaries: PASS. Diff/allowlist self-review and git diff --check: PASS.
- API contract review: no new DTO, endpoint, query parameter, response field, migration or dependency. Existing C# create adapter and TaskCreateModel remain unchanged. Existing options filter now evaluates effective task.create for creation-capable callers.
- Generated Task/TaskCreate, OpenAPI and DTO field catalog inspected. No alternative duplicate mapper was present; one allowlist factory seeds the existing editor.
- Dashboard: only PROD-01 evidence/note updated; existing user edits preserved. dashboard:order and dashboard:validate PASS; product progress was not raised.

Relations outside the first 200 options are conservatively cleared with an explanation; users can select them through existing search. Failed options reads never retain unverified references. Save still validates current server state and never bypasses validation.

Package includes scoped source snapshots and final gate evidence. It is a code-change package, not a rebuilt customer release. See source-inventory.json for changed files and manifest.json / SHA256SUMS for integrity.
"@ | Set-Content -LiteralPath "$output/VALIDATION_REPORT.md" -Encoding UTF8
$payload = Get-ChildItem -LiteralPath $output -Recurse -File | Where-Object Name -notin @('manifest.json','SHA256SUMS') | ForEach-Object {
    @{ path = $_.FullName.Substring($output.Length + 1).Replace('\','/'); sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(); bytes = $_.Length }
}
@{ version='1.0.0'; feature='Create similar task'; baseCommit=$base; status='local source verified'; files=@($payload) } | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath "$output/manifest.json" -Encoding UTF8
Get-ChildItem -LiteralPath $output -Recurse -File | Where-Object Name -ne 'SHA256SUMS' | ForEach-Object {
    '{0}  {1}' -f (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $_.FullName.Substring($output.Length + 1).Replace('\','/')
} | Set-Content -LiteralPath "$output/SHA256SUMS" -Encoding UTF8
$manifest = Get-Content -LiteralPath "$output/manifest.json" -Raw | ConvertFrom-Json
foreach ($item in $manifest.files) {
    if ((Get-FileHash -LiteralPath (Join-Path $output $item.path) -Algorithm SHA256).Hash.ToLowerInvariant() -ne $item.sha256) { throw "Hash mismatch: $($item.path)" }
}
$before = Get-Content -LiteralPath "$PSScriptRoot/roadmap-before.json" -Raw | ConvertFrom-Json
$after = Get-Content -LiteralPath "$root/.project-dashboard/roadmap.json" -Raw | ConvertFrom-Json
foreach ($item in $before.items | Where-Object id -ne 'PROD-01') {
    $current = $after.items | Where-Object id -eq $item.id
    if (($item | ConvertTo-Json -Depth 20 -Compress) -ne ($current | ConvertTo-Json -Depth 20 -Compress)) { throw "Unrelated roadmap entry changed: $($item.id)" }
}
Write-Output "Package verified: $output ($($manifest.files.Count) payload files). Only PROD-01 changed in the existing working dashboard."
