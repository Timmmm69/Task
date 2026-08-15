# Validation report — Task container deployment foundation 0.4.0

Date: 2026-08-15

Base commit: `a55f83a1e68bb8581868fed5785d5713d1c7208e`

Environment: Windows PowerShell 5.1, .NET SDK 10.0.400/runtime 10.0.11, Docker Desktop linux/amd64, PostgreSQL 16.15 Alpine validation image.

## Result

PASS. The increment provides a verified container packaging foundation. It does not claim production deployment readiness.

## Gates

| Gate | Result | Evidence |
|---|---|---|
| `dotnet restore work/production/Task.sln` | PASS | All projects restored/current. |
| Release solution build | PASS | 0 warnings, 0 errors. |
| Full solution tests | PASS | 176 passed: Task.Tests 137, ServiceHosts 30, Desktop 9. |
| Scoped `dotnet format --verify-no-changes` | PASS | 0 of 4 files required formatting. |
| `Test-ProjectBoundaries.ps1` | PASS | All production/test boundaries valid. |
| `Test-TaskApi.ps1` | PASS | Live, unconfigured readiness and correlation checks passed. |
| `Test-GapOverrides.ps1` | PASS | Resolved 1001, unresolved 245. |
| NuGet vulnerability audit | PASS | No vulnerable direct or transitive packages reported. |
| Four Linux OCI image builds | PASS | API, Worker, BackupAgent and DatabaseMigrator built for linux/amd64. |
| Image contract | PASS | Non-root UID, SDK absent, exact version/revision labels, no credential/connection metadata. |
| Migration role | PASS | Initial status exit 6, apply exit 0/Applied, final status exit 0/Ready. |
| Runtime grants | PASS | Minimal explicit grants applied twice idempotently. |
| API runtime role | PASS | `/health/live` 200 and `/health/ready` 200/Ready without DDL credentials. |
| Production task store | PASS | Add, organization-boundary get, save and stale optimistic-concurrency rejection. |
| Negative role checks | PASS | CREATE TABLE, ALTER TABLE, DROP TABLE and schema-history UPDATE rejected. |
| Network topology | PASS | PostgreSQL no host port/internal network only; API validation port on `127.0.0.1`. |
| SIGTERM | PASS | Worker and BackupAgent exited 0 within one second in the measured run. |
| Cleanup | PASS | No validation containers, networks, volumes or credential directory remained. |
| Manifest verification | PASS | Canonical CRLF-to-LF SHA-256 verification. |
| `git diff --check` and `sources/` scope | PASS | No whitespace errors; no source artifact change. |

## Base image verification

Official Microsoft Container Registry tags were enumerated on 2026-08-15 and their linux/amd64 manifests were inspected before pinning:

- SDK `10.0.400-noble-amd64` — `sha256:5657c5f725f2e8923f31b2eb9d743662f2e0be50a2bee41de685fc9f12ae68ef`;
- ASP.NET `10.0.11-noble-amd64` — `sha256:282c2e90dd35c6a720b744f4848d3dce9de4bfb404011270cc8ee63f07e56c36`;
- runtime `10.0.11-noble-amd64` — `sha256:3e4906ba425366b848fc1899c4414cbc4d1c70f423a4e5a0d5c39c226e134f21`.

The official .NET documentation also confirmed the SDK/ASP.NET/runtime image split, multi-stage publishing, linux/amd64 targeting and built-in non-root `app` user pattern.

## Scope statement

No `sources/`, product Domain/Application API, DTO, authentication/authorization contract, Desktop code, backup implementation, migration 001 or migration CLI exit code was changed. Credentials were random, test-only, stored below ignored `work/tmp`, not printed by the verification script, and deleted in `finally`.

Backup/restore orchestration, TLS reverse proxy, registry/signing, production secrets manager, update delivery, monitoring and production network provisioning remain outside scope. Stage 1 remains incomplete with 245 unresolved gaps.
