# Task identity, audit and offline bootstrap foundation 0.7.0

This package delivers persistence migration 002, an Argon2id password provider, an append-only audit ledger and a one-shot offline first-administrator bootstrap command.

Run migrations first, then run `Task.DatabaseMigrator bootstrap-admin` with the following environment variables:

- `ConnectionStrings__TaskDatabase`
- `TASK_BOOTSTRAP_ORGANIZATION_CODE`, `TASK_BOOTSTRAP_ORGANIZATION_NAME`, `TASK_BOOTSTRAP_TIME_ZONE`
- `TASK_BOOTSTRAP_ADMIN_FIRST_NAME`, `TASK_BOOTSTRAP_ADMIN_LAST_NAME`, `TASK_BOOTSTRAP_ADMIN_LOGIN`
- `TASK_BOOTSTRAP_PASSWORD_FILE`, `TASK_BOOTSTRAP_PEPPER_FILE`

The two secret variables are paths to local files. Secret contents are neither command-line arguments nor command output. The command requires schema version 2, takes a transaction-scoped advisory lock and refuses once an organization or account already exists.

This is a persistence/bootstrap foundation only. Interactive sign-in, token issuance and password-change UI remain later work.
