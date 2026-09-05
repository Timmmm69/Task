# PROD-02 — Today 1.1.0

Implemented remaining roadmap scope: overdue, review and waiting task summaries, with task-card navigation for editing deadlines/planning and status changes.

## Behaviour
- Includes active tasks where the signed-in user is author, assignee or watcher. Completed/cancelled tasks are excluded.
- Overdue means deadline strictly before the current instant. Review uses the canonical review status. Waiting means assigned to other employees, with the current user as author/watcher and not an assignee; review tasks appear in the review group.
- Reads every Task API page, deduplicates IDs and rejects repeated cursors. Groups are published only after the complete successful load. Failed refresh preserves previously confirmed data with a warning.
- Calendar.Read and Task.Read are checked independently. Revocation/logout invalidates pending requests and clears protected data.
- Schedule and summary rows open their existing task/calendar cards. Task lookup works beyond the first page, preserves an existing editor draft and reuses API-backed editing/status actions. Returning to Today reloads the summary.

## Validation
- Release production solution: Task.Tests 780 passed, 4 skipped; Task.ServiceHosts.Tests 550 passed.
- Final desktop regression: 269 passed, including 10 added tests for summary/navigation/draft preservation.
- Project-boundary verification: passed.
- Dashboard order recalculated: next item OPS-03. Dashboard validation remains blocked by pre-existing SEC-02 progress=55, while its validator accepts only 0/25/50/75/100; unrelated security readiness was preserved.
- Security gate: passed (complete solution tests and tracked configuration scan).
- Whole-solution whitespace gate: failed on existing formatting violations, including unchanged authentication/session/API files. Unrelated files were not reformatted.
- Four PostgreSQL-dependent tests were skipped because TASK_POSTGRES_TEST_ADMIN_CONNECTION was not configured. The new lists have not had a manual WPF or new live HTTPS/PostgreSQL smoke test in this run. Existing Today 1.0.0 live evidence is historical and is not claimed as verification of these changes.

This package completes the remaining scope recorded on the PROD-02 roadmap item. It does not add a reminder API or new drag-and-drop interaction; wider concept requirements remain separate from this incremental implementation.
