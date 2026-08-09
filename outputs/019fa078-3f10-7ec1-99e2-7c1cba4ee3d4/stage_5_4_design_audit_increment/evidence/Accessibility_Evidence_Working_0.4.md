# Stage 5.2 Accessibility Evidence — Working 0.4

**Date:** 2026-07-28  
**Prototype:** Direction 2 component-completion wave  
**Status:** WORKING PASS

## Browser semantic evidence

| Surface | Evidence | Result |
|---|---|---|
| Bulk actions | labelled row/page checkboxes, named SelectionBar and live BulkResultSummary | PASS |
| Context menu | menu/menuitem semantics, expanded state and disabled Archive capability | PASS |
| Project lifecycle | named lifecycle region with explicit active/paused state and reversible action | PASS |
| Project history | labelled tablist/tabpanel and chronological authorized/redacted entries | PASS |
| Task scheduling | labelled date, time and timezone controls grouped by a named region | PASS |
| Reminder | labelled select and live status summary containing date, time, offset intent and timezone | PASS |
| Recurrence | labelled rule/end controls and readable preview text | PASS |
| File location | status announced through the containing live definition; unavailable Open action disabled | PASS |

## Keyboard evidence

- Row selection uses native checkboxes.
- Task context actions are native menu buttons and menuitems; `Escape` closes the menu.
- Project tabs are native buttons with tab roles and selected state.
- Task-editor `Shift+Tab` from the first control wraps to Save, proving the focus trap boundary.
- `Escape` closes the task editor and focus returns to the previously active Edit control.
- Date, time, timezone, reminder and recurrence controls remain reachable through normal tab order.

## Visual and scaling evidence

| Check | Evidence | Result |
|---|---|---|
| Bulk failure wording remains visible at 1280 × 720 | `implementation-direction2-bulk-actions.png` | PASS |
| Project history fits the inspector without document horizontal overflow | `implementation-direction2-project-history.png` | PASS |
| Editor sections remain readable within an internally scrolling modal | `implementation-direction2-task-scheduling.png`, `implementation-direction2-task-scheduling-lower.png` | PASS |
| Long UNC path is truncated without hiding file identity or state | `edge-file-location-unavailable.png` | PASS |
| Reduced-motion rule remains present | `src/styles.css` | IMPLEMENTED |
| Actual Windows 200% scaling | controlled OS-level session required | PENDING |

## Remaining formal evidence

- Windows UI Automation name/role/state capture;
- Narrator walkthrough and announcement behavior;
- contrast-tool measurements for semantic states;
- controlled Windows 200% scaling with long Russian strings;
- Product Owner, Windows/WPF Tech Lead and QA approval.

These checks keep Gate 5.2 open even though all 45 component families now have representative prototype evidence.
