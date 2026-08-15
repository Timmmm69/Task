# Task persistence foundation

Status: first production-compatible persistence increment; not database-readiness completion.

## Storage contract

- PostgreSQL 16+ is the authoritative durable store.
- `core.objects` owns immutable identity, organization boundary, lifecycle, audit timestamps and the monotonic business version.
- `work.tasks` owns the Task aggregate fields currently implemented by the domain: title, work status, priority, UTC schedule/deadline and completion metadata.
- `(organization_id, id)` is enforced between both tables. Store reads and writes always use both values.
- The immutable domain is hydrated only through `SyncableEntityMetadata.Reconstitute` and `TaskAggregate.Reconstitute`; persistence never uses reflection or mutable domain setters.

The Stage 1 requirements take precedence over omissions in the Stage 2 physical DDL. Therefore the first migration explicitly persists `lifecycle_state_before_trash` (required to restore the previous lifecycle state) and `completed_by` (required by the task lifecycle contract).

## Concurrency and UTC

`PostgresTaskAggregateStore.Save` updates `core.objects` only when `(organization_id, id, expected_version)` matches. The task row update is in the same statement and transaction. A stale version raises `TaskLifecycleConcurrencyException`; a missing same-tenant object raises `KeyNotFoundException`. There is no last-write-wins fallback.

All machine timestamps use PostgreSQL `timestamptz` and .NET `DateTimeOffset` with offset zero. Npgsql 10 rejects non-UTC `DateTimeOffset` values for this type; the domain reconstitution boundary also validates UTC before accepting stored state.

## Migrations and readiness

`TaskPersistenceMigrator` applies ordered embedded SQL under a PostgreSQL transaction and advisory lock. `infrastructure.schema_migrations` records version, name and SHA-256; a checksum mismatch fails closed. Application startup does not call the migrator automatically. Production deployment should apply reviewed migrations before enabling write traffic.

The real PostgreSQL test creates and drops an isolated generated database and runs only when `TASK_POSTGRES_TEST_ADMIN_CONNECTION` is set. Without that environment variable it is reported as skipped, never as proof of PostgreSQL readiness. API readiness remains unchanged until connectivity and applied-schema checks are deliberately wired in a later increment.

## Deliberately deferred

The migration contains only the organization/object/task subset needed by this aggregate. Project relations, assignees, watchers, recurrence, audit, change feed, authorization, API/DTO, dependency injection and readiness wiring remain out of scope. Later migrations must extend the canonical tables rather than introduce a parallel task store.
