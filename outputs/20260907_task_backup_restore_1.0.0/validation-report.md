# OPS-03 validation report — 1.0.0

Date: 2026-09-07. Baseline: current local `main`; local refs reported `main...origin/main` as 0/0 before work. A fresh fetch was unavailable because the sandbox denied writing `.git/FETCH_HEAD`.

Result: PASS. Backup/restore release readiness is reproducibly proven on a clean synthetic fixture. Customer-specific storage placement, custodians, ACLs and representative data remain documented deployment actions and are not claimed as performed.

## Implemented readiness gate

- Creates a migrated Task database, bootstraps a synthetic administrator and writes a real task through `Task.Api`.
- Produces two encrypted full backups with WAL/PITR, authenticated recovery assets and policy-protected retention.
- Restores from a read-only secondary, a protected snapshot copy and an independently mounted escrow-B key copy without primary volumes.
- Starts a recovered `Task.Api` and verifies readiness, login, task, audit, catalog and capabilities through HTTP.
- Enforces RPO <= 900 seconds and full service-ready RTO <= 14,400 seconds; binds schema and synthetic row-set fingerprints to the receipts.
- Keeps receipts explicit that fixture release readiness is proven while customer production acceptance remains a post-handoff deployment step.

## Defects found and fixed by the clean-room run

- Added the GSSAPI runtime library required by Npgsql to the API and DatabaseMigrator production images.
- Added a restricted `samenet` SCRAM rule to the container PostgreSQL HBA so password-authenticated application/migrator traffic can reach a fresh database over its internal Docker network.
- Corrected synthetic JWT public-key naming so the active signing key has a matching verification key id.
- Corrected protected snapshot volume initialization and deterministic fixture fingerprint parsing.

## Verification actually performed

- `Test-BackupRestore.ps1 -SkipBuild`: PASS after rebuilding all changed images in prior full runs.
- 16 destructive integration scenarios: PASS, including encrypted copies, PITR, tamper/wrong-key/corruption rejection, retention floors, locks, scheduler and unhealthy-state checks.
- 11 Python acceptance regression tests: PASS.
- Three clean-room restorations: PASS; storage targets `readonly-secondary` and `protected-snapshot`, escrow copies `escrow-a` and `escrow-b`.
- Recovered service smoke: HTTP 200 for readiness, login, task, audit, catalog and capabilities.
- Measured requested loss window: 1.110791 seconds; measured incident-to-service-ready RTO: 83.986 seconds.
- Cleanup: zero fixture containers and volumes remain; generated fixture secrets removed.
- PostgreSQL 16.15, pgBackRest 2.50, Python cryptography package and exact image IDs are recorded in evidence.

## Scope boundary

No customer data, production storage device, corporate secret, real custodian or production cutover was used. The release artifact proves that the product mechanism and acceptance workflow are ready; the receipt's `productionAccepted=false` prevents this synthetic run from being misrepresented as customer deployment acceptance.

## Package

The package contains version, validation report, source overlay, evidence, ZIP, `manifest.json` and `SHA256SUMS`. Packaging rejects stale source hashes, incomplete receipts, failed cleanup and mismatched ZIP members. `sources/` was not modified.
