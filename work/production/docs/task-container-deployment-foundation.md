# Task container deployment foundation

Status: production-compatible packaging foundation with reproducible release tooling 0.5.0. This is not a production deployment readiness claim.

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

### Verified release (0.5.0)

Prerequisites: PowerShell 7+, Git, Node.js, tar, Docker Buildx and a running Linux/amd64 Docker engine with the containerd image store (required to load OCI archives). Internet access to NuGet, Docker Hub and MCR is required. Allow space for two sets of OCI archives and two isolated build caches. Run from a clean committed checkout:

```powershell
pwsh -NoProfile -File work/production/deployment/containers/Build-ContainerRelease.ps1 -Version 0.5.0 -OutputDirectory outputs/20260902_task_container_release_0.5.0
node work/production/deployment/containers/verify-release.mjs outputs/20260902_task_container_release_0.5.0
```

If both independent builds finished but a later import/runtime check failed, rerun the build command with `-Resume`. Resume requires the same version and all original archive/checksum evidence; it revalidates the source archive against Git and rechecks every OCI archive and provenance before retrying the runtime gate. It records the verifier's current commit separately as `verificationRevision` and retains the prior failed release record. It never rebuilds or relabels the original images. A completed release cannot be resumed.

The release command refuses dirty production sources, existing output directories without explicit resume and output paths outside `outputs/`. It archives `HEAD:work/production` directly from Git; untracked files and Windows checkout line endings cannot affect build inputs. The full commit, production tree, source archive hash, tool versions and epoch are recorded in `source.json`. Version and revision are embedded in each deployable image. Container restores use eight committed `packages.lock.json` files, including transitive versions/content hashes, with `--locked-mode` and an explicit NuGet source. When deliberately updating dependencies, regenerate the locks with SDK 10.0.400 and `dotnet restore <project> --runtime linux-x64 --use-lock-file --force-evaluate`, then review the diff before committing.

Both builds use separate empty `docker-container` builders pinned to BuildKit 0.23.2 by its linux/amd64 digest. The Dockerfile frontend and SDK/runtime bases are also digest-pinned. BuildKit receives the source commit timestamp as `SOURCE_DATE_EPOCH`; the OCI exporter rewrites layer timestamps. .NET publishing enables deterministic compilation and continuous integration mode. Cached steps may be shared among targets within one pass; no build or NuGet cache is imported between passes.

Every exported OCI blob is checked against its size and SHA-256. The verifier checks the platform, non-root user, creation timestamp, version/revision labels and mode=max SLSA provenance subject/build arguments. The two **image manifest digests** must match, which also binds the config and all compressed layer digests. Provenance includes actual build timestamps, so the enclosing attestation/index/archive hashes may differ; byte identity of those envelopes is not claimed.

The first-pass OCI archives are loaded and the existing PostgreSQL, role isolation, image hardening, task-store, health and SIGTERM gate consumes those exact images by immutable OCI index ID. The validation-only fifth image is included in evidence and repeatability checks but is not a deployment service. A failed build, comparison, runtime check or cleanup produces a failed report and a nonzero exit.

The package retains both builds' `images/*.oci.tar`, `source.tar`, raw Buildx metadata/logs, extracted OCI manifest/config/provenance, `image-map.json`, `release.json` (manifest and version), `SHA256SUMS`, and `validation-report.md`. Binary archives are ignored by Git; transfer the **whole output directory** to retain the complete offline release. Compact evidence is tracked without line-ending transformations. `verify-release.mjs` requires all listed files, checks every checksum, rejects uncovered files and rechecks image pairs. It does not authenticate unsigned metadata; signatures and registry publication remain separate work.

After a successful release, import an archive with `docker load --input <package>/images/task-api-1.oci.tar`; the saved image tag is `task-release/task-api:0.5.0`. For runtime selection use the immutable OCI index ID in `image-map.json`; use `release.json` image manifest digests when publishing/selecting OCI images in a registry. Never substitute an unverified rebuilt image for the tested artifact.

References: [Docker timestamp rewriting](https://docs.docker.com/build/exporters/image-registry/), [Build attestations and image-store support](https://docs.docker.com/build/metadata/attestations/), [NuGet locked restores](https://learn.microsoft.com/en-us/nuget/consume-packages/package-references-in-project-files#locking-dependencies).

### Development packaging

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
