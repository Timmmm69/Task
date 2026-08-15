# Task container deployment foundation

Status: production-compatible packaging foundation increment 0.4.0. This is not a production deployment readiness claim.

## Image inventory

The multi-target Dockerfile is `work/production/deployment/containers/Dockerfile`. It builds Linux x64 images with official Microsoft .NET multi-stage images:

| Target | Executable | Final base |
|---|---|---|
| `task-api` | `Task.Api.dll` | ASP.NET runtime `10.0.11-noble-amd64` |
| `task-worker` | `Task.Worker.dll` | .NET runtime `10.0.11-noble-amd64` |
| `task-backup-agent` | `Task.BackupAgent.dll` | .NET runtime `10.0.11-noble-amd64` |
| `task-database-migrator` | `Task.DatabaseMigrator.dll` | .NET runtime `10.0.11-noble-amd64` |

The build stage uses SDK `10.0.400-noble-amd64`. Every base is pinned to its exact tag and immutable linux/amd64 manifest digest. Final images contain only the selected executable's framework-dependent publish output; they do not contain the SDK, source tree or NuGet cache. The built-in non-root `app` user owns and runs the application under fixed `/app`. OCI version and revision labels are supplied at build time.

`task-container-validation` is an additional ephemeral test target. It calls the production `TaskPersistenceRuntime` and `PostgresTaskAggregateStore`; it is not a deployable service image.

## Build commands

Run from the repository root and replace the sample revision with the full Git SHA being packaged:

```powershell
$dockerfile = 'work/production/deployment/containers/Dockerfile'
$context = 'work/production'
$revision = git rev-parse HEAD

docker build --platform linux/amd64 --target task-api --build-arg VERSION=0.4.0 --build-arg GIT_SHA=$revision -t task-api:0.4.0 -f $dockerfile $context
docker build --platform linux/amd64 --target task-worker --build-arg VERSION=0.4.0 --build-arg GIT_SHA=$revision -t task-worker:0.4.0 -f $dockerfile $context
docker build --platform linux/amd64 --target task-backup-agent --build-arg VERSION=0.4.0 --build-arg GIT_SHA=$revision -t task-backup-agent:0.4.0 -f $dockerfile $context
docker build --platform linux/amd64 --target task-database-migrator --build-arg VERSION=0.4.0 --build-arg GIT_SHA=$revision -t task-database-migrator:0.4.0 -f $dockerfile $context
```

The complete disposable gate is:

```powershell
powershell -ExecutionPolicy Bypass -File work/production/verification/Test-ContainerPackaging.ps1
```

It can be launched from any current directory. If Docker is absent or the engine is not linux/amd64, the script reports a failed/unexecuted gate and returns nonzero.

## Required external variables

No connection string or credential is stored in the repository or image. A deployment system must provide:

- the migration connection string as `ConnectionStrings__TaskDatabase` only to the one-shot migrator;
- the runtime connection string under the same environment key only to `Task.Api`;
- version and full Git SHA as non-secret build arguments for OCI labels;
- deployment-specific image names, network and port configuration.

Worker and BackupAgent do not receive database credentials in this increment because their current placeholder loops do not access PostgreSQL.

## Migration/runtime role separation

The fixed database roles are `task_migration` and `task_runtime`. Passwords remain external. `initialize-validation-roles.sql` is a disposable validation setup template; `grant-runtime.sql` is the idempotent post-migration grant contract.

`task_migration` receives database `CONNECT` and `CREATE`, owns the schemas/tables created by the reviewed migration catalog, and owns `infrastructure.schema_migrations`. It runs explicit `status` and `apply` commands.

`task_runtime` receives only:

- database `CONNECT`;
- schema `USAGE` on `infrastructure`, `core` and `work`;
- `SELECT` on `infrastructure.schema_migrations` and `core.organizations`;
- `SELECT`, `INSERT`, `UPDATE` on `core.objects` and `work.tasks`.

It receives no database/schema `CREATE`, ownership, DDL rights, migration-history write, superuser, createdb, createrole or bypassrls capability. Grants name every current table explicitly; future migrations must update this contract explicitly rather than inheriting blanket future-table permissions.

## Startup order

1. Start PostgreSQL 16+ on the internal database network and wait for readiness.
2. Provision the external migration/runtime credentials and fixed roles.
3. Run `Task.DatabaseMigrator status` with the migration role. Exit 6 means migrations are required.
4. Run `Task.DatabaseMigrator apply` as a separate one-shot container.
5. Run `grant-runtime.sql` with the migration role.
6. Run `status` again and require exit 0 with `code=Ready`.
7. Start `Task.Api` with only the runtime-role connection string.
8. Start Worker and BackupAgent independently when required.

The API entrypoint never applies migrations and never receives migration-role credentials.

## Health verification

`Task.Api` listens on HTTP port 8080 inside the container. `/health/live` must return HTTP 200/`Alive`; `/health/ready` must return HTTP 200/`Ready` after migration and grants. TLS is not configured inside the application; a future reverse proxy must terminate TLS.

The validation Compose file publishes the API only on a random `127.0.0.1` host port. PostgreSQL publishes no host port and joins only the internal database network, so it is not exposed to the company user VLAN.

## Security controls

The validation topology fixes these controls for application containers:

- non-root `app` user;
- read-only root filesystem and a bounded `/tmp` tmpfs;
- all Linux capabilities dropped;
- `no-new-privileges:true`;
- Docker init for signal forwarding and zombie reaping;
- direct exec-form `dotnet` entrypoints;
- no privileged mode, Docker socket or source-code bind mount;
- restart policies only for long-lived API/Worker/BackupAgent services;
- no infinite restart for the migration or validation one-shots.

The verification gate also checks that runtime images contain no SDK, expected OCI labels are present, image environment/history contains no validation credential or secret-like connection configuration, the runtime role cannot run CREATE/ALTER/DROP or update migration history, and Worker/BackupAgent exit on SIGTERM within ten seconds.

## Cleanup

The validation script uses a unique Compose project and random test-only credentials in an ignored directory below `work/tmp`. A `finally` path removes containers, networks, volumes and the credential directory after both success and failure, then verifies that no project containers remain.

## Known limitations

This increment provides packaging and a disposable role-isolation gate only. Real credentials are not included in the repository or images. PostgreSQL must not be exposed to the user VLAN. The API runtime role cannot change schema.

Backup/restore orchestration, TLS reverse proxy, image registry and signing, production secrets manager, update strategy, monitoring/alerting and production topology remain outside scope. The current BackupAgent is still a placeholder. No production deployment readiness or Stage 1 completion is claimed; the traceability baseline still has 245 unresolved gaps.
