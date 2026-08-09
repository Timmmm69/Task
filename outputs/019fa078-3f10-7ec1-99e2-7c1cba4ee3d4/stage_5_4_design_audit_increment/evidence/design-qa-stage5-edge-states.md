# Stage 5.2 Edge-State Design QA — Direction 2

**Version:** 0.3  
**Date:** 2026-07-28  
**Visual truth:** `work/stage_5_1/directions/Stage_5_1_Direction_2.png`  
**Implementation:** `work/stage_5_prototype`  
**Result:** PASS

## Scope

This pass extends the Direction 2 P0 prototype with contract-required edge states from `FLOW-001`, `FLOW-002` and `FLOW-022–025`:

- endpoint invalid, unavailable, TLS failure and client/server incompatibility;
- invalid credentials, temporarily locked account and administrator-blocked account;
- authorized bootstrap progress, expired synchronization cursor and changed access scope;
- maintenance and storage-full modes;
- reconnecting, interrupted retry, scope revalidation and authoritative restoration of writes.

## Same-input comparison

`design-qa-comparison-edge-final.png` contains the selected Direction 2 visual truth and the clean Today implementation after this edge-state wave. The selected timeline composition, shell hierarchy, density, color roles, Fluent iconography and detail-panel structure remain unchanged.

New banners and Auth states intentionally extend the visual truth using the same tokens and component rules. They do not replace the selected Today composition.

## Findings

| Severity | Count | Result |
|---|---:|---|
| P0 — blocking | 0 | PASS |
| P1 — major | 0 | PASS |
| P2 — visible quality | 0 | PASS |

## Browser walkthrough

| Scenario | Expected | Result |
|---|---|---|
| Non-HTTPS endpoint | continuation disabled, validation message | PASS |
| TLS verification failure | no bypass action, IT recovery text | PASS |
| Incompatible server | client update required, continuation disabled | PASS |
| Unavailable endpoint | no stale data claim, retry guidance | PASS |
| Temporarily locked account | timed recovery and support guidance | PASS |
| Blocked account | no self-service retry claim | PASS |
| Cursor expired | read-only preservation and safe full snapshot action | PASS |
| Scope changed | inaccessible objects removed before open | PASS |
| Bootstrap loading | labelled progressbar, counts and scope statement | PASS |
| Storage full | required free space, current cache preserved | PASS |
| Maintenance | retry-after guidance and no unconfirmed data | PASS |
| Reconnecting | writes remain disabled during checks | PASS |
| Interrupted retry | returns to honest read-only mode | PASS |
| Scope revalidation | writes remain disabled until cache refresh | PASS |
| Authoritative recovery | writes restored only after explicit update | PASS |
| Maintenance shell | read-only banner and diagnostics | PASS |
| Storage-full shell | read-only banner and storage diagnostics | PASS |

## Visual evidence

- `edge-auth-tls.png`
- `edge-auth-locked.png`
- `edge-bootstrap-cursor.png`
- `edge-reconnecting.png`
- `edge-scope-recovery.png`
- `edge-maintenance-readonly.png`
- `edge-storage-readonly.png`
- `implementation-direction2-edge-final.png`
- `design-qa-comparison-edge-final.png`

## Boundary

This is working Design QA evidence, not Gate approval. Formal Product Owner and Windows/WPF technical review, UI Automation/Narrator execution, contrast-tool output and actual Windows 200% scaling remain separate Gate 5.1/5.2 evidence.

