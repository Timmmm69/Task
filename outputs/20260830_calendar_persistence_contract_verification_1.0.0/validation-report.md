# Validation report — Calendar persistence contract 1.0.0

Date: 2026-08-30
Base: `bb37ad9` (`main`, synchronized with `origin/main` before work)

## Result

Calendar persistence contract is complete and verified on a fresh, isolated PostgreSQL 16 cluster. The HTTP/UI layer was not implemented or changed.

## Checks

| Check | Result |
|---|---|
| Critical calendar PostgreSQL gate | PASS: 2/2, 0 failed, 0 skipped |
| Full `Task.Tests` with real PostgreSQL | PASS: 755/755, 0 failed, 0 skipped |
| Solution Debug build | PASS: 0 errors; 8 pre-existing analyzer warnings |
| PostgreSQL isolation | Fresh temporary cluster on PostgreSQL 16, trust auth limited to the disposable test cluster |

Critical scenarios executed:

- `PostgresCalendarEventStoreTests.RealPostgres_CalendarEventRoundTripTenantBoundaryAndConcurrency`;
- `PostgresScheduleStoreTests.RealPostgres_ScheduleWindowTenantBoundaryFiltersAndDiCoverage`.

The complete gate also executed the migration, readiness, bootstrap, fail-closed migration-history, and existing PostgreSQL persistence scenarios.

## Corrective change

The full gate exposed stale migration-count assumptions in `PostgresTaskAggregateStoreTests`: after migration 006, the test still expected five history rows and used version 6 as a synthetic future migration. The test now derives both values from `TaskPersistenceRuntime.ExpectedMigrationVersion`. No production schema, migration, domain, application, HTTP, or UI contract changed.

## Commands

```powershell
$env:TASK_POSTGRES_TEST_ADMIN_CONNECTION='Host=127.0.0.1;Port=55432;Database=postgres;Username=postgres;SSL Mode=Disable'
dotnet test work/production/tests/Task.Tests/Task.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~PostgresCalendarEventStoreTests|FullyQualifiedName~PostgresScheduleStoreTests"
dotnet build work/production/Task.sln -c Debug --no-restore
dotnet test work/production/tests/Task.Tests/Task.Tests.csproj -c Debug --no-restore
```

The temporary connection string contains no production credential and the disposable cluster is removed after validation.
