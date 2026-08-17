# VALIDATION_REPORT — CalendarEvent read model 0.1.0

Packet: TASK-PROD-MOD-009-CALENDAR-EVENT-READ-MODEL-0.1.0
Base: 09d726e11dbe5a794620b98158376e81f92962bb (origin/main)
Date: 2026-08-17

## Delivered scope
- `work/production/src/Task.Application/Calendar/CalendarEventDetails.cs` —
  immutable read-only projection of a single calendar event (OpenAPI
  `CalendarEvent` shape): identity and organization, project reference,
  title, description, flattened timing fields (`eventDate`, `isAllDay`,
  `startAtUtc`, `endAtUtc`, `timeZone`), status, lifecycle state, version,
  creation/update timestamps and both attendee collections in stored order.
- `work/production/src/Task.Application/Calendar/CalendarEventQueryService.cs`
  — read-only application service that loads through
  `ICalendarEventStore.Get(eventId, organizationId)` and projects into
  `CalendarEventDetails`; never mutates the aggregate and never applies
  lifecycle transitions (mirrors the approved `TaskQueryService` pattern).
  A missing event or an event of another organization returns `null`.
- `work/production/tests/Task.Tests/Calendar/CalendarEventQueryServiceTests.cs`
  — 7 new facts: full scalar/timing projection, attendee order projection,
  all-day event shape, cancelled/trashed state projection, missing event,
  organization boundary and read-only behavior (repeated reads do not change
  the aggregate).
- Output package `outputs/20260817_task_calendar_event_read_model_0.1.0/`.

## Verification results (actual)
| Check | Command / method | Result |
|---|---|---|
| Unit tests | `dotnet test work/production/tests/Task.Tests/Task.Tests.csproj --no-restore` | PASS 461/461 (0 failed, 0 skipped); previously 454 |
| Solution build | `dotnet build work/production/Task.sln --no-restore` | 0 errors, 0 warnings |
| Diff hygiene | `git diff --check` | clean |
| Scope | `git status --short` after implementation | only the two new Task.Application files, the new test file and the new output package |
| Manifest parse | `ConvertFrom-Json` | valid JSON |
| Hashes | canonical-LF SHA-256, UTF-8 without BOM, CRLF -> LF | every MANIFEST.sha256 entry and manifest.json artifactHash matches a fresh recomputation after the final rebase |
| Rebase | `git fetch origin --prune` + `git rebase origin/main` before push | no conflicts; pushed HEAD == origin/main |

## Read projection coverage (actual)
- Every `CalendarEvent` scalar and timing field is projected with its stored
  value; attendee collections keep their stored order.
- All-day events project `null` instants and the date-only `eventDate`;
  timed events project both UTC instants and the time-zone identifier.
- Cancelled and trashed events project their stored status and lifecycle
  state with the current version.
- Missing events and events of another organization project `null`.
- Repeated queries leave the stored aggregate untouched (no version advance,
  no status change).

## Not delivered (explicitly out of scope)
- PostgreSQL persistence implementation of `ICalendarEventStore` (next
  slices: schema migration, then store).
- Window/range queries and the unified calendar/conflict read model
  (`SchedulePage`, `ScheduleConflict`).
- API endpoints, permissions, If-Match/idempotency, audit/outbox, sync and
  notifications.
- Attendee response workflow (`/respond` with `expectedAttendeeVersion`).
- Recurrence, worker, Desktop UI, authorization and deployment.
- Any change to `Task.Domain/**`, `Task.Infrastructure/**`, `Task.Api/**`,
  existing `Task.Application` files, solution/project files, dependencies or
  `sources/**`.

## Hash rule
UTF-8 without BOM; CRLF normalized to LF before SHA-256. MANIFEST.sha256 is
not recursively hashed. All hashes verified after the final rebase.

## Revision history
- 2026-08-17: initial delivery, base 09d726e11dbe5a794620b98158376e81f92962bb.
