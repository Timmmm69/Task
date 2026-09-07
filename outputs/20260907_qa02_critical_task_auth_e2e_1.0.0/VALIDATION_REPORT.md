# QA-02 validation report

Version: 1.0.0. Date: 2026-09-07. Result: **PASS**.

## Verified clean-stand path

- Release solution build passed with 0 errors.
- A new isolated PostgreSQL 16.14 cluster was initialized from an absent runtime directory.
- The production migrator applied schema version 13 to a new database.
- Production `Task.Api` started on HTTPS with ephemeral local secrets and certificate material.
- Deterministic admin and read-only identities authenticated through `/api/v1/auth/login`.
- The replay probe returned HTTP 201 twice with `Idempotency-Replayed=true`; PostgreSQL retained
  exactly one task, audit record, domain event, outbox message and completed idempotency record.
- The real Release WPF client restored the admin session, connected to the HTTPS API and created
  `QA-02 deterministic WPF task` through UI Automation.
- PostgreSQL retained exactly one task/audit/event/outbox/idempotency record for that WPF create.
- After a production API restart, direct GET/list and a fresh WPF process both returned the task.
- A read-only account received HTTP 200 for task read and HTTP 403 for task create. Its WPF task
  screen opened with task creation disabled; the unrelated admin task remained visibility-filtered.
- Final cleanup stopped the API and PostgreSQL, removed the isolated runtime and restored the
  pre-existing Desktop AppData.

## Evidence

- `evidence/db-assertions.json` — PostgreSQL, migration, persistence, idempotency and authorization.
- `evidence/ui-assertions.json` — real WPF create, restart/reopen and read-only UI assertions.
- `evidence/qa02-clean-stand.log` and `evidence/phase-*.log` — phase-level execution and cleanup.
- `evidence/qa02-release-build.log` — Release build output.

The clean stand intentionally uses synthetic data and a local trusted HTTPS certificate. Corporate
addresses, certificates, secrets and customer acceptance belong to post-handoff deployment and are
outside the QA-02 readiness boundary defined by the project dashboard.
