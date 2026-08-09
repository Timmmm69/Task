# Stage 5.1 — Accessibility Baseline 0.1

**Product:** Task Windows desktop organizer  
**Version:** 0.1  
**Status:** normative interaction baseline complete; visual tokens await `VIS-001`  
**Date:** 2026-07-28

## 1. Purpose

This baseline converts the approved Stage 3.5 and PRD 4.5 accessibility requirements into design checks for Stage 5. It does not add product behavior, permissions, API fields or durable UI states.

Normative product inputs:

- `Stage_4_Product_PRD_4.5.md`: NFR-002, NFR-003, NFR-004 and NFR-005.
- `Stage_3_User_Flows_Final_3.5.md`: keyboard-completable critical flows and focus recovery.
- `Stage_3_State_Matrix_Final_3.5.md`: loading, empty, permission, offline/read-only, conflict, lifecycle and recovery semantics.
- `Stage_3_Role_Interface_Matrix_Final_3.5.md`: hidden/disabled/forbidden rules.
- `Stage_3_Screen_Catalog_Final_3.5.md`: surface-level actions, states and transitions.

Platform guidance used for design verification:

- Microsoft Windows accessibility overview: https://learn.microsoft.com/en-us/windows/apps/design/accessibility/accessibility-overview
- Microsoft Windows accessibility checklist: https://learn.microsoft.com/en-us/windows/apps/design/accessibility/accessibility-checklist
- Microsoft Windows app accessibility implementation overview: https://learn.microsoft.com/en-us/windows/apps/develop/accessibility
- Microsoft WPF/Win32 accessibility design basics: https://learn.microsoft.com/en-us/windows/win32/uxguide/inter-accessibility

## 2. Mandatory design principles

1. Every critical flow is completable without a pointer.
2. Keyboard focus is always visible, deterministic and restored to a meaningful origin after overlays close.
3. Every interactive element has an accessible name, role, state and value appropriate for Windows UI Automation.
4. Status, urgency, error, selection and permission are never communicated by color alone.
5. The layout remains usable at Windows scaling from 100% through 200%, including multi-monitor transitions.
6. Read-only, offline, stale, hidden, forbidden and unavailable are distinct states with distinct language and actions.
7. Server authorization remains authoritative; accessible UI must not reveal hidden objects, counts or fields.
8. Error recovery preserves valid user input and moves focus to the first actionable problem when appropriate.

## 3. Keyboard contract

| Area | Required behavior | Design evidence |
|---|---|---|
| Global navigation | `Tab` reaches navigation; arrows move within composite navigation; activation does not require pointer | Focused and selected variants; navigation order annotation |
| Page command bar | Predictable order from page title to primary and secondary actions | Numbered focus sequence |
| Lists and grids | Arrows move within the collection; selection and activation are distinct; multi-select has a keyboard path | Row focus, selected, checked and unavailable variants |
| Forms | `Tab`/`Shift+Tab` follow visual reading order; labels precede controls; first invalid field receives focus after submit | Tab order and validation focus annotation |
| Dialogs and popovers | Focus enters the surface, remains contained when modal, closes with `Esc` when safe, and returns to the invoking control | Entry/exit focus annotations |
| Context menus | Keyboard invocation is available, including `Shift+F10` where applicable | Keyboard invocation note |
| Primary actions | `Enter` activates the default action only when unambiguous; `Space` activates the focused control | Activation mapping |
| Calendar drag/resize | Every pointer drag/resize action has a keyboard alternative | Alternative command and result state |
| Command palette | Shortcut, active descendant, result count and selection are announced | Shortcut and active-item annotations |
| Destructive actions | No destructive action is triggered by an accidental single key; confirmation focus is safe | Confirmation sequence |

Project shortcuts already specified by Stage 3 remain canonical. Stage 5 may display them but may not invent conflicting shortcuts.

## 4. Programmatic access and screen-reader semantics

| Element | Required UI Automation semantics |
|---|---|
| Button/action | Accessible name describes the action; enabled/disabled state and reason are available |
| Text input | Programmatic label, current value, required state and validation relation |
| Picker/combobox | Name, expanded/collapsed state, selected value and result count when permitted |
| List/grid/tree | Collection role, item role, position where safe, selection state and expansion state |
| Status/urgency | Text label plus icon/shape semantics; color is supplementary |
| Progress | Name, current status and determinate value when available |
| Banner | Severity and message; connection/read-only changes are announced without stealing focus |
| Dialog | Accessible title, modal state, initial focus and return-focus target |
| Tabs | Tab role, selected state and associated panel |
| Redacted field | Neutral “недоступно по правам” semantics; no hidden value or hidden count |
| Empty state | Successful empty result is distinguishable from loading, error and missing cache |
| Inline error | Associated with the owning field; summary links to the first invalid field |
| Toast/notification | Action names are explicit; stale/forbidden result is announced honestly |

Custom WPF controls require explicit UI Automation peers/patterns where the platform control does not expose sufficient semantics.

## 5. Visual accessibility

### Contrast and color

- Normal text target: at least `4.5:1`.
- Large text and essential graphical/control boundaries target: at least `3:1`.
- Focus indicators, selected rows, validation, urgency and lifecycle states remain identifiable in Windows high-contrast mode.
- Semantic status always combines at least two channels: text plus icon/shape/state.
- Disabled content remains readable; disabled styling is not used to hide permission-sensitive actions that must be removed.

### Typography and density

- Default body text is designed around a readable 14–16 px equivalent at 100% scaling.
- Text can grow under Windows scaling without overlap, clipping or loss of actions.
- Long Russian labels, user names, project names and error messages are tested.
- Truncation provides an accessible full value where disclosure is allowed.
- Dense tables preserve a clear focus row and enough vertical target area for keyboard/magnifier use.

### Scaling matrix

Every P0 surface is reviewed at:

| Scaling | Required result |
|---:|---|
| 100% | Baseline layout and density |
| 125% | No clipped primary actions or labels |
| 150% | Inspector/panel adapts or scrolls without obscuring the primary task |
| 175% | Navigation and command bar remain operable |
| 200% | Critical flow remains complete; no two-dimensional scroll trap for primary content |

Multi-monitor review includes moving the active window between monitors with different scaling.

## 6. State accessibility

| State family | Mandatory presentation |
|---|---|
| Loading | Layout-compatible progress; not presented as empty data |
| Refreshing | Existing data retained; subtle announced update |
| Empty | Purpose-specific text and only permitted creation/recovery action |
| Validation | Draft retained; inline message; focus first invalid field |
| Forbidden | Action removed or disabled according to role matrix; no hidden object detail |
| Object unavailable | Neutral message without sensitive identifiers |
| Conflict | Local draft preserved; base/server/local comparison has labeled regions |
| Offline/read-only | Persistent text banner, last successful update time and disabled-write reason |
| Reconnecting | Status update without false success |
| Archived/trashed | Read-only lifecycle banner and only allowed recovery actions |
| Partial access/redaction | Explicit neutral marker; no inferred or hidden counts |
| Background operation | Progress/status link; completion and failure announced |

## 7. Component-level evidence required in Stage 5.2

Each P0 component must contain:

1. Default, hover, pressed, focused, selected and disabled variants when applicable.
2. High-contrast-safe focus evidence.
3. Accessible name/state/value annotations.
4. Keyboard activation and navigation notes.
5. Long-text and 200% scaling example.
6. Loading, empty, error and permission variants where applicable.
7. Non-color status demonstration.
8. Reference to owning `SCR`, `FLOW`, `STATE`, NFR or role rule.

## 8. Review gates

### Gate 5.1

- Visual direction demonstrates visible focus, non-color status and readable hierarchy.
- Proposed tokens include semantic contrast roles, not fixed feature-specific colors.
- No critical action depends on pointer-only interaction.
- Result: cannot close until `VIS-001` is selected and the chosen direction is reviewed.

### Gate 5.2

- Vertical slice is keyboard-completable.
- UI Automation annotations cover all custom or composite controls.
- Focus order and focus return are documented.
- 100–200% scaling evidence exists.
- Critical/High accessibility findings = 0.

### Gate 5.4

- End-to-end role/state/accessibility matrix is complete.
- Narrator, keyboard-only, high-contrast, magnifier/scaling and long-Russian-text checks have evidence.
- Critical/High findings = 0; Medium findings are fixed or formally accepted with rationale and owner.

## 9. Current status

| Item | Status |
|---|---|
| Normative requirements extracted | PASS |
| Keyboard contract | PASS |
| UI Automation semantic contract | PASS |
| State accessibility mapping | PASS |
| Scaling review matrix | PASS |
| Visual tokens | BLOCKED by `VIS-001` |
| Selected-direction evidence | PENDING user selection |

This baseline is ready to drive component architecture and review. It does not by itself close Gate 5.1 or Gate 5.2.
