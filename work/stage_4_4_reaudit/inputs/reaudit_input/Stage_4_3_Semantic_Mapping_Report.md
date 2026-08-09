# Stage 4.3 Semantic Mapping Report

## Overall assessment

**Ready for catalog remediation with explicit caveats.** All three required maps are complete and referentially valid. No random or module-only FR assignment was used.

## Dataset and grain

| Map | Grain | Rows | Result |
| --- | --- | ---: | --- |
| BR → FR | one business rule | 113 | PASS |
| DATA-owned AC → FR | one DATA-owned acceptance criterion | 354 | PASS |
| Cross-cutting requirement → AC | one blank DATA/PERM/ERR/SYNC/AUDIT trace row | 87 | PASS |
| Stage 2.3.1 API → FR | one operationId | 244/244 | bijective PASS |

## Methodology

1. BR relations were mapped to existing FRs by rule semantics, module PRD wording, API behavior, and the current trace row. Existing normative relations in BR-070 and BR-098…113 were preserved.
2. DATA-owned ACs were mapped by the chain `schema.field → api_catalog request/response → method/path/operationId → FR trace`. Explicit operationId or exact OpenAPI path in the AC Source takes precedence; exact schema use is the fallback.
3. The 87 blank cross-cutting rows were linked only to existing ACs with matching category and module semantics:
   - DATA: response/validation behavior of the affected read-only or archive module;
   - PERM: module permission-denied ACs plus global server-enforcement rules;
   - ERR: existing negative ACs for validation, permission, session, conflict, outage, and idempotency;
   - SYNC: core sync endpoint ACs plus module-specific outage/conflict ACs;
   - AUDIT: global audit/history/redaction ACs plus local happy paths whose Stage 2.3.1 API effects explicitly declare audit.

## Validation results

- BR IDs mapped: **113/113**.
- DATA-owned AC IDs mapped: **354/354**.
- Blank cross-cutting requirements mapped: **87/87**.
- Unknown FR links: **0**.
- Unknown AC links: **0**.
- Cross-cutting mappings with empty AC: **0**.
- Stage 2.3.1 operations with zero or multiple trace FRs: **0**.
- DATA mapping strategies: `{"schema_request_response_exact_with_module_scope": 73, "source_explicit_operation_id": 264, "source_exact_openapi_path": 17}`.
- Confidence BR: `{"Medium": 24, "High": 89}`.
- Confidence DATA: `{"High": 352, "Medium": 2}`.
- Confidence cross-cutting: `{"High": 79, "Medium": 8}`.

## Caveats and cases requiring owner confirmation

1. **Global and cross-module BRs (24 Medium-confidence rows).** Their universal applicability cannot be exhaustively represented by one CSV cell without linking nearly every FR. The JSON records the smallest semantically sufficient implementing FR set and marks these rows Medium.
2. **Shared DATA schemas (2 rows).** When one exact schema is used by several operations, all valid operation-to-FR branches are retained. This is referentially correct; a test owner may narrow a criterion if the intended screen scope is smaller.
3. **PERM-002.** MOD-002 uses Anonymous/Authenticated access policies and has no generated `Permission denied` scenario. The fallback is evidence-based: session interruption plus hidden-control and deep-link non-disclosure ACs.
4. **AUDIT read/orchestration modules (7 Medium-confidence rows).** Stage 2.3.1 does not declare a local audit effect for: `AUDIT-002;AUDIT-003;AUDIT-014;AUDIT-015;AUDIT-016;AUDIT-017;AUDIT-020`. They are mapped to global audit/history/redaction verification. Architecture owner confirmation is recommended.
5. **BR-046 and BR-081.** The mapping is semantically supported but partly negative: trigger deduplication spans reminder creation/dismiss/snooze behavior; absence of generic UserAccount trash is evidenced by account deactivation plus generic trash/purge boundaries.

## Reproducibility

The full row-level evidence, method, confidence, exact API operations, and questionable-case inventory are in `semantic_maps.json`. Source artifacts were read from:

- `C:\Users\novik\Таск\work\stage_4_2_audit\stage_2_3_1\stage_2_3`;
- `C:\Users\novik\Таск\work\stage_4_3_remediation\candidate_4_3\Stage_4_Requirements_Traceability_4.1.2.csv`;
- `C:\Users\novik\Таск\work\stage_4_3_remediation\candidate_4_3\Stage_4_Module_PRDs_4.3.md`;
- `C:\Users\novik\Таск\work\stage_4_3_remediation\candidate_4_3\Stage_4_Acceptance_Criteria_Catalog_4.1.2.csv`;
- `C:\Users\novik\Таск\work\stage_4_3_remediation\candidate_4_3\Stage_4_Business_Rules_Catalog_4.1.2.csv`.
