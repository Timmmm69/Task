# Validation report — Calendar Direction 2 1.0.0

## Scope

Production WPF week/day/month calendar, hour scale/current moment, deterministic overlap lanes, selection/inspector, week navigation/today, event editor, explicit loading/empty/offline/read-only/save/conflict/rollback states and compact viewport behavior. No domain, persistence, server endpoint or `sources/` change.

## Checks

| Check | Result | Evidence |
| --- | --- | --- |
| Release solution build | PASS, 0 warnings/errors | `dotnet build Task.sln --configuration Release --no-restore` |
| Desktop regression | PASS, 333/333 after the short-card lane and moving current-moment tests | `dotnet test tests/Task.Desktop.Tests/Task.Desktop.Tests.csproj --configuration Release --no-restore`; pre-existing analyzer warning xUnit1031 in unrelated `DesktopCredentialVaultTests.cs:347` during test compilation |
| Domain calendar/schedule | PASS, 232/232 | Targeted `Task.Tests` filter `Calendar|Schedule` |
| Service-host calendar | PASS, 33/33 | Targeted `Task.ServiceHosts.Tests` filter `Calendar` |
| Real critical E2E | PASS | `evidence/ui-assertions.json`, `qa03-gate.log`: PostgreSQL 16, HTTPS API, worker and Release WPF; overlap UIA, inspector, editor create/update, server-stop cache/read-only, reconnect and cleanup |
| Windows UIA/viewport matrix | PASS, 7 focused tests and 100/125/150/200%-equivalent calendar captures | `evidence/scaling/windows-ux.json` and `calendar-*.png`; native window DPI 144 |
| Direction 2 visual QA | PASS with documented differences | `design-qa.md` and three `comparison-calendar-*.png` |
| Dashboard order | PASS | `npm run dashboard:order`; only PROD-03 and QA-03 evidence updated |
| Integrity | PASS after manifest generation | `manifest.json`, `MANIFEST.sha256` |

## Behavioral evidence

- `calendar-week.png`: week grid, day headings, all-day rail, time scale, timed tasks/events.
- `calendar-overlap.png`: overlapping event buttons remain distinct; selected event inspector shows complete title/time/status/conflict.
- `calendar-editor.png`: canonical-form structure, editable event fields and enabled save action. The gate created and updated a real server event through this form.
- `calendar-offline.png`: confirmed week remains visible while new-event is disabled and read-only state is explicit.
- Unit tests cover timezone/DST conversion preservation, overlap lane assignment including minimum card heights, editor round-trip, conflict rebase, save failure rollback, and offline reactivation.

## Limits

Native 100–200% verification uses logical viewport equivalents on a 144-DPI host; it does not alter the OS display-scale setting. Calendar project/attendee fields remain identity-based under the preserved API contract; directory-backed named pickers and drag/resize are outside this package. No customer workstation acceptance is claimed.
