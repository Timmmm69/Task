# Stage 5.2 P0 Design QA — Direction 2

**Version:** 0.2  
**Date:** 2026-07-28  
**Visual truth:** `work/stage_5_1/directions/Stage_5_1_Direction_2.png`  
**Implementation:** `work/stage_5_prototype`  
**Result:** PASS

## Scope

This pass extends the already approved Direction 2 Today timeline slice with the remaining P0 vertical-slice surfaces:

- first connection, endpoint verification, authentication failure and authorized bootstrap;
- Inbox quick capture and deterministic Inbox → Task conversion;
- task edit with explicit `VERSION_CONFLICT` comparison and recovery choices;
- global search, grouped results, keyboard selection and permission-safe employee redaction;
- server loss, authorized read-only cache, diagnostics and authoritative recovery.

The visual truth contains only the Today state. New Auth, Inbox, Search, Conflict and Diagnostics surfaces are therefore checked for token, density, hierarchy, control and state consistency with Direction 2; no unsupported source frames are claimed.

## Same-input visual comparison

Comparison input: `design-qa-comparison-p0-final.png`.

The left side is the selected Direction 2 visual truth. The right side is the browser implementation after the P0 wave. The exact 1487 × 1058 Today comparison passed in the preceding Direction 2 QA; this continuation also passed a compact 1280 × 720 browser check after adding Search and Inbox navigation.

Intentional, contract-driven additions:

- Search command with `Ctrl+K`;
- Inbox and Search navigation entries;
- writable online wording instead of the earlier read-only placeholder;
- edit action and resilience states.

These changes do not alter the selected timeline planner composition, split-panel hierarchy, typography family, density model or color semantics.

## Findings

| Severity | Count | Result |
|---|---:|---|
| P0 — blocking | 0 | PASS |
| P1 — major | 0 | PASS |
| P2 — visible quality | 0 | PASS |

## Interaction evidence

| Scenario | Evidence | Result |
|---|---|---|
| First connection | HTTPS endpoint verification enables continuation | PASS |
| Invalid credentials | Inline non-destructive error, password retained for correction | PASS |
| Authorized bootstrap | Data scope, counts and synchronization cursor shown before entry | PASS |
| Search keyboard | `Ctrl+K`, Arrow Up/Down, Enter and Escape exercised | PASS |
| Employee redaction | Restricted result remains closed and emits access-policy status | PASS |
| Search filters | Employees filter returns only employee-group results | PASS |
| Inbox capture | Quick capture adds and selects a new inbox record | PASS |
| Inbox conversion | Required task fields shown; task created; source marked converted | PASS |
| Edit conflict | Local draft and server values compared; no silent overwrite | PASS |
| Conflict recovery | Reapply updates the selected task only after explicit choice | PASS |
| Server loss | New task, edit, status and checklist writes disabled | PASS |
| Diagnostics | Server/cache/sync/mode facts exposed without external telemetry | PASS |
| Recovery | Write controls restored only after successful retry state | PASS |
| Compact desktop | 1280 × 720 document width/height overflow = 0 | PASS |
| Production build | Vite production build completed successfully | PASS |

## Accessibility observations

- Core controls use native buttons, inputs, selects and checkboxes.
- Dialogs expose accessible names, modal state and labelled headings.
- Search results expose listbox/option selection and restricted-result disabled state.
- Connection and write restrictions are communicated in text, not color alone.
- Focus-visible styling is retained across new components.

Full UI Automation, Narrator, 200% scaling and contrast-tool evidence remain outside this working pass and continue under `S5-0214`.

## Evidence files

- `p0-auth-endpoint.png`
- `p0-auth-ready.png`
- `p0-inbox-conversion.png`
- `p0-search-redaction.png`
- `p0-conflict.png`
- `p0-offline-readonly.png`
- `implementation-direction2-p0-final.png`
- `design-qa-comparison-p0-final.png`

