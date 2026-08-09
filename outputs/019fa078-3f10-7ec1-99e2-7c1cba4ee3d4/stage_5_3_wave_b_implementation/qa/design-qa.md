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
