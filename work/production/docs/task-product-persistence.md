# DATA-04 — Product persistence

Version: 1.0.0

This increment supplies the PostgreSQL persistence layer for the modules named in DATA-04. It does not add HTTP endpoints, desktop screens, file bytes, notification delivery workers, purge jobs or synchronization consumers.

## Storage ports

| Port | Durable projection |
|---|---|
| `IProjectStore` | `projects.projects` plus `core.objects` |
| `IContactStore` | `crm.contacts` plus `core.objects` |
| `ICatalogItemStore` | `files.catalog_items` plus `core.objects` |
| `INotificationStore` | `notify.notifications` plus `core.objects` |
| `IProductSettingsStore` | `core.organization_settings`, `org.user_settings`, `notify.notification_preferences` |
| `IProductLifecycleStore` | Current `governance.archive_entries` and `governance.trash_entries` |

`TaskPersistenceRuntime` creates every port, and the API composition root registers them when PostgreSQL is configured. These are application persistence snapshots, not new public HTTP DTOs or permission codes.

## Guarantees

- Object identity and tenant are always included in reads and writes. Cross-tenant project owners/managers, catalog parents/creators and notification recipients/sources are rejected. No file contents or credentials are stored in the catalog.
- `Add` accepts initial active version-1 metadata. `Save` requires exactly `expectedVersion + 1`, locks the stored object, checks the previous version, preserves creation metadata and commits object metadata and the module projection together.
- Archived/trashed objects are read-only until restored. Lifecycle changes cannot hide content edits in the same save. Archive/trash ledger changes run in the same database transaction through the shared `core.objects` trigger, including existing task and calendar stores.
- Archive and trash history is retained after restoration. Trash retention uses the organization's persisted setting, falling back to the canonical 30-day default when no settings row exists. Purge is not executed by these stores.
- Catalog hierarchy changes serialize within one organization and reject both sequential and concurrent cycles. A live/restored item cannot remain under a trashed parent.
- Notification content, recipient, source, deduplication key and action payload are immutable after creation; JSONB formatting does not count as a content change. Read/dismiss timestamps cannot be rewritten and terminal user states cannot be reversed.
- Settings have their own optimistic versions. Missing settings return `null`; callers explicitly persist defaults through `Add*`. User settings validate unique weekend days, workday ranges, JSON-object preferences and the canonical field bounds.

## Migration and operations

Migration `009_product_entity_stores.sql` is additive. Migrations 1–8 remain unchanged. The runtime expects version 9 and readiness checks the new tables and enabled lifecycle trigger. The offline migrator remains the only schema-changing production component.

The runtime grant script includes the new schemas and only the required SELECT/INSERT/UPDATE privileges. Runtime DDL and object hard-deletion remain denied. PostgreSQL integration tests embed and execute that actual grant script against a disposable non-superuser role.

Upgrade backfill preserves pre-v9 archived/trashed object state. Existing `core.objects` did not record a distinct archive actor; backfill uses its last `updated_by` as the available actor, without claiming to reconstruct lost historical attribution. New transitions record the actual metadata actor and timestamp.

## Source mapping

The field contracts are grounded in `sources/stage_2_2/Organizer_Stage2_Technical_Specification_2.2/db/001_initial_schema.sql`, `db/003_audit_corrections.sql` and the corresponding `Project`, `Contact`, `CatalogItem`, `Notification`, `OrganizationSettings`, `UserSettings`, `ArchiveEntry` and `TrashEntry` OpenAPI schemas. `sort_order` uses the current OpenAPI integer contract. Actor IDs in the shared lifecycle ledger retain the existing production `core.objects` semantics; authentication and authorization validate request actors.

## Verification

`PostgresProductStoresTests` covers real PostgreSQL round-trips, null fields, tenant boundaries, foreign-key rollback, stale versions, settings timestamp ordering, runtime grants, catalog concurrency, immutable notifications, lifecycle retention, existing calendar integration and a v8-to-v9 upgrade with existing data. The full production solution and security gate must also pass before this increment is considered validated.
