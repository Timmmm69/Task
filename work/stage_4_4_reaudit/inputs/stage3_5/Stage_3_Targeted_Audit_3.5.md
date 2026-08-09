# Stage 3.5 — Targeted Audit

## Scope and evidence

Audit scope is limited to OQ-001 (organization urgency scale) and OQ-003 (employees in global search). Evidence: Stage 2.3.1 OpenAPI `1.2.0-stage2.3`, DTO field catalog, API/permission/error catalogs, contract delta and runtime validation; Stage 3.4 baseline; Stage 4.1.1 candidate only as downstream context.

## Gate results

| # | Check | Evidence/result | Status |
|---:|---|---|---|
| 1 | New operations exist | GET/PUT urgency scale, POST reset; employee reuses GET search | PASS |
| 2 | New DTOs exist | 5/5 schemas confirmed | PASS |
| 3 | All fields confirmed | 9 urgency fields + 10 employee/search fields; no avatar/HEX inventions | PASS |
| 4 | Type/format | int32/int64, uuid, uri, date-time and nullable unions match OpenAPI | PASS |
| 5 | Required/nullable | Urgency response/request and Employee required/nullable sets preserved | PASS |
| 6 | Enum/limits/defaults | 4 urgency levels; scores 0–100; 4 intervals; search types max 10; defaults recorded | PASS |
| 7 | Permissions | Existing Settings.ReadOwn, System.Configure, Search.Use, User.Block only | PASS |
| 8 | Stable errors | Existing AUTHENTICATION_REQUIRED, FORBIDDEN, VALIDATION_FAILED, VERSION_CONFLICT and cursor errors only | PASS |
| 9 | Optimistic locking | PUT/reset require If-Match; ETag response; idempotency and recovery documented | PASS |
| 10 | Employee search concept | Separate group/type; DTO-only fields; blocked/redaction/deep link/cursor rules | PASS |
| 11 | Urgency concept | Organization owner; no override; semantic labels; defaults; audit; old-client behavior | PASS |
| 12 | No UI without API | CMP-001/002 and FLOW-035 map to confirmed operations/DTO | PASS |
| 13 | No API without UX | All three operations and employee search delta have screens/flows/states | PASS |
| 14 | No provisional/unverified | Exact scan: `unverified=0`, `provisional=0` | PASS |
| 15 | No client post-filter | Explicitly forbidden in screens, flow, state and API traceability | PASS |
| 16 | Accessibility | Keyboard, screen reader, High Contrast, non-color indicators, interval messages | PASS |
| 17 | Critical/High | No open Critical or High defect | PASS |

## Field traceability audit

- Baseline rows: 1040.
- Added Stage 3.5 rows: 38.
- Final rows: 1078.
- New contract-dependent controls: 20: 19 field-backed controls plus one bodyless reset command bound directly to its OpenAPI operation.
- The bodyless reset command has no request DTO by contract; its operation, response fields, permission, If-Match/ETag, idempotency and stable errors are traced explicitly.
- Existing `types` rows for SCR-133/134/135 were updated from maxItems 9 to 10 and enum `employee`.
- Read-only metadata cannot be edited; PUT uses complete replacement semantics; nullable employee fields remain nullable.

## Findings

| Audit ID | Severity | Artifact | UX ID | Source | Defect | Consequence | Fix | Verification | Status |
|---|---|---|---|---|---|---|---|---|---|
| AUD-3.5-001 | Low | Field traceability | SCR-153 | Reset operation | Bodyless reset initially had no action-level row | Command evidence could be overlooked | Added operation-bound trace row in addition to response-field rows | Row/operation check | Fixed |

Open findings: Critical 0; High 0; Medium 0; Low 0.

## OQ closure

- OQ-001: `Fixed` at UX level. Contract confirmed; controls and rows complete; permissions/errors/accessibility/concurrency audited.
- OQ-003: `Fixed` at UX level. Result type/group, fields, server filtering/redaction, blocked policy, cursor and navigation audited.

Targeted audit result: **PASS**.
