
# Stage 4.2 — Independent Validation

**Version:** 4.2-audit.1  
**Assessment:** Needs revision / FAIL

## Input validation

| Check | Result |
|---|---|
| Audit Input SHA-256 | PASS — 4CC6DF2A7CF54F3E692971BDB2A39322615442748E95AD7104A1564229CD845F |
| Candidate SHA-256 | PASS — 84260071D3917AE00AA617FDBF2E5AB540A719F7D717367B0504E36159845AF9 |
| CRC/read-to-completion/reopen | PASS |
| Unsafe paths / empty files / temp files | 0 / 0 / 0 |
| Audit Input manifest | 23/23 size+hash PASS |
| Candidate manifest | 14/14 size+hash PASS |
| Stage 2.3.1 SHA/CRC/reopen | PASS |
| Stage 3.5 SHA/CRC/reopen | PASS |

## Calculation spot checks

| Claim | Result |
|---|---|
| 21 modules | Verified |
| 279 FR / 113 BR / 1824 AC / 25 NFR | Verified unique IDs |
| 244 OpenAPI operations | Verified by independent operationId parse |
| 244/244 mapped | Verified at operationId/FR/AC level; exhaustive DTO-field and migration execution not certified |
| FR blank AC=0 | Verified |
| Semantic updated-FR coverage | Discrepancy — 9/10 updated FR retain legacy AC |
| AC complete Gherkin | Discrepancy — 211 blank |
| Lost references=0 | Discrepancy — one missing target, 1565 occurrences |
| Unverified/provisional=0 | Discrepancy — minimum 1/1 |
| OQ-001/OQ-003 Fixed | Discrepancy — Conflicted |

## Validation stance

The numeric inventory is mostly accurate, but the candidate's conclusions are not ready to share as an approval baseline. Findings are evidence-backed and prioritized by implementation risk. Final ZIP CRC/reopen and external SHA-256 are validated after deterministic package assembly; hashes are recorded in adjacent `.sha256` files.
