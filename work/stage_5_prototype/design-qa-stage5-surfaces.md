# Stage 5.2 Core Surfaces Design QA — Direction 2

**Version:** 0.4  
**Date:** 2026-07-28  
**Visual truth:** `work/stage_5_1/directions/Stage_5_1_Direction_2.png`  
**Implementation:** `work/stage_5_prototype`  
**Result:** PASS  
**Gate status:** open

## Scope

This wave extends the selected Direction 2 prototype with working product surfaces and remaining P0/P1 boundary states:

- My Tasks table with status/project filters and pagination;
- Projects tree with selection, grouping and project inspector;
- Notification Center with unread/read filters, target recheck and explicit changed-target result;
- People and Project picker patterns in the Task editor;
- `SESSION_REVOKED`, update download/signature failure and repeated-recovery-failure states.

## Browser walkthrough

| Scenario | Expected | Result |
|---|---|---|
| My Tasks navigation | active navigation state and task table | PASS |
| Task filters | native labelled filters update results and reset pagination | PASS |
| Pagination | page 1/2 controls expose current/disabled states | PASS |
| Project tree | groups expand/collapse; selected project updates inspector | PASS |
| Project inspector | progress, facts, participants and tabs remain readable | PASS |
| Notification Center | unread/read filters and mark-read actions are keyboard-operable | PASS |
| Changed target action | no false success; current server state is shown inline | PASS |
| People/Project pickers | labelled native combobox semantics with visible selected chips | PASS |
| Session revoked | blocking alert dialog hides authorized cache until sign-in | PASS |
| Signature/download failure | unsupported client cannot bypass verification | PASS |
| Repeated failure | recovery loop stops after two safe attempts | PASS |
| 1280 × 720 layout | document/body horizontal overflow = 0 | PASS |
| Production build | Vite build, 222 modules | PASS |
| Serena diagnostics | no diagnostics | PASS |

## Visual review

The new table, tree, inspector and notification panel use the same typography, density, blue selection role, neutral surfaces, Fluent iconography and compact Windows desktop shell as the selected timeline direction.

One P1 layout issue was found during visual QA: at 1280 px the expanded header pushed connection/profile controls into an implicit second row. The compact header breakpoint was moved to 1400 px and the page title switched to a stacked title/subtitle composition. The repeated screenshot shows all header controls in one row and no horizontal overflow.

| Severity | Open findings |
|---|---:|
| P0 — blocking | 0 |
| P1 — major | 0 |
| P2 — visible quality | 0 |

## Visual evidence

- `implementation-direction2-tasks-final.png`
- `implementation-direction2-projects-final.png`
- `edge-notification-target-changed.png`
- `edge-session-revoked.png`
- `edge-bootstrap-repeated-failure.png`
- `implementation-direction2-surfaces-final.png`

## Boundary

This is working Design QA evidence, not Gate approval. UI Automation, Narrator, controlled Windows 200% scaling, contrast-tool output, component/spec freeze and Product Owner/Windows Tech Lead/QA approval remain required.
