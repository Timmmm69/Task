# PROD-04 validation report — 1.0.0

Result: SOURCE/BUILD/TARGETED TESTS PASS. Base commit: c4fedeb8ab44adb37f7790e0547f4ba47406416a.

## Verified

- Solution build: PASS, zero errors.
- Desktop project client and view-model scenarios: 8/8.
- Product route policies, deny-before-store and HTTP envelope suite: 208/208.
- Product API contract/store unit suite: 15/15.
- Total focused assertions: 231 passed, zero failed.

The Windows client now loads projects, project roles, active members and linked tasks; supports
create/update/status/archive and add/change/remove member operations; applies server responses only;
and handles permissions, auth failures, validation and version conflicts. The canonical
`GET /api/v1/project-roles` endpoint is policy-protected by `Project.Read` and filters roles that a
non-administrator is not allowed to assign.

## Environment boundary

Docker Desktop and `TASK_POSTGRES_TEST_ADMIN_CONNECTION` were unavailable on this host, so a new
live PostgreSQL execution was not claimed. No schema migration was introduced: project-role reads
use the already deployed `iam.roles` and `iam.role_permissions` tables. Existing API-04 project and
member writes retain their previously packaged PostgreSQL evidence. Customer deployment, native
Windows UIA/Narrator walkthrough and an end-to-end server session remain deployment acceptance,
not claims of this source package.
