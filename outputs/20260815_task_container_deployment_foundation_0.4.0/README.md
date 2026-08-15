# Task container deployment foundation 0.4.0

This package records the first production-compatible Linux container packaging foundation for the Task server executables. It provides four pinned, non-root runtime image targets, explicit migration/runtime PostgreSQL role separation, a hardened validation-only Compose topology, and a real PostgreSQL 16 verification gate.

Primary artifacts:

- `work/production/deployment/containers/Dockerfile`
- `work/production/deployment/containers/compose.validation.yaml`
- `work/production/deployment/containers/sql/initialize-validation-roles.sql`
- `work/production/deployment/containers/sql/grant-runtime.sql`
- `work/production/verification/Test-ContainerPackaging.ps1`
- `work/production/docs/task-container-deployment-foundation.md`

Run the container gate from any directory:

```powershell
powershell -ExecutionPolicy Bypass -File C:\path\to\Task\work\production\verification\Test-ContainerPackaging.ps1
```

Run `Verify-Manifest.ps1` from any directory to verify the canonical CRLF-to-LF SHA-256 manifest.

This package contains no images, binaries, credentials, database volumes or build cache. It does not claim production deployment readiness or completion of Stage 1; 245 traceability gaps remain unresolved.
