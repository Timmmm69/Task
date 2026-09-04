# API-01 — identity lifecycle, version 1.0.0

The production API supports account administration, device management and server-authoritative sessions. New accounts start pending activation; an administrator sets a temporary password and activates the account. Password change remains mandatory before business operations. There is no public registration or online bootstrap.

## Contract

| Operation | Permission | Concurrency / retry |
| --- | --- | --- |
| GET `/api/v1/users`, GET `/users/{id}` | User.Read | Stable ID paging; detail ETag |
| POST `/users` | User.Create | Idempotency-Key; pending account, HTTP 201 |
| PATCH `/users/{id}` | User.Update | If-Match and Idempotency-Key |
| POST `/users/{id}/activate` | User.Create | If-Match, expectedVersion, Idempotency-Key |
| POST `/users/{id}/block`, `/deactivate` | User.Block | If-Match, expectedVersion, reason, Idempotency-Key |
| POST `/users/{id}/unblock`, `/reactivate` | User.Block | If-Match; natural per-version replay key |
| POST `/auth/admin-reset-password` | User.ResetPassword | targetUserId, expectedVersion, temporaryPassword, Idempotency-Key |
| GET `/devices`, GET `/devices/{id}` | Device.ReadOwnOrAll | Own devices; all requires identity.account.manage |
| PATCH `/devices/{id}` | Device.UpdateOwnOrAll | Own devices; all requires identity.account.manage; If-Match |
| POST `/devices/{id}/heartbeat` | Authenticated owner | observedAt, appVersion; revoked/foreign devices hidden |
| POST `/devices/{id}/revoke` | Device.Revoke | Own devices; all requires identity.account.manage; If-Match, expectedVersion, reason, Idempotency-Key |
| GET `/auth/sessions` | Session.ReadOwnOrAll | SessionPage; userId, page, cursor; administrator organization scope |
| POST `/auth/sessions/{id}/revoke` | Session.RevokeOwnOrAll | Own session; all requires identity.account.manage; repeated revoke is a no-op |

All abbreviated routes above are under `/api/v1`. Tenant and actor come from the authenticated request, never from the JSON body. Foreign objects and foreign-owner sessions/devices are concealed with `OBJECT_NOT_VISIBLE`. Object versions use strong `"vN"` ETags; absent If-Match is 428, stale version is 412, and idempotency mismatch is 409. Wrong JSON types, duplicate user properties and invalid field values fail before persistence. Account status transitions use dedicated operations instead of profile PATCH.

User and device writes commit the object change, audit, domain event, outbox and idempotency receipt together. Request fingerprints include the HTTP method, target path, If-Match and canonical JSON. A retry cannot replay a receipt for another target or version. Administrative reasons are retained in restricted audit metadata; credential material is excluded from receipts stored in the database, audit and events.

Temporary passwords expire 24 hours after the database transaction timestamp. The exact expiry is stable on replay. Login and authenticated session validation reject expiry; successful password change clears it. Reset increments the credential version and revokes every session and refresh token for the account. Block/deactivate invalidate the authorization scope and revoke sessions. Device revocation invalidates its sessions and refresh tokens, and request validation also checks device state to cover races with login.

Refresh checks the presented device key against the bound fingerprint, the device owner and revocation, and current account/session/credential/scope state before token rotation. Existing refresh-reuse detection, login lockout, logout, logout-all and password-history checks remain covered by the regression gate.

## Compatibility

`GET /auth/session` now includes user, device, permissionCodes, capabilities, scopeVersion and the validated token's accessExpiresAt. Existing desktop fields userId, credentialVersion, authorizationScopeVersion and mustChangePassword remain as additive compatibility fields from the accepted identity foundation. Existing desktop consumers therefore continue working. This is a documented compatibility extension to the strict Stage 2 schema, not a claim that the response has no additional properties.

`GET /auth/sessions` uses the canonical page shape rather than the earlier bare-array simplification. There is no production desktop consumer of that bare-array endpoint. Dates in session resource items are UTC. Sessions use a bounded 100-item page. User/device searches use a text filter and stable ID sort (`id` or `+id`). Login-attempt reads keep the existing cursor API and security-audit backing permission `audit.entry.read`, expose `SecurityAudit.Read`, and apply actor/event filters in PostgreSQL before paging.

## Migration and operation

Migration 012 introduces the dedicated permissions, profile department reference and device metadata, and temporary credential expiry. Existing account-administration role grants/denies are expanded to the new capabilities. The domain-event constraint retains existing task, calendar, recurrence and product event types while adding user/device events. Previous migrations are unchanged. Runtime grants already cover these tables; the API does not receive migration privileges or run migrations at startup.

Deploy with the normal database migrator before starting the new API. Existing short-lived sessions must refresh their capability list after administrative permission changes. Configure employee role grants for the own-session/device capabilities as appropriate; cross-user scope always additionally requires account administration. No production deployment was performed as part of this source completion.

## Verification

Run `pwsh -NoProfile -File work/production/verification/Test-IdentityApi.ps1 -Filter ''` from the repository root for the complete solution gate with disposable PostgreSQL 16. The script needs the local PostgreSQL 16 binaries and .NET 10. It starts a loopback-only cluster, passes the integration-test connection explicitly and stops its own cluster in finally. The ASCII temporary directory is required by PostgreSQL on Windows and is separate from production data.

The focused default filter tests account and device lifecycle against the real schema using a non-superuser runtime role. Full runs additionally cover existing auth, policy, persistence, API and desktop behavior. Release evidence, test result files, source hashes and the validation report are in `outputs/20260905_api01_identity_lifecycle_1.0.0/`.
