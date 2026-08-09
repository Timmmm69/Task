# Stage 5.2 — Component Library Architecture 0.1

**Status:** architecture complete; visual styling awaits `VIS-001`  
**Inputs:** `Component_Inventory_0.1.csv`, `Component_Family_Summary_0.1.csv`, Accessibility Baseline 0.1  
**Coverage:** 128/128 normative surfaces, 45 shared component families  
**Date:** 2026-07-28

## 1. Library structure

The editable design library is organized into the following pages/tiers:

| Order | Page / tier | Purpose |
|---:|---|---|
| 00 | Cover & Release | Version, owner, status, changelog and usage rules |
| 01 | Foundations | Typography, semantic color, spacing, geometry, elevation, motion and density tokens |
| 02 | Primitives | Focus ring, icons, labels, text, dividers and basic containers |
| 03 | Core Components | Inputs, actions, navigation, lists, dialogs and status components |
| 04 | State Components | Loading, empty, error, permission, offline/read-only, conflict and lifecycle |
| 05 | Domain Components | Task, people, project, recurrence, reminder, file and notification families |
| 06 | Patterns | Page, command bar, inspector, filtering, selection, forms and recovery patterns |
| 07 | Templates | Shell, list-detail, editor, settings, admin and blocking-state templates |
| 08 | Vertical Slice | Auth, Shell/Today, Inbox/Task, Search and resilience flows |
| 09 | QA & Traceability | SCR/FLOW/STATE usage, accessibility evidence and deprecated components |

Visual foundations are not populated with final values before `VIS-001`. Architecture, naming, ownership and semantic roles are independent of that decision.

## 2. Naming contract

Component names use:

```text
Task/{Tier}/{Family}/{Component}
```

Examples:

```text
Task/Core/Navigation/NavigationRail
Task/Core/DataDisplay/DataList
Task/State/Connectivity/ReadOnlyBanner
Task/Domain/Task/TaskRow
Task/Pattern/Layout/ListDetail
```

Variant property names use English identifiers for development handoff; visible sample content remains Russian:

```text
Size        = Compact | Standard | Comfortable
State       = Default | Hover | Pressed | Focused | Disabled
Validation  = None | Error | Warning | Success
Selection   = None | Focused | Selected | Checked
Connection  = Online | Offline | Reconnecting | Maintenance
Lifecycle   = Active | Completed | Archived | Trashed
Permission  = Available | DisabledWithReason | Hidden | ForbiddenResult
Density     = Compact | Standard
```

Properties are added only where semantically valid. Components must not expose every global property as a meaningless Cartesian product.

## 3. Semantic token architecture

Final values are selected after `VIS-001`; token roles are fixed now.

### Color roles

```text
color.background.base
color.background.subtle
color.surface.default
color.surface.selected
color.text.primary
color.text.secondary
color.text.disabled
color.border.subtle
color.border.strong
color.focus
color.action.primary
color.action.destructive
color.status.info
color.status.success
color.status.warning
color.status.danger
color.status.offline
```

Status colors are always paired with text and/or icon semantics.

### Typography roles

```text
type.display.page
type.heading.section
type.heading.component
type.body.default
type.body.secondary
type.label.control
type.label.compact
type.code.identifier
```

### Layout roles

```text
space.1 … space.8
size.control.compact
size.control.standard
size.row.compact
size.row.standard
radius.control
radius.overlay
elevation.overlay
elevation.dialog
```

Tokens must survive Windows scaling 100–200% without clipped labels or inaccessible hit/focus areas.

## 4. Component tiers

### P0 — foundation and vertical slice

| Family | Surfaces | Required first variants |
|---|---:|---|
| SurfaceTitle | 128 | page, panel, dialog |
| PermissionState | 76 | disabled-with-reason, forbidden-result, unavailable |
| ConnectivityBanner | 52 | offline, reconnecting, maintenance, unavailable |
| FieldLabel | 52 | normal, required, disabled, error |
| FormLayout | 52 | simple, sectioned, dirty, read-only |
| ReadOnlyBanner | 52 | offline, archived, trashed, permission |
| RetryAction | 52 | safe-read retry, diagnostics |
| ValidationMessage | 52 | inline, summary-linked |
| PageLayout | 47 | list, list-detail, editor, settings |
| ConflictNotice | 45 | stale, precondition, compare/reapply |
| DialogShell | 40 | modal, blocking, destructive |
| FocusTrap | 40 | initial, cycle, return target |
| ErrorMessage | 38 | inline, block, page, trace ID |
| DataList | 37 | loading, empty, selected, stale, partial |
| SemanticStatus | 34 | text + icon + color |
| TaskRow | 32 | default, focused, selected, overdue, read-only |
| TaskStatusControl | 32 | allowed, pending, disabled, conflict |
| UrgencyIndicator | 32 | low, medium, high, critical with text |
| InspectorPanel | 28 | empty selection, loading, details, stale |
| EmptyState | 20 | authorized empty, filtered empty, no cache |
| LoadingState | 18 | initial, refreshing, section |
| CommandBar | 3 | page, selection, narrow |
| ConnectionStatus | 3 | online, syncing, offline/read-only |
| NavigationRail | 3 | expanded, compact, capability-filtered |
| ProfileMenu | 3 | normal, offline, session issue |

### P1 — module expansion

PeoplePicker, DateTimePicker, TimelineHistory, ProjectPicker, FilterBar, ReminderEditor, FileLocationView, RecurrenceEditor, ProgressIndicator, LifecycleBanner, NotificationItem, SearchBox, RedactionMarker, Pagination, PopoverSurface and ContextMenu.

### P2 — specialized patterns

BulkResultSummary, CommentThread, SelectionBar and TreeView.

Priority defines build order, not business importance. A P2 component still remains mandatory for every owning SCR.

## 5. Composition rules

1. `PageLayout` composes `SurfaceTitle`, `CommandBar`, content and optional `InspectorPanel`.
2. `DataList` composes collection semantics, row focus/selection, `LoadingState`, `EmptyState`, `ErrorMessage` and pagination where required.
3. `TaskRow` composes title, project/assignee summary, `TaskStatusControl`, `UrgencyIndicator` and due-date semantics.
4. `FormLayout` composes `FieldLabel`, input/picker, `ValidationMessage`, dirty/read-only state and command area.
5. `DialogShell` owns title, initial focus, focus trap, actions and return-focus target.
6. `ConnectivityBanner` and `ReadOnlyBanner` may coexist only when each adds distinct information; duplicate messages are prohibited.
7. `PermissionState` controls presentation only; server authorization remains authoritative.
8. `ConflictNotice` preserves the local draft and exposes reload/compare/reapply/discard only where the owning SCR allows them.
9. Domain components consume core/state components; they do not fork accessibility or status semantics.
10. Page templates must use the same shared components across Admin, Settings and daily-work modules.

## 6. State model

Durable variants reuse the published State Matrix:

```text
Initial → Loading → Content
Content → Refreshing → Content
Content → Offline/ReadOnly → Reconnecting → Content
Editor → ValidationError → Editor
Editor → Conflict → Compare/Reload/Reapply
Object → Archived/Trashed → allowed lifecycle action
Any authorized list → Empty or FilteredEmpty
Any sensitive target → ObjectUnavailable or PartialAccess
```

Transient animation or visual feedback is not promoted to a new `STATE-XXX`.

## 7. Accessibility contract

Every P0 component must include:

- visible focus;
- accessible name, role, state and value annotation;
- keyboard activation/navigation;
- high-contrast-safe evidence;
- non-color state meaning;
- 200% scaling example;
- long Russian content example;
- owner `SCR/FLOW/STATE/NFR` references.

The full contract is in `Accessibility_Baseline_0.1.md`.

## 8. Traceability contract

Each component entry records:

```text
Component ID
Owning tier/family
Variant properties
Source SCR IDs
Related FLOW/STATE/NFR
Accessibility status
Implementation status
Design version
Deprecated/replacement reference
```

`Component_Inventory_0.1.csv` is the source for surface usage. `Component_Family_Summary_0.1.csv` is the source for the 45-family backlog.

## 9. Gate status

| Check | Result |
|---|---|
| 128 unique surfaces mapped | PASS |
| 45 shared families classified | PASS |
| Library pages/tiers defined | PASS |
| Naming contract defined | PASS |
| Semantic token roles defined | PASS |
| Variant model defined | PASS |
| Accessibility contract linked | PASS |
| Final visual values | BLOCKED by `VIS-001` |
| Component visual construction | BLOCKED by `VIS-001` |

Architecture and naming are ready. This artifact does not close Gate 5.2; visual construction, vertical slice, accessibility evidence and review remain required.
