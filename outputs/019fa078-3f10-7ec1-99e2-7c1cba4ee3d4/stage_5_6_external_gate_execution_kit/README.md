# Task — Gate 5.6 External Evidence Execution Kit 0.2.0

**Date:** 2026-08-09

**Purpose:** make the remaining external readiness work reproducible without claiming that it has already happened.

## Execution order

1. Freeze the exact compiled Windows client build and record its SHA-256 in every result file.
2. Run the UIA/Inspect and Narrator protocol.
3. Run the Windows DPI/multi-monitor matrix at 100/125/150/175/200%.
4. Conduct moderated sessions with all four role lenses using the canonical 10 scenarios.
5. Resolve or formally disposition every new Critical/High/Medium finding.
6. Obtain Product owner, Design owner, Desktop tech lead and QA decisions.
7. Place signed/approved evidence under `evidence/incoming/`, update the evidence index, and run `node tools/validate-gate-evidence.mjs`.

## Honest status

The kit itself is validated, but Gate 5.6 is **NOT_READY** until every required evidence row is present, hash-addressed and accepted by its named owner. Templates are not evidence and blank signature fields are not approvals.
