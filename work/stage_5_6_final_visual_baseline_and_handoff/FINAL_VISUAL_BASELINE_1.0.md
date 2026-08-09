# Task — Stage 5 Final Visual Baseline 1.0.1

**Frozen:** 2026-08-09  
**Direction:** Direction 2 — Timeline planner.  
**Editable baseline:** code-based React/CSS source in `prototype/src`; no external Figma file is claimed.

## Canonical visual language

Task uses a dense Windows desktop shell with a persistent left navigation, contextual top command bar, list/timeline workspace and task inspector. Fluent icons, system typography, restrained blue emphasis, neutral surfaces, semantic status colours and visible focus remain the canonical implementation.

## Frozen surfaces

Auth/bootstrap; Today; Calendar and CalendarEvent editor; Inbox and conversion; My Tasks; Projects; Files and file-location recovery; CRM; Search/redaction; notifications; Archive/Trash; Settings; Admin; Operations; offline/read-only/reconnect; conflict/session/maintenance/storage and validation states.

## Source of truth

1. `prototype/src/App.jsx` — interactive surfaces, roles, permissions, states and flows.
2. `prototype/src/styles.css` — layout, tokens, responsive rules, focus, reduced motion and forced-colors behavior.
3. `design-system/Design_System_1.0.md` and frozen component specs — implementation contract.
4. `traceability/` — normative evidence mapping.
5. `evidence/usability-screenshots/` — current-run representative states.

Production build is frozen in `prototype/dist`; the code remains editable for implementation handoff.
