# Validation report

Status: PASS for persistence runtime/readiness increment; NOT a deployment or product-completion claim.

## Environment

- .NET SDK: 10.0.400
- Target framework: .NET 10
- Npgsql: 10.0.3
- Real database: PostgreSQL 16 (`postgres:16-alpine`) in an isolated disposable Docker container

## Automated gates

- `dotnet restore work/production/Task.sln` — PASS.
- `dotnet build work/production/Task.sln --no-restore` — PASS, 0 warnings, 0 errors.
- `dotnet test work/production/Task.sln --no-build --no-restore` — PASS:
  - Task.Tests: 128;
  - Task.Desktop.Tests: 9;
  - Task.ServiceHosts.Tests: 11;
  - total: 148.
- `dotnet format Task.sln --verify-no-changes --no-restore --include <changed C# files>` — PASS.
- `Test-ProjectBoundaries.ps1` — PASS.
- `Test-TaskApi.ps1` without database configuration — PASS; live 200, ready 503, code `NotConfigured`.
- `Test-TaskApi.ps1` with reachable but unmigrated PostgreSQL — PASS; ready 503, code `MigrationsNotApplied`.
- Real PostgreSQL integration — PASS:
  - empty database reports `MigrationsNotApplied`;
  - migration applies and is idempotent;
  - runtime reports `Ready` with PostgreSQL 16 and exact migration checksum;
  - actual `Task.Api` reports HTTP 200/`Ready` against the migrated database;
  - deliberate history-checksum corruption reports `SchemaVersionMismatch`;
  - Task aggregate round-trip, organization isolation and stale-version rejection remain PASS.
- `Test-GapOverrides.ps1` — PASS; resolved 1001, unresolved 245.
- NuGet vulnerability audit — no vulnerable packages reported; dependency graph did not change in 0.2.0.

## Final audit corrections

- Reconstitution rejects completion timestamps outside the aggregate lifetime and empty completion/deletion actor identifiers.
- Initial inserts require the complete canonical version-1 state, preventing malformed reconstituted aggregates from bypassing creation invariants.
- Migration metadata and checksums are shared by migration execution and readiness, eliminating duplicated compatibility constants and supporting validation of the complete migration catalog.
- A solution-wide formatting check also reports pre-existing whitespace findings in unchanged `Task.Desktop/AssemblyInfo.cs`; all C# files changed by this package pass the scoped formatting check.

## Safety and scope

No connection string or credential was added to tracked configuration. Health responses return stable safe messages rather than database exception text. API startup and readiness do not apply migrations. `sources/` is unchanged. Auth, API task operations, deployment migration execution, backup and the remaining 245 Stage 1 gaps remain outside this increment.
