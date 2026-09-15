# Validation report

Package: `20260915_tasks_inbox_direction2_1.0.0`

Version: `1.0.0`

Validated: `2026-09-15`

## Scope

The production WPF `Задачи` and `Входящие` sections were aligned with the
frozen Direction 2 baseline. The change reuses the existing task API, DTOs,
permissions, optimistic-version conflict rules, and production data sources.
No file under `sources/` was changed and no mock data was added to production.

An inbox item is an existing `New` task whose scheduling, assignment,
description, project, relations, and planning fields are empty. Conversion
patches that same task through the existing API; the deadline remains required
by this UI flow, while project selection follows the current permission-aware
project options and remains optional under the existing business rules.

## Acceptance checks

| Check | Result | Evidence |
| --- | --- | --- |
| Compact Direction 2 task table and task inspector | PASS | `evidence/tasks-direction2.png`, `evidence/tasks-direction2-comparison.png` |
| Canonical status, priority, and due-date semantics | PASS | visual comparison and desktop view-model tests |
| Baseline create/edit UI | PASS | `evidence/tasks-created.png` and QA-03 gate |
| Start/complete/cancel commands retained | PASS | desktop view-model tests and unchanged API command contract |
| Inbox quick capture and same-record conversion | PASS | `evidence/inbox-direction2.png`, `evidence/inbox-conversion-direction2.png`, QA-03 gate |
| Empty/loading/error/offline/conflict states | PASS | view-model tests, visual-foundation tests, conflict captures, QA-03 offline/reconnect flow |
| Keyboard focus, UI Automation, scaling, long Russian text | PASS | UIA dump, accessibility assertions, 150% DPI screenshots and responsive layout checks |
| Production solution Release build | PASS — 0 warnings, 0 errors | `evidence/release-build.txt` |
| Relevant desktop test suite | PASS — 326/326 | `dotnet test tests/Task.Desktop.Tests/Task.Desktop.Tests.csproj --configuration Release --no-restore` |
| Real PostgreSQL/API/worker/WPF QA-03 gate | PASS | `evidence/ui-assertions.json`, `evidence/qa03-gate.txt` |

## State and accessibility coverage

- Loading retains a stable shell; empty content exposes a focused primary action.
- API, offline, read-only, and version-conflict messages retain drafts and loaded data where possible.
- Rows, filters, editors, and inspector actions expose stable AutomationIds and complete accessible names.
- Enter opens/selects, Escape closes editing or clears selection, and F6 cycles the primary regions.
- Responsive breakpoints preserve the six-column task hierarchy at the verified desktop width and collapse the inspector safely at narrow widths.
- Long or missing Russian values use ellipsis/placeholders visually while keeping full text in tooltips and automation names.

## Repository checks

- Diff is limited to the two requested desktop sections, shared task-editor styling,
  their focused tests, the existing QA gate, and this output package.
- API, DTO, database schema, and domain business rules were not changed.
- `.project-dashboard/roadmap.json` was not changed because the affected desktop
  baseline item was already recorded as complete; no unsupported progress increase was made.

Final result: passed
