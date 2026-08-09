# Stage 4.1.2 Update Report

**Version:** 4.1.2-candidate.1  
**Type:** targeted candidate update; not Stage 4.2 independent audit.

## Outcome

- Normative technical contract: Stage 2.3.1.
- Normative UX baseline: Stage 3.5.
- Historical baselines retained: Stage 2.2 and Stage 3.4.
- Modules preserved: 21; changed: MOD-002, MOD-014, MOD-015, MOD-018, MOD-019, MOD-020, MOD-021.
- New FR: 10 (`FR-270…FR-279`).
- Changed FR: 10 (FR-159, FR-160, FR-243, FR-244, FR-260, FR-261, FR-264, FR-265, FR-266, FR-269).
- New BR: 16 (`BR-098…BR-113`); BR-070 retained deprecated and replaced by BR-105.
- New AC: 35 (`AC-1790…AC-1824`), all with Gherkin.
- NFR: 25 total; 9 existing NFR updated, no arbitrary SLA.
- Analytics: 10 new allowlisted events (`AN-043…AN-052`).

## Contract/UX alignment

- 244/244 API operations map to FR; new operations map to FR-270/271/272.
- Exact urgency and employee fields are copied from Stage 3.5 field traceability; avatar/HEX/personal override are absent.
- Permissions remain 91; stable errors remain 44; no new codes are created.
- `FLOW-035` input collision is resolved downstream by preserving project FLOW-035 and assigning urgency management FLOW-038.

## Gate

FR without AC=0; unverified=0; provisional=0; unknown permissions/errors/UX IDs=0; duplicate IDs=0; lost references=0; client post-filter=0. OQ-001/OQ-003=Fixed. Internal candidate validation Critical/High/Medium=0/0/0. Candidate is ready for Stage 4.2.
