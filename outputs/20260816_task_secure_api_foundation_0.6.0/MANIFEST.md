# Manifest — Task secure API foundation

- Version: `0.6.0`
- Date: `2026-08-16`
- Base commit: `2d2812c6e9f3d2969082080b9c4ec90d88b54616`
- Scope: accepted identity/organization/API foundation; no Task HTTP CRUD

## Included implementation artifacts

- `work/production/docs/secure-identity-organization-api-foundation-decision.md`
- `work/production/src/Task.Application/Security/AuthenticatedRequestContext.cs`
- `work/production/src/Task.Application/Security/PermissionCode.cs`
- `work/production/src/Task.Api/AssemblyInfo.cs`
- `work/production/src/Task.Api/Program.cs`
- `work/production/src/Task.Api/Security/TaskApiProblemResponse.cs`
- `work/production/src/Task.Api/Security/TaskApiSecurityFoundation.cs`
- `work/production/src/Task.Api/Security/TaskFoundationAuthenticationHandler.cs`
- `work/production/tests/Task.ServiceHosts.Tests/Task.ServiceHosts.Tests.csproj`
- `work/production/tests/Task.ServiceHosts.Tests/TaskApiSecurityFoundationTests.cs`

`SHA256SUMS.txt` contains SHA-256 checksums for every included implementation artifact. `VALIDATION_REPORT.md` records the gates executed against this version.

## Security boundary

All mapped API endpoints require authentication by default. Only `/health/live` and `/health/ready` explicitly allow anonymous access. Until the later JWT plus server-session adapter exists, the foundation scheme authenticates no client-supplied identity and returns correlated `AUTHENTICATION_REQUIRED` Problem Details for protected endpoints.

The package does not store a signing key, pepper, password, access token, refresh token or reusable credential.
