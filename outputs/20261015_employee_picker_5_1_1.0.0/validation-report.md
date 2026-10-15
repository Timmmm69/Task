# Employee Picker — Stage 5.1 validation report

Version: 1.0.0. Date: 2026-10-15 (Europe/Minsk).
Base: origin/main 5465c4efee3f7aa815be38fcd4ed33b867e85113. Source commit: 9a1bddc21811ee1abda3de484250b5b68cd562a6. Branch: feature/employee-picker.
Previous four fixes are already on origin/main. Implementation is in an isolated checkout under work/employee_picker_5_1/repo. The original dirty checkout was not switched, stashed, reset or overwritten. No push or main integration performed.

## Delivered

Reusable EmployeePickerViewModel, EmployeePickerView and IDesktopEmployeeDirectory, implemented by the existing DesktopAdministrationApiClient. Minimal partial-class extension reuses session/authentication and DesktopAdminUser. Existing administration list path and behavior preserved. No server/DB/OpenAPI/roles/permissions changes, no contacts, Project or Calendar integration.

Changed production/documentation/test files (relative to checkout):

- work/production/docs/EMPLOYEE-PICKER-5.1.md
- work/production/src/Task.Desktop/Administration/DesktopAdministrationApiClient.cs
- work/production/src/Task.Desktop/Administration/DesktopEmployeeDirectory.cs
- work/production/src/Task.Desktop/ViewModels/EmployeePickerViewModel.cs
- work/production/src/Task.Desktop/Views/EmployeePickerView.xaml
- work/production/src/Task.Desktop/Views/EmployeePickerView.xaml.cs
- work/production/tests/Task.Desktop.Tests/Employees/EmployeeDirectoryClientTests.cs
- work/production/tests/Task.Desktop.Tests/Employees/EmployeePickerTests.cs
- work/production/tests/Task.Desktop.Tests/Employees/EmployeePickerWindowsTests.cs

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