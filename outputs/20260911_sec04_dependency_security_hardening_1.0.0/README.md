# SEC-04 dependency and security hardening 1.0.0

This evidence package records the first completed SEC-04 increment: fail-closed direct and transitive NuGet auditing on every production CI run, plus an independent weekly/manual replay of dependency, configuration/TLS, security, and production-container hardening gates.

Implementation remains in the repository at the revisions listed in `manifest.json`; this directory contains verification metadata rather than a deployable binary bundle.

See `validation-report.md` for executed checks and hosted-run evidence. Verify this package with `Get-FileHash -Algorithm SHA256` against `SHA256SUMS`.
