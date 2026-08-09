# Design QA — Wave C Admin

- Date: 2026-07-30
- Viewport: 1280 × 720
- Browser: Codex in-app Browser
- Code state: `work/stage_5_prototype/src/App.jsx` and `src/styles.css` after Admin implementation and visual fixes
- Result: PASSED

## UX baseline comparison

The Admin surface was compared side-by-side with the existing Wave B Projects surface at the same viewport. It preserves Direction 2 and the established Windows desktop shell, navigation, typography, Fluent iconography, neutral surfaces, compact controls, borders, radii, status language, split list/inspector model and contained scrolling.

The review also corrected three visible issues before acceptance: the long Admin navigation label, document-level scrolling caused by the extra navigation item, and spacing in the dangerous-permission warning. The final resource list uses a concise human-readable status while the inspector retains the canonical error code.

Evidence: `design-qa-wave-c-admin-comparison.png`.

## Functional scenarios

| Scenario | Evidence checked | Result |
|---|---|---|
| Admin shell | Main navigation opens Admin and updates page metadata | PASS |
| Capability filtering | Limited role shows only permitted sections and does not disclose hidden section counts | PASS |
| User lifecycle | Create, block and deactivate controls respect online and capability state | PASS |
| User validation | Required fields and duplicate login surface validation and `DUPLICATE_RESOURCE` | PASS |
| Self-lockout guard | Current administrator cannot block or deactivate their own account | PASS |
| Last-admin guard | Last system administrator cannot be deactivated (`INVALID_STATE_TRANSITION`) | PASS |
| User conflict | `VERSION_CONFLICT` cancels the local action and offers refresh | PASS |
| Restricted user | Protected fields and identifiers remain redacted outside the authorized scope | PASS |
| Departments | Hierarchy, parent, manager, create and lifecycle controls are present | PASS |
| Department cycle | `DEPENDENCY_CYCLE` prevents an invalid parent relationship | PASS |
| Active children | Archive is blocked while active child nodes exist | PASS |
| Hidden parent | Restricted hierarchy does not reveal parent, manager, members or child count | PASS |
| System role | Immutable system role exposes permissions read-only | PASS |
| Dangerous permission | `Backup.Restore` requires explicit warning and preserves object scope boundaries | PASS |
| Effective access | Allow and Deny results explain scope without naming a hidden object | PASS |
| Sessions/devices | Current session is protected; other session revoke yields `SESSION_REVOKED` | PASS |
| Session states | Active, stale-heartbeat and suspicious states are filterable | PASS |
| Network resources | Available/unavailable resources, enable/disable and probe are represented | PASS |
| Unsafe path | Non-UNC resource path is rejected with `UNSAFE_PATH` | PASS |
| Resource conflict | Concurrent resource update surfaces `VERSION_CONFLICT` | PASS |
| Loading | Refresh exposes an accessible busy/skeleton state | PASS |
| Offline | Admin switches to cache-only read-only and disables mutations/probe | PASS |
| Console | Fresh final-code tab has no warnings or errors | PASS |

## Build and diagnostics

- Serena diagnostics for `App.jsx`: 0 errors, 0 warnings.
- CSS syntax and bundling: validated by the production build; Serena's current language service routes `.css` through TypeScript and is not used as CSS evidence.
- Vite production build: passed, 222 modules.
- Output CSS: `index-CnoiaoPz.css`, 81.17 kB.
- Output JS: `index-CJhup5EL.js`, 462.84 kB.
- Sites packaging preparation: passed; `dist/server/index.js` and `dist/.openai/hosting.json` emitted.
- Node test suite: 4/4 passed.

## Evidence files

- `qa-wave-c-admin-users.png`
- `qa-wave-c-admin-limited.png`
- `qa-wave-c-admin-roles.png`
- `qa-wave-c-admin-resources-offline.png`
- `qa-wave-c-admin-reference-projects.png`
- `design-qa-wave-c-admin-comparison.png`
