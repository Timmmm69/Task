# SEC-02 validation report — 1.0.0

Result: PASS. Schema version 13. Base commit: a6cd27aebbf011ce21350598f79ca274bc42b9a4.

## Verified

| Assembly | Passed / total | Skipped |
|---|---|---|
| Task.Desktop.Tests | 269/269 | 0 |
| Task.ServiceHosts.Tests | 554/554 | 0 |
| Task.Tests | 791/791 | 0 |

Total: 1614 passing tests. The full solution ran against a disposable local PostgreSQL 16 instance, including schema upgrades and the non-superuser runtime grants. No customer database was used.

Coverage includes private contacts/catalog/interactions, project inheritance and revocation, tree and search visibility, personal tasks/calendar/recurrence, generated task assignees, explicit-deny precedence, system role allowlists, department and expiration scope, stale request capability revocation, atomic role replacement/idempotency/versioning, last-administrator role removal and account blocking. Existing endpoint tests cover route authorization and pre-handler denial.

Additional checks: security gate, architecture boundaries, dashboard order/validation and git diff whitespace validation passed. Existing analyzer warnings are not new runtime failures. No manual desktop UX acceptance or customer deployment was performed; TLS/secrets and physical SMB ACL remain separate concerns.

## Deployment

Apply migration 013 through the normal migrator and reapply the runtime grant script. Assign roles explicitly: business roles are never automatically granted to existing users. Administrative roles must be permanent and organization-wide. Reserved system-role collisions stop migration rather than overwriting a custom role. See source/work/production/docs/SEC-02-authorization.md for scope rules.

This package contains the reviewed source delta and test evidence, not a full installer. Sources under sources/ were not modified. SHA-256 entries in manifest.json were recalculated from every packaged file; the ZIP has a separate SHA-256 file.
