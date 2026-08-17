# VALIDATION_REPORT — CalendarEvent application commands 0.1.0

Packet: TASK-PROD-MOD-009-CALENDAR-EVENT-APPLICATION-COMMANDS-0.1.0
Base: 42ccd2caffef50b04a5168baf19efbf0b9dd0352 (origin/main)
Date: 2026-08-17

## Delivered scope
- `work/production/src/Task.Application/Calendar/ICalendarEventStore.cs` —
  storage port for the CalendarEvent aggregate mirroring the approved
  `ITaskAggregateStore` pattern: `Get(eventId, organizationId)`,
  `Add(calendarEvent)` and `Save(calendarEvent, expectedVersion)`; the
  organization-scoped read and the expected-version guard are part of the
  port contract, and `Save` must atomically confirm the optimistic
  concurrency guarantee.
- `work/production/src/Task.Application/Calendar/CalendarEventConcurrencyException.cs`
  — optimistic concurrency signal with `EventId`, `ExpectedVersion` and
  `ActualVersion`, modeled on `TaskLifecycleConcurrencyException`.
- `work/production/src/Task.Application/Calendar/CalendarEventLifecycleService.cs`
  — command entry points for the full MOD-009 write path:
  `Create` (scalar and with attendee collections), `UpdateDetails`,
  `Cancel`, `Reschedule`, `ReplaceAttendees`, `Archive`,
  `RestoreFromArchive`, `MoveToTrash` and `RestoreFromTrash`. Every mutating
  operation loads the aggregate through the store, verifies the
  caller-supplied expected version against the stored one, delegates the
  transition to the aggregate (domain rules are never duplicated or
  bypassed) and persists the result with the original expected version.
- `work/production/tests/Task.Tests/Calendar/CalendarEventLifecycleServiceTests.cs`
  — 17 new facts covering create-add semantics, attendee create, all nine
  command operations, organization boundary, missing events, stale versions,
  domain-rule rejection without Save and the expected-version propagation to
  the store.
- Output package `outputs/20260817_task_calendar_event_application_commands_0.1.0/`.

## Verification results (actual)
| Check | Command / method | Result |
|---|---|---|
| Unit tests | `dotnet test work/production/tests/Task.Tests/Task.Tests.csproj --no-restore` | PASS 454/454 (0 failed, 0 skipped); previously 437 |
| Solution build | `dotnet build work/production/Task.sln --no-restore` | 0 errors, 0 warnings |
| Diff hygiene | `git diff --check` | clean |
| Scope | `git status --short` after implementation | only the three new Task.Application files, the new test file and the new output package |
| Manifest parse | `ConvertFrom-Json` | valid JSON |
| Hashes | canonical-LF SHA-256, UTF-8 without BOM, CRLF -> LF | every MANIFEST.sha256 entry and manifest.json artifactHash matches a fresh recomputation after the final rebase |
| Rebase | `git fetch origin --prune` + `git rebase origin/main` before push | no conflicts; pushed HEAD == origin/main |

## Command path coverage (actual)
- `Create` calls `Add` exactly once, returns a version-1 Active Scheduled
  event and never calls `Save`; the attendee-collection overload stores both
  collections.
- `UpdateDetails`, `Cancel`, `Reschedule`, `ReplaceAttendees`, `Archive`,
  `RestoreFromArchive`, `MoveToTrash` and `RestoreFromTrash` each delegate to
  the aggregate and call `Save` exactly once with the original expected
  version; a genuine change advances the version by exactly one.
- A no-op `UpdateDetails` returns the stored instance unchanged (same
  instance, no version bump) while still confirming the expected version
  through `Save`.
- Organization boundary: an event of another organization behaves exactly
  like a missing event — `KeyNotFoundException`, no `Save`.
- Stale expected version: `CalendarEventConcurrencyException` with correct
  fields and message, no `Save`, before any domain transition.
- Invalid state transitions (e.g. cancel a cancelled event, update an
  archived event) throw the aggregate's domain exceptions and never reach
  `Save`.

## Not delivered (explicitly out of scope)
- PostgreSQL persistence implementation of `ICalendarEventStore` (next
  slices: schema migration, then store).
- Calendar read/query projection and conflict projection.
- API endpoints, permissions, If-Match/idempotency, audit/outbox, sync and
  notifications.
- Attendee response workflow (`POST /api/v1/calendar-events/{id}/respond`
  with `AttendeeResponse.expectedAttendeeVersion`).
- Recurrence, worker, Desktop UI, authorization and deployment.
- Any change to `Task.Domain/**`, `Task.Infrastructure/**`, `Task.Api/**`,
  solution/project files, dependencies or `sources/**`.

## Hash rule
UTF-8 without BOM; CRLF normalized to LF before SHA-256. MANIFEST.sha256 is
not recursively hashed. All hashes verified after the final rebase.

## Revision history
- 2026-08-17: initial delivery, base 42ccd2caffef50b04a5168baf19efbf0b9dd0352.
