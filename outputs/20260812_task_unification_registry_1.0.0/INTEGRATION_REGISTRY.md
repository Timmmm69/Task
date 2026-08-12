# Task integration registry

Version: 1.0.0
Generated: 2026-08-12
Canonical product baseline: `origin/main` at `aa9b56d77f27651d2efb8cba5d08a41389840cda`

## Decision register

| Source | Decision | Reason |
| --- | --- | --- |
| `main` / `origin/main` | Already in main | Current product baseline; local and remote refs matched before integration. |
| `codex/fix-electron-renderer-accessibility` | Already in main | Merged by GitHub PR #3. |
| `codex/gate-5-6-windows-client` | Already in main | Merged by GitHub PR #2. |
| `codex/native-client-inspect-recheck` | Already in main | Merged by GitHub PR #6. |
| `codex/gate-5-6-scenario-continuation` | Already in main | Merged by GitHub PR #7. |
| `codex/gate-5-6-technical-recheck` | Archive | Closed without merge; changes are Gate 5.6 reports, evidence and runtime-kit maintenance, not product behavior. |
| `codex/gate-5-6-uia-recheck` | Archive | Closed without merge; changes are an unaccepted UIA recheck report and Gate 5.6 metadata, not product behavior. |
| `codex/backup-pre-sync-20260812` | Retain locally | Recovery checkpoint for the original agenda package; never push or open a PR. |
| Today agenda checkpoint | Integrate | Only unsynced product behavior: compact agenda in the central Today surface, sorting model and tests. |
| Legacy copied agenda package | Do not accept as code | It predates current `main` and would overwrite already merged Quick Wins, accessibility and global UI changes. Its content is retained by the backup branch. |

## Resulting policy

- GitHub `main` is the only product branch after the agenda PR is merged.
- Product work uses short-lived branches from current `origin/main`; each PR carries one independently tested behavior change.
- `outputs/` contains only versioned final evidence packages. Gate 5.6 drafts remain referenced by quarantine records and their historical Git commits, not copied into product PRs.
- Existing remote branches and worktrees are retained until acceptance; cleanup is intentionally outside this integration.

## Evidence

- Baseline checks: production build passed; desktop tests passed; Sites packaging test passes when run after the build creates `dist/`.
- Agenda checks: 19 automated tests passed, production build passed, and desktop/narrow-width visual checks passed.
- Known non-blocking advisory: Vite reports one existing JavaScript chunk above 500 kB after minification.
