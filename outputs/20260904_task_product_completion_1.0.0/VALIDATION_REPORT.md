# PROD-01 validation report

Version: 1.0.0. Date: 2026-09-04. Result: PASS.

## Implemented and checked

- Existing paged task list, task details, create, presence-aware update, optimistic conflicts, safe retries and lifecycle remain functional.
- Description, project, parent, requester, counterparty, assignees, watchers, date/local time/time zone/UTC, duration and completion time now round-trip through WPF, HTTP and PostgreSQL.
- Task workspace supports checklist changes, comments, one-level subtasks, related catalog files, cycle-safe predecessor dependencies and history. Participant/relation choices use authorized server data and search.
- Task visibility applies project membership and task relationships. Transactional validation rejects inactive/foreign references and project detach/move without old/new project authority. Completed tasks reject field, checklist, dependency and file-link changes. Comments have a separate capability.
- Existing schema 1–10 contents are preserved; migration 11 and limited-runtime database grants are exercised on real PostgreSQL 16.14.

## Automated validation

Release build succeeded: 0 errors. Eight pre-existing test-code warnings remain (deprecated ASP.NET test host APIs and one xUnit blocking-wait warning); no new production-code warnings.

| Suite | Passed | Failed | Skipped |
| --- | ---: | ---: | ---: |
| Task.Tests | 779 | 0 | 0 |
| Task.ServiceHosts.Tests | 432 | 0 | 0 |
| Task.Desktop.Tests | 247 | 0 | 0 |
| Total | 1458 | 0 | 0 |

`TASK_POSTGRES_TEST_ADMIN_CONNECTION` was set to a separate local PostgreSQL cluster. PostgreSQL-dependent tests executed against real databases; they were not silently bypassed by a missing environment variable. The new workspace round-trip uses the limited runtime role; the transactional project/participant validation test confirms rejection with no version change.

Targeted coverage includes full create/read fields, invalid HTTP patches, preserved schedule on description-only edits, explicit null clearing during option refresh, schedule validation, card survival through lifecycle copies, checklist replay and stale ETag, dependency cycles, file links, visibility filtering, terminal-state writes, unauthorized project detachment and unavailable participant rejection.

The final complete suite also passed the existing security gate. The architecture boundary check and dashboard validation passed. TRX counters and implementation SHA-256 values are verified by `Build-TaskProductPackage.mjs`.

## Real Windows + HTTPS + database E2E

Used an isolated PostgreSQL database, production HTTPS API and the real Release WPF executable with ephemeral test accounts. Existing desktop app data was preserved by the fixture. No mock server was used for this scenario.

Observed through Windows UI automation:

1. Create a task with description and one assignee; open saved details.
2. Edit title and priority, set date-only planning to 2026-09-05 and duration to 45 minutes; verify saved fields.
3. Start the task, add a comment, add and complete a checklist item.
4. Send for review, complete; verify the completed status and disabled task/checklist mutation controls.
5. Close the application, restart the production API, GET the task/list/workspace and compare persisted values.

Final task version **8**. PostgreSQL contains **8 audit entries, 8 domain events, 8 outbox entries and 8 durable idempotency results** (5 core commands + 3 workspace commands). Description, assignee, date, duration, completion timestamp, completed checklist item and comment survived restart. Initial create replay generated exactly one row/event/audit/outbox/idempotency result; the read-only account could GET (200) and could not write (403). See `evidence/db-assertions.json` and the WPF capture.

An independent read-only review found a project-detachment authorization gap; it was fixed and covered by a real transaction test before this final gate.

The fresh Release client was reopened after the API restart; the completed task was present, and the corrected relation-search command was enabled. The test client/API and both temporary PostgreSQL clusters were stopped, and the pre-existing desktop app data was restored. The E2E helper was corrected to resolve the Windows 8.3 backup path before restoring it; final cleanup passed. See the cleanup and E2E verification logs.

## Scope boundaries

Files are linked to existing catalog entries; catalog editing and opening file locations belong to PROD-05. Recurrence remains configured by the existing calendar/recurrence workflow and is displayed in the task card. Predecessor links expose status and reject cycles; they do not automatically complete/block tasks. Comments/history display the latest 200 entries; search choices are bounded with an explicit refinement notice.

This is evidence for PROD-01/DESK-02, not a declaration that the entire product or customer deployment is ready. Other roadmap items and release gates remain open.
