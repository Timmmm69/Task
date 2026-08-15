# Task persistence runtime and readiness

Status: implemented runtime wiring and schema-compatibility readiness increment.

## Configuration and dependency injection

`Task.Api` reads the PostgreSQL connection string from `ConnectionStrings:TaskDatabase`. In environment-variable form this is `ConnectionStrings__TaskDatabase`. No credential is stored in tracked `appsettings` files.

The API container always owns one `TaskPersistenceRuntime`. When a non-empty connection string is present, DI also exposes the runtime-backed `ITaskAggregateStore`, `TaskPersistenceMigrator`, `TaskLifecycleService` and `TaskQueryService`. The runtime owns and disposes the shared thread-safe Npgsql data source.

The API never invokes `TaskPersistenceMigrator.ApplyPending()` during startup or readiness checks. Schema changes remain an explicit deployment action with elevated migration credentials; normal application startup cannot silently change production schema.

## Health contract

`/health/live` reports only process liveness and never contacts PostgreSQL.

`/health/ready` performs a bounded PostgreSQL check and returns HTTP 200 only when all conditions hold:

- the connection string is valid and PostgreSQL is reachable;
- PostgreSQL server version is 16 or newer;
- `infrastructure.schema_migrations`, `core.organizations`, `core.objects` and `work.tasks` exist;
- the applied migration set exactly matches the server's expected version, name and embedded SQL SHA-256.

Every failure returns HTTP 503 with a stable `persistenceCode` and a safe message. Connection strings, database exception text and credentials are never returned. Supported codes are `NotConfigured`, `InvalidConfiguration`, `DatabaseUnavailable`, `Timeout`, `UnsupportedServerVersion`, `MigrationsNotApplied`, `SchemaObjectsMissing`, `SchemaVersionMismatch` and `CheckFailed`.

## Verification

The real PostgreSQL integration gate validates readiness before migration, after migration, through the actual HTTP endpoint, and after deliberate checksum corruption. The ordinary API smoke test validates both the unconfigured state and a configured but unmigrated database. Readiness wiring does not imply deployment readiness, backup readiness, authorization completion or Stage 1 completion.
