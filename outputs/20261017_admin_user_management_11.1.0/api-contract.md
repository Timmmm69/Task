# Employee account API foundation — 11.1.0

Implemented by `DesktopAdministrationApiClient` through the existing
`DesktopAuthenticatedGetExecutor` and `SessionService`. No new authentication,
DI registration, server endpoints, database schema, permission policy or UI is introduced.
The existing administration lists and Employee Picker retain their contracts.

## Client surface for stage 11.2

| Method | Wire contract | Permission |
| --- | --- | --- |
| `GetUserPageAsync(DesktopUserQuery)` | GET `/api/v1/users?sort=id&filter=…&page=…` or `cursor=…`; `Page<User>` | User.Read |
| `GetUserDetailsAsync(id)` | GET `/api/v1/users/{id}`; `User`, strong ETag | User.Read |
| `CreateUserAsync(DesktopUserCreate, key)` | POST `/api/v1/users`; `UserCreate`, Idempotency-Key; 201 User | User.Create |
| `UpdateUserAsync(id, version, DesktopUserPatch, key)` | PATCH `/api/v1/users/{id}`; `UserPatch`, If-Match and Idempotency-Key; 200 User | User.Update |
| `TransitionUserAsync(…, Activate, key)` | POST `…/activate`; expectedVersion body, If-Match and key | User.Create |
| `TransitionUserAsync(…, Block/Deactivate, key, reason)` | POST `…/block` or `…/deactivate`; expectedVersion/reason body, If-Match and key | User.Block |
| `TransitionUserAsync(…, Reactivate/Unblock)` | POST `…/reactivate` or `…/unblock`; no body or explicit key, If-Match | User.Block |

`DesktopManagedUser` represents the existing `User` fields: id, organizationId,
version, displayName, firstName, lastName, login, workEmail, departmentId,
jobTitle, accountStatus, createdAt and updatedAt. Timestamps are optional in
the canonical schema. Required fields, types, nonempty IDs, versions, account
statuses, response identity and strong ETag/body agreement are checked.
Only explicitly mapped permitted fields reach callers.

The list supports only the server's existing text `filter` over display name,
login and work email, stable id order, page and cursor. It has no department or
status filter and no caller-controlled page size (server page size: 100).
Pass `NextCursor` unchanged with the same filter for the next page. When a cursor
is supplied, the client omits page to avoid applying an offset twice. Returned
pages retain total; duplicate IDs, repeated cursor, empty continuation and
oversized/malformed pages are rejected. The API is a live list, not a snapshot.

`DesktopUserPatch` reuses `OptionalUserField<T>` from the application contract.
Leave a field unspecified to preserve it; use `new(true, value)` for a changed
field. `new(true, null)` clears workEmail, departmentId or jobTitle. Null is
rejected for names/login. Empty patches are rejected before transport.
Account status is changed only by a transition, never through PATCH.

Callers must allocate a key once per logical create/update/explicit-key
transition and retain it with the unchanged payload if a result is uncertain.
Never generate a new key just to retry an uncertain write. No network-error,
timeout, 409 or 503 retry or offline write queue is added. The shared executor
alone may refresh once after 401 and replay identical bytes, If-Match, key and
correlation ID. Reactivate/unblock use the existing server's implicit
operation/ID/version key. The caller should prevent concurrent form submissions.

Only confirmed server data is returned as `Succeeded`, with `EntityVersion`
and the `Idempotency-Replayed` receipt. A malformed/absent receipt is never
replaced by an optimistic local mutation. The optional connectivity service
blocks writes while offline/reconnecting; reads can confirm recovery. Session
readiness, including mandatory password change, remains enforced by the existing
executor. Read-only permissions remain authoritative on the server.

`DesktopUserResult<T>` distinguishes AuthenticationFailure, Forbidden, NotFound,
ValidationFailure (400/422), Conflict (409), VersionConflict (412),
PreconditionRequired (428), ServerUnavailable and MalformedResponse.
Stable server codes (including DUPLICATE_RESOURCE, INVALID_STATE_TRANSITION,
IDEMPOTENCY_KEY_REUSED and REQUEST_IN_PROGRESS), correlation and retry metadata
are retained where available. Arbitrary problem bodies/titles are not forwarded
or logged. Cancellation propagates as OperationCanceledException.

## Contract findings and boundaries

- Verified against the concept, Stage 1 architecture, Stage 2.2 OpenAPI,
  endpoints dump, permissions catalog and normative corrections, together with
  `UserEndpoints`, `IUserAccountReadStore` and `IUserAccountCommandStore`.
  The available Stage 2.2 archive is named `…2.2_Repacked.zip`; an extracted
  canonical tree is present in sources. No source artifact was modified.
- Stage 2.2 `UserCreate` lists multiple account statuses, but its normative
  correction and the live endpoint require pending_activation. The client
  omits accountStatus and lets the server apply that default.
- Stage 2.2 `UserPatch` lists accountStatus, while the current endpoint rejects
  it. The client follows the supported seven profile fields and explicit state
  commands. Resolving the schema discrepancy is outside this client task.
- User.ManageRoles and User.ResetPassword exist in the canonical permissions
  catalog; current password reset is `/api/v1/auth/admin-reset-password`.
  Role/password management is not part of this client surface.
- Existing `GetUsersAsync` remains a legacy first-page summary for the unchanged
  WPF administration screen. Stage 11.2 should adopt `GetUserPageAsync` and
  the full-card operations. Transitions have no UI binding in 11.1.
- No live corporate deployment, real database mutation, manual WPF acceptance
  or role-administration acceptance is claimed by this stage.

Validation evidence and the source manifest are in
`outputs/20261017_admin_user_management_11.1.0/` of the delivery package.
