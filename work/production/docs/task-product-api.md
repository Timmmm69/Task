# API-04 — Product module APIs

Version: 1.0.0. Database schema: 10. Base path: `/api/v1`.

This increment exposes the seven modules named in API-04 through the existing authenticated API host and PostgreSQL runtime. `ProductApiRoutes.All` is the executable route/permission/precondition catalog; `IProductApiStore` is the application port. No additional service, framework or package dependency is introduced.

## Available operations

| Module | Operations |
|---|---|
| Projects | List, create, get, patch, archive/unarchive, trash/restore; membership, roles, permission overrides, transfer of ownership, history |
| Contacts and companies | CRUD and lifecycle; contact channels/addresses, company-contact relations; interactions and participants |
| File catalog | CRUD and lifecycle, bounded virtual tree, cycle-safe move, location registration/change/removal, device-aware resolution, client check observations, network allowlist resources |
| Search | Global search and suggestions across persisted tasks, calendar events, projects, contacts, companies, interactions, catalog items, permitted locations and employees |
| Notifications | Own visible notifications, read, dismiss, bounded read-all, own notification preferences |
| Archive and trash | Permission-filtered lists, retention metadata, versioned restore/unarchive |
| Settings | Own user settings, organization settings; defaults without writing on GET |
| Object relationships | Typed links; both endpoints must be readable and changing the source requires its update capability |

These routes supplement, rather than replace, the existing task/calendar/auth/audit endpoints. The implementation extends the DATA-04 tables with companies, relationships, project membership, file locations, check observations and a query snapshot cache.

## Calling the API

Use the existing JWT login/session flow. Organization, user and device identity come from validated server session context, never from a caller-supplied organization header. Permission decisions use the existing lower-case backing capability codes. New capabilities are seeded by migration 10; existing organization-management roles inherit their previous grant/deny effect. Other roles need explicit grants.

Create requests and commands marked `Idempotent` in `ProductApiRoutes.All` require an `Idempotency-Key` of 8–200 printable non-space ASCII characters. An identical retry returns the committed result; reusing a key with different request content returns 409. Current authorization is checked on HTTP requests, including replays.

Versioned mutations require one strong `If-Match: "vN"` header. Missing headers return 428; malformed headers return 400; stale versions return 412. Successful object reads/writes return the corresponding ETag. Child mutations normally use the parent aggregate ETag; membership overrides use the member ETag and required `expectedMemberVersion`. A notification read accepts an optional If-Match. Removal of CRM/member/location relationships and a file-check acknowledgement return 204; moving an object to trash returns a 202 receipt.

For example, after authentication:

```http
POST /api/v1/projects
Authorization: Bearer <access-token>
Content-Type: application/json
Idempotency-Key: project-create-0001

{"name":"Office rollout","ownerUserId":"<current-user-uuid>"}
```

Patch the returned ID using its ETag, e.g. `PATCH /api/v1/projects/{id}` with `If-Match: "v1"` and `{"description":"First delivery"}`. Owner IDs, project roles, related objects and device IDs must be valid within the authenticated organization.

JSON uses camelCase. Unknown writable fields, invalid scalar types, duplicate JSON properties, spoofed metadata, incompatible links, unsafe URL schemes, invalid local paths and UNC paths outside registered active roots are rejected. The organization request-size ceiling applies before JSON parsing. Problems use the existing error envelope and correlation ID; SQL, credentials and raw paths are not returned in error details.

Lists support bounded `limit` (default 50, maximum 200), `page` or `cursor`, returning `items`, `hasMore`, `nextCursor`. Do not combine page and cursor. Stable list ordering is ID ascending; unsupported sort/filter keys fail explicitly. The tree supports depth 1–8 with a 1,000-node bound. Object-link visibility filtering and pagination happen in SQL, without locking every linked object on GET.

Search accepts the Stage 2.2 filter names, comma-separated UUID/type lists and UTC date bounds. Search text is 2–200 characters (suggestions allow one), limit is 1–500. Results use deterministic relevance/updatedAt/type/ID ordering. Opaque cursors identify a 15-minute server snapshot bound to normalized filters, user, organization, permissions and authorization scope version. Authorization/lifecycle is rechecked before returning cached results. Invalid/mismatched cursors return 400; expired snapshots/scopes return 410. Queries exceeding 10,000 candidates must be narrowed. Suggestions return an array.

## Transaction and security boundaries

Writes commit object metadata, module data, lifecycle ledger, existing domain events, existing outbox and redacted audit together. Idempotency results are in the same transaction. Optimistic versions and tenant-scoped write serialization protect concurrent commands; catalog operations also share the existing catalog hierarchy lock. Database commands have cancellation, a 3-second lock timeout and 15-second statement timeout.

Project visibility is restricted to the owner, manager, active members or organization administrator. Project mutations also require the project role/override, with deny taking precedence for members. Membership/ownership changes increment authorization scope versions. Notification access is recipient-only. Cross-tenant relations are rejected. Sensitive location fields require the owning user/device or the explicit sensitive-path capability; opening a local path additionally requires the session's owning device. Search does not expose raw paths.

File location resolution only selects metadata and reports that a client check is required. It never opens, copies, moves or deletes physical files. Check-result records a validated observation from the session's device; it is not evidence that the server itself accessed a share.

## Deployment

Run the existing offline migrator with schema-owner credentials, then apply `deployment/containers/sql/grant-runtime.sql` for the runtime role. Migration `010_product_api.sql` is additive except for extending the existing domain-event type constraint; migrations 1–9 are unchanged. Runtime DDL and hard deletion of product objects remain forbidden. Readiness and server capabilities now expect schema 10. Existing rows remain the source of truth; no second product database is created.

## Scope limits

API-04 means the named modules have usable server APIs, not that the complete Stage 2.2 catalog, desktop UI or every background workflow is finished. This package does not implement network probing from the server, destructive purge jobs, notification generation/delivery, delegated toast actions such as task completion, or an administrative feature-flag editor. Existing capabilities remain the feature-discovery surface. These require their respective operational/client workflows; no placeholder success is returned for them.

Search currently covers persisted production entity types. Comments and department assignment persistence are not provided by DATA-04, so those sources cannot produce matches here. There is no external index or file-content search. Broad filter/sort DSLs are not accepted; supported filters are explicit in the route store. This is not a claim of full OpenAPI 2.2 response-schema conformance for the entire product.

## Verification

`ProductEndpointsTests` exercises every route's fail-closed permission gate plus HTTP response serialization, identity, ETags, idempotency headers, malformed/duplicate JSON, payload limits and safe failures. `PostgresProductApiTests` runs real transactional workflows, constraints, limited runtime grants, concurrency, tenant isolation, membership, redaction, search snapshots and lifecycle transitions. Set `TASK_POSTGRES_TEST_ADMIN_CONNECTION` to an isolated PostgreSQL test cluster to exercise database tests; running without it is not sufficient evidence of database correctness. The final package contains TRX results and hashes, and states the exact environment used.
