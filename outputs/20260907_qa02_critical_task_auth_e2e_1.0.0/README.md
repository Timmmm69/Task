# QA-02 critical task/auth clean-stand E2E

This package records the reproducible QA-02 gate against a disposable local stand. The gate uses
PostgreSQL 16, the production database migrator, the production HTTPS API and the Release Windows
WPF client. It does not substitute an in-memory store, SQLite database or mock HTTP server.

The deterministic `qa02-deterministic-v1` seed creates fixed admin/reader identities, a fixed replay
probe and the fixed WPF task title `QA-02 deterministic WPF task`. Runtime secrets, certificates,
ports and server-generated entity identifiers remain ephemeral by design.

Run from the repository root:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File work/production/verification/Test-Qa02CleanStand.ps1 `
  -EvidenceDirectory outputs/20260907_qa02_critical_task_auth_e2e_1.0.0/evidence
python work/production/verification/Build-Qa02Package.py
npm run dashboard:validate
```

The runner preserves existing Desktop AppData, creates its PostgreSQL runtime only under the ASCII
`%LOCALAPPDATA%\TaskE2ERuntime` path, and restores/removes everything in `finally`.
