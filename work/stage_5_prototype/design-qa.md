# Stage 5.3 Wave B — Design QA

## Comparison target

- Source visual truth: `C:\Users\novik\Таск\work\stage_5_1\directions\Stage_5_1_Direction_2.png`.
- Combined comparison: `C:\Users\novik\Таск\work\stage_5_prototype\design-qa-wave-b-comparison.png`.
- Implementation evidence:
  - `qa-wave-b-projects.png`;
  - `qa-wave-b-files.png`;
  - `qa-wave-b-crm.png`;
  - `qa-wave-b-watchers.png`.
- Source pixels: 1536 × 1024.
- Implementation captures: 1280 × 720 CSS px at device density 1.
- Normalization: source reduced to 1280 × 853 without cropping; implementation captures kept at native 1280 × 720. The comparison assesses the shared desktop design language. The source and Wave B captures intentionally represent different product sections, so no false pixel-level claims are made about their content layouts.
- State: authenticated desktop client, online/read-write, Direction 2, Projects / Files / CRM / task watchers.

## Full-view comparison evidence

The five-view comparison confirms that Wave B keeps the selected Direction 2 shell and visual hierarchy:

- the Windows title bar, left navigation, workspace header, primary blue action and bottom status bar remain stable;
- Projects, Files and CRM use the same split list/inspector grammar as the approved desktop direction;
- Segoe UI hierarchy, Fluent icons, blue selection, neutral surfaces, subtle borders and restrained state colors stay consistent;
- no visible image assets from the target were replaced by CSS art, custom SVG drawings, emoji or placeholder imagery;
- the new surfaces remain legible at 1280 × 720 and preserve internal scrolling instead of clipping the persistent navigation or status bar.

## Focused region evidence

- `qa-wave-b-projects.png`: project list, filters, progress, lifecycle banner and inspector cards.
- `qa-wave-b-files.png`: catalog list, OS/SMB trust boundary, metadata cards and tab strip.
- `qa-wave-b-crm.png`: allowed-scope list, unavailable-record treatment, contact facts and channel action.
- `qa-wave-b-watchers.png`: expanded task watchers, Task.Watch explanation, watch toggle, refresh action and participant rows.

These focused captures are readable at native density and cover the areas where copy, icon alignment, semantic state color and spacing are most sensitive.

## Primary interactions verified

- Projects: empty-name validation, project creation, member addition, separate Complete / Archive / Trash actions, archive confirmation and archive read-only state.
- Files: all six open-diagnostic states are exposed; NETWORK_RESOURCE_UNAVAILABLE report is shown; empty path is rejected with UNSAFE_PATH; an alternative SMB location can be added without moving the physical file.
- CRM: unavailable record is redacted; interaction editor validates required summary; a manual interaction is saved without external sending.
- Task watchers: expand/collapse, watch/unwatch, server refresh, watcher count update and disabled mutation in read-only mode.

## Required fidelity surfaces

- Fonts and typography: passed. Segoe UI/fallback hierarchy, weights, line height and compact helper text match the selected desktop direction. No broken wrapping or truncation affects comprehension.
- Spacing and layout rhythm: passed. The shell proportions, list/inspector split, cards, tab spacing and status banners are coherent. Internal scrollbars preserve access to below-the-fold content at 1280 × 720.
- Colors and tokens: passed. Primary blue, neutral grays, success green, warning yellow and destructive red are used semantically. Refresh is a quiet blue action, not a destructive red action.
- Image quality and asset fidelity: passed. The reference has no content imagery requiring generation. Fluent icons are used consistently; no prohibited substitutes are present.
- Copy and content: passed. Projects, Files and CRM explain permissions, read-only state, metadata/OS distinction and external-send boundaries. Technical permission identifiers remain only as short diagnostic helper text.
- Accessibility and behavior: passed for the prototype scope. Semantic controls, labels, focus outlines, disabled states and read-only explanations are present. Full Windows UIA/Narrator runtime proof remains a later Stage 5 gate and is not claimed here.

## Findings

No actionable P0, P1 or P2 findings remain.

- [P3] Permission identifiers such as `Task.Watch` and object type `CatalogItem` are intentionally visible in diagnostic/helper copy. A final production copy pass may replace them with friendlier labels while retaining the support code in diagnostics.

## Comparison history

### Pass 1 — blocked

- [P2] The watcher refresh action inherited the destructive red ghost-button style, which incorrectly implied data loss.
- Fix: introduced a quiet blue action style and applied it to refresh.
- Post-fix evidence: `qa-wave-b-watchers.png` and `design-qa-wave-b-comparison.png`.

### Final pass — passed

The post-fix comparison contains no remaining actionable P0/P1/P2 differences. Build, language diagnostics and Sites package tests also pass.

## Residual verification boundary

- A new browser reload on 2026-07-30 was denied by the current browser URL policy. No alternate browser or policy workaround was used.
- The accepted visual and interaction evidence was captured earlier against the same Wave B code state; the only later operation was a successful reproducible build and package test.
- Native Windows runtime, UI Automation, Narrator, actual 200% OS scaling and external SMB handler behavior remain Stage 5.4/Gate verification items.

## Implementation checklist

- [x] Preserve Direction 2 shell and component language.
- [x] Implement Projects, Files, CRM and task watchers.
- [x] Exercise primary success, validation, permission, error and destructive states.
- [x] Compare source and implementation in one combined visual input.
- [x] Fix the semantic color mismatch found during QA.
- [x] Pass build and Sites package tests.
- [x] Pass Design QA with P0/P1/P2 = 0.

final result: passed

---

# User feedback — planner and new-task form QA

## Comparison target

- Source visual truth: user-provided screenshots:
  - `C:\Users\novik\AppData\Local\Temp\codex-clipboard-db2b902c-58c3-4dc2-8792-bdb5eb83cf00.png` — remove the current-time line;
  - `C:\Users\novik\AppData\Local\Temp\codex-clipboard-da0742d2-4130-419d-8518-673535ab6161.png` — keep the profile and primary actions aligned in the header;
  - `C:\Users\novik\AppData\Local\Temp\codex-clipboard-6d3acdf3-adbb-4391-95bd-e8f7c8aa83d0.png` — extend New task;
  - `C:\Users\novik\AppData\Local\Temp\codex-clipboard-bc03afa9-0c61-4eee-b87e-51da54ee2898.png` — make the planner/details split adjustable.
- Combined visual comparison: `qa-user-feedback-comparison.png` (source and implementation pairs for the header and New task form).
- Browser-rendered implementation captures:
  - `qa-user-feedback-planner.png` — Today view after the header and divider changes;
  - `qa-user-feedback-new-task.png` — New task dialog with all requested fields.
- Viewport: 1280 × 720 CSS px, device density 1; implementation captures are 1280 × 720 px. Source screenshots have varying desktop dimensions, so the combined image normalizes each comparison pair to 720 px wide for visual review rather than asserting pixel-for-pixel equality.
- State: authenticated online desktop client; Today view, selected planned task; New task dialog open where applicable.

## Full-view and focused comparison evidence

- Header: the profile button stays in the same horizontal grid row as search, New task and notifications. The active header layout has six explicit grid tracks; this removes the former implicit wrap that placed the profile below the bar. At this viewport, the connection-status control intentionally collapses so the labelled search control remains visible.
- Planner: the red current-time line and marker are absent. The split has a visible narrow divider and no overlapping borders.
- New task: the dialog now has `День недели`, `Проект (необязательно)` with `Без проекта` as the default, and `Приоритет` in one row.
- Divider interaction: browser test moved the separator by pointer and then by ArrowRight; its CSS custom property changed from `519px` to `543px`. Arrow keys operate the focused separator; Shift increases the adjustment step.

## Primary interactions verified

- Opened New task, selected `Четверг`, left project as `Без проекта`, entered a title and created the task. The created list entry displays `Проект: Без проекта` and `Четверг`.
- Verified that the divider is exposed as the labelled vertical separator `Изменить ширину плана дня` and responds to keyboard resizing.
- Reloaded the screen to verify the corrected header and absent current-time line in the rendered implementation.
- Browser console: no error-level messages.

## Required fidelity surfaces

- Fonts and typography: passed. The existing Segoe UI hierarchy is retained; search text and the New task label stay readable without wrapping the profile into a second row.
- Spacing and layout rhythm: passed. The explicit header tracks keep actions centred, the form uses a wider modal with three balanced fields, and the divider preserves minimum widths for both panels.
- Colors and tokens: passed. Existing blue primary actions, neutral surfaces, borders and status colours are unchanged; divider hover/focus uses the existing primary blue token.
- Image quality and asset fidelity: passed. These targeted desktop controls do not introduce imagery. Existing Fluent UI icons remain in use; no placeholder or custom-drawn asset was added.
- Copy and content: passed. The new labels state the intent directly and `Без проекта` makes the optional choice explicit.
- Accessibility and behavior: passed for the implemented scope. The divider is keyboard-focusable, labelled, vertically oriented and exposes its current width as accessible text. The form fields retain native select semantics.

## Findings

No actionable P0, P1 or P2 findings remain.

## Comparison history

### Pass 1 — blocked

- [P2] At the 1280 × 720 review viewport, the old responsive header hid the `Поиск по Task` label, which made the requested alignment change incomplete.
- Fix: retained the labelled search control, hid only the lower-priority connection-status control at this breakpoint, and used a five-track responsive header that keeps the profile in the same row.
- Post-fix evidence: `qa-user-feedback-planner.png` and `qa-user-feedback-comparison.png`.

### Final pass — passed

The header, dialog and split view have no remaining actionable P0/P1/P2 differences against the requested changes. Build and both targeted test suites pass.

## Implementation checklist

- [x] Remove the current-time line.
- [x] Keep profile, search and New task aligned on all desktop header layouts.
- [x] Add weekday selection to New task.
- [x] Make project optional with an explicit no-project value.
- [x] Add pointer and keyboard resizing for the planner/details divider.
- [x] Verify build, tests, browser interactions and console.

final result: passed
