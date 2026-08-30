# SEC-01 validation report

- Package: `20260830_sec_01_security_completion_1.0.0`
- Version: `1.0.0`
- Validated: `2026-08-30T21:17:07+03:00`
- Result: PASS

## Implemented controls

- Identity configuration is validated at API startup.
- The active ES256 private key must have a matching public `<kid>.pem` verification key.
- The verification key ring is bounded to current + at most one previous P-256 public key.
- Private, malformed, unreadable, empty and oversized verification key rings fail closed.
- Password pepper is external and must contain at least 32 non-whitespace characters.
- Startup exceptions do not expose key material, pepper contents or configured full paths.
- A documented restart-based key rotation procedure preserves the five-minute JWT lifetime plus
  30-second clock-skew overlap.
- The repeatable gate scans tracked runtime configuration for embedded private keys/credentials.

## Verification evidence

Command:

```powershell
powershell -ExecutionPolicy Bypass -File work/production/verification/Test-SecurityGate.ps1
```

Observed result:

- `Task.Tests`: 753 passed, 2 skipped, 0 failed.
- `Task.ServiceHosts.Tests`: 270 passed, 0 failed.
- `Task.Desktop.Tests`: 220 passed, 0 failed.
- Total: 1,243 passed, 2 skipped, 0 failed.
- Secret scan: passed for tracked runtime appsettings, container YAML and Dockerfile inputs.
- `git diff --check`: passed; only repository line-ending notices were emitted.

The two skipped tests require a real PostgreSQL runtime and are unrelated task-write integration
tests. Existing compiler/analyzer warnings concern deprecated test-host setup and one pre-existing
blocking test call; this change introduced no build error or test failure.

## Residual deployment checks

Host ACLs, approved TLS termination and physical secret handling depend on the target environment
and must be verified during deployment. The application now fails startup when configured key
material is unsafe or inconsistent; it does not claim to validate host ACLs remotely.
