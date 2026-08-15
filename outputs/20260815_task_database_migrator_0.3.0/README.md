# Task database migrator 0.3.0

This package delivers the explicit, one-shot `Task.DatabaseMigrator` for safe PostgreSQL schema inspection and application before a new API version starts.

It includes shared read-only migration inspection, exact history-prefix validation, atomic asynchronous application, a fail-fast transaction advisory lock, stable credential-safe CLI output and exit codes, PostgreSQL 16/15 integration coverage, repository boundary checks and operator documentation.

The API still never applies migrations during startup or readiness. This package does not claim rollback, role creation, backup orchestration, container publication, deployment readiness or Stage 1 completion. The Stage 1 baseline remains resolved 1001 / unresolved 245.

Run `Verify-Manifest.ps1` to verify the package. The verifier canonicalizes CRLF to LF before SHA-256 calculation, making the result independent of Git checkout line endings.
