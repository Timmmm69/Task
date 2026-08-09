# Stage 5.2 — Component Implementation Specs 0.9

**Date:** 2026-07-28  
**Status:** implementation candidate, behavior frozen  
**Source:** `Component_Usage_Map_1.0.csv`

## Shared rules

1. Server authorization, object version and synchronization readiness are authoritative.
2. No component may report success before the server-confirmed result is known.
3. Disabled actions disclose a reason only when disclosure itself is authorized.
4. Status is never color-only; text and/or an official Fluent icon carry the meaning.
5. Keyboard order follows visual order. Modal surfaces trap focus and return it deterministically.
6. All components expose stable UIA name, role, state and value.
7. Components must retain meaning at Windows 200% scaling and with long Russian strings.
8. Loading, empty, error, read-only, permission and conflict behavior are composed from shared state components.

## Readiness

| Status | Families | Meaning |
|---|---:|---|
| Prototype-verified | 45 | Representative variants work in the Direction 2 prototype |
| Partially verified | 0 | Some behavior is proven; named variants remain |
| Specified | 0 | Contract is frozen; visual construction/evidence is pending |

## Family index

| ID | Component | Path | Priority | Readiness | Surface count |
|---|---|---|---|---|---:|
| CMP-001 | SurfaceTitle | `Task/Core/SurfaceTitle` | P0 | Prototype-verified | 128 |
| CMP-002 | PermissionState | `Task/State/PermissionState` | P0 | Prototype-verified | 76 |
| CMP-003 | ConnectivityBanner | `Task/State/ConnectivityBanner` | P0 | Prototype-verified | 52 |
| CMP-004 | FieldLabel | `Task/Core/FieldLabel` | P0 | Prototype-verified | 52 |
| CMP-005 | FormLayout | `Task/Pattern/FormLayout` | P0 | Prototype-verified | 52 |
| CMP-006 | ReadOnlyBanner | `Task/State/ReadOnlyBanner` | P0 | Prototype-verified | 52 |
| CMP-007 | RetryAction | `Task/State/RetryAction` | P0 | Prototype-verified | 52 |
| CMP-008 | ValidationMessage | `Task/State/ValidationMessage` | P0 | Prototype-verified | 52 |
| CMP-009 | PageLayout | `Task/Pattern/PageLayout` | P0 | Prototype-verified | 47 |
| CMP-010 | ConflictNotice | `Task/State/ConflictNotice` | P0 | Prototype-verified | 45 |
| CMP-011 | DialogShell | `Task/Core/DialogShell` | P0 | Prototype-verified | 40 |
| CMP-012 | FocusTrap | `Task/Primitive/FocusTrap` | P0 | Prototype-verified | 40 |
| CMP-013 | ErrorMessage | `Task/State/ErrorMessage` | P0 | Prototype-verified | 38 |
| CMP-014 | DataList | `Task/Core/DataList` | P0 | Prototype-verified | 37 |
| CMP-015 | SemanticStatus | `Task/State/SemanticStatus` | P0 | Prototype-verified | 34 |
| CMP-016 | TaskRow | `Task/Domain/TaskRow` | P0 | Prototype-verified | 32 |
| CMP-017 | TaskStatusControl | `Task/Domain/TaskStatusControl` | P0 | Prototype-verified | 32 |
| CMP-018 | UrgencyIndicator | `Task/Domain/UrgencyIndicator` | P0 | Prototype-verified | 32 |
| CMP-019 | InspectorPanel | `Task/Pattern/InspectorPanel` | P0 | Prototype-verified | 28 |
| CMP-020 | EmptyState | `Task/State/EmptyState` | P0 | Prototype-verified | 20 |
| CMP-021 | LoadingState | `Task/State/LoadingState` | P0 | Prototype-verified | 18 |
| CMP-022 | CommandBar | `Task/Core/CommandBar` | P0 | Prototype-verified | 3 |
| CMP-023 | ConnectionStatus | `Task/State/ConnectionStatus` | P0 | Prototype-verified | 3 |
| CMP-024 | NavigationRail | `Task/Core/NavigationRail` | P0 | Prototype-verified | 3 |
| CMP-025 | ProfileMenu | `Task/Core/ProfileMenu` | P0 | Prototype-verified | 3 |
| CMP-026 | PeoplePicker | `Task/Domain/PeoplePicker` | P1 | Prototype-verified | 32 |
| CMP-027 | DateTimePicker | `Task/Domain/DateTimePicker` | P1 | Prototype-verified | 30 |
| CMP-028 | TimelineHistory | `Task/Domain/TimelineHistory` | P1 | Prototype-verified | 28 |
| CMP-029 | ProjectPicker | `Task/Domain/ProjectPicker` | P1 | Prototype-verified | 23 |
| CMP-030 | FilterBar | `Task/Pattern/FilterBar` | P1 | Prototype-verified | 22 |
| CMP-031 | ReminderEditor | `Task/Domain/ReminderEditor` | P1 | Prototype-verified | 22 |
| CMP-032 | FileLocationView | `Task/Domain/FileLocationView` | P1 | Prototype-verified | 21 |
| CMP-033 | RecurrenceEditor | `Task/Domain/RecurrenceEditor` | P1 | Prototype-verified | 19 |
| CMP-034 | ProgressIndicator | `Task/State/ProgressIndicator` | P1 | Prototype-verified | 18 |
| CMP-035 | LifecycleBanner | `Task/State/LifecycleBanner` | P1 | Prototype-verified | 15 |
| CMP-036 | NotificationItem | `Task/Domain/NotificationItem` | P1 | Prototype-verified | 15 |
| CMP-037 | SearchBox | `Task/Core/SearchBox` | P1 | Prototype-verified | 15 |
| CMP-038 | RedactionMarker | `Task/State/RedactionMarker` | P1 | Prototype-verified | 10 |
| CMP-039 | Pagination | `Task/Pattern/Pagination` | P1 | Prototype-verified | 7 |
| CMP-040 | PopoverSurface | `Task/Pattern/PopoverSurface` | P1 | Prototype-verified | 7 |
| CMP-041 | ContextMenu | `Task/Pattern/ContextMenu` | P1 | Prototype-verified | 6 |
| CMP-042 | BulkResultSummary | `Task/State/BulkResultSummary` | P2 | Prototype-verified | 4 |
| CMP-043 | CommentThread | `Task/Domain/CommentThread` | P2 | Prototype-verified | 4 |
| CMP-044 | SelectionBar | `Task/Pattern/SelectionBar` | P2 | Prototype-verified | 4 |
| CMP-045 | TreeView | `Task/Pattern/TreeView` | P2 | Prototype-verified | 2 |

## Detailed contract

The machine-readable `Component_Implementation_Specs_0.9.csv` is authoritative for per-family purpose, variants, STATE/NFR inputs, keyboard/UIA/scaling behavior, failure rule, evidence and remaining verification.

## Gate boundary

This artifact completes the specification/usage-map portion of `S5-0215`. Gate 5.2 still requires remaining component construction, controlled Windows UIA/Narrator/200%/contrast evidence and formal Product Owner/Windows Tech Lead/QA approval.
