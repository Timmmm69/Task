# Native Windows Inspect/UIA report — partial, 2026-08-09

Tool: Microsoft Windows SDK `10.0.26100.7705`, x64 `Inspect.exe` version `7.2.0.0`; installed with WinGet and elevated official `winsdksetup.exe /quiet /norestart`.

Client: `Task-Gate-5.6-Client-0.1.2-win-x64.exe`, SHA-256 `7E0B7439975E8009A51A0DBB4865D5AD1DFCD9EFEA6B0C93A4141F57845DFA9F`.

| Checkpoint | Inspect/UIA observation | Result |
| --- | --- | --- |
| First connection | `Первое подключение` Text (`sign-in-title`); `Логин` Edit (`login`, keyboard-focusable/enabled); `Пароль` Edit (`password`, keyboard-focusable/enabled); `Войти` Button (enabled). | PASS |
| Auth defect recheck | Baseline 0.1.1 exposed no Login or Password because it started authenticated. 0.1.2 exposes both fields. | FIXED |
| Shell/navigation sample | Inspect saw the Electron Document and named keyboard-focusable buttons: `Сегодня`, `Календарь`, `Входящие`, `Мои задачи`, `Поиск`, `Архив и корзина`, `Создать задачу`, `Настройки`. | PASS sample |
| Inbox sample | `Быстро добавить во входящие` is an enabled keyboard-focusable Edit. | PASS sample |

Task editor, CalendarEvent editor, safe unavailable search, offline/reconnect, conflict draft, Admin/Observer, dangerous actions, Archive/Trash and all tab/list states remain untested here. Narrator, voice control, DPI scaling and multi-monitor are out of scope.

2026-08-10 continuation: Inspect.exe x64 was launched, but its automated surface did not expose a property pane to this session. A separate native-EXE UIA snapshot confirmed the signed-in Employee shell and selected named Buttons; it does not promote any remaining checkpoint to PASS. See `windows-remaining-scenarios-recheck.md`.
