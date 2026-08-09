# Design QA — Wave C Settings

- Date: 2026-07-30
- Viewport: 1280 × 720
- Browser: Codex in-app Browser
- Code state: `work/stage_5_prototype/src/App.jsx` and `src/styles.css` after Settings implementation
- Result: PASSED

## UX baseline comparison

The Settings surface was compared side-by-side with the existing Wave B Projects surface at the same viewport. It preserves Direction 2: the Windows desktop shell, navigation, typography, Fluent icons, neutral background, split navigation/content layout, compact controls, borders, radii, status language and internal scroll behavior.

Evidence: `design-qa-wave-c-settings-comparison.png`.

## Functional scenarios

| Scenario | Evidence checked | Result |
|---|---|---|
| Settings shell | Bottom navigation opens the Settings workspace and updates page metadata | PASS |
| Scope-labelled sections | Personal, this-device and organization scope are visible in navigation and panel header | PASS |
| Profile | Editable own fields and disabled server-managed role/department | PASS |
| Profile validation | Blank/whitespace display name shows `ValidationError` | PASS |
| Save conflict | `VERSION_CONFLICT` offers reload or reapply after server recheck | PASS |
| Forbidden | Missing `Settings.UpdateOwn` switches the section to read-only without optimistic save | PASS |
| Security | Wrong current password shows `INVALID_CREDENTIALS` | PASS |
| Session guard | Logout-all confirmation distinguishes current session and safer “other sessions” action | PASS |
| Notifications/DND | OS permission denial uses explicit Windows hand-off; invalid quiet hours show validation | PASS |
| Calendar | Work hours, default view, first day and reset to organization defaults | PASS |
| Device/startup | Autostart/tray preferences and Windows policy denial | PASS |
| Cache/sync | Sync progress, `SYNC_CURSOR_EXPIRED`, safe bootstrap and cache-clear boundary | PASS |
| Connection | Locked organization endpoint, TLS error, unsupported client and redacted diagnostics | PASS |
| Accessibility | Scale change produces restart-required status; strong focus and reduced motion controls | PASS |
| Own sessions/devices | Current-session guard, other-session revoke, `SESSION_REVOKED`, `DEVICE_REVOKED` and sign-in route | PASS |
| Loading | Refresh shows accessible busy/skeleton state | PASS |
| Offline | Cache-only read-only banner; refresh, save and destructive actions disabled | PASS |
| Console | No warnings or errors from the implementation | PASS |

## Build and diagnostics

- Serena diagnostics: 0 errors, 0 warnings.
- Vite production build: passed, 222 modules.
- Output CSS: `index-CSryKPAh.css`, 72.22 kB.
- Output JS: `index-BNs2LDAR.js`, 425.66 kB.
- Sites packaging preparation: passed; `dist/server/index.js` and `dist/.openai/hosting.json` emitted.
- Node test suite: 4/4 passed.

## Evidence files

- `qa-wave-c-settings.png`
- `qa-wave-c-settings-notifications.png`
- `qa-wave-c-settings-device-revoked.png`
- `qa-wave-c-settings-offline.png`
- `qa-wave-c-settings-reference-projects.png`
- `design-qa-wave-c-settings-comparison.png`
