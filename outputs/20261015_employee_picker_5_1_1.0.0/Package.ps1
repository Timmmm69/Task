param([switch]$Refresh)
$ErrorActionPreference = 'Stop'
$repo = Join-Path $PSScriptRoot 'repo'
Set-Location -LiteralPath $repo
$base = '5465c4efee3f7aa815be38fcd4ed33b867e85113'
$sourceCommit = (git log -1 --format=%H -- work/production).Trim()
$branch = (git branch --show-current).Trim()
if ($branch -ne 'feature/employee-picker') { throw 'Unexpected branch' }
$evidence = Join-Path $repo 'work/employee-picker-evidence'
[xml]$desktop = Get-Content -LiteralPath (Join-Path $evidence 'desktop-serial.trx') -Raw
[xml]$server = Get-Content -LiteralPath (Join-Path $evidence 'user-contract.trx') -Raw
$desktopCounters = $desktop.TestRun.ResultSummary.Counters
$serverCounters = $server.TestRun.ResultSummary.Counters
$scoped = @($desktop.TestRun.Results.UnitTestResult | Where-Object testName -like 'Task.Desktop.Tests.Employees.*')
if ($desktopCounters.failed -ne '0' -or $desktopCounters.passed -ne '901' -or $scoped.Count -ne 42 -or $serverCounters.passed -ne '13' -or $serverCounters.failed -ne '0') { throw 'Final test receipts do not match expected passing gate' }
$name = '20261015_employee_picker_5_1_1.0.0'
$package = Join-Path $repo ('outputs/' + $name)
if ((Test-Path -LiteralPath $package) -and -not $Refresh) { throw 'Output already exists; do not overwrite versioned evidence' }
New-Item -ItemType Directory -Force -Path $package | Out-Null
[IO.File]::WriteAllText((Join-Path $package '.gitattributes'), "* -text whitespace=cr-at-eol`n")
$files = @(git diff --name-only $base $sourceCommit -- work/production)
if ($files.Count -ne 9) { throw 'Unexpected source scope' }
foreach ($relative in $files) {
    $target = Join-Path $package ('source/' + $relative)
    New-Item -ItemType Directory -Force -Path (Split-Path $target) | Out-Null
    Copy-Item -LiteralPath (Join-Path $repo $relative) -Destination $target
}
foreach ($receipt in @('desktop-serial.trx','user-contract.trx','build.log','desktop-regression.trx','employee-picker-ready.png','employee-picker-narrow.png','employee-picker-forbidden.png')) {
    Copy-Item -LiteralPath (Join-Path $evidence $receipt) -Destination $package
    if ($receipt.EndsWith('.trx')) {
        $receiptPath = Join-Path $package $receipt
        $text = [IO.File]::ReadAllText($receiptPath)
        [IO.File]::WriteAllText($receiptPath, [regex]::Replace($text, '[ \t]+(?=\r?$)', '', [Text.RegularExpressions.RegexOptions]::Multiline))
    }
}
Copy-Item -LiteralPath (Join-Path $repo 'work/production/docs/EMPLOYEE-PICKER-5.1.md') -Destination (Join-Path $package 'usage.md')
Copy-Item -LiteralPath $PSCommandPath -Destination (Join-Path $package 'Package.ps1')
[IO.File]::WriteAllText((Join-Path $package 'VERSION'), "1.0.0`n")
$report = @"
# Employee Picker — Stage 5.1 validation report

Version: 1.0.0. Date: 2026-10-15 (Europe/Minsk).
Base: origin/main $base. Source commit: $sourceCommit. Branch: $branch.
Previous four fixes are already on origin/main. Implementation is in an isolated checkout under work/employee_picker_5_1/repo. The original dirty checkout was not switched, stashed, reset or overwritten. No push or main integration performed.

## Delivered

Reusable EmployeePickerViewModel, EmployeePickerView and IDesktopEmployeeDirectory, implemented by the existing DesktopAdministrationApiClient. Minimal partial-class extension reuses session/authentication and DesktopAdminUser. Existing administration list path and behavior preserved. No server/DB/OpenAPI/roles/permissions changes, no contacts, Project or Calendar integration.

Changed production/documentation/test files (relative to checkout):

$(($files | ForEach-Object { '- ' + $_ }) -join "`n")

Dashboard: only DESK-03 evidence, note and update date; progress/status preserved. npm run dashboard:order executed. The original checkout's uncommitted roadmap was not touched.

## Contract and security

Both GET /api/v1/users and GET /api/v1/users/{id} require User.Read (backing user.read). Bootstrap system_administrator has all active grants; migration 012 carries grants and denies from identity.account.manage. Other roles depend on effective assignments; deny wins. Deployed role assignments were not queried. Project.ManageMembers and Employee.Read do not guarantee User.Read.

**INTEGRATION BLOCKER:** project managers without User.Read cannot browse/search/resolve employees through these endpoints. Universal availability is not implemented or claimed; a separately approved contract/permission decision is needed for full role coverage.

The server performs organization-scoped ILIKE search over display name, login and work email, stable account-ID ordering and opaque cursor paging, 100 rows/page. Picker holds only its requested pages, deduplicates by UserAccountId and stops repeated/cyclic cursors. It returns iam.user_accounts.id, not employee_profile_id. It displays name/login/job title, consumes only the existing safe desktop projection and adds no logging of identities/query/response bodies.

Active-only defaults to accountStatus=active; pending_activation, blocked, deactivated and unknown statuses cannot be selected in that mode. Server object lifecycle is also active; employment status is not available in this DTO. Final write endpoints still own current account and object permission validation.

## Acceptance evidence

| Criterion | Evidence | Result |
|---|---|---|
| AC-1 search | authenticated server-filter request, native bound search, no local full-directory scan | PASS |
| AC-2 namesakes | same display name, distinct visible login and account ID; job title displayed | PASS |
| AC-3 Guid selection | candidate/confirm separation, Guid result, no UUID entry, preselect/clear | PASS |
| AC-4 forbidden | no HTTP calls without capability; 403/401 clear names, counts and selection; late revoked replies ignored | PASS |
| AC-5 stale reply | cancellation plus generation check, non-cooperative transport, debounce, disposal | PASS |
| AC-6 unavailable | network exception/5xx/static error, known offline no request, retry of same operation | PASS |
| AC-7 pagination | explicit next page, duplicate IDs, inactive-only page, cycle and repeated-cursor protection | PASS |
| AC-8 keyboard | shown WPF control, focus traversal, Down/Enter/Escape routed keys, labels and UIA names, normal/narrow bounds | PASS (automated native) |

## Executed checks

- Release build of work/production/Task.sln: PASS, 0 errors. Two existing test-analyzer warnings (DesktopCredentialVaultTests xUnit1031, PersonalPlanningTests xUnit2031) in final incremental receipt. Earlier first solution build also reported existing ASP.NET deprecations in service-host tests.
- Desktop Release regression: **901 passed / 0 failed / 0 skipped**, sequential test collections. Includes **42 Employee Picker tests** (41 prior cases plus preselection retry/access-revocation regression).
- UserEndpoints, CapabilitiesEndpoints and permission authorization server tests: **13 passed / 0 failed / 0 skipped**.
- Native control test uses its own process and WPF Application. Normal 480-DIP and narrow 360-DIP host widths, search/selection/cancel/clear, UIA names and focus checked. PNGs are WPF RenderTargetBitmap captures, not physical desktop screenshots.
- Initial parallel full-suite run: existing InboxZeroUiTests and TaskContextActionsUiTests focus assertions failed (898/900 passed). Retained desktop-regression.trx is this exploratory failure receipt. The sequential final desktop-serial.trx is the final successful gate. New native collection is isolated from other collections; repository-wide serial run avoids other native probes competing for focus.
- git diff --check and exact scoped file review: PASS. No sources/ changes. Packaged TRX receipts only trim trailing horizontal whitespace; result/error content is preserved. Package .gitattributes disables line-ending conversion so hashes survive a Git checkout.

## Limits and next step

No live PostgreSQL/large catalog test of the new client, physical network toggle, production role walkthrough, physical keyboard or screen-reader listening audit was executed. HTTP directory tests use authenticated fixtures; endpoint authorization tests run the existing server test harness. Offline versus unavailable is explicit: host-known offline prevents HTTP, transport failures are unavailable because physical connectivity cannot be inferred reliably.

The component is ready for a separately scoped Project integration for callers with User.Read. Broad role integration remains blocked. Calendar integration remains a subsequent task. Usage/lifetime/XAML instructions are in usage.md. The host must forward session/capability/network changes on the UI thread and dispose the ViewModel with the form; required fields still need non-null form validation.

## Package integrity

manifest.json lists SHA-256 and size for every payload file; SHA256SUMS includes that manifest. The adjacent ZIP SHA-256 and validation JSON record actual verification of ZIP contents against the manifest. Versioned outputs are source/evidence artifacts, not a new portable client release.
"@
[IO.File]::WriteAllText((Join-Path $package 'validation-report.md'), $report)
$entries = @(Get-ChildItem -LiteralPath $package -File -Recurse | Where-Object { $_.FullName -ne (Join-Path $package 'manifest.json') -and $_.FullName -ne (Join-Path $package 'SHA256SUMS') } | Sort-Object FullName | ForEach-Object {
    [ordered]@{ path = [IO.Path]::GetRelativePath($package, $_.FullName).Replace('\','/'); size = $_.Length; sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() }
})
$manifest = [ordered]@{ version='1.0.0'; date='2026-10-15'; base=$base; sourceCommit=$sourceCommit; branch=$branch; files=$entries }
[IO.File]::WriteAllText((Join-Path $package 'manifest.json'), ($manifest | ConvertTo-Json -Depth 10))
$sums = @(Get-ChildItem -LiteralPath $package -File -Recurse | Where-Object { $_.FullName -ne (Join-Path $package 'SHA256SUMS') } | Sort-Object FullName | ForEach-Object {
    (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + [IO.Path]::GetRelativePath($package, $_.FullName).Replace('\','/')
})
[IO.File]::WriteAllText((Join-Path $package 'SHA256SUMS'), ($sums -join "`n") + "`n")
$zip = $package + '.zip'
Compress-Archive -Path (Join-Path $package '*') -DestinationPath $zip -CompressionLevel Optimal -Force
$archive = [IO.Compression.ZipFile]::OpenRead($zip)
try {
    foreach ($entry in $entries) {
        $member = $archive.GetEntry($entry.path)
        if ($null -eq $member -or $member.Length -ne $entry.size) { throw ('Missing/incorrect ZIP member: ' + $entry.path) }
        $stream = $member.Open()
        try { $hash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream)).ToLowerInvariant() } finally { $stream.Dispose() }
        if ($hash -ne $entry.sha256) { throw ('ZIP hash mismatch: ' + $entry.path) }
    }
} finally { $archive.Dispose() }
$zipHash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText(($zip + '.sha256'), ($zipHash + '  ' + [IO.Path]::GetFileName($zip) + "`n"))
$validation = [ordered]@{ version='1.0.0'; sourceCommit=$sourceCommit; zipSha256=$zipHash; zipPayloadHashesVerified=$entries.Count; desktopPassed=901; employeePickerPassed=42; serverContractPassed=13; failed=0; skipped=0; universalRoleCoverage='BLOCKED'; projectCalendarIntegration='OUT_OF_SCOPE' }
[IO.File]::WriteAllText(($zip + '.validation.json'), ($validation | ConvertTo-Json))
Write-Output ('Verified ZIP payloads: ' + $entries.Count)
Write-Output ('ZIP SHA-256: ' + $zipHash)
