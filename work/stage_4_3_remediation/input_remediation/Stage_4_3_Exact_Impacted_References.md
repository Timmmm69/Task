# Stage 4.3 — Exact Impacted PRD References

**Input version:** 4.2-audit.1  
**Purpose:** finite edit map for remediation of Candidate 4.1.2.

| Audit ID | Exact impacted reference | Required edit |
|---|---|---|
| AUDIT-4.2-001 | `Stage_4_Product_PRD_4.1.2.md:186,229`; `Stage_4_Dependency_Risk_Register_4.1.2.md:63`; OQ register entries OQ-001/OQ-003 | Establish one current status and remove contradictory blocking/open language or retract Fixed. |
| AUDIT-4.2-002 | Appendix P.2, FR-159, FR-160, FR-243, FR-244, FR-260, FR-261, FR-265, FR-266, FR-269 and their linked AC | Replace legacy AC with criteria that test the effective addendum text. Preserve identifiers or provide a migration map. |
| AUDIT-4.2-003 | MOD-014 main contract near lines 4446 and 4508; addendum near lines 6814–6817 and 6862–6873 | Consolidate search types to include `employee`, set maxItems=10, and replace obsolete AC-070 language. |
| AUDIT-4.2-004 | 211 AC identified in `Stage_4_2_FR_BR_AC_Audit.csv` | Supply explicit Given/When/Then precondition, action and observable result. |
| AUDIT-4.2-005 | 466 AC rows with blank `Direct FR` | Add direct FR linkage or classify as non-FR verification with an explicit parent rule. |
| AUDIT-4.2-006 | DATA-002, DATA-003, DATA-016 and all PERM/ERR/SYNC/AUDIT requirements flagged in traceability audit | Add verification AC or an approved verification-method reference. |
| AUDIT-4.2-007 | 96 BR rows with blank direct FR | Add Related FR mappings or document an approved BR-only hierarchy. |
| AUDIT-4.2-008 | All references to `Stage_3_Field_Traceability.csv` | Replace with `Stage_3_Field_Traceability_Final_3.5.csv`; re-run reference resolution. |
| AUDIT-4.2-009 | Requirement-trace occurrences of FLOW-038; Stage 3.5 flow catalogue | Define FLOW-038 or replace every occurrence with the intended existing FLOW identifier. |
| AUDIT-4.2-010 | Active trace source fields naming Stage 3.4 or Stage 2.2 | Repoint current normative mappings to Stage 3.5 / Stage 2.3.1, retaining historical provenance separately. |
| AUDIT-4.2-011 | NFR-001, NFR-003, NFR-006, NFR-007, NFR-015, NFR-024; OQ-008 | Add measurable thresholds, measurement method, environment and acceptance boundary; resolve provisional status. |
| AUDIT-4.2-012 | RISK-001–RISK-025, especially RISK-022–RISK-025 | Add probability, owner, trigger, impact and testable mitigation/contingency. |
| AUDIT-4.2-013 | CMP-001; combobox/listbox behavior; Esc focus return; adaptive behavior below 1100 logical px | Add atomic keyboard, focus, active-descendant, tab-order and narrow-window requirements. |
| AUDIT-4.2-014 | Product PRD line 191; Readiness report lines 107 and 127 | Replace stale 241-operation claims with independently verified 244. |
| AUDIT-4.2-015 | AC-1486, AC-1487, AC-1501, AC-1579, AC-1709, AC-1710, AC-1715, AC-1716, AC-1767 | Replace “корректно” with an observable, testable result. |
| AUDIT-4.2-016 | OQ-010 analytics-retention decision | Define retention duration, deletion/anonymization behavior, owner and approval evidence. |
