# Task 14.2 — server return validation

Version: **1.0.0**. Date: **2026-10-22**, Europe/Minsk.
Baseline: `origin/main` **fa504bc01584547afc50398ba6c1735391c4173c**.
Branch: `codex/task-14-2-server-return`.
Result: **PASS — server increment**.

## Approved contract

The original Task 14.1 audit is preserved in `contract-audit/` with its original
CONTRACT_CHANGE_REQUIRED result. The user subsequently authorized the narrow
correction described by Codex ("Теперь действитвуй"). The recorded decision is
`work/production/docs/task-14-2-return-contract-decision.md`.
It resolves OQ-14.1-01 and supersedes only the old implementation specification's
prohibition of `review -> in_progress`. Historical sources were not rewritten.

## Implementation and regression boundary

The existing transition endpoint accepts the existing `in_progress` target from
`review`. TaskRules, TaskAggregate.Start and TaskStatusTransitionCommandService
now agree on this transition. Version increases exactly once; task content,
priority, schedule and completion metadata are preserved. The existing command
executor persists state, audit, event/history, realtime outbox and replay response
in one transaction. No executor, authorization, notification or persistence-schema
implementation was changed.

The complete 5 x 5 HTTP transition matrix passes: **9 allowed, 16 rejected**.
All original 8 allowed transitions retain their semantics, including direct
completion and cancellation. Same-state transitions and terminal reopening remain
rejected. New DTOs, endpoints, permissions, roles, migrations and OpenAPI changes
were not introduced. Reason is optional and is not newly persisted as business
text or a comment. There is no new return notification.

## Final verification

| Check | Result |
|---|---|
| Release Task.Api build (`--no-restore`) | PASS, 0 warnings, 0 errors |
| HTTP matrix, return, independent status policy and permission authorization | 60 passed, 0 failed/skipped |
| Domain, updates, permissions, PostgreSQL executor and role/scope scenarios | 92 passed, 0 failed/skipped |
| Final PostgreSQL return scenarios and TaskLifecycleService regression | 26 passed, 0 failed/skipped |
| TasksViewModel and DesktopTasksApiClient regression | 132 passed, 0 failed/skipped |
| TodayViewModel, PersonalWorkflow and PersonalSafety shared-rule regression | 174 passed, 0 failed/skipped |
| dashboard:order and dashboard:validate | PASS; only API-02 evidence/note/date changed, progress unchanged |
| git diff --check and scoped diff review | PASS |
| manifest payload and SHA256SUMS verification | PASS; re-read every listed file |

TRX files are the evidence for test counts. Runs overlap on six PostgreSQL return
cases; the counts above are executions, not an additive unique-test total.

### Security, concurrency and side effects

- HTTP: absent permission -> 403 FORBIDDEN; unavailable/foreign/missing task ->
  404 OBJECT_NOT_VISIBLE; archived/deleted -> existing 409 codes. Rejected requests
  leave aggregate, mutation counter and replay storage unchanged.
- Session revoked/expired/absent -> 401 with the existing codes. If-Match remains
  required (428); malformed/weak ETag and absent/invalid idempotency key -> 400;
  stale version -> 412 VERSION_CONFLICT, with no overwrite.
- Real PostgreSQL **16**, isolated cluster on loopback, all migrations applied;
  commands execute through the existing restricted runtime database role.
  Employee creator succeeds; observer, unrelated employee, visible watcher without
  action scope and explicit deny are rejected. Replay rechecks revoked permission.
- Same-key concurrent requests produce one execution and one replay, identical
  response and ETag; different intent with the same key is rejected. Exactly one
  audit entry, event, outbox message and completed idempotency record persist.
- Different concurrent commands at one version yield one winner and one
  TaskLifecycleConcurrencyException. A further stale command does not overwrite it.
- Injected duplicate event failure rolls back status, audit, outbox and idempotency.
- History projection contains one TaskStatusChanged entry with version, actor and
  changed field `status`; event payload records review/in_progress, correlation and
  version; audit uses task.change_status. No duplicate history/audit or notifications.

## Reproduction

Set `TASK_POSTGRES_TEST_ADMIN_CONNECTION` to an isolated PostgreSQL 16+ admin
connection (CREATE DATABASE/ROLE required). Tests create and clean synthetic databases.
Run from the repository root:

```powershell
dotnet build work/production/src/Task.Api/Task.Api.csproj -c Release
dotnet test work/production/tests/Task.ServiceHosts.Tests/Task.ServiceHosts.Tests.csproj -c Release --filter 'FullyQualifiedName~TransitionTask_|FullyQualifiedName~ReturnTask_|FullyQualifiedName~TransitionPolicy_|FullyQualifiedName~TaskPermissionAuthorizationTests'
dotnet test work/production/tests/Task.Tests/Task.Tests.csproj -c Release --filter 'FullyQualifiedName~TaskAggregateTests|FullyQualifiedName~TaskWorkStatusTests|FullyQualifiedName~TaskUpdateTests|FullyQualifiedName~PermissionDecisionServiceTests|FullyQualifiedName~TaskReturn_Postgres_|FullyQualifiedName~PostgresTaskWriteCommandExecutorTests|FullyQualifiedName~Authorization_|FullyQualifiedName~TaskLifecycleServiceTests'
dotnet test work/production/tests/Task.Desktop.Tests/Task.Desktop.Tests.csproj -c Release --filter 'FullyQualifiedName~TasksViewModelTests|FullyQualifiedName~DesktopTasksApiClientTests|FullyQualifiedName~TodayViewModelTests|FullyQualifiedName~PersonalWorkflowTests|FullyQualifiedName~PersonalSafetyTests'
npm run dashboard:order
npm run dashboard:validate
git diff --check
```

## Limitations and earlier attempts

- No WPF production source was edited and no dedicated return button/text was
  designed. The existing client commands consume shared TaskRules, so their
  eligibility now includes the approved transition; its matrix test was updated.
  Visual UI acceptance and network end-to-end return were not run.
- Original audit gaps in history DTO alignment, reason storage/display, return
  notification policy, reviewer identity and terminal recovery remain outside scope.
- This is a local committed server increment, not a deployment or a main-branch
  publication. Original checkout/user changes were preserved in a separate worktree.
- Initial HTTP test used PERMISSION_DENIED instead of the existing FORBIDDEN code;
  its expectation was corrected. The initial PostgreSQL 15 attempt failed the
  version prerequisite; a startup race also tried its stopped port. Neither attempt
  counts as successful DB validation. Final PostgreSQL 16 gates pass without skips.
- A desktop matrix initially expected rejection of the newly approved transition;
  the expectation was updated. Existing clean-build test warnings ASPDEPR004,
  xUnit1031/xUnit2031 were not changed; final incremental server build is warning-free.
