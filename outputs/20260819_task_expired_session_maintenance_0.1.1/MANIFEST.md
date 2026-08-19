# Manifest

- Package: `task_expired_session_maintenance`
- Version: `0.1.1`
- Date: `2026-08-19`
- Base commit: `a04a7842f80450042c192fbff37264789bea848e`
- Branch at implementation: `main`

Increment 0.1.1 fixes a robustness gap found during self-review: `Task.Worker/Program.cs` now registers `ISessionRepository` only when `TaskPersistenceRuntime.IsConfigured` is true, so a malformed `TaskDatabase` connection string no longer crashes the worker at startup (it runs empty passes with a warning, matching the absent-connection-string behavior). The guarded PostgreSQL integration gate was additionally executed against a real database for this increment.

`MANIFEST.sha256` contains SHA-256 values for every implementation, test, documentation, verification and package-metadata file changed or added by increment 0.1.1. Text is hashed after canonical CRLF-to-LF normalization so the same checkout validates on Windows and Linux. Run `Verify-Manifest.ps1` from any location to verify it. The digest file intentionally excludes itself to avoid a recursive digest.