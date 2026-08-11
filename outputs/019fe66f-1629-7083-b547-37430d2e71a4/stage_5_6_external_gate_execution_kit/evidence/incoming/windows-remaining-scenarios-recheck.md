# Native Windows remaining-scenarios recheck — partial, 2026-08-10

## Test identity and method

- Client actually launched: `bin/Task-Gate-5.6-Client-0.1.2-win-x64.exe`.
- SHA-256 verified with `Get-FileHash -Algorithm SHA256`: `7E0B7439975E8009A51A0DBB4865D5AD1DFCD9EFEA6B0C93A4141F57845DFA9F`.
- `Inspect.exe` x64 from Windows SDK `10.0.26100.0` was launched (`Inspect  (HWND: 0x000C0946) UIAccess`). Its automated window exposed only its title, so it did not yield an attachable property pane in this session. It is not claimed as the source of the observations below.
- Native Electron accessibility tree was captured from the running `Task — Вход` and `Task — Сегодня` windows. It is a UIA snapshot of the EXE, not browser-prototype evidence.
- The employee fixture was authenticated successfully with the synthetic account. The log-in field was corrected by pointer automation; it is not counted again as a remaining keyboard scenario.

## Reliable current UIA observations

| Context | Element name | UIA type | Availability | Focus observed | Result |
| --- | --- | --- | --- | --- | --- |
| First connection | `Логин` | Edit (`login`) | enabled, keyboard-focusable in prior Inspect record | Root document before interaction | Existing sign-in recheck remains valid. |
| First connection | `Пароль` | Edit (`password`) | enabled, keyboard-focusable in prior Inspect record | Root document before interaction | Existing sign-in recheck remains valid. |
| Today shell | `Создать задачу` | Button | exposed; no disabled state reported | outer Task window | Entry point exists in the EXE; workflow not executed. |
| Today shell | `Календарь` | Button | exposed; no disabled state reported | outer Task window | Entry point exists in the EXE; workflow not executed. |
| Today shell | `Открыть глобальный поиск` | Button | exposed; no disabled state reported | outer Task window | Entry point exists in the EXE; workflow not executed. |
| Today shell | `Переключить демонстрационное состояние подключения` | Button | exposed; no disabled state reported | outer Task window | Entry point exists in the EXE; workflow not executed. |
| Today task details | `Удалить пункт «Собрать данные из CRM»` | Button | exposed; no disabled state reported | outer Task window | Local destructive control was not invoked. |
| Today task details | `Удалить пункт «Сформировать сводную таблицу и диаграммы»` | Button | exposed; no disabled state reported | outer Task window | Local destructive control was not invoked. |

## Keyboard attempt

From the native `Task — Сегодня` window the following keys were injected after window activation:

1. `Ctrl+K` — expected to open global search.
2. `Tab` — attempted on the sign-in screen before the successful pointer-assisted fixture login.

After both attempts UIA still reported focus as the outer Task window / `RootWebArea`, and the `Today` tree remained visible; the expected search overlay was not exposed. The automation session therefore did not establish document focus for Electron after authentication. This is a limitation of this run's automation channel, **not** a confirmed application keyboard defect.

## Remaining scenario ledger

| Scenario | Keys actually sent | Inspect/UIA element seen | Outcome | Problem / status |
| --- | --- | --- | --- | --- |
| 1. Ordinary task create/edit, validation, save and focus return | None in the editor; `Tab` only before sign-in | `Создать задачу` — Button, exposed; outer window had focus | Editor was not opened. Required fields, validation, save and focus return were not observed. | **NOT VERIFIED**. No conclusion about availability or defect. |
| 2. Calendar event editor, date/time/participants, save/cancel, keyboard | None in the editor | `Календарь` — Button, exposed; outer window had focus | Calendar and event editor were not opened. | **NOT VERIFIED**. No conclusion about availability or defect. |
| 3. Search normal/unavailable result and keyboard | `Ctrl+K` | `Открыть глобальный поиск` — Button, exposed; outer window retained focus | Search overlay/page did not become observable after the keystroke; no result or protected-data boundary was inspected. | **BLOCKED BY AUTOMATION FOCUS**; not an app defect. |
| 4. Offline and reconnect | None | `Переключить демонстрационное состояние подключения` — Button, exposed; outer window had focus | Connection state was not switched. No write-block, message or recovery was observed. | **NOT VERIFIED**. |
| 5. Save conflict | None | No conflict dialog reached | No draft/conflict state was reached. | **NOT VERIFIED**. |
| 6. Roles Admin and Observer, including keyboard bypass | None | Employee shell was authenticated; `Профиль Тестовый сотрудник, роль Сотрудник` — Button, exposed | Separate Admin/Observer fixture processes were not driven in this run. | **NOT VERIFIED**. |
| 7. Delete, empty trash, restore, confirmation and cancel | None | Two checklist delete Buttons listed above, exposed; outer window had focus | No destructive control was invoked. The desktop-automation safety policy requires immediate confirmation before local deletion; none was requested because the other flows were already not controllable. | **NOT VERIFIED**; no false result. |
| 8. Archive and trash tabs, lists, filters, empty states, restore/delete | None | `Архив и корзина` — Button, exposed; outer window had focus | Workspace was not opened. | **NOT VERIFIED**. |

## Conclusion

No new product defect was established. The checked executable, synthetic fixture login and native UIA shell are real and recorded, but this session is insufficient to pass any of the eight remaining scenario groups. The Gate remains `NOT_READY`; no Narrator, voice control, DPI, multi-monitor, staff session, or approval activity was performed.
