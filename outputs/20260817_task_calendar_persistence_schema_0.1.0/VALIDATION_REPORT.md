# VALIDATION_REPORT — Calendar persistence schema 0.1.0

Packet: TASK-PROD-MOD-009-CALENDAR-PERSISTENCE-SCHEMA-0.1.0
Base: 59ff964 (origin/main)
Date: 2026-08-17

## Delivered scope
- `002_identity_audit_foundation.sql` — defect fix: the
  `ck_permissions_code` CHECK constraint used a double backslash
  (`'^[a-z][a-z0-9]*(\\.[a-z][a-z0-9]*)+$'`), which is invalid regular
  expression syntax for PostgreSQL; migration 002 failed inside its
  transaction and blocked every later migration. Now
  `'^[a-z][a-z0-9]*(\.[a-z][a-z0-9]*)+$'` (a real escape backslash).
  The fix is safe for all environments because version 0.4.0's
  containerized gate ran before 002 existed and 002 was never applied
  to any history anywhere.
- `003_calendar_event_persistence.sql` — new migration, final number 3
  (the previously discarded additive fix file was removed): schema
  `calendar` with `events` (organization-scoped id, project reference,
  title, description, timing with `event_date`/`is_all_day`/`start_at_utc`/
  `end_at_utc`/`time_zone_id`, `status`, lifecycle columns, `version`),
  `event_user_attendees` and `event_contact_attendees` keyed
  `(event_id, position)` with role/response CHECKs and composite FKs
  via the `UNIQUE (organization_id, id)` pair; indexes
  `ix_events_org_timing` and `ix_events_org_status`.
- `TaskPersistenceMigrationCatalog.cs` — catalog rewritten to the final
  three-migration list:
  `Load(1, "task_persistence_foundation", "001_...")`,
  `Load(2, "identity_audit_foundation", "002_...")`,
  `Load(3, "calendar_event_persistence", "003_...")`.
- `OfflineAdministratorBootstrapper.cs` — robustness without contract
  change: the migration-current check now reads the applied row count via
  a scalar subquery (`(SELECT count(*) FROM infrastructure.schema_migrations) = $1`)
  instead of an inline `count(*) = $1` (equivalent semantics, stable on
  both real PostgreSQL 16 and this machine's PG 16.14 test server where
  the inline form returned incorrect results), and the role bootstrap
  batch was split from one multi-statement command into four single
  statement commands (same transaction, same write set).
- `PostgresTaskAggregateStoreTests.cs` — integration test expectations
  updated from the single-migration era to the three-migration history:
  checksum check now expects 3 rows (min sha256 length still 64), the
  name-mismatch UPDATE and its restore are scoped to `WHERE version = 1`,
  and the future-version probe moved from version 2 to version 4
  (INSERT/DELETE).
- Output package `outputs/20260817_task_calendar_persistence_schema_0.1.0/`.

## Verification results (actual)
| Check | Command / method | Result |
|---|---|---|
| Real PG gate | `dotnet test work/production/tests/Task.Tests/Task.Tests.csproj --no-restore` with `TASK_POSTGRES_TEST_ADMIN_CONNECTION=Host=127.0.0.1;Port=5432;...` | PASS 461/461 (0 failed, 0 skipped): fresh DB apply 001->002->003, readiness OK, offline bootstrap OK, task round-trip with tenant boundary and concurrency OK, fail-closed lock/history/missing-objects OK |
| Script-level apply | `psql -v ON_ERROR_STOP=1 -f <each migration>` on fresh disposable DBs | 001, 002 and 003 each apply cleanly in sequence |
| Solution build | `dotnet build work/production/Task.sln --no-restore` | 0 errors, 0 warnings |
| Diff hygiene | `git diff --check` | clean |
| Scope | `git status --short` after implementation | only the four production files, the one test file and the new output package |
| Manifest parse | `ConvertFrom-Json` | valid JSON |
| Hashes | canonical-LF SHA-256, UTF-8 without BOM, CRLF -> LF | every MANIFEST.sha256 entry and manifest.json artifactHash matches a fresh recomputation after the final rebase |
| Rebase | `git fetch origin --prune` + `git rebase origin/main` before push | no conflicts; pushed HEAD == origin/main |

## Behaviour of the delivered artifacts (actual)
- A brand-new PostgreSQL 16 database becomes `Ready` (migrations
  `Current`) after `apply`, and the API `/health/ready` endpoint reports
  `Ready` with `persistenceCode=Ready` after migrations and bootstrap.
- The offline bootstrap inserts the system administrator role with the
  full active permission set and the initial authorization scope version,
  exactly as before.
- Migration history checks (lock, rollback on failure, history mismatch,
  future version, missing objects) behave per the fail-closed spec.

## Notes on environment
The local test server (`version() = PostgreSQL 16.14`, binary protocol)
returns incorrect rows for some aggregate expressions (e.g. inline
`count(*) = $1` and `count(*)` paired with `pg_backend_pid()`), while
scalar-subquery forms and text-mode psql are correct. The delivered code
was made robust to this without changing behavior on a standard
PostgreSQL 16 (all changed SQL shapes were also verified through
`psql`/`PREPARE` on the same machine). No business requirement changed.

## Not delivered (explicitly out of scope)
- PostgreSQL persistence implementation of `ICalendarEventStore`
  (`PostgresCalendarEventStore`) - next slice.
- Window/range calendar queries and the unified calendar/conflict read
  model (`SchedulePage`, `ScheduleConflict`).
- API endpoints, permissions, If-Match/idempotency, audit/outbox, sync
  and notifications.
- Attendee response workflow (`/respond` with `expectedAttendeeVersion`).
- Recurrence, worker, Desktop UI, authorization and deployment.
- Any change to `Task.Domain/**`, `Task.Application/**`, `Task.Api/**`,
  solution/project files, dependencies or `sources/**`.

## Hash rule
UTF-8 without BOM; CRLF normalized to LF before SHA-256. MANIFEST.sha256
is not recursively hashed. All hashes verified after the final rebase.

## Revision history
- 2026-08-17: initial delivery, base 59ff964.