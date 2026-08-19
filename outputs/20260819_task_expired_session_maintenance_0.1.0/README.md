# Task expired session maintenance 0.1.0

This package delivers the periodic `ExpiredSessionMaintenanceWorker` in `Task.Worker` that keeps the session store within its retention rule: expired refresh tokens and sessions must be removed, not kept.

It adds two maintenance operations to `ISessionRepository` (`PurgeExpiredRefreshTokensAsync`, `PurgeExpiredSessionsAsync`) implemented in `PostgresSessionRepository` as parameterized, batched DELETE statements in oldest-first order. Tokens are purged before sessions because `iam.refresh_tokens` references `iam.sessions` with `ON DELETE RESTRICT`. Sessions still referenced by append-only audit entries are skipped because `governance.audit_entries.actor_session_id` uses `ON DELETE RESTRICT`; their removal is owned by the separate audit retention policy.

The worker runs every 60 minutes, purges batches of 1000 until a batch comes back non-full, uses a 30-day retention cutoff and never stops the hosting loop on failure (database unavailability is logged as a warning and retried on the next pass). Without a configured `TaskDatabase` connection string the worker keeps running with empty passes and logs a warning; no connection string was added to the worker's `appsettings`.

Coverage includes worker unit tests (batch loop, empty pass, exception survival, DI resolution without a repository) and a guarded PostgreSQL integration gate for purge semantics and batch limits. No audit retention (7 years), API, Desktop, schema or business-requirement changes are included.

Run `Verify-Manifest.ps1` to verify the package. The verifier canonicalizes CRLF to LF before SHA-256 calculation, making the result independent of Git checkout line endings.