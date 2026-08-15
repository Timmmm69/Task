# Task product continuation

## Mandatory startup in a new Codex chat

1. Activate this repository in Serena and read the Serena manual when Serena is available.
2. Read `AGENTS.md`, this file, and `work/delegation/README.md`.
3. Run `git fetch origin --prune`, inspect branch/status, and compare `HEAD` with `origin/main` before changes.
4. Do not modify `sources/`. Work under `work/`; final packages belong under `outputs/`.

## Current increment

- Increment: container deployment foundation 0.4.0.
- Starting `origin/main`: `a55f83a1e68bb8581868fed5785d5713d1c7208e`.
- Final implementation commit: the commit containing this document; Git history is authoritative after publication.
- Production solution: `work/production/Task.sln`, .NET 10, Npgsql 10.0.3, no EF Core.
- Linux container targets: Task.Api, Task.Worker, Task.BackupAgent and Task.DatabaseMigrator.
- API startup/readiness never applies schema changes or receives migration-role credentials.

## Verified 0.4.0 gates

- restore and build: PASS, 0 warnings/errors;
- full unit/component suite and required project/API/gap checks: PASS;
- four linux/amd64 runtime images: PASS, pinned exact official .NET tags/digests, non-root, no SDK and valid OCI labels;
- PostgreSQL 16 disposable-container gate: PASS, including migration-role `status(6)/apply/status(0)`, idempotent minimal runtime grants and API live/ready under `task_runtime`;
- production `PostgresTaskAggregateStore` under `task_runtime`: PASS for add, organization-boundary get, save and optimistic concurrency;
- runtime-role CREATE/ALTER/DROP and migration-history update: rejected by PostgreSQL;
- Worker/BackupAgent SIGTERM within ten seconds: PASS;
- PostgreSQL has no host port, API validation port is loopback-only, and Docker containers/networks/volumes/credential files are cleaned up.

The final validation report and manifest are in `outputs/20260815_task_container_deployment_foundation_0.4.0`.

## Remaining scope

Increment 0.4.0 does not provide production secrets management, image registry/signing, TLS termination, production network provisioning, backup/restore orchestration, monitoring, updates, authorization completion, Desktop changes or a deployment-readiness claim. The BackupAgent remains a placeholder. The Stage 1 matrix remains at resolved 1001 / unresolved 245.

## Next safe stage

Define the production secrets and registry/signing contract, then design TLS reverse-proxy and monitored deployment topology without weakening the migration/runtime role split. Backup/restore policy and destructive-migration approval must remain explicit and independently verified.
