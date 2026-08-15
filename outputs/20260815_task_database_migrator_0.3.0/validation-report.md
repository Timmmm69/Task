# Validation report

Status: PASS for Task database migrator increment 0.3.0; NOT a deployment-readiness or Stage 1 completion claim.

## Environment

- .NET SDK: 10.0.400
- Target framework: .NET 10
- Npgsql: 10.0.3
- Disposable database gates: `postgres:16-alpine` and `postgres:15-alpine`

## Verified behavior

- Read-only inspection and readiness share one compatibility contract.
- History is accepted only as the exact ordered version/name/SHA-256 prefix of the embedded catalog.
- Migration application is asynchronous and atomic; the synchronous compatibility wrapper remains covered.
- The transaction advisory lock is attempted before bootstrap DDL and returns exit 8 immediately when held.
- Complete history with missing required objects returns exit 7 and is not repaired.
- CLI parsing, every inspection mapping, post-check, cancellation and credential-safe failures are covered by component tests.
- PostgreSQL 16 process-level gate passed clean `status -> 6`, `apply -> 0/Applied`, `status -> 0/Ready`, repeat `apply -> 0/AlreadyCurrent`, actual API HTTP 200/`Ready`, lock refusal, history mismatch and missing-object refusal.
- PostgreSQL 15 process-level gate passed: `status/apply -> 5`; no `infrastructure` schema was created.
- Temporary gate containers were removed.

## Final automated gates

- `dotnet restore work/production/Task.sln` — PASS.
- `dotnet build work/production/Task.sln --no-restore` — PASS, 0 warnings, 0 errors.
- `dotnet test work/production/Task.sln --no-build --no-restore` — PASS, 176 tests:
  - Task.Tests: 137;
  - Task.ServiceHosts.Tests: 30;
  - Task.Desktop.Tests: 9.
- Scoped `dotnet format --verify-no-changes` for all changed C# files — PASS.
- `Test-ProjectBoundaries.ps1` — PASS.
- `Test-TaskApi.ps1` — PASS.
- `Test-GapOverrides.ps1` — PASS, resolved 1001 / unresolved 245.
- NuGet vulnerability audit including transitive packages — PASS, no vulnerable packages reported.
- Real PostgreSQL 16 and 15 gates — PASS.
- `git diff --check` — PASS.
- Manifest SHA-256 verification — PASS.
- `sources/` unchanged and temporary gate containers absent — PASS.

## Scope

No credential was added to tracked files. `sources/` is unchanged. API/DTO/auth/Desktop, rollback, database-role creation, backup orchestration and deployment automation remain outside this increment.
