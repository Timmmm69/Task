# Stage 4.1.1. Candidate Normalization Report

**Version:** 4.1.1-candidate.1  
**Date:** 2026-07-26  
**Input:** `Organizer_Stage4_PRD_Candidate.zip`  
**Output:** `Organizer_Stage4_PRD_Candidate_4.1.1.zip`  
**Scope:** normalization only; no new PRD, OpenAPI, DTO, permission, error or MVP function.

## 1. OQ disposition

| OQ | Requirement source | Decision | Changed files | Verification | Final status |
| --- | --- | --- | --- | --- | --- |
| OQ-001 | Concept §17.3, §23.2, §27.1 item 20: configurable thresholds/color intervals are explicit. | Confirmed as genuine gap; rejection condition not met. System-defined urgency remains supported; no DTO/API/settings invented. | Product PRD; Module PRDs; Open Questions; Decision Log; Validation; Manifest | Exact concept text and absence of writable fields in NotificationPreferences/UserSettings checked. | Open / High |
| OQ-002 | Stage 3.0 published STATE-001…024; Stage 3.4 retained STATE-007, STATE-014, STATE-025…031 and a full unnamed State Matrix. | Fixed documentation defect: restored prior IDs, retained existing IDs, added STATE-032…039 only for unique semantics. | Product PRD; Module PRDs; AC Catalog; Requirements Traceability; Open Questions; Decision Log | All STATE references resolve to registry; no duplicate IDs or name-only placeholders. | Closed / Fixed |
| OQ-003 | Concept §20.1 explicitly searches employees; §20.2 groups employee results. Search Contract types omit employee/user. | Confirmed as genuine gap; rejection condition not met. Administrative user list/filtering is not global search. No Search API change. | Product PRD; Module PRDs; Open Questions; Decision Log; Validation; Manifest | Concept and Search Contract compared; userIds confirmed as filter only. | Open / High |

## 2. State normalization

- Restored `STATE-001…024` from the Stage 3.0 registry.
- Retained `STATE-025…031` from Stage 3.4 without renumbering.
- Added `STATE-032…039` only where no prior state had equivalent semantics: expired session, revoked session/token family, revoked device, storage full, expired sync cursor, blocked account, temporarily locked account and invalid credentials.
- Mapped module errors, AC scenarios and requirement traceability to stable IDs.
- Business statuses and nonblocking warnings were not converted into technical states.

## 3. Source integrity decisions

`OQ-001` and `OQ-003` could not be rejected because the final concept contains direct requirements. Removing them would be a product change, not normalization. The candidate therefore remains structurally valid but does not pass the Stage 4.2 gate.

## 4. Updated artifacts

- `Stage_4_Product_PRD.md`
- `Stage_4_Module_PRDs.md`
- `Stage_4_Acceptance_Criteria_Catalog.csv`
- `Stage_4_Requirements_Traceability.csv`
- `Stage_4_Open_Questions.md`
- `Stage_4_Candidate_Validation.md` and `.json`
- `Stage_4_Decision_Log.md`
- `Stage_4_Dependency_Risk_Register.md`
- `00_MANIFEST.md`
- Added `Stage_4_0_PRD_Readiness.md`
- Added `Stage_4_1_1_Normalization_Report.md`

## 5. Audit gate

Critical must be 0 and High must be 0 before Stage 4.2. The normalized candidate has Critical 0 and High 2. Independent audit is therefore not authorized yet.
