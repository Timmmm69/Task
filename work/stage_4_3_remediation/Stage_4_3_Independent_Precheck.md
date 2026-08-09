# Stage 4.3 Independent Precheck

**Nature of check:** internal cross-artifact precheck prepared for Stage 4.4. It is not the repeat independent audit and does not declare a final baseline.

## 1. Governance/quality scope

| Audit ID | Root-cause repair in this scope | Verification evidence | Precheck status |
| --- | --- | --- | --- |
| AUDIT-4.2-009 | Full downstream `FLOW-038` errata; `FLOW-035` reserved for project completion/archive | `Stage_4_3_Reference_Repair_Report.md`; unique FLOW mapping gate | Fixed in owned artifacts; candidate-wide scan required |
| AUDIT-4.2-010 | Stage 2.3.1/3.5 declared current; Stage 2.2/3.4 restricted to explicit historical provenance | Readiness §1/§11; validation §1; source-string scan | Fixed in owned artifacts; CSV/other-artifact scan required |
| AUDIT-4.2-011 | Owned validation no longer claims zero provisional/unverified without a final NFR scan | Candidate Validation §§2/4 | Dependent on final NFR catalog owner and machine recount |
| AUDIT-4.2-012 | Risk schema expanded to probability, impact, owner, trigger, prevention, contingency, verification and status for RISK-001…025 | Risk register lint: 25/25 non-empty fields | Fixed in owned artifact |
| AUDIT-4.2-013 | Atomic accessibility gate enumerated for CMP-001/CMP-002, shell resizing and 200% scaling | Candidate Validation §3; Stage 3.5 exact normative refs | Dependent on AC/module-owner transfer and final AC scan |
| AUDIT-4.2-014 | All active readiness/validation gates use 244/244 | scan for the superseded operation total | Fixed in owned artifacts; Product PRD scan required |
| AUDIT-4.2-016 | OQ-010 model fixed: no separate analytics store; storage/access boundary, company-approved 30–90-day application-log retention, rotation/deletion and 1095-day audit retention | Analytics §5/§8; deployment config and retention tests | Fixed in owned artifact; Open Questions alignment required |

## 2. Normative evidence

- Stage 2.3.1 `openapi/openapi.yaml`: 244 unique operationId.
- Stage 2.3.1 `catalogs/permissions.csv`: 91 permission codes.
- Stage 2.3.1 `catalogs/errors.csv`: 44 stable error codes.
- Stage 2.3.1 `db/004_stage_2_1_foundation.sql` lines 1307–1327: audit/history 1095-day retention, monthly partitioning, batch 10 000, legal hold.
- Stage 3.5 `Stage_3_Field_Traceability_Final_3.5.csv`: 1078 rows.
- Stage 3.5 `Stage_3_User_Flows_Final_3.5.md`: duplicated FLOW-035 evidence and full urgency/project definitions.
- Stage 3.5 `Stage_3_UX_Architecture_Final_3.5.md` §§5.2, 6, 25.2, 26 and CMP-001/CMP-002 accessibility section.

## 3. Candidate-wide commands for the package owner

1. Scan all active source cells/paragraphs: unqualified Stage 2.2/3.4 = 0.
2. Scan for the superseded operation total in active gates → zero; all current gates read 244/244.
3. Resolve filenames: legacy field-traceability shorthand occurrences = 0; canonical final filename resolves.
4. Resolve flow registry: project-only FLOW-035; urgency-only FLOW-038; unknown FLOW = 0.
5. Risk lint: exactly 25 risk rows; required governance fields non-empty.
6. AC scan: atomic CMP-001/CMP-002 keyboard/focus/UIA and below-1100/200% resize cases present.
7. NFR scan: every row has source, measurable condition, method, scope and infrastructure dependency; unverified/provisional ledgers reflect facts.
8. Analytics tests: allowlist, server-only boundary, access separation, rotation and deletion.

## 4. Residual gates

- Candidate-wide CSV and Product/Module PRD changes are owned by other remediation workstreams and must be synchronized before final validation.
- OQ-010 must be moved to resolved history by the Open Questions owner using the same policy.
- OQ-001/OQ-003 and MOD-014 status are outside this owned scope.
- Runtime, PostgreSQL and accessibility execution evidence remains a Stage 4.4/implementation concern where specified.

No statement in this precheck authorizes visual design, Stage 5, or final-baseline designation.
