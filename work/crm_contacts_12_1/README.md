# Task 12.1 — Corporate contact workspace

The corporate Contacts area now loads a complete contact card with GET /api/v1/contacts/{id} and edits the six existing writable fields with PATCH and a strong If-Match. A separate desktop projection carries the already contracted middleName; server DTOs, OpenAPI, database, permissions and the personal SQLite model are unchanged.

ContactCardViewModel owns loading, confirmed data, edit drafts, cancellation and request generations. AsyncCommand prevents double Save. Validation matches ContactPatch and migration 009 limits: first/last/middle 100, display 300, notes 20000, status active/inactive. Status is separate from lifecycle. Only a response with matching ID and a canonical strong ETag matching the body version can populate the card; PATCH confirmation must advance the version.

Errors retain the draft except authentication/scope loss, which clears protected data. 412 never retries or overwrites automatically; cancel editing and reload before another edit. Missing update capability, object-level 403, inactive lifecycle or offline state disable Save. Server authorization remains authoritative. A selection generation rejects late reads and saves even when the transport ignores cancellation.

The existing search/link navigation and cursor pagination are retained. A refreshed first page retains a selected contact from a later page. List replacement suppresses WPF's transient SelectedItem reset; the confirmed selection is restored afterward. ModeSwitchGuard includes the edit draft and in-flight Save. Personal mode never creates the corporate detail view model.

## Verification

Run from this worktree:

```powershell
dotnet build work/production/Task.sln -c Release
dotnet test work/production/tests/Task.Desktop.Tests/Task.Desktop.Tests.csproj -c Release --no-build --settings work/crm_contacts_12_1/desktop.runsettings
npm run dashboard:order
npm run dashboard:validate
```

Final counts, earlier failures and native screenshots are in outputs/20261018_crm_contacts_12.1.0/validation-report.md and its evidence directory. Native WPF tests show an actual Window and operate the ListBox, TextBox bindings and Button automation peers at 1100×1000 and 800×600; the HTTP server is substituted. This does not claim live corporate server/database acceptance or manual installed-client acceptance.

Normative checks: Task_Concept_Final.txt §16; architecture_organizer.md CRM boundaries; Stage 2.2 Contact/ContactCreate/ContactPatch and contact routes; Stage 3.4 SCR-110/111/112 and role matrix; Stage 4.1.1 CRM PRD. Later contact channels, addresses, companies, interactions and lifecycle actions are outside this task.

Task 12.2 can start from this implementation; its separate contract and acceptance scope still need to be followed. No claim is made that the whole CRM module is complete.
