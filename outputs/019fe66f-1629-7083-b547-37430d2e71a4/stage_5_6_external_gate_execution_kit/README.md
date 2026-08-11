# Task — Gate 5.6 External Evidence Execution Kit 0.2.1

**Date:** 2026-08-10
**Purpose:** make the remaining external readiness work reproducible without claiming that it has already happened.

## Execution order

1. Freeze the exact compiled Windows client build and record its SHA-256 in every result file.
2. Run the native UIA/Inspect and keyboard protocol.
3. Conduct moderated sessions with all four role lenses using the canonical 10 scenarios.
4. Resolve or formally disposition every new Critical/High/Medium finding.
5. Obtain Product owner, Design owner, Desktop tech lead and QA decisions only where the Gate still formally requires them.
6. Place factual evidence under `evidence/incoming/`, update the evidence index, and run `node tools/validate-gate-evidence.mjs`.

## Explicitly out of scope

Narrator, Windows voice control, OS DPI scaling and multi-monitor testing are not Gate-evidence requirements for this execution kit. Do not add them back as a prerequisite or substitute them with browser evidence.

## Honest status

The kit itself is validated, but Gate 5.6 is **NOT_READY** until every required evidence row is present, hash-addressed and accepted by its named owner. Templates are not evidence and blank signature fields are not approvals.

The 2026-08-10 native EXE recheck is recorded in `evidence/incoming/windows-remaining-scenarios-recheck.md`. It confirms artifact identity, synthetic fixture login and selected UIA shell controls, but does not claim completion of any remaining scenario whose editor or state could not be driven by this Windows automation session.
