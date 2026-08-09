# Stage 5.2 Accessibility Evidence — Working 0.3

**Date:** 2026-07-28  
**Prototype:** Direction 2 core surfaces + boundary-state wave  
**Status:** WORKING PASS

## Browser semantic evidence

| Surface | Evidence | Result |
|---|---|---|
| Today / Inbox | named regions, native controls and explicit state text | PASS |
| My Tasks | named region, labelled filters, semantic table and pagination navigation | PASS |
| Projects | named region, grouped tree/treeitems, selected state and labelled tablist | PASS |
| Notification Center | named dialog, filter group, unread count and inline target-change status | PASS |
| Task editor pickers | labelled Project and Assignee comboboxes with visible selected values | PASS |
| Session revoked | modal `alertdialog`, explicit error code and single recovery action | PASS |
| Bootstrap failure | alert/status semantics and no bypass action | PASS |
| Global commands | Search, New Task, Notifications, Connection and Profile retain accessible names | PASS |

## Keyboard evidence

- `Ctrl+K` opens Global Search.
- Tab traversal reaches Tasks filters, table row actions and pagination.
- Native select controls support keyboard selection for filters and pickers.
- Project group toggles expose expanded state; tree items expose selected state.
- Notification filter/actions are native buttons; changed-target result uses a live status region.
- Escape closes non-blocking overlays.
- `SESSION_REVOKED` remains blocking until “Войти снова”.
- Disabled write controls retain disabled state during read-only recovery.

## Scaling and responsive evidence

| Check | Evidence | Result |
|---|---|---|
| Active Windows/browser device scale | `devicePixelRatio = 1.5` | PASS at 150% |
| 1280 × 720 viewport | document/body horizontal overflow = 0 | PASS |
| Compact header | six core commands remain in one row at 1280 px | PASS |
| Long Russian labels | table, tree, inspector and notification messages wrap or truncate with visible context | PASS |
| Reduced motion | reconnect spinner disabled under `prefers-reduced-motion: reduce` | IMPLEMENTED |
| Actual Windows 200% scaling | controlled OS-level test session required | PENDING |

## Issue found and corrected

The first 1280 × 720 project screenshot showed header controls wrapping into an implicit second grid row. The compact header breakpoint was moved to 1400 px and the title/subtitle composition was stacked. The repeated screenshot has no document/body horizontal overflow and preserves names for all compact controls.

## Remaining formal evidence

- Windows UI Automation name/role/state capture;
- Narrator walkthrough and announcement behavior;
- contrast-tool measurements for semantic colors;
- actual Windows 200% scaling and long-string screenshots;
- deterministic focus-return verification in the desktop runtime.

These checks keep `S5-0214` and Gate 5.2 open.
