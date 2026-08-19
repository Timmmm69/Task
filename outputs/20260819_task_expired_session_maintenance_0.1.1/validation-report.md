# Validation report

Status: PASS for Task expired session maintenance increment 0.1.1 (robustness fix + real-database gate); NOT a deployment-readiness or Stage 1 completion claim.

## Environment

- .NET SDK: 10.0.400
- Target framework: .NET 10
- Npgsql: 10.0.3
- OS: Windows (win32, PowerShell 5.1)
- PostgreSQL gate: native `postgresql-x64-16` 16.14 on 127.0.0.1:5432 (admin connection supplied via `TASK_POSTGRES_TEST_ADMIN_CONNECTION`)

## Verified behavior

- `ISessionRepository` gained exactly two operations: `PurgeExpiredRefreshTokensAsync` and `PurgeExpiredSessionsAsync`; nothing else changed.
- `PostgresSessionRepository` implements both as parameterized DELETE statements with a positive `maxCount` guard, oldest-first ordering and a `LIMIT` batch cap, returning the actual deleted row count.
- Refresh tokens are purged before sessions; session purge excludes sessions still referenced by `governance.audit_entries.actor_session_id` (ON DELETE RESTRICT) instead of failing the whole statement.
- `ExpiredSessionMaintenanceWorker` (BackgroundService, same pattern as `TaskBackgroundWorker`): 60-minute period, 30-day retention cutoff, batch size 1000 with the loop continuing until a batch comes back non-full, empty passes logged at Debug, generic failures logged as errors and database unavailability as warnings, cancellation honored in the interval wait and the repository calls, and a clean exit on shutdown.
- `Task.Worker/Program.cs` registers `TaskPersistenceRuntime` and conditionally registers `ISessionRepository` through `TaskPersistenceRuntime.CreateSessionRepository()` only when `TaskPersistenceRuntime.IsConfigured` is true (0.1.1 change: covers both an absent and a malformed `TaskDatabase` connection string, so a malformed string no longer crashes the worker at startup). No connection string was added to the worker's `appsettings`.
- Worker unit tests cover: constants, empty pass, batch loop with the retention cutoff, failure survival with a successful retry, database-unavailable warning with retry, and DI resolution of the hosted service without a registered repository.
- Guarded PostgreSQL integration gate (runs only when `TASK_POSTGRES_TEST_ADMIN_CONNECTION` is set) covers: expired tokens/sessions removed, fresh records kept, records newer than the cutoff kept, FK-safe order (tokens before sessions), and batch limits completing in multiple passes.

## Final automated gates

- `dotnet build work/production/Task.sln` — PASS, 0 errors.
- `dotnet test work/production/Task.sln` — PASS:
  - Task.Tests: 607 passed — includes the guarded PostgreSQL maintenance gate now executed against a real PostgreSQL 16.14 database (2/2 green);
  - Task.ServiceHosts.Tests: 79 passed (7 worker tests included);
  - Task.Desktop.Tests: 47 passed.
- Worker unit tests additionally re-run 3x consecutively for timing stability — PASS (21/21).
- Scoped `dotnet format --verify-no-changes` on all files of this increment — PASS; the full-solution format check reports only pre-existing issues in unrelated in-flight files (`PostgresAuditEntryStoreTests.cs`, `DesktopCredentialVaultTests.cs`), which are outside this increment.
- `verification/Test-ProjectBoundaries.ps1` — PASS, all project boundaries valid.
- `git diff --check` — PASS.
- Portable canonical manifest SHA-256 verification via `Verify-Manifest.ps1` — PASS.

## Scope

No credential was added to tracked files. `sources/` is unchanged. Audit retention (7 years), API/DTO/auth, Desktop, schema, deployment and backup orchestration remain outside this increment. The two existing test fakes updated to implement the extended `ISessionRepository` are a compile-required consequence of the interface addition.