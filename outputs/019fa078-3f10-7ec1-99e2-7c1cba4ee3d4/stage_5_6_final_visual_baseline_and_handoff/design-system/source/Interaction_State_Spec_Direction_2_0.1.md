# Stage 5.1 — Interaction State Spec, Direction 2

Version: 0.1  
Date: 2026-07-28  
Decision: VIS-001 — Direction 2, Timeline planner

## Required component states

| Surface | Default | Hover | Focus | Selected / active | Disabled / read-only |
|---|---|---|---|---|---|
| Navigation item | Icon + label | Neutral fill | 2 px brand focus | Brand icon/text, soft fill, leading bar | Muted text, no action |
| Primary button | Brand fill | Strong brand fill | 2 px brand focus | Pressed strong brand | 50% opacity |
| Timeline task | White surface | Neutral fill | 2 px brand focus | Brand border + soft fill + `aria-pressed` | Read-only details remain inspectable |
| Unscheduled row | White surface | Soft neutral fill | 2 px brand focus | Soft brand fill | Metadata remains visible |
| Section disclosure | Chevron + label | Neutral fill | 2 px brand focus | Chevron orientation + `aria-expanded` | N/A |
| Status selector | Border + icon + value | Native select hover | Native keyboard focus | Current status text and icon | Disabled when capability denies edit |
| Checklist | Native checkbox + label | Pointer affordance | Native focus | Checkmark + checked state | Disabled with visible label |
| Dialog | Hidden | N/A | Initial focus on title field | Modal with focusable actions | Create disabled until title exists |

## Resilience states

| State | Visible treatment | Behavior |
|---|---|---|
| Connected, writable | Green dot + “Подключено…” + “Онлайн”; footer source text | Authorized create/edit actions are available |
| Offline | Red dot + “Нет подключения…” + “Работа офлайн” | Authorized cache stays readable; business writes are disabled |
| Reconnecting | Progress icon + session/server/cursor copy | Writes remain disabled; retry can be interrupted without false success |
| Scope changed | Lock icon + explicit cache-rebuild copy | Inaccessible objects are removed before writes return |
| Maintenance | Server icon + retry-after guidance | Authorized cache remains read-only |
| Storage full | Storage icon + required free-space guidance | Current cache is preserved; writes and cache update remain disabled |
| Recovered | Green dot + “Подключение восстановлено” + sync confirmation | Writes return only after authoritative session/scope/data readiness |
| Loading | Reserved skeleton/state component in Stage 5.2 library | Preserve layout dimensions |
| Empty | Plain-language empty message plus primary next action | No decorative-only empty state |
| Error | Critical icon + title + recovery action + diagnostic detail | Never color-only |
| Conflict | Side-by-side values, origin/timestamp labels, explicit resolution action | No silent overwrite |

The connected, offline, reconnecting, scope-changed, maintenance, storage-full and recovered states are demonstrable through the prototype connection control and recovery actions.

## Keyboard and focus order

1. Window controls.
2. Sidebar navigation.
3. Date controls and primary “Новая задача”.
4. Connection and user controls.
5. Timeline and unscheduled task rows.
6. Detail status, checklist, file and comments disclosure.
7. Status footer sync action.

Global shortcut: `Alt+N` opens New Task. `Escape` closes the dialog. Native Tab/Shift+Tab traversal is preserved.

## Non-color semantics

- Priority: directional Fluent icon + text label.
- Current time: textual timestamp + red rule/dot.
- Connection: dot + two-line text.
- Selection: border + fill + pressed/expanded state.
- Completion: checkmark icon + status text.
- Read-only: lock icon + explicit footer copy.

## Prototype evidence

- `work/stage_5_prototype/src/App.jsx`
- `work/stage_5_prototype/src/styles.css`
- `work/stage_5_prototype/design-qa.md` after the browser comparison loop

## P0 vertical-slice extension

| Surface | Required states | Implemented behavior |
|---|---|---|
| First connection | endpoint idle / invalid / verified | HTTPS validation, certificate-success message, continuation gated by verification |
| Authentication | idle / invalid credentials / authorized | inline error without destructive reset; successful authentication advances to authorized bootstrap |
| Authorized bootstrap | tasks/projects / directories / sync cursor ready | scope and counts shown before the user may enter the workspace |
| Global Search | default / filtered / selected / empty / redacted / cached | `Ctrl+K`, arrow selection, Enter, Escape, grouped results and permission-safe restricted item |
| Inbox | quick capture / selected / converted / read-only | capture creates a source record; conversion gathers required task fields and deterministically closes the source |
| Task editor | editable / saving / conflict | save can surface `VERSION_CONFLICT`; the local draft is retained |
| Conflict comparison | local / server / explicit resolution | reload, reapply or discard are separate user actions; no silent overwrite |
| Server loss | connected / unavailable / diagnostics / recovered | authorized cache remains readable; write controls are disabled; retry restores writes only after authoritative readiness |

### P0 keyboard order

1. Sidebar navigation, including Inbox and Search.
2. Global search trigger (`Ctrl+K`) and query field.
3. Search filters, results and close action.
4. Inbox quick capture, record list and inspector conversion action.
5. Conversion or edit form in source order.
6. Conflict-resolution actions in least-destructive to primary recovery order.
7. Diagnostics close and retry actions.

### P0 evidence

- `work/stage_5_prototype/design-qa-stage5-p0.md`
- `work/stage_5_prototype/p0-auth-endpoint.png`
- `work/stage_5_prototype/p0-inbox-conversion.png`
- `work/stage_5_prototype/p0-search-redaction.png`
- `work/stage_5_prototype/p0-conflict.png`
- `work/stage_5_prototype/p0-offline-readonly.png`
- `work/stage_5_prototype/design-qa-stage5-edge-states.md`
- `work/stage_5_2/Accessibility_Evidence_Working_0.2.md`

## Core surface extension

| Surface | Interaction contract | State contract |
|---|---|---|
| My Tasks | Navigate from rail; filter by Status/Project; open row; change page | Filter reset returns to page 1; empty filters expose recovery; writes disable in degraded mode |
| Projects | Expand/collapse groups; select tree item; inspect progress/facts/tabs | Selected project uses non-color highlight and `aria-selected`; cached project is explicitly read-only |
| Notification Center | Open from bell; filter unread/all; mark read; execute target action | Server target is rechecked before action; changed target shows current state and never reports false success |
| People/Project picker | Native keyboard selection with visible selected chip | Options are permission-safe; unavailable people/projects are not disclosed |
| Session revoked | Blocking recovery action only | Authorized cache is covered and not available until sign-in |
| Update failure | Retry verified download/signature sequence | Partial package is removed; incompatible client cannot continue |
| Repeated recovery failure | Retry is bounded | After two safe failures the loop stops in an explicit blocking/error state |

### Core surface keyboard order

1. Sidebar navigation.
2. Page title and global commands.
3. Tasks filters, table rows/actions and pagination; or Projects tree then inspector tabs.
4. Notification filters, notification actions and close.
5. Task editor title, Project picker, Assignee picker, description and save actions.
6. Blocking session-revoked recovery action.

### Core surface evidence

- `work/stage_5_prototype/design-qa-stage5-surfaces.md`
- `work/stage_5_prototype/implementation-direction2-tasks-final.png`
- `work/stage_5_prototype/implementation-direction2-projects-final.png`
- `work/stage_5_prototype/edge-notification-target-changed.png`
- `work/stage_5_prototype/edge-session-revoked.png`
- `work/stage_5_prototype/edge-bootstrap-repeated-failure.png`
- `work/stage_5_2/Accessibility_Evidence_Working_0.3.md`
