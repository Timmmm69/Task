# Stage 4.2 — independent API / data / permissions / security audit draft

Scope: Stage 4.1.2 candidate against the unpacked Stage 2.3.1 technical contract (plus targeted Stage 3.5 checks). This is an independent recount; candidate PASS declarations were not used as evidence.

## Recounted metrics

- Normative OpenAPI operations: **244 total / 244 unique operationId** (direct count of `operationId` in `stage_2_3_1/stage_2_3/openapi/openapi.yaml`).
- Candidate operation references: **244 references / 244 unique operationId** in `Stage_4_Requirements_Traceability_4.1.2.csv`.
- Set comparison: **0 missing operations; 0 unknown operations; 0 duplicate operation references**.
- The traceability file has 497 rows: 279 FR, 113 BR, 21 DATA, 21 PERM, 21 ERR, 21 SYNC. Exactly 244 FR rows carry a concrete operationId; the remaining FR are explicitly desktop-only or cross-cutting.
- Normative permission catalog: **91 rows / 91 unique codes**.
- Normative stable-error catalog: **44 rows / 44 unique codes**.
- Candidate unknown permission codes found: **0**. `Anonymous`, `Authenticated` and relation/capability-policy prose are OpenAPI access policies, not additions to the 91-code capability catalog.
- Candidate unknown stable-error codes found: **0**.
- Normative DTO field catalog: **1,340 field rows**.
- Normative entity catalog: **66 entity rows**.

## Targeted contract checks

- Method/path/operationId coverage is complete as a set.
- Spot checks confirmed that candidate request/response DTO names, permission/access policy, ETag/If-Match, idempotency and stable-error references are copied from Stage 2.3.1 for:
  - `POST_api_v1_auth_admin_reset_password`;
  - `POST_api_v1_auth_change_password`;
  - `GET_api_v1_catalog_items_id_locations`;
  - `POST_api_v1_catalog_items_id_locations`;
  - `POST_api_v1_catalog_items_id_resolve_location`.
- Sensitive `FileLocation.rawPath` handling is not lost: candidate FR-103/FR-104/FR-108 preserve the ownership-or-`FileLocation.ReadSensitivePath` redaction rule from OpenAPI and `dto_field_catalog.csv`.
- Candidate cross-cutting rules preserve server-authoritative writes, no offline business-command queue, read-only cache during outage, optimistic conflict recovery, no silent last-write-wins, server-side filtering before pagination, and cache purge on authorization-scope change.

## Material findings

**No independently confirmed Critical/High/Medium defect in this scoped API/data/security pass.**

The apparently broad stable-error lists in individual FR rows were re-opened against OpenAPI and are present verbatim in Stage 2.3.1 `x-error-codes`; they are therefore not a Stage 4.1.2 divergence, even where the combinations appear semantically over-broad.

## Observation

The traceability CSV's `Permission` column is not a pure list of permission codes: it also contains access-policy literals (`Anonymous`, `Authenticated`), explanatory prose, and combined UI summaries. Automated “unknown permission” validation must parse catalog codes from these cells rather than compare whole-cell strings. This is a validation-format weakness, not evidence of a new permission or authorization defect.

## Evidence locations

- `work/stage_4_2_audit/stage_2_3_1/stage_2_3/openapi/openapi.yaml`
- `work/stage_4_2_audit/stage_2_3_1/stage_2_3/catalogs/api_catalog.csv`
- `work/stage_4_2_audit/stage_2_3_1/stage_2_3/catalogs/permissions.csv`
- `work/stage_4_2_audit/stage_2_3_1/stage_2_3/catalogs/errors.csv`
- `work/stage_4_2_audit/stage_2_3_1/stage_2_3/dto_field_catalog.csv`
- `work/stage_4_2_audit/stage_2_3_1/stage_2_3/catalogs/entities.csv`
- `work/stage_4_2_audit/candidate/Organizer_Stage4_PRD_Candidate_4.1.2/Stage_4_Requirements_Traceability_4.1.2.csv`
- `work/stage_4_2_audit/candidate/Organizer_Stage4_PRD_Candidate_4.1.2/Stage_4_Module_PRDs_4.1.2.md` (FR-103, FR-104, FR-108)
- `work/stage_4_2_audit/candidate/Organizer_Stage4_PRD_Candidate_4.1.2/Stage_4_Product_PRD_4.1.2.md` (BR-003, BR-009, BR-013; STATE-010..016, STATE-024..026; NFR-008..017)

## Audit limitation

This bounded pass established complete operation-ID set coverage and performed targeted semantic spot checks; it did not independently reconstruct every one of the 1,340 DTO field constraints or execute the Stage 2.3.1 migration SQL against PostgreSQL. Those unexecuted checks are not reported as PASS.
