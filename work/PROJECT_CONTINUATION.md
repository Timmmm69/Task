# Task product continuation

## Mandatory startup in a new Codex chat

1. Activate this repository in Serena and read the Serena manual when Serena is available.
2. Read `AGENTS.md`, this file, and `work/delegation/README.md`.
3. Run `git fetch origin --prune`, inspect branch/status, and compare `HEAD` with `origin/main` before changes.
4. Do not modify `sources/`. Work under `work/`; final packages belong under `outputs/`.

## Current increment

- Increment: safe PostgreSQL migration runner 0.3.0.
- Starting `origin/main`: `3d52389e0ac131c6f424a5ff9a215277be73e90e`.
- Production solution: `work/production/Task.sln`, .NET 10, Npgsql 10.0.3, no EF Core.
- New executable: `work/production/src/Task.DatabaseMigrator`.
- API startup/readiness never applies schema changes.

## Verified 0.3.0 gates

- restore and build: PASS, 0 warnings/errors;
- unit/component migration and CLI tests: PASS;
- PostgreSQL 16 disposable-container gate: PASS, including executable `status/apply/status`, idempotence, actual API readiness, incompatible history, missing required object and held advisory lock;
- PostgreSQL 15 disposable-container gate: PASS, `status/apply` exit 5 and no bootstrap schema;
- project boundaries: PASS, `Task.DatabaseMigrator -> Task.Infrastructure` only.

The final validation report and manifest are in `outputs/20260815_task_database_migrator_0.3.0`. Git history is authoritative for the final implementation commit after the gate and publication sequence.

## Remaining scope

Increment 0.3.0 does not provide rollback, database-role creation, backup orchestration, container publication, deployment readiness, API task operations, authorization, Desktop changes or completion of Stage 1. The Stage 1 matrix remains at resolved 1001 / unresolved 245.

## Next safe stage

Define and review deployment packaging for the already-built service executables without granting the API schema-mutation privileges. Keep backup/restore policy and destructive-migration approval explicit rather than coupling either to API startup.
