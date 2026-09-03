# OPS-03 validation report — 0.6.1

Date: 2026-09-03. Baseline: 58e2b97 (main, synchronized with origin/main before work).

Result: implemented changes and local recovery gates PASS. Company deployment acceptance remains open; OPS-03 stays at 75% / in_progress. No company server, remote storage access, independently held escrow copies or representative production workload was supplied.

## Changes

- Replaced the fixed 120-second PostgreSQL recovery startup/replay wait with a shared bounded restore budget. TASK_RESTORE_TIMEOUT_SECONDS accepts 1–14400 seconds (default 14400). Physical restore, startup, replay, pg_amcheck and offline checks consume the same deadline. Bounded shutdown/cleanup is separate. BackupAgent's existing parent operation timeout is preserved.
- Added a standalone secondary-only recovery Compose profile. It needs only the tested image, selected repository/snapshot and independently retrieved keys. Read-only bind mounts cannot auto-create missing host paths; no primary database/socket or container network is configured.
- Added a read-only schema baseline and an isolated acceptance drill. It rejects validation bypass, writable secondary mounts, visible primary data/socket, a populated unselected repository, invalid UTC chronology, requested loss window >15 minutes, incident age >=4 hours, undersized backup and schema mismatch. Actual PITR, authenticated assets, pg_amcheck and fixed core-table SQL smoke are required.
- Receipts distinguish requested data-loss window, restore/database-smoke duration and elapsed incident-to-database-readiness. They bind to runtime source SHA-256 and never claim complete production acceptance. Successful temporary recoveries are stopped and removed; failures cannot create a successful receipt. Existing operation locks serialize drills and runner operations.
- Added the company acceptance procedure for off-host separation, protected snapshots, independent retrieval of two escrow copies, representative workload, API/business smoke, complete service RTO and alert ownership.

## Verification actually performed

- Final Test-BackupRestore.ps1 run: succeeded and cleaned up; run.json binds all tested backup sources to SHA-256. A first full run also passed; only the final run is packaged.
- PostgreSQL 16.15 / pgBackRest 2.50 / cryptography 41.0.7, exact package builds in evidence/backend-versions.txt; image ID in evidence/image-id.txt.
- 16 existing integration scenarios passed: both encrypted repositories, PITR before deletion with post-base commits, untouched primary, WAL restore-point replay, corruption and key failures, retention protection, storage rules, operation locking, real hosted scheduler and health failures.
- 10 new Python regression tests passed, including malformed requests, RPO boundary, overdue incident, read-only isolation, cleanup on schema mismatch, redacted failure receipts, a shrinking deadline and passing >120 seconds of remaining budget to the actual pg_ctl call path.
- 16 existing .NET Backup tests passed, zero failures/skips. The build reported seven existing ASPDEPR004 WebHostBuilder deprecation warnings outside changed files. Full 1325-test suite was not repeated for this increment; its prior result belongs to 0.6.0.
- Final incident recovery succeeded with the original server stopped, new state/work volumes, only the read-only secondary repository and fixture keys. Expected pre-delete rows remained recoverable.
- New acceptance drill then performed another physical recovery plus database smoke: 32,170,320 backup bytes; 1.11227-second requested loss window; 3.340 seconds restore plus database smoke; 71.311 seconds from fixture incident to database readiness. These are small local fixture measurements, not company RPO/RTO or complete service readiness.
- Standalone recovery Compose parsed successfully using only its three required environment values; network, mount targets, read-only binds and disabled automatic bind-directory creation checked. See evidence/recovery-compose.json.
- Project architecture boundary gate passed. Final diff inspection and whitespace verification passed. Only OPS-03 dashboard evidence/note/next action changed; progress stayed at 75%, and dashboard:order was run.
- Named fixture containers and volumes removed, generated fixture keys removed. Existing unrelated Docker workloads were left running. See evidence/cleanup.json.

## Remaining production acceptance

An actual separate storage host/device and independently protected immutable/offline snapshot must be provisioned and their retention/ACL checked. Retrieve both escrow copies independently and run the documented drills. Approve a representative company dataset and verify the expected business transaction. Complete restricted API login/task/audit/catalog checks, full service-ready RTO <=4h and alert delivery with OPS-04. A self-declared scope=company or storage identifier does not prove these properties.

The schema signature covers columns/defaults/constraints/indexes of the six implemented application schemas, not every PostgreSQL object or application behavior. SQL smoke is not an API login or catalog acceptance test. The receipt intentionally leaves productionAccepted=false. No production cutover or remote deployment was performed.

## Package

Version, source overlay, ZIP, manifest.json, SHA256SUMS and this validation report are generated by Package-BackupEvidence.ps1. Generation rejects stale/failed recovery evidence and verifies archive members and manifest file hashes. The overlay requires the baseline repository. Sources under sources/ were not modified. Changes have not been committed or pushed in this task; remote CI was not run for this increment.

Canonical requirements: sources/concept/Task_Concept_Final.txt and sources/stage_1/architecture_organizer.md sections 16 and 17.8; existing 0.6.0 implementation and validation baseline. PostgreSQL wait semantics checked against the official [PostgreSQL 16 pg_ctl reference](https://www.postgresql.org/docs/16/app-pg-ctl.html).
