# API-01 validation report — 1.0.0

Result: PASS. API-01 is complete in the validated source baseline.

| Suite | Passed / total | Skipped |
| --- | --- | --- |
| task.desktop.tests | 259/259 | 0 |
| task.servicehosts.tests | 550/550 | 0 |
| task.tests | 784/784 | 0 |

Total: 1593 passed, zero failed, zero skipped. These are real PostgreSQL-enabled full solution runs, not the environment-guarded no-database path.

Command: `pwsh -NoProfile -File work/production/verification/Test-IdentityApi.ps1 -Filter ''`.

The tests cover migration application, limited runtime permissions, user create/update/activate/block/unblock/deactivate/reactivate/reset, version conflicts, idempotency replay and mismatch, tenant isolation, session/device ownership, stable temporary-password expiry, token/session revocation, current-session metadata, malformed JSON, device-key validation, authentication, refresh, password change and existing API/desktop regressions. Existing product-event types are preserved by migration 012. Architecture boundary and Git whitespace checks were also run.

The new PostgreSQL lifecycle tests execute against a disposable PostgreSQL 16 cluster using a non-superuser runtime role. Stored audit reasons contain no credential material. Full raw TRX files are included for independent inspection.

Dashboard: API-01 is done/100 and recommended ordering was recalculated. The separate global dashboard validator reports the pre-existing `SEC-02: invalid progress` because SEC-02 is 55 in both the baseline and current roadmap while its validator accepts only 0/25/50/75/100. SEC-02 readiness and dashboard validation rules were not changed to hide this unrelated inconsistency. This does not affect the product test gate above.

Compatibility: current-session desktop metadata remains as documented additive fields; session lists now use SessionPage. See `source/work/production/docs/api01-identity-lifecycle.md` for supported query shapes, permission mapping and deployment order.

Scope: source implementation and reproducible automated acceptance of API-01. This does not claim a production deployment or completion of unrelated roadmap items. Existing ASP.NET deprecation and desktop test-analyzer warnings are not introduced by this work.

This is a source delta against repository baseline e03bcf077afad450e9abec2421e2f4449a121f39. Apply with the repository's normal release procedure and run the database migrator before the new API.
