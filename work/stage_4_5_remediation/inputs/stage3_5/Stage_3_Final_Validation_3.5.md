# Stage 3.5 — Final Validation

## Package gate

| Check | Result |
|---|---|
| Stage 2.3.1 input SHA-256 | PASS — `75EFC3E83F09FBCC41AE7DA68A96F2EC0EBDFC74E61F62615F4DA3478AFE5019` |
| UX delta input SHA-256 | PASS — `099E125F20FBB1F952B789D0BB5B8C276250576EC44A73F9B95350079C97E9C9` |
| Contract identity | PASS — 244 operations, 237 schemas, 91 permissions, 44 stable errors |
| OpenAPI/codegen/runtime evidence | PASS |
| OQ-001 / OQ-003 | Fixed / Fixed |
| Existing SCR/FLOW/STATE IDs preserved | PASS |
| New IDs | CMP-001, CMP-002, FLOW-035 only |
| New SCR / STATE | 0 / 0 |
| Contract-dependent controls | 20 (19 field-backed + 1 bodyless operation command) |
| Added field trace rows | 38; final 1078 |
| `unverified` / provisional | 0 / 0 |
| Unknown permissions / stable errors | 0 / 0 |
| Client post-filtering | 0 |
| Accessibility | PASS |
| Critical / High / Medium | 0 / 0 / 0 |
| Targeted audit | PASS |

## Behavioral validation

- Organization-only urgency ownership and lack of personal override are explicit.
- Four ordered, contiguous, non-overlapping inclusive intervals cover 0..100; defaults are 0–24, 25–49, 50–74, 75–100.
- PUT/reset use If-Match, ETag and Idempotency-Key; conflict, missing precondition, forbidden and unavailable recovery are defined.
- Existing and future notifications resolve presentation through the current scale without changing semantic urgency; Stage 2.2 clients remain compatible.
- Employee is a distinct global-search result type/group with exact DTO fields, optional job/department, no avatar, server redaction/filtering, blocked policy, deep link and cursor stability.
- Search employees is not conflated with admin users, contacts or `userIds`.

## Definition of Done

All Stage 3.5 criteria are met. Readiness: **100%**. Transition to a separate Stage 4.1.2 task: **ALLOWED**. Stage 4.1.2 was not started here.

ZIP CRC, manifest hashes, external `.sha256` files and reopen checks are recorded after package assembly.
