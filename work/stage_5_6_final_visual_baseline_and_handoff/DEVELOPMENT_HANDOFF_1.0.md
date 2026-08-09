# Task — Development Handoff 1.0.1

## What development receives

- Editable React/CSS prototype source and reproducible production build.
- Design System 1.0 with 45 component-family contracts, tokens, variants, states, keyboard/UIA/scaling and failure rules.
- Final traceability for 128 SCR, 37 FLOW, 56 named state contracts and 38 role/capability contracts.
- Accessibility, High-DPI proxy evidence, usability evidence, decision log, finding register and asset/license inventory.

## Implementation sequence

1. Establish native Windows shell, tokens, focus and state primitives.
2. Implement P1: Auth, Shell, Today, Inbox, Tasks, Search, offline/read-only/conflict.
3. Implement P2: Calendar, Projects, Files, CRM, Notifications.
4. Implement P3: Archive/Trash, Settings, Admin and Operations.
5. Validate each module against the packaged SCR/FLOW/role/state rows before merging.

## Contract rules

Do not infer missing permissions, errors, DTO fields or API operations. Treat the canonical sources and packaged traceability as authoritative. Permission-safe redaction, honest offline read-only behavior, version-conflict draft preservation and explicit dangerous-action guards are mandatory.

## Reproduction

From `prototype/`, install locked dependencies, run the production build and execute all `tests/*.test.mjs`. The accepted snapshot is Vite 6.4.2, 224 transformed modules, 15/15 tests passing. The single JavaScript chunk warning above 500 kB is non-blocking but should be considered during production optimization.

## Sign-off boundary

The package is design-delivery complete, but Gate 5.6 is not signed. Native Windows UIA/Narrator, actual OS DPI, moderated participants and named Product/Design/Desktop/QA approvals remain external readiness evidence. Execute the included `gate-execution-kit/`; its validator currently reports NOT_READY 0/9.
