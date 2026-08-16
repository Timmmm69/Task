# Secure identity, organization and API foundation decision

- Status: Accepted
- Date: 2026-08-16
- Approved: 2026-08-16
- Scope: Stage 1 identity/organization/authorization/API foundation; no Task HTTP CRUD

## Context

The current production baseline has a PostgreSQL organization boundary in task persistence, separate migration/runtime database roles, readiness checks, health endpoints and correlation-ID logging. It has no authenticated identity, server session, authorization policy engine, identity/audit persistence, secure API endpoints or public Task DTOs.

This decision records the security contract that must exist before any business endpoint is published. It does not claim authentication, authorization, audit or production readiness.

## Canonical constraints

The following are requirements, not implementation options:

1. The first version serves one provisioned organization, has no public registration and gives each employee an individual account.
2. Login uses a username and password over TLS. The server creates a device-bound server session and returns a short-lived signed access token plus a rotating opaque refresh token.
3. The server stores only a password hash and refresh-token hash. Password hashing is memory-hard Argon2id with per-user salt and a server-side pepper outside the database.
4. Every authenticated request validates the access token, active server session, active account, credential version, authorization-scope version and organization context.
5. Organization identity comes only from the validated server session/token pair. A body, query, route or custom header cannot select or override the organization for an ordinary user request.
6. Authorization is server-side, deny-by-default RBAC + ReBAC + ABAC. Explicit deny wins. Roles expand to permissions centrally; business modules never check role-name strings.
7. Query authorization must become an access-aware database predicate. Loading all rows and filtering them in memory is prohibited.
8. Sensitive denies and all administrative bypasses are audited. Security audit and object history are distinct access scopes. Audit rows are append-only and include organization and correlation identifiers.
9. API errors use `application/problem+json` with a stable code, safe message, correlation/trace identifiers and retry guidance. Validation errors add stable field paths. Hidden or cross-organization objects return the same not-visible result and do not disclose existence.
10. Passwords, tokens, authorization headers, password hashes, raw query payloads and other listed sensitive data are never written to operational logs or audit.

Primary sources:

- `sources/concept/Task_Concept_Final.txt`, sections 3.1, 3.2, 7, 24 and 25.
- `sources/stage_1/architecture_organizer.md`, sections 2.6, 3.8–3.10, 5.3–5.4, 9, 10, 14.2 and 15.
- `sources/stage_2_2/Organizer_Stage2_Technical_Specification_2.2/docs/01_core_domain_and_data.md`, identity/authentication and authorization rules.
- `sources/stage_2_2/Organizer_Stage2_Technical_Specification_2.2/docs/04_adr_and_independent_audit.md`, ADR-002 and ADR-003.
- Stage 2.2 `catalogs/permissions.csv`, `catalogs/errors.csv` and `openapi/openapi.yaml`.

## Proposed decisions

### D1. Authenticated identity source

Use the canonical server-session model:

- five-minute signed JWT access token;
- opaque random 256-bit refresh token, one-time rotation, only its hash persisted;
- server-side session is authoritative for revocation, expiry and device binding;
- access-token claims identify user account, session, organization, credential version and authorization-scope version, but do not carry the full permission list;
- each request reloads or safely caches the authoritative session/account/scope state; a valid JWT alone is insufficient;
- refresh-token reuse revokes the token family/session and creates a critical security-audit event.

No Windows username, forwarded username, client certificate subject or custom identity header is an authenticated application identity in this increment.

### D2. Organization boundary

Create an immutable request identity context only after token and session validation. It contains `UserAccountId`, `SessionId`, `OrganizationId`, `CredentialVersion`, `AuthorizationScopeVersion` and correlation/trace identifiers.

Application commands receive identity/organization from this context, never from public DTO fields. Infrastructure queries retain mandatory organization predicates. Cross-organization absence and unauthorized invisibility map to `OBJECT_NOT_VISIBLE`/404. System operations that can cross the boundary require a separate explicit capability and mandatory audit; none are exposed in the first increment.

The single-organization MVP is a provisioning/UI constraint, not permission to use a global constant as the request tenant boundary.

### D3. Permission model

The permission codes in the canonical Stage 2.2 permission catalog are stable identifiers. Evaluation order is:

1. authenticated active account and active session;
2. organization match;
3. applicable security/state deny;
4. explicit user/role/department deny;
5. explicit grant;
6. project-role/relationship grant;
7. scoped global-role grant;
8. default deny.

Endpoint policy checks whether a capability is possible. The application layer performs object-level and field/state checks. Security-relevant attributes are rechecked immediately before commit when a command changes them. Authorization returns a structured decision with a safe reason code; only privileged diagnostics may expose detailed reasoning.

### D4. Correlation, errors and audit

- Accept a valid `X-Correlation-ID` UUID or generate one; always return it.
- Keep the platform trace identifier separately and expose both identifiers in Problem Details.
- Reject malformed request correlation IDs by replacement, as the current baseline does; never treat a client correlation ID as trusted identity or audit evidence.
- Map authentication/session states to the canonical stable codes and HTTP statuses (`AUTHENTICATION_REQUIRED`, `INVALID_CREDENTIALS`, `SESSION_EXPIRED`, `SESSION_REVOKED`, `REFRESH_TOKEN_REUSE`, `ACCOUNT_BLOCKED`, `ACCOUNT_LOCKED_TEMPORARILY`).
- Map policy denial to `FORBIDDEN`/403 only when the object may safely be known; otherwise use `OBJECT_NOT_VISIBLE`/404.
- Sanitize unhandled failures as `INTERNAL_ERROR`/500; never return exception text or stack traces.
- Persist security audit in the same transaction as the security state change. Operational logs remain non-authoritative and contain only safe metadata.

### D5. Development and test strategy

- There is no header-based, environment-variable-user or automatically authenticated development scheme.
- Production and Development fail closed when signing material, pepper or required identity configuration is absent. Health/liveness remain anonymous; business endpoints remain unavailable until the secure foundation is configured.
- Component tests use explicit ephemeral signing material and disposable PostgreSQL identity/session fixtures. They exercise the real authentication handler, session validation and authorization pipeline.
- Unit tests may construct an internal request identity context directly only below the HTTP authentication boundary.
- Seed users and credentials exist only in test fixtures or an explicit local provisioning command. They are never embedded in appsettings, images, migrations or production compose files.
- A development convenience login, if ever requested, must still use the real password/session flow and opt-in provisioning; it is not evidence of production authentication readiness.

## Decisions requiring approval

The canonical sources define the security properties but do not uniquely select these implementation details. The recommended choices are:

1. **JWT signing and rotation:** asymmetric ECDSA P-256 signing, with key ID, issuer and audience validation, current + previous verification keys during bounded rotation, and private key loaded from an external secret/file mount. No signing key in repository, appsettings, environment committed to compose, or database. Alternative: RSA-3072 under the same lifecycle. Symmetric shared-secret signing is not recommended because it broadens signing authority to every verifier.
2. **Initial administrator bootstrap:** an offline one-shot provisioning command executed with migration/provisioning credentials. It creates the single organization, first employee/account and temporary one-time credential, then records audit evidence and refuses to run after bootstrap completion. No anonymous HTTP bootstrap endpoint.
3. **Argon2id provider:** add one maintained .NET Argon2id dependency only after its .NET 10 compatibility, native/runtime behavior, constant-time verification path and license are verified. Store algorithm/version/parameters with each hash and benchmark the canonical 64–128 MiB, 3-iteration, parallelism-2 baseline on the target server. Do not substitute PBKDF2 merely because it is built into the framework.

Approval of this record approves the recommended choices above. Selecting RSA instead of ECDSA changes only item 1; selecting any online bootstrap or non-Argon2 password scheme requires a separate security decision.

## First implementation increment after approval

The first increment is intentionally narrower than login or Task CRUD:

1. internal identity, session and authorization contracts in Application;
2. canonical permission/error identifiers and Problem Details mapping;
3. API correlation/error pipeline with explicit anonymous-health policy and fail-closed default authorization;
4. configuration validation for issuer, audience and external signing/pepper references without storing secrets;
5. tests proving health remains available, an unmarked endpoint is denied, organization cannot be supplied by the client, errors are sanitized/correlated, and no development bypass exists.

It does not add identity database migrations, login/refresh endpoints, seed accounts, Task endpoints or a production-authentication claim. Those follow as separately reviewed increments after the foundation contract is executable.

## Compatibility with current boundaries

- `Task.Api` remains composition/middleware only and may reference Application and Infrastructure.
- Authorization abstractions and request identity belong in Application; domain task behavior remains independent of HTTP/authentication.
- Persistence implementations belong in Infrastructure; schema changes remain owned by DatabaseMigrator and never run at API startup.
- Runtime and migration database roles remain separate. Future identity/audit tables require explicit least-privilege runtime grants; the runtime role never receives DDL or migration-history write access.
- Existing task persistence methods currently accept caller-provided organization/user IDs. They are not safe public API boundaries and must only be reached through the approved application identity context when Task HTTP endpoints are later introduced.

## Consequences and stop conditions

Implementation must stop and return to decision review if any change would:

- accept user, role, permission or organization identity from a client header/payload;
- validate JWT without authoritative session/account state;
- put secrets or reusable credentials in tracked configuration;
- expose Task CRUD before object-level authorization and public DTO/error contracts exist;
- weaken the migration/runtime database-role split;
- introduce an anonymous bootstrap endpoint;
- claim production authentication readiness before external secret loading, TLS termination, key rotation, lockout/rate limiting, audit persistence and container validation are verified.
