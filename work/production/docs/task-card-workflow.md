# PROD-01 — Task card and lifecycle

Version 1.0.0, schema 11. Extends the existing task vertical slice and API-04.

## User workflow

Open **Задачи**, create a task, enter title and optional description, priority, date/time, deadline and duration. Choose project, top-level parent, requester, contact/company, assignees and watchers from server-provided choices. Search narrows choices; selected values survive a search. Explicitly choosing **Не указано** clears a relation.

A date without a time remains a planning date; a timed start persists its local date/time, IANA time zone and matching UTC instant. Description-only edits preserve the original schedule and time zone. Duration is 1–10080 minutes, description up to 50000 characters, each participant list has at most 100 unique active accounts.

The saved card contains checklist, child tasks, related catalog files, predecessor dependencies, comments and history. A child is created through **Новая задача** by selecting the parent; nesting is limited to one level. Dependencies are finish-to-start links with cycle rejection; their statuses are visible and do not automatically complete or block the task. File associations reference existing catalog entries; opening file locations and managing catalog entries remain the file-catalog scenario. Recurrence is displayed from the existing calendar/recurrence service and is configured there.

Use **Начать → На проверку → Завершить** (or a permitted direct terminal transition). Completion time is persisted. A completed/cancelled task cannot have its fields, checklist, dependencies or file links changed. Comments remain available with `Comment.Create`. Lists of comments/history show the latest 200 entries. Choice lists show at most 200 results and report when the user should narrow the search.

## HTTP and persistence

Existing `GET/POST /api/v1/tasks`, `GET/PATCH /api/v1/tasks/{id}` and transition endpoints now round-trip the card fields instead of returning null placeholders. PATCH is presence-aware: omitted fields survive and explicit null clears nullable values. Unknown/duplicate fields, invalid schedules, unavailable related objects, second-level parents and cycles are rejected.

Additional routes, defined by `ProductApiRoutes.All`:

| Method and suffix | Operation |
| --- | --- |
| GET `/tasks/options?q=...&limit=...` | Permission-filtered relation choices |
| GET `/tasks/{id}/workspace` | Checklist, comments, children, dependencies, files, history |
| GET `/tasks/{id}/history` | Existing generic object history reader |
| POST `/tasks/{id}/checklist` | Add `{text}` |
| PATCH `/tasks/{id}/checklist/{childId}` | Change `{text,isCompleted,sortOrder}` |
| DELETE `/tasks/{id}/checklist/{childId}` | Remove item |
| POST `/tasks/{id}/comments` | Add `{body}` |
| POST `/tasks/{id}/dependencies` | Add `{predecessorId}` |
| DELETE `/tasks/{id}/dependencies/{childId}` | Remove dependency |

Files use the existing `/objects/{id}/links` routes with `linkType: task_file`. Child mutations use the parent task's `If-Match: "vN"` and an `Idempotency-Key`. Every successful mutation increments the task version once and commits its data, audit entry, domain event, outbox entry and durable idempotency result in one transaction. Core task writes and workspace writes share the tenant advisory lock to serialize relation/version changes.

Schema 11 adds JSONB card content, generated project/parent references with tenant-scoped foreign keys, indexed checklist/comments/dependencies, and task visibility functions. Existing migration contents are unchanged. Deploy the migrator first and reapply `deployment/containers/sql/grant-runtime.sql` for the limited runtime role. New task assignment/watch and comment capabilities inherit existing task-update role effects during migration; later role grants follow normal administration.

## Permissions and failure handling

Task reads apply project membership or creator/requester/assignee/watcher visibility. Personal tasks retain the existing organization-wide task-read behavior. Setting or clearing a project requires authority in the old project and the new target. Assign/watch mutations require their own capability; options additionally require the relevant employee/project/contact/catalog read capability. Foreign-organization, invisible and inactive references cannot be introduced. Replay checks current object visibility.

The desktop keeps unsent text on network, permission and version errors, sends no background offline writes, and updates the visible card from server responses. A stale ETag is a conflict, and retrying an uncertain write reuses its idempotency key. Search never clears selected participants or silently restores an explicitly cleared relation.

## Reproduction

1. Build `work/production/Task.sln -c Release`; set `TASK_POSTGRES_TEST_ADMIN_CONNECTION` to an isolated PostgreSQL 16 admin connection and run the full solution tests.
2. Run `Test-ProjectBoundaries.ps1` and `Test-SecurityGate.ps1 -Configuration Release -NoBuild`.
3. Run `Test-TaskWriteE2E.ps1 -Phase Setup`; for a Cyrillic checkout, an explicit ASCII 8.3 `-RuntimePath` under `work/tmp/` is supported. All phases must receive the same path.
4. In the real WPF app, create the fixture's initial title with description `Согласовать договор с заказчиком и проверить комплект документов.` and one assignee. Edit to the fixture's final title, high priority, date `05.09.2026`, duration 45. Start, add/complete one checklist item, add comment `Условия договора согласованы.`, send for review and complete.
5. Close WPF. Run `-Phase Verify -VerifyCard -EvidencePath <work evidence directory>` to restart the API and assert persisted card/lifecycle/children and one audit/event/outbox/idempotency record per mutation. Run `-Phase Cleanup` to stop the fixture and restore pre-existing desktop credentials.

Verified results and hashes are in `outputs/20260904_task_product_completion_1.0.0/`.
