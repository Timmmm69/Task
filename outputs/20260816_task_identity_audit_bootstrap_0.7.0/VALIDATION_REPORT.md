# Validation report

Executed locally on 2026-08-16:

- `dotnet test work/production/Task.sln --logger "console;verbosity=minimal"` — passed: Task.Tests 174, Task.ServiceHosts.Tests 40, Task.Desktop.Tests 12; total 226; no failures.
- `dotnet run --project work/production/src/Task.DatabaseMigrator -- --help` — reports `status|apply|bootstrap-admin`.
- `dotnet run --project work/production/src/Task.DatabaseMigrator -- bootstrap-admin` with no connection configuration — safely reports `TASK_DB_MIGRATOR code=NotConfigured` and no secret value.
- `git diff --check` — clean.

The real PostgreSQL integration gate was expanded to apply migration 002, bootstrap an administrator, reject a second bootstrap and verify audit deletion is denied. It could not be executed locally because Docker Desktop's Linux engine was unavailable and `TASK_POSTGRES_TEST_ADMIN_CONNECTION` was not configured.
