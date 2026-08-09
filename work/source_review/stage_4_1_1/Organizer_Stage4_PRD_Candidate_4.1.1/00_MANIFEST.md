# 00_MANIFEST

**Package:** Organizer Stage 4 PRD Candidate 4.1.1  
**Version:** 4.1.1-candidate.1  
**Formed:** 2026-07-26  
**Status:** Candidate, not Final  
**Normalization:** Based on `Organizer_Stage4_PRD_Candidate.zip`; not a new PRD.

## Files

| File | Status | Version | Purpose | Size bytes | SHA-256 |
| --- | --- | --- | --- | --- | --- |
| Stage_4_0_PRD_Readiness.md | Candidate | 4.1.1-candidate.1 | Reconstructed Stage 4.0 source/readiness and decomposition baseline | 10101 | 69b6d449c593eb820a61b07af68ac1e3a869b6aa9bcb76df4d4370fb1ead0bef |
| Stage_4_1_1_Normalization_Report.md | Candidate | 4.1.1-candidate.1 | OQ disposition, state normalization and audit-gate report | 3217 | 569ba5b00abd3cdcf6740362255ff91aad0021b02c5448ef9b49c237bc7f3aab |
| Stage_4_Acceptance_Criteria_Catalog.csv | Candidate | 4.1.1-candidate.1 | Acceptance catalog with stable STATE coverage | 1171837 | 990779c985ea0ad0c21a3d5a81e453474f663d4c884186b19a94f449ff77a054 |
| Stage_4_Analytics_Audit_Requirements.md | Candidate | 4.1.1-candidate.1 | Analytics and audit requirements | 22524 | 5ff88bdfdcf5860d599fe8b99212c818286e68b37ccdc834a27fe3e1b2cccee5 |
| Stage_4_Business_Rules_Catalog.csv | Candidate | 4.1.1-candidate.1 | Business rules catalog | 14908 | beeb145ec95672a0b1241c0da5411c29eba5f22262de41718bc7ab1889fe0e29 |
| Stage_4_Candidate_Validation.json | Candidate | 4.1.1-candidate.1 | Machine-readable validation | 805 | 63ded418d9c479541ac13b91466271887ed87816c1831f09b414e42ef0af6cd4 |
| Stage_4_Candidate_Validation.md | Candidate | 4.1.1-candidate.1 | Human-readable validation and audit gate | 829 | 0cf1973f5098bec8f42f93166bb2e4e2447db225e4eb4f754de91fa6a78e3451 |
| Stage_4_Decision_Log.md | Candidate | 4.1.1-candidate.1 | Stage 4 decisions including 4.1.1 normalization | 2566 | c1bd8ed0da91eab8f72fc389634f0c8f81a9bac41bcce3db5257f56b5fcd9eec |
| Stage_4_Dependency_Risk_Register.md | Candidate | 4.1.1-candidate.1 | Dependencies and risks | 9959 | e841699dcff3974673e7501395c7eeeb59e03874719b096986f3dc87b03c62e0 |
| Stage_4_Module_PRDs.md | Candidate | 4.1.1-candidate.1 | Normalized PRDs for 21 modules | 1300722 | af338931a13abd6f54fa5a805da17f674dda82773cc966748b58b6c83fc87ac1 |
| Stage_4_NFR_Catalog.csv | Candidate | 4.1.1-candidate.1 | NFR catalog | 4864 | 64a0483490f89e5c4b06071d5f47579b58d238ed3337e4c805e01d13749b4315 |
| Stage_4_Open_Questions.md | Candidate | 4.1.1-candidate.1 | Active and resolved source-backed questions | 4070 | dcbcfc8de2b7c4642de2c51ee3d86951ff538eb6e369c08ee473933505618397 |
| Stage_4_Product_PRD.md | Candidate | 4.1.1-candidate.1 | Normalized product-level PRD | 32257 | 2f5834eeddbead5f9c99cb5fb88e4274ce5eeb5b868631eb312c825ca886cb8d |
| Stage_4_Requirements_Traceability.csv | Candidate | 4.1.1-candidate.1 | Requirement traceability with stable STATE IDs | 273498 | b7f5cf632c9c8dd2869dddf66c8128993feb9f19342d607d991daa0e259a7f75 |

## Canonical sources

1. Final concept: `architecture_organizer.md`.
2. Stage 1 architecture: `01_core_domain_and_data.md`.
3. Stage 2.2 contract baseline: OpenAPI 1.2.0-stage2.2, permissions, errors, DTO and Search Contract in Stage 3.4 baseline.
4. Stage 3.4: `Organizer_Stage3_Final_Baseline_3.4.zip`, including 128 SCR, 37 FLOW and field/state traceability.
5. Reconstructed readiness: `Stage_4_0_PRD_Readiness.md`.

## Package metrics and gate

- Modules: 21
- FR: 269 (241 API-backed; 28 desktop-only)
- AC: 1789
- OpenAPI coverage: 241/241
- Structural validation: PASS
- Critical: 0
- High: 2 (`OQ-001`, `OQ-003`)
- Medium: 5
- Readiness: 90/100
- Stage 4.2 gate: CLOSED

## Normalization decisions

- `OQ-002` fixed by restoring/preserving stable STATE identifiers and adding only unique semantics.
- `OQ-001` cannot be rejected because configurable thresholds are explicit in concept §17.3, §23.2 and §27.1.20.
- `OQ-003` cannot be rejected because employee search/results are explicit in concept §20.1–20.2.
- OpenAPI, DTO, permissions and stable errors were not changed.

Manifest intentionally excludes its own recursive hash.
