# Validation report

Version: 1.0.0  
Validated: 2026-08-12

## Result

PASS. The registry reflects the fetched GitHub state and the checked local worktrees.

## Checks

- `main` and `origin/main` both resolved to `aa9b56d77f27651d2efb8cba5d08a41389840cda` before agenda integration.
- No open GitHub pull requests existed at snapshot time.
- Closed PR #4 and #5 were not merged and contain only Gate 5.6 evidence/runtime changes.
- The backup branch `codex/backup-pre-sync-20260812` resolves to `61375c2835119ea0bda050c8aaab9710197bb036`.
- Its bundle passed `git bundle verify`; SHA-256: `BF655172C0074E9224FB968BD6BC4D871953CE2F2337744A1A7422A58E13E47F`.
