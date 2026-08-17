# VALIDATION_REPORT — Calendar store and schedule read model 0.1.0

Packet: TASK-PROD-MOD-009-CALENDAR-STORE-SCHEDULE-0.1.0
Base: 9002d13 (origin/main)
Head: f244b72 (origin/main), both commits reviewed after push
Date: 2026-08-17

## Delivered scope
Two commits on `main`, reviewed and verified after push:

- `75ef2d4` — `PostgresCalendarEventStore` with PostgreSQL integration tests.
- `f244b72` — unified calendar schedule read model with conflict detection.

### `PostgresCalendarEventStore.cs` (commit 75ef2d4)
- `ICalendarEventStore` persistence on the existing `calendar` schema
  (migration 003, unchanged): `Get` joins `core.objects` with
  `calendar.events` and reads both attendee tables ordered by `position`,
  scoped by `organization_id` and `object_type = 'calendar_event'`.
- `Add` enforces the initial version-1 aggregate state and writes
  `core.objects`, `calendar.events` and the attendee rows in one
  transaction (attendee order preserved via `position`).
- `Save` performs an atomic CTE update guarded by
  `version = expectedVersion`; on conflict it throws
  `CalendarEventConcurrencyException` (with the actual stored version) and
  `KeyNotFoundException` when the object is missing; attendee collections
  are replaced (DELETE + INSERT in the same transaction).
- Hydration uses `CalendarEvent.Reconstitute` with `CalendarEventTiming.Create`,
  string-to-enum parsing mirrors the schema CHECK values, bigint version is
  range-checked before conversion to int.
- Registered via `TaskPersistenceRuntime.CreateCalendarEventStore()` and
  DI in `Program.cs` (`ICalendarEventStore` + `CalendarEventLifecycleService`).

### Schedule read model (commit f244b72)
- `IScheduleStore` + `ScheduleItemRow`: raw union of active tasks and
  calendar events intersecting `[fromUtc, toUtc)`; optional `users` /
  `projects` (events only) and `status` (both tables) filters.
- `ScheduleContracts`: `ScheduleItem`, `SchedulePage` (cursor pagination
  deliberately out of scope, `NextCursor` always null), `ScheduleConflict`
  with half-open overlap interval and severity (>= 30 minutes -> Blocking,
  shorter -> Warning; Info never produced).
- `ScheduleQueryService`: exact window semantics (all-day events use day
  boundaries in the event time zone, point tasks must fall inside the
  window), task local dates resolved in the requested time zone, page
  sorted by `(intervalStart, itemType, objectId)`, conflicts computed only
  for positive-duration items with `excludeObjectId` support; range
  validation (UTC offsets, non-empty window, at most 366 days).
- `PostgresScheduleStore`: two read-only queries; the coarse all-day date
  pre-filter (`day-1/+1`) is intentionally widened — the service applies
  the precise rule afterwards.
- Registered via `TaskPersistenceRuntime.CreateScheduleStore()` and DI
  (`IScheduleStore` + `ScheduleQueryService`).

## Verification results (actual)
| Check | Command / method | Result |
|---|---|---|
| Real PG gate | `dotnet test work/production/Task.sln --no-restore` with `TASK_POSTGRES_TEST_ADMIN_CONNECTION` pointing at the local real PostgreSQL 16.14 (127.0.0.1:5432) | PASS 495/495 (0 failed, 0 skipped): `PostgresCalendarEventStoreTests.RealPostgres_CalendarEventRoundTripTenantBoundaryAndConcurrency` PASS, `PostgresScheduleStoreTests.RealPostgres_ScheduleWindowTenantBoundaryFiltersAndDiCoverage` PASS, all three `PostgresTaskAggregateStoreTests` real-PG scenarios PASS, plus the full existing suite |
| PostgreSQL 15 rejection gate | `RealPostgres15_IsRejectedBeforeBootstrapDdl` | NOT RUN: `TASK_POSTGRES15_TEST_ADMIN_CONNECTION` not configured (unchanged from previous packages; a PG 15 server is not installed) |
| Solution build | `dotnet build work/production/Task.sln --no-restore` | 0 errors, 0 warnings |
| Diff hygiene | `git diff --check` over `9002d13..HEAD` | clean |
| Scope | `git status --short` after review | clean; only the two commits' files plus the output package |
| Manifest parse | `ConvertFrom-Json` | valid JSON |
| Hashes | canonical-LF SHA-256, UTF-8 without BOM, CRLF -> LF | every MANIFEST.sha256 entry and manifest.json artifactHash matches a fresh recomputation at HEAD `f244b72` |
| Push state | `git fetch origin` | local HEAD == origin/main == f244b72, working tree clean |

## Behaviour of the delivered artifacts (actual)
- Calendar event round trip on a fresh real-PG database: Add -> Get
  preserves scalars, timing (timed and all-day), attendee collections in
  stored order, and the lifecycle metadata; stale saves raise
  `CalendarEventConcurrencyException` with the correct actual version;
  saves of missing events raise `KeyNotFoundException`; tenant boundary
  (`Get` from another organization returns null) holds; attendee
  replacement rewrites rows instead of accumulating them.
- Schedule page: half-open window overlap for timed items, all-day day
  boundaries in the event time zone, task intervals and points; users /
  projects / status filters; conflicts only between positive-duration
  items with the 30-minute severity threshold; `excludeObjectId` omits
  every pair containing the object.
- The three earlier real-PG scenarios (migration round trip, fail-closed
  migrator, bootstrap) still pass unchanged on the same server.

## Notes on environment
The local test server (PostgreSQL 16.14, binary protocol) exhibits the
previously documented aggregate-expression anomaly; neither the calendar
store nor the schedule queries use the affected SQL shapes. The PostgreSQL
15 rejection gate is not runnable locally (no PG 15 server installed),
exactly as in the previous package.

## Not delivered (explicitly out of scope)
- API endpoints/controllers for calendar events and the schedule read
  model, permissions wiring for the new query, If-Match/idempotency,
  audit/outbox, sync and notifications.
- Attendee response workflow (`/respond` with `expectedAttendeeVersion`).
- Cursor pagination of `SchedulePage` (contract declares `NextCursor` null).
- Recurrence, worker, Desktop UI, authorization and deployment.
- Any change to migrations (`calendar` schema 003 is untouched),
  `Task.Domain/**`, solution/project files, dependencies or `sources/**`.

## Hash rule
UTF-8 without BOM; CRLF normalized to LF before SHA-256. MANIFEST.sha256
is not recursively hashed. All hashes computed at HEAD `f244b72`.

## Revision history
- 2026-08-17: initial delivery, base 9002d13, head f244b72.
