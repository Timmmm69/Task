# SEC-03 validation report — 1.0.0

Result: SOURCE/CONTRACT PASS. Base commit: 65e7008241d854326aa334d3c605cb7b6a22bc08.

## Verified

| Assembly | Passed / total | Skipped |
|---|---:|---:|
| Task.Desktop.Tests | 269/269 | 0 |
| Task.ServiceHosts.Tests | 558/558 | 0 |
| Task.Tests | 790/794 | 4 |

Total: 1617 passed, 4 skipped. The SEC-03 executable contract also generated an
ephemeral CA, edge certificate, PostgreSQL certificate and JWT P-256 key ring outside the
repository; it accepted the complete valid bundle and rejected a DNS-name mismatch. The checked
topology exposes only the TLS proxy, keeps API/database networks internal, passes credentials via
owner-readable files, uses Npgsql VerifyFull and rejects non-TLS PostgreSQL traffic. Docker Compose
configuration parsing and dashboard validation passed. No private key or reusable credential is
included in this package.

## Validation limitations

The repository-wide `dotnet format whitespace --verify-no-changes` gate still reports formatting
differences in pre-existing C# files outside this SEC-03 change; no C# source was modified here.
The Docker daemon and a customer-like endpoint were unavailable, so Compose configuration was
parsed but the containers and live network path were not exercised locally.

## Production sign-off boundary

This package implements the source, CI gate, CSR tooling and operations runbook. It does not claim
that a customer environment, corporate CA, company secrets manager, host ACL or firewall has been
configured. SEC-03 remains a hard release blocker until `Test-ProductionSecretsTls.ps1` passes with
the real `SecretRoot` and trusted live `Endpoint`, and the protected evidence also contains the
firewall/port scan and rotation rehearsal required by the runbook. The certificate metadata in
this package is explicitly ephemeral test evidence, not a production certificate.
