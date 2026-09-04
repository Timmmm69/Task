# API-04 — validation report

Version: 1.0.0. Validated: 2026-09-04. State: implemented, validated and prepared for main publication; not deployed. Git history records the publication itself.

## Environment and isolation

- Windows, .NET SDK 10.0.400, Release configuration, net10.0 / net10.0-windows.
- PostgreSQL 16.14 (Visual C++ 1944, 64-bit), dedicated loopback-only cluster on port 55444 under `work/tmp/api04-postgres`, not the installed application databases.
- `TASK_POSTGRES_TEST_ADMIN_CONNECTION` was set for both full test runs. Integration tests actually created disposable databases; absence-of-environment early returns were not used as verification.
- Each new product API fixture applies production migrations, executes the actual embedded deployment grant script and then runs API store operations as a non-superuser, non-DDL runtime role. Fixture databases and roles are dropped after successful tests.

## Results

| Gate | Result |
|---|---|
| Release solution build | Pass; 0 errors. Existing test warnings: ASPDEPR004, xUnit1031; no new package dependencies |
| Task.Tests | 773 passed, 0 failed, 0 skipped |
| Task.ServiceHosts.Tests | 417 passed, 0 failed, 0 skipped |
| Task.Desktop.Tests | 245 passed, 0 failed, 0 skipped |
| Total | 1,435 passed, 0 failed, 0 skipped |
| Test-SecurityGate.ps1 -Configuration Release -NoBuild | Pass; configuration credential scan and a second complete solution run |
| Test-ProjectBoundaries.ps1 | Pass, all production/test project boundaries |
| dotnet format whitespace --verify-no-changes --no-restore | Pass |
| git diff --check | Pass; Git line-ending normalization notices only |

TRX evidence is retained in `evidence/`. The package builder verifies the counters from those files, requires three test assemblies, hashes every implementation/evidence file, and rereads all hashes. Dashboard order and validation are run after changing only API-04 readiness; mechanical queue ordering can affect other items without changing their progress.

## Exercised behavior

1. Every registered route denies a caller lacking its capability before entering persistence. HTTP tests also validate authenticated identity propagation, JSON response serialization, ETags, required idempotency headers, strict If-Match syntax, duplicate/malformed JSON, payload bounds and sanitized errors.
2. Projects, contacts, companies and catalog objects round-trip through PostgreSQL. CRUD, hierarchy cycle prevention, CRM child records, company relations, archive/unarchive, trash/restore and retention ledger projections are exercised.
3. Optimistic concurrency: four writers with the same version produce one commit and three version conflicts. Six identical concurrent create commands produce one object. Reused keys with a different body fail; invalid data rolls back.
4. Tenant isolation, invisible projects, project membership role checks, member overrides with member ETags, authorization scope invalidation and stale member versions are covered.
5. User/organization settings and notification preferences persist; own-only notifications support read/dismiss/read-all. List pagination is stable and binds its cursor to filters and identity.
6. Local paths are device/owner scoped; other users receive neither raw paths nor can-open permission. UNC roots are allowlisted. Resolve is explicitly metadata-only. File-check observations validate device, location version and timestamp.
7. Search includes persisted entity types, file-location visibility and employees; explicit read capabilities filter results. Stable server snapshots exclude later inserts, bind filters and prevent cross-tenant reuse.
8. Typed object links require update rights on the source and visibility of the target. Read-only source users cannot mutate links; invisible targets are filtered before SQL pagination. Interactions and participants persist and appear in search.
9. Existing task/calendar/data/auth/desktop tests remain green. Domain events and outbox rows are committed together. Existing PostgreSQL readiness tests now launch the matching Debug/Release API binary instead of accidentally checking stale Debug output.

## Independent review

A read-only reviewer checked the authorization, SQL scoping and location handling. Two P2 findings (source-update authorization for links and unbounded locking of link lists) were fixed and regression-tested. A targeted follow-up found no remaining P1/P2 blocker in those fixes, member-version handling and authorization-scope UPSERTs.

Minor documented behavior: an out-of-order file-check observation is acknowledged with 204 but does not replace a newer check. This preserves the latest known state and does not claim that the supplied observation became current.

## Limits and non-claims

- This validates the API-04 milestone's seven working API modules, not full product readiness or complete implementation of every OpenAPI 2.2 endpoint/schema.
- No server network probe, destructive purge worker, notification generation/delivery worker, or delegated toast task-completion command is implemented here. No physical file is opened, moved or deleted by these APIs.
- Search has no production comment/department-assignment source yet, no external index and no file-content extraction. The supported query/filter subset is documented; unsupported filter DSLs are rejected.
- Desktop-to-new-API user journeys, customer deployment, production-size load, PostgreSQL versions other than this run and full SEC-02 acceptance were not validated by this package. Unit/integration tests and the existing security gate are not a penetration-test certification.
- Canonical `sources/` were not modified. Publication to main was authorized after validation; customer deployment remains a separate operation.

## Publication integrity

Before preparing the commit, all previously recorded source/evidence SHA-256 values were checked and matched: production code and test results had not changed after the successful gates. Only publication metadata and the package builder were then adjusted. Manifest implementation hashes use UTF-8 text with CRLF normalized to LF, matching committed text blobs; artifact hashes use exact bytes, protected by the package-local `.gitattributes`. This avoids false hash failures caused by Windows `core.autocrlf=true`.
