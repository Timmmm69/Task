# Design QA — Wave C Archive/Trash

- Date: 2026-07-30
- Viewport: 1280 × 720
- Browser: Codex in-app Browser
- Code state: `work/stage_5_prototype/src/App.jsx` and `src/styles.css` after Archive/Trash implementation
- Result: PASSED

## UX baseline comparison

The Archive/Trash surface was compared side-by-side with the existing Wave B Projects surface at the same viewport. It preserves the Direction 2 desktop shell, navigation, neutral background, compact toolbar, split list/inspector layout, typography, borders, radii, status language, and scroll behavior.

Evidence: `design-qa-wave-c-lifecycle-comparison.png`.

## Functional scenarios

| Scenario | Evidence checked | Result |
|---|---|---|
| Dedicated Archive/Trash navigation | Sidebar item opens the lifecycle surface and updates page metadata | PASS |
| Archive default | Read-only project inspector, lifecycle reason, history, restore action | PASS |
| Restore forbidden | `Archive.Restore` denial shown without optimistic state change | PASS |
| Permission-safe partial | Restricted archive object shows neutral redaction; title, owner, links, history and hidden counts are not exposed | PASS |
| Trash default | Cross-object list for tasks, projects and file metadata | PASS |
| Duplicate-name restore conflict | `DUPLICATE_RESOURCE` dialog requires a changed name and does not reveal the conflicting active object | PASS |
| Parent-unavailable restore conflict | Only allowed destinations are offered; hidden parent and path remain undisclosed | PASS |
| Legal hold | `RetentionBlocked` blocks purge, explains why, and provides a safe retry without false success | PASS |
| Typed purge confirmation | Purge action remains disabled until the exact object title is entered | PASS |
| File metadata purge | Success explicitly states that Task metadata was deleted and the physical file was not touched | PASS |
| Loading | Refresh shows an accessible busy/skeleton state | PASS |
| Empty/filter | No-results state and reset action work | PASS |
| Offline cache-only | Cache-only banner is shown; refresh, restore and purge are disabled | PASS |
| Console | No warnings or errors from the implementation | PASS |

## Build and diagnostics

- Serena diagnostics: 0 errors, 0 warnings.
- Vite production build: passed, 222 modules.
- Output CSS: `index-CYg8_2cr.css`, 65.69 kB.
- Output JS: `index-Ugx4YFUj.js`, 394.88 kB.
- Sites packaging preparation: passed; `dist/server/index.js` and `dist/.openai/hosting.json` emitted.
- Node test suite: 4/4 passed.

## Evidence files

- `qa-wave-c-lifecycle-archive.png`
- `qa-wave-c-lifecycle-trash-retention.png`
- `qa-wave-c-lifecycle-offline.png`
- `qa-wave-c-lifecycle-reference-projects.png`
- `design-qa-wave-c-lifecycle-comparison.png`
