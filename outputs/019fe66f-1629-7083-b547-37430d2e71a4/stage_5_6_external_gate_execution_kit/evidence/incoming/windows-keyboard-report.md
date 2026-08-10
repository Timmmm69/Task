# Native Windows keyboard report — partial, 2026-08-09

Client: `Task-Gate-5.6-Client-0.1.2-win-x64.exe`, SHA-256 `7E0B7439975E8009A51A0DBB4865D5AD1DFCD9EFEA6B0C93A4141F57845DFA9F`.

| Path | Keys | Observed focus/result | Status |
| --- | --- | --- | --- |
| First connection | Start EXE; `Tab` | Focus moved to `Пароль` Edit (`password`, enabled, keyboard-focusable); preceding `Логин` Edit is exposed and keyboard-focusable. | PASS |
| Sign-in | Synthetic `gate.employee` credentials entered via keyboard automation | Client loaded `Task — Сегодня`. | PASS, fixture only |
| Shell → Inbox | From document, `Tab` ×6; `Tab` ×2; `Enter` | `Сегодня` then `Входящие`; Inbox opened. | PASS |
| Inbox | `Tab` and Inspect review | `Быстро добавить во входящие` was exposed as an enabled Edit. | PASS sample |
| Inbox → Settings | `Tab` ×8; `Enter` | Settings opened; `Сохранить профиль` was exposed as an enabled Button. | PASS sample |

All remaining requested keyboard paths are PENDING. Narrator, voice control, DPI scaling and multi-monitor were not tested by explicit scope decision.

2026-08-10 continuation: `Ctrl+K` was injected into the signed-in native Electron window after activation. UIA focus remained on the outer window / RootWebArea and the expected Search overlay was not exposed. This records an automation-focus limitation, not an application keyboard failure; no remaining keyboard path is marked PASS. See `windows-remaining-scenarios-recheck.md`.
