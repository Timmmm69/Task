# Stage 3.4. Fix Registry

## 1. Closure registry

| ID | Previous severity | Defect | Fix | Evidence | Status |
|---|---|---|---|---|---|
| AUD-001 | High | No machine-readable OpenAPI; forms could not be field-verified | Loaded validated OpenAPI 3.1, generated field traceability, updated forms/states/flows/roles/API map | `1040` trace rows; OpenAPI/codegen PASS | Closed |
| GAP-001 | High | Required/nullable/enum/limits/PATCH unresolved | Exact DTO fields and concurrency semantics recorded | `Stage_3_Field_Traceability.csv` | Closed |
| GAP-002 | Medium | Search and relation controls were contract-dependent | Search filters restored: contactIds, hasFiles, lifecycle, types; ObjectLink DTO mapped | Search contract + trace rows | Closed |
| FIX-3.4-001 | — | Task fields mixed core and relation semantics | Split Task DTO, assignees/watchers, recurrence, reminders, checklist and links | Contract Delta §§3, 6, 7 | Fixed |
| FIX-3.4-002 | — | PATCH could erase omitted values | Dirty-field serialization and explicit-null rule made normative | DEC-037; STATE-028/029 | Fixed |
| FIX-3.4-003 | — | Version errors not fully distinguished | 412/428/409 handling fixed in flows/states/trace | DEC-038; If-Match rows | Fixed |
| FIX-3.4-004 | — | Search UI lacked recovered filters | SCR-133–135 and FLOW-019 updated | Search audit PASS | Fixed |
| FIX-3.4-005 | — | Settings had provisional controls | Unsupported avatar/threshold/channel controls removed | DEC-042 | Fixed/observed |

## 2. Remaining observations

| ID | Severity | Observation |
| --- | --- | --- |
| OBS-3.4-01 | Medium | Profile avatar is required by the broad concept but no request/response DTO field or operation exists. The active editor control is removed; contract change is required before implementation. |
| OBS-3.4-02 | Medium | Configurable urgency color thresholds are not present in NotificationPreferences or UserSettings. No invented control remains; a future contract extension is required if this concept option is kept. |

## Stage 3.5 fixes

| Fix ID | Related OQ/GAP | Severity before | Change | Verification | Status |
|---|---|---:|---|---|---|
| FIX-3.5-001 | OQ-001 / OBS-3.4-02 / AUD-014 | Medium | Organization four-interval editor in SCR-153 with exact DTO, defaults, validation, permission, ETag/If-Match, audit and backward behavior | OpenAPI + field rows + accessibility/conflict audit | Fixed |
| FIX-3.5-002 | OQ-003 / employee search gap | Medium | Employee type/group in SCR-133/134/135 and FLOW-019; separated from admin users, contacts and userIds | DTO, filtering, blocked/redaction, cursor, deep link audit | Fixed |
| FIX-3.5-003 | DEC-042 historical text | Low | Urgency-only portion superseded; unsupported-control rule retained | DEC-044–046 | Fixed |

- OQ-001: `Fixed`.
- OQ-003: `Fixed`.
- `unverified`: 0; provisional controls: 0; unknown permissions/errors: 0.
- Open Critical / High / Medium: 0 / 0 / 0.

## 3. Gate

Critical = 0; High = 0. Remaining Medium observations are explicit and do not create unimplementable controls. Contract-dependent Stage 3 work is closed.
