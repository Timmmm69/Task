# Employee Picker 5.1 — version 1.0.0

Reusable WPF control and form-local MVVM mechanism. No Project or Calendar integration in this increment.

## Verified server contract and integration blocker

- `UserEndpoints.MapUserEndpoints`: both `GET /api/v1/users` and `GET /api/v1/users/{id}` require `UserReadPolicyName`.
- `TaskPermissionAuthorization`: backing permission `user.read`, published capability `User.Read`. `Project.ManageMembers` and `Employee.Read` do not imply it.
- `OfflineAdministratorBootstrapper` creates `system_administrator` with all currently active permissions. Migration 012 copies grants **and denies** from `identity.account.manage` to `user.read`. Other configured roles can read only with an effective grant; explicit deny wins, organization and role validity checks still apply (`PostgresAuthorizationPolicyStore`, `PermissionDecisionService`). There is no role-name bypass. Deployed role assignments have not been enumerated against a live database.
- Thus a project manager/member editor with `Project.ManageMembers` but without `User.Read` cannot search, browse namesakes or resolve an existing assignee ID using this picker. This is a blocker for full role coverage in the later integration stages. No permission, role, endpoint, server DTO, schema or OpenAPI was changed. The broader organization directory must not be exposed merely because a user can edit a project.
- `PostgresUserAccountReadStore.GetPageAsync`: organization-scoped, active **object lifecycle**, `ILIKE` substring across `display_name`, login and work email. Job title is displayed but is not a search field. `%`/`_` retain the existing server wildcard semantics. The client URL-encodes the raw filter; it does not invent filter expressions.
- `UserAccountReadPageRequest`: default page size 100; the HTTP endpoint exposes no page-size argument. Stable ascending account ID, opaque `nextCursor` returned by the server. Picker requests one page at a time and keeps the filter unchanged for subsequent pages. No automatic all-pages traversal.
- User DTO: `id`, `organizationId`, `version`, `createdAt`, `updatedAt`, `displayName`, `firstName`, `lastName`, `login`, `workEmail`, `departmentId`, `jobTitle`, `accountStatus`. Picker reuses the desktop `DesktopAdminUser` projection: ID, version, display name, login, job title, status. No new server DTO. It neither stores nor logs email or other extra fields.
- The selected `Guid` is **iam.user_accounts.id / UserAccountId**, not `employee_profile_id` or contact ID. Preselection reads `/users/{id}` and rejects a mismatching returned ID.
- Selectable active account: `accountStatus == "active"`; known alternatives are `pending_activation`, `blocked`, `deactivated`. Active-only defaults to true. Unknown values fail closed. Object lifecycle alone does not mean the account is active. Employment status is not in this API projection and cannot be independently checked here. Later submit endpoints must revalidate current status and object-specific write permissions.

## Components and ownership

- `Administration/DesktopEmployeeDirectory.cs`: `IDesktopEmployeeDirectory`; implemented by the existing partial `DesktopAdministrationApiClient`, reusing its authenticated executor and session refresh. Only bounded search and get-by-ID methods are added. The administrative list methods remain unchanged.
- `ViewModels/EmployeePickerViewModel.cs`: debounced input (300 ms), generation/cancellation guards including non-cooperative stale replies, page deduplication by ID, cursor-cycle protection, candidate versus confirmed selection, preselection, optional clear, retry and safe states.
- `Views/EmployeePickerView.xaml[.cs]`: a hostable control using Task theme resources. No global singleton or extra UI library. The host owns the ViewModel lifetime and injects the interface, following the existing constructor/composition-root pattern.
- `tests/Task.Desktop.Tests/Employees/`: transport, ViewModel and native Windows tests. The native collection runs without other test collections to avoid stealing focus from unrelated WPF probes.

## Use from another ViewModel / composition root

```csharp
// Reuse the same server, HttpClient ownership, SessionService and connectivity as the host.
IDesktopEmployeeDirectory directory = new DesktopAdministrationApiClient(
    httpClient, serverEndpoint, sessionService, connectivity);
EmployeePicker = new EmployeePickerViewModel(directory,
    sessionService.CurrentSessionMetadata?.Capabilities,
    activeOnly: true, allowClear: true);

// Optional: await before showing the form, for an existing value.
await EmployeePicker.PreselectAsync(existingUserAccountId, cancellationToken);

// On accepted form submission, read the confirmed value; a typed name is never an ID.
Guid? userAccountId = EmployeePicker.SelectedUserAccountId;
// Required field: construct with allowClear:false AND validate non-null on form submission.
```

```xml
<!-- Host window loads Task Resources/Theme.xaml, as the application already does. -->
<views:EmployeePickerView DataContext="{Binding EmployeePicker}" />
<!-- xmlns:views="clr-namespace:Task.Desktop.Views" -->
```

The control starts a single page on load if the model is idle; preselection performs only a single-account read. Search text starts a debounced replacement search. Enter in search triggers it immediately; Down moves focus to the first result, standard ListBox arrows navigate, Enter in results confirms, Escape cancels the pending operation/candidate and restores search focus. Tab/Shift+Tab and access keys reach search, pagination, retry, confirm, clear and cancel. Cancellation preserves a previously confirmed value. Clear removes it only for optional fields. The host's form cancel/rollback remains the host's responsibility.

Forward **changes** in capabilities, session readiness and known network availability to `UpdateAccess(capabilities, sessionReady, online)` on the UI thread. It cancels work and removes names, results and selected ID; initialize/preselect again after an access/session transition when appropriate. A local missing capability prevents any request; the server remains authoritative and a 403/401 clears previously loaded identity data. Never share a picker across users, organizations or forms. `Unloaded` cancels in-flight work; the form must also `Dispose()` its model when permanently closed. All public model mutations belong on the WPF dispatcher.

Known offline state makes no request and displays Offline. A transport exception or 5xx displays Unavailable (a failed connection cannot reliably distinguish server outage from loss of network). 401 uses the shared executor's one refresh/replay; failed authentication displays a sign-in message. 404 means absent or not visible. Malformed responses and other errors show static safe feedback; response bodies and personal data are not logged. Retry repeats the failed preselection, page or current search. On failed next-page loads the earlier pages remain, with confirmation disabled until successful retry/search. Zero eligible rows can still have a next cursor, so “Ещё” remains available.

## Validation and next stage

```powershell
dotnet build work/production/Task.sln -c Release --nologo
dotnet test work/production/tests/Task.Desktop.Tests/Task.Desktop.Tests.csproj -c Release -- xUnit.ParallelizeTestCollections=false xUnit.MaxParallelThreads=1
dotnet test work/production/tests/Task.ServiceHosts.Tests/Task.ServiceHosts.Tests.csproj -c Release --filter 'FullyQualifiedName~UserEndpointsTests|FullyQualifiedName~CapabilitiesEndpointsTests|FullyQualifiedName~TaskPermissionAuthorizationTests'
```

Serial desktop execution is needed for deterministic focus tests in this repository; the existing parallel desktop run can produce Inbox/Task focus failures. xUnit's runner settings are documented at <https://xunit.net/docs/config-runsettings>. Native checks instantiate the real control in a shown WPF window, verify bindings, focus traversal, routed keyboard actions and UI Automation names, and render normal/narrow/forbidden frames. They do not constitute a physical-keyboard or screen-reader listening audit. New directory transport tests use authenticated HTTP fixtures; no live organization/large PostgreSQL catalog, real offline switching or production role assignment walkthrough was run.

Ready as a reusable component for the next bounded integration task for callers with `User.Read`. Universal project/calendar availability remains blocked by the permission contract described above; resolving it needs a separate approved product/security decision. See the versioned output package for actual test receipts, hashes and the validation report.
