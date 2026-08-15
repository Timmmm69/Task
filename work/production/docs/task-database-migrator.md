# Task database migrator

Status: implemented explicit PostgreSQL migration runner increment 0.3.0.

## Commands and configuration

Build once, then run the one-shot executable before starting a new API version:

```powershell
$env:ConnectionStrings__TaskDatabase = '<migration-role connection string>'
dotnet Task.DatabaseMigrator.dll status
dotnet Task.DatabaseMigrator.dll apply
```

The connection string is accepted only through `ConnectionStrings__TaskDatabase`; it is never accepted as a command-line argument. Commands are case-insensitive. `--help` and `-h` print usage. Missing, unknown or extra arguments are rejected.

Success writes one `TASK_DB_MIGRATOR` line to stdout. Failure writes one safe line to stderr. Output contains only a stable code and safe migration version values; it never contains a connection string, host, user, database name, stack trace or raw Npgsql exception text. `Ctrl+C` cancels current database I/O and exits with 130.

## Exit codes

| Exit | Meaning |
|---:|---|
| 0 | `Ready`, `Applied` or `AlreadyCurrent` |
| 2 | `InvalidArguments` |
| 3 | missing or invalid connection configuration |
| 4 | database unavailable, timeout or safely classified infrastructure failure |
| 5 | PostgreSQL older than 16 |
| 6 | `status` found unapplied migrations |
| 7 | incompatible history or required schema objects missing |
| 8 | migration advisory lock unavailable |
| 9 | migration execution or post-migration inspection failed |
| 130 | cancelled by the operator |

## Safety contract

`status` performs read-only inspection. Applied history is compatible only when its version, name and SHA-256 rows are an exact ordered prefix of the embedded catalog. Unknown versions, gaps, extra rows, changed names and changed checksums fail closed. A complete history is followed by checks for `core.organizations`, `core.objects` and `work.tasks`; missing objects are reported and are never repaired automatically.

`apply` first inspects, then applies only a missing or pending catalog. It opens one transaction, rejects PostgreSQL older than 16, obtains `pg_try_advisory_xact_lock` before bootstrap DDL, repeats history inspection under the lock and applies all missing embedded migrations. Commit occurs only after the whole catalog succeeds. Exceptions and cancellation roll back DDL and history together. A held lock returns immediately instead of waiting indefinitely. After commit, the executable repeats inspection and reports success only for `Current`.

The API does not invoke this runner or mutate schema during startup/readiness.

## Deployment order and database role

Use a dedicated migration database role with only the privileges required by reviewed migrations. The ordinary API role must not receive DDL or migration-history write privileges. Creating roles and distributing credentials remain deployment responsibilities outside this increment.

Deployment order:

1. Take and verify a backup before any destructive migration.
2. Run `status`.
3. Run `apply` explicitly.
4. Run `status` again and require exit 0/`Ready`.
5. Start the new API version.

Migration 001 in the current catalog is non-destructive. A rollback command, database-role creation, backup orchestration and container publication are outside increment 0.3.0.
