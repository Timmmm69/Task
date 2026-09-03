# Validation report — DATA-04 product persistence 1.0.0

Date: 2026-09-03

Base: `2167f6bd78dcf7b27fee34c59e97ea1bbd394c15` (`main`, clean and synchronized with `origin/main` before implementation).

## Result

The persistence contract for the modules described by DATA-04 is implemented and verified. Projects, contacts, catalog items and notifications have typed production PostgreSQL stores in the shared object model. Organization settings, user settings and notification preferences have persistent optimistic versions. Archive/trash entries are maintained transactionally for all shared objects, including the existing task/calendar stores.

Migration 9 is additive; migrations 1–8 and `sources/` were not changed. No deployment to a company database was performed. Changes remain local and are not committed or pushed by this task.

## Executed checks

| Check | Result |
|---|---|
| Final Release solution build | PASS — 0 errors, 0 warnings on the final incremental build |
| Full solution whitespace gate | PASS — `dotnet format whitespace --verify-no-changes --no-restore` |
| Architecture/project boundaries | PASS — all production/test projects valid |
| `Task.Tests` with real PostgreSQL 16.10 | PASS — 768/768, zero failed/skipped |
| `Task.ServiceHosts.Tests` Release | PASS — 316/316, zero failed/skipped |
| `Task.Desktop.Tests` Release | PASS — 245/245, zero failed/skipped |
| Security gate | PASS — no embedded runtime credential/key patterns; its no-DB run skipped two existing DB-only tests, both executed successfully in the separate real-PostgreSQL gate |
| Container-release verification unit tests | PASS — 8/8 |
| Dashboard order and validation | PASS — 40 items; only DATA-04 factual readiness changed |
| Diff whitespace | PASS |

The earlier full Release rebuild reported eight pre-existing analyzer warnings (seven deprecated test `WebHostBuilder` uses and one blocking-task xUnit warning); no new warning was introduced.

Raw final test evidence is retained in `evidence/Task.Tests.trx`, `evidence/Task.ServiceHosts.Tests.trx` and `evidence/Task.Desktop.Tests.trx`. Their counters were reopened and checked: total 1,329, executed 1,329, passed 1,329, failed/error/notExecuted all zero.

## Critical scenarios actually executed

- All four new product projections round-trip with shared metadata and nullable fields.
- Stale versions and wrong-tenant saves fail without leaking another tenant's current version.
- Cross-tenant owner/manager, catalog-parent and notification-recipient references fail; failed inserts and updates roll back the shared object row/version.
- Organization, user and notification settings round-trip, reject stale versions and reject backward update timestamps.
- The actual production `grant-runtime.sql` is embedded in the test and applied to a disposable non-superuser runtime role. All stores work under that role; DDL and object hard-delete fail with SQLSTATE 42501.
- Sequential and simultaneous catalog moves cannot create a cycle; restoring beneath a trashed parent is rejected.
- Notification content remains immutable across status writes, including semantically equal JSONB payloads with different whitespace.
- Archive/trash transitions preserve content, maintain one current entry, retain restored history and apply the persisted 45-day retention setting.
- Existing calendar writes automatically populate the same lifecycle ledger.
- A database with migrations 1–8 and an already archived/trashed calendar event upgrades to v9 without losing data; historical lifecycle entries are backfilled and repeated migration application is safe.
- Readiness fails if the lifecycle trigger is disabled and recovers when it is enabled.

## Operational boundaries

This is a data-layer milestone. HTTP/UI workflows, delivery/outbox/reminder workers, actual filesystem access, synchronization and purge execution remain their separate roadmap work. No binary deployment bundle or production rollout is claimed here.

Historical pre-v9 `core.objects` has no separate `archived_by` field. The backfill uses the available `updated_by`; it does not claim to recover lost historical attribution. New transitions record the current metadata actor correctly.

Verification used a fresh disposable Docker PostgreSQL 16.10 container bound only to loopback. Tests created and removed uniquely named databases and a temporary runtime role. No existing user database was used.

## Reproduction

```powershell
dotnet format whitespace work/production/Task.sln --verify-no-changes --no-restore
dotnet build work/production/Task.sln -c Release --no-restore
# Set TASK_POSTGRES_TEST_ADMIN_CONNECTION to an isolated PostgreSQL 16+ test administrator.
dotnet test work/production/tests/Task.Tests/Task.Tests.csproj -c Release --no-build --no-restore
dotnet test work/production/tests/Task.ServiceHosts.Tests/Task.ServiceHosts.Tests.csproj -c Release --no-build --no-restore
dotnet test work/production/tests/Task.Desktop.Tests/Task.Desktop.Tests.csproj -c Release --no-build --no-restore
powershell -ExecutionPolicy Bypass -File work/production/verification/Test-ProjectBoundaries.ps1
powershell -ExecutionPolicy Bypass -File work/production/verification/Test-SecurityGate.ps1 -Configuration Release -NoBuild
node --test work/production/verification/container-release.test.mjs
npm run dashboard:order
npm run dashboard:validate
```
