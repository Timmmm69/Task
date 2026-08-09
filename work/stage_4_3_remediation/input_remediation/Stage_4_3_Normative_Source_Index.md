# Stage 4.3 — Normative Source Index

The remediation must preserve the project's precedence order. No source artifact was modified during Stage 4.2.

| Priority | Normative input | Stage 4.2 use |
|---:|---|---|
| 1 | `sources/concept/Task_Concept_Final.txt` | Business intent and scope guardrail |
| 2 | `sources/stage_1/architecture_organizer.md` | Stage 1 architecture baseline |
| 3 | Stage 2.3.1 contract embedded in Audit Input | Effective technical contract; verified ZIP SHA-256 `75EFC3E83F09FBCC41AE7DA68A96F2EC0EBDFC74E61F62615F4DA3478AFE5019` |
| 4 | Stage 3.5 baseline embedded in Audit Input | Effective UX baseline; verified ZIP SHA-256 `6C2447E935DD413488E482F7DB3C481C8DC6E53AEB57A07D1DF23D3ADA85381E` |
| 5 | Candidate 4.1.2 | Artifact under audit; SHA-256 `84260071D3917AE00AA617FDBF2E5AB540A719F7D717367B0504E36159845AF9` |

## Stage 4.3 evidence rules

- Treat Stage 2.2 and Stage 3.4 references as historical provenance, not current normative targets.
- Resolve conflicts by the project precedence order and record each affected identifier.
- Do not silently change business requirements.
- Recalculate counts from the remediated artifacts rather than carrying forward declared totals.
