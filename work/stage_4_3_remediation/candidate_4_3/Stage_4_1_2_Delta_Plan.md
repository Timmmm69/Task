# Stage 4.1.2 Delta Plan

**Version:** 4.1.2-candidate.1  
**Scope:** targeted update of Candidate 4.1.1; no MVP expansion.

| Delta ID | Source | Affected module | Existing FR/BR/AC | Required change | New IDs | Affected files | Verification |
| --- | --- | --- | --- | --- | --- | --- | --- |
| D-001 | Stage 2.3.1 contract diff | MOD-018 | FR-264 | Add GET/PUT/reset scale mappings and exact DTO/permission/concurrency behavior | FR-270–274;BR-098–102,104;AC-1790–1802,1819,1821,1824 | Product/Module/BR/AC/NFR/Trace | 3 operations + fields + permissions/errors |
| D-002 | Stage 2.3.1 search delta | MOD-014 | FR-159,160,260;BR-070 | Employee type/group, DTO-only fields, server filtering/redaction/blocked/cursor/deep link | FR-275–278;BR-105–112;AC-1804–1820 | Module/BR/AC/Trace | Search contract and Gherkin gates |
| D-003 | Stage 3.5 CMP-001 | MOD-015 | FR-261 | Current scale controls presentation of existing/future notifications; semantic urgency unchanged | FR-279;BR-103;AC-1803,1822 | Product/Module/AC/Trace | Projection and legacy-client tests |
| D-004 | Stage 3.5 SCR-133/134/135 | MOD-002 | FR-243,244 | Shell group accessibility and deep-link recheck | — | Module/Trace/NFR | Keyboard/focus/navigation tests |
| D-005 | Permissions/audit contract | MOD-019,MOD-021 | FR-265,269 | System.Configure ownership and notification_urgency_scale.changed | — | Module/Analytics/Trace | Known permission + audit event checks |
| D-006 | Stage 3.5 states | MOD-020 | FR-266 | No offline urgency writes; conflict/precondition recovery | — | Module/NFR/Trace | STATE-010/011/014/025 tests |
| D-007 | Accessibility/NFR delta | Cross-cutting | NFR-002,003,005,011,013,014,017,020,025 | Update measurable targets without adding SLA | — | NFR/Module/AC | Accessibility/security/privacy checks |
| D-008 | Stage 3.5 duplicate FLOW-035 | MOD-010,MOD-018 | Existing project FLOW-035 | Preserve historical ID and allocate FLOW-038 to new urgency flow | DEC-060;AC-1824 | Product/Module/Decision/Validation | Duplicate IDs=0; references resolved |
| D-009 | OQ closure evidence | MOD-014,MOD-018 | OQ-001,OQ-003 | Retain history and set Fixed after FR/BR/AC/trace gates | — | Open Questions/Validation/Readiness | All closure conditions PASS |

## Preserved areas

All 21 modules remain. MOD-001,003–013,016–017 keep their existing business requirements; only their shared baseline citations/NFR references are normalized where applicable.
