# Task 14.2 — approved server return contract

Version: 1.0.0. Approved: 2026-10-22 (Europe/Minsk).

## Approval and precedence

Task 14.1 audit reported CONTRACT_CHANGE_REQUIRED (OQ-14.1-01): the
canonical state machine permits `review -> in_progress`, whereas
`outputs/20260826_task_write_vertical_slice_specs_1.0.0/04_SERVER_TASK_TRANSITION.md:33`
explicitly rejects it. After Codex described the exact correction and the
blocking condition, the user instructed: «Теперь действитвуй».
This records approval of that narrow correction. It supersedes only the
`review -> in_progress` prohibition in the implementation specification;
the historical specification and audit remain unchanged.

## Implementation contract

- Permit `review -> in_progress` through the existing
  `POST /api/v1/tasks/{id}/transition`, with `targetStatus: "in_progress"`.
- Keep `Task.ChangeStatus` (`task.changestatus`), role/object/tenant scope,
  session validation, lifecycle restrictions, If-Match, stable error codes,
  durable idempotency and transactional concurrency checks.
- Increment the version once and write the existing `task.change_status`
  audit, `TaskStatusChanged` event/history projection and realtime outbox
  atomically with the task and idempotent response.
- Reason remains optional under the existing request schema; do not introduce
  reason storage, a comment prerequisite, new DTOs, fields or migrations.
- Do not add return notifications. Preserve the current notification projector.
- Preserve every other allowed/forbidden transition and completion rule.
- No WPF source or UI changes; a dedicated return UI is a later task.

The approval resolves OQ-14.1-01 for this server increment. Other audit gaps
(history DTO alignment, terminal recovery, reason display, notifications and
reviewer identity policy) remain outside its scope.

The final pre-main correction DEC-14-CONTRACT-002 is recorded in
[task-14-final-contract-corrections.md](task-14-final-contract-corrections.md).
It resolves the reported HistoryEntry/error-contract drift with the existing
WPF fields preserved and a reproducible derived runtime OpenAPI.
