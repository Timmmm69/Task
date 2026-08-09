# Source Integrity Report

## Normalization

- Renamed `sources/concept/Task_Concept_Final.txt.txt` to the canonical `sources/concept/Task_Concept_Final.txt` without altering its bytes.
- The original Stage 2.2 ZIP was absent; the verified unpacked Stage 2.2 directory was archived as `Organizer_Stage2_Technical_Specification_2.2_Repacked.zip`.
- Stage 3.4 and Stage 4.1.1 SHA-256 values match their supplied control values and their ZIP entry streams passed CRC reading.
- No material was moved to quarantine. `archive/quarantine/.gitkeep` is the existing empty placeholder.

## Stage 2.2 independent check

The supplied contract gate passed: OpenAPI 3.1, 241 operations, 232 schemas, 1322 DTO fields, 91 permissions, 44 stable errors, 2741 resolved local references, and zero contract differences. Search has `contactIds`, `hasFiles`, typed `lifecycle`, server-side filtering, and cursor-safe pagination. Existing code-generation reports confirm successful desktop SDK and server-stub generation/build for the baseline.

## Scope and blockers

The source scan found no material from another product. Historic references to an unrelated repository occur only inside Stage 2.2 recovery/exclusion documentation; they are not source materials. No substantive blocker was found.

## Stage 2.3.1 and Stage 3.5 update

- Stage 2.3.1 final technical ZIP SHA-256 matches `75EFC3E83F09FBCC41AE7DA68A96F2EC0EBDFC74E61F62615F4DA3478AFE5019`.
- Stage 3.5 delta input ZIP SHA-256 matches `099E125F20FBB1F952B789D0BB5B8C276250576EC44A73F9B95350079C97E9C9`.
- Both ZIPs and their `.sha256`, validation, runtime and contract-diff evidence were copied unchanged to `sources/stage_2_3/`; Stage 2.2 was retained.
- Stage 3.5 field traceability contains 1078 rows (38 added), 25 columns, `unverified=0`, provisional=0.
- Final Stage 3.5 and Stage 4.1.2 delta-input ZIPs reopened successfully; every entry stream read to completion and matched the final package source bytes.
- Stage 3.5 ZIP SHA-256: `6C2447E935DD413488E482F7DB3C481C8DC6E53AEB57A07D1DF23D3ADA85381E`.
- Stage 4.1.2 delta-input ZIP SHA-256: `866F5DAC06ABA44B847F3C06D6AC8C326363B71DCB594F8E92C7A06A2E8AD21A`.
- No quarantine action was required.

## Stage 4.1.2 normalization and release

- Stage 3.5 ZIP, `.sha256`, package validation, internal manifest, contract delta, final validation and targeted UX audit were copied unchanged to `sources/stage_3_5/`.
- The Stage 2.3.1 internal manifest was normalized into `sources/stage_2_3/`; the final ZIP and supporting evidence remain byte-identical.
- Candidate 4.1.2 maps 244/244 OpenAPI operations, 279 FR, 113 BR, 1824 AC and 25 NFR; FR without AC, unknown permissions/errors, unverified/provisional and duplicate IDs are zero.
- The Stage 3.5 input duplicate `FLOW-035` was not changed in its source archive. Downstream PRD traceability preserves historical project `FLOW-035` and normalizes the new urgency flow as `FLOW-038`.
- `Organizer_Stage4_PRD_Candidate_4.1.2.zip` and `Organizer_Stage4_2_Audit_Input.zip` reopened successfully; every entry passed CRC/read-to-completion.
- Candidate ZIP SHA-256: `84260071D3917AE00AA617FDBF2E5AB540A719F7D717367B0504E36159845AF9`.
- Audit-input ZIP SHA-256: `4CC6DF2A7CF54F3E692971BDB2A39322615442748E95AD7104A1564229CD845F`.
