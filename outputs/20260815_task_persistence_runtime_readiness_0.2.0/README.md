# Task persistence runtime and readiness 0.2.0

This cumulative package is the current persistence handoff for both implemented increments.

It contains the PostgreSQL Task aggregate foundation plus:

- API dependency-injection wiring from `ConnectionStrings:TaskDatabase`;
- shared Npgsql runtime ownership and disposal;
- store, migrator and application-service registration when configured;
- bounded PostgreSQL 16 connectivity and schema-compatibility readiness checks;
- exact migration version/name/SHA-256 verification;
- fail-closed, credential-safe readiness codes;
- real PostgreSQL and real HTTP positive/negative integration coverage.

Migrations are never applied automatically by API startup or readiness. This package does not claim deployment, authorization, backup or Stage 1 completion. The Stage 1 baseline still has 245 unresolved gaps.
