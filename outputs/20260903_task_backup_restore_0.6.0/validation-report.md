# OPS-03 validation report — 0.6.0

Date: 2026-09-03. Baseline: 25dc021 (main). Result: software implementation and local recovery gate PASS; company deployment acceptance remains open.

## Delivered behavior

The former idle BackupAgent now schedules daily physical backups, continuous WAL archive checks and weekly isolated PITR drills. PostgreSQL 16 and pgBackRest operate with a dedicated database identity, two encrypted repositories, minimum retention windows, separate recovery keys and authenticated recovery-asset bundles. Failed secondary backup or restore verification prevents expiry. Operator restore requires an explicit backup label and past UTC target, allocates fresh isolated storage and never overwrites production. Persistent scheduler status, serialization locks, critical failure logs and a health command expose failed/overdue protection.

Configuration, recovery keys/references, certificates, system assets, installers and migration inputs are included in the encrypted bundle. Physical employee/SMB files remain under the separate corporate backup process required by the architecture. The runbook covers provisioning, escrow, rotation, incident recovery, cutover and deployment acceptance.

The existing production-package CI gate now invokes Test-BackupRestore.ps1 after container packaging. CI execution for this commit is verified separately after push. The standalone backup gate has actually run locally, including its cleanup path.

## Evidence and checks

- Build: Release production solution succeeds. Whitespace verification and project-boundary gate PASS. Recompiling the existing test project surfaced seven pre-existing ASPDEPR004 WebHostBuilder deprecation warnings; none originates in the backup implementation.
- Final full production tests with disposable real PostgreSQL 16.14: 1,325 passed, zero failed, zero skipped (764 Task.Tests, 316 ServiceHosts, 245 Desktop). See postgres-full_*.trx and postgres-tests-version.txt. Earlier production_*.trx record the pre-Docker run with two skipped tests; the later full run supersedes it.
- Targeted backup tests: 16 passed. Includes daily/weekly scheduling, durable restart, retry, cancellation, invalid/future state and configuration validation.
- Security gate PASS: tracked runtime configuration scan and solution tests. Secrets are generated only for disposable fixtures, never committed or included in this package.
- Real backup integration: 16 scenario checks PASS on PostgreSQL 16.15 / pgBackRest 2.50 / cryptography 41.0.7. See integration.json and integration.txt.
- Actual physical restoration from both encrypted repositories; pg_amcheck and offline page checksums succeed.
- PITR restores rows 1 and 2 (including a commit after the base backup) before a destructive deletion; the primary still contains row 3. Restored schema fingerprint equals the migrated source schema.
- Weekly WAL restore-point replay succeeds for both repositories. Tampered asset ciphertext, corrupt physical backup, absent/wrong/duplicate keys, future targets, concurrent operations, stale health and too-short retention are rejected.
- Off-host outage does not expire the last verified set. Time retention preserves the minimum recovery window. Accelerated count retention exercises deletion and leaves the newest set restorable; elapsed 14/366-day aging is not simulated.
- Separate final incident exercise: original server stopped, fresh operator state and restore volumes, read-only secondary repository plus escrow keys only. Expected PITR rows recovered without the primary data/socket or prior scheduler state.
- All containers and volumes created by the recovery fixture removed; generated test keys removed. See cleanup.json. Other pre-existing Docker workloads were not modified.
- run.json binds the successful final run to source SHA-256 hashes. The second run reused the image built in the first run: subsequent edits only connected CI and hardened evidence packaging; runtime C#/Python/Dockerfile content was unchanged.
- Package generation verifies every ZIP member against the source overlay and every manifest file hash. Package-local Git attributes preserve exact artifact bytes across Windows/Linux checkouts.

## Scope and deployment acceptance

OPS-03 advances to 75% / in_progress, not production-certified protection. Before company acceptance, its operator must prove actual separate storage and NFS/SMB off-host placement, immutable/offline snapshot recovery, both escrow copies, representative-data RPO <=15 minutes and RTO <=4 hours, restricted API/business smoke after recovery, and alert delivery/ownership (OPS-04). No company infrastructure or production data was supplied or accessed. Local fixture restore timings are recorded in integration.json and must not be presented as production RTO.

Retention is intentionally conservative: daily full copies for 30 days locally and 366 days on the secondary cover the architecture's weekly/monthly examples. Size the repositories for those complete windows. Key rotation preserves old repository/key generations until expiry. Administrative API/UI commands and the production cutover are separate from this offline operator.

.NET base images and NuGet locks are pinned. Operator Ubuntu packages are resolved from signed repositories at build time: this additional operator image does not claim the independent reproducibility of release 0.5.0. The tested image ID and exact backend package versions are in evidence.

Canonical sources consulted: sources/concept/Task_Concept_Final.txt; sources/stage_1/architecture_organizer.md (15.15, 16, 17.8); the existing extracted stage-2.2 docs/03_runtime_operations_and_testing.md (background-job catalog, backup sequence and weekly restore checks). The original ZIP filename in AGENTS.md is absent locally; sources/stage_2_2 contains the extracted source and Repacked ZIP. No sources were changed.

Local environment recovery: Docker Desktop initially failed on stale dockerInference and secrets-engine AF_UNIX endpoints. Its transient runtime directories were renamed with an ops03-stale-20260903 suffix and retained, after stopping only the Docker processes launched for this session. Docker restarted successfully; no factory reset, image or existing volume deletion was used. The registry download for the general PostgreSQL test image failed once; that gate used the already cached immutable PostgreSQL 16.14 image instead. The backup gate used the freshly built PostgreSQL 16.15 operator image.