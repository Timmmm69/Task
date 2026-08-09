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
