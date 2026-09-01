# E2E report

## Среда

- Windows desktop build: `net10.0-windows`, Release;
- PostgreSQL: 16, изолированный ephemeral cluster на loopback random port;
- API: production `Task.Api.dll`, HTTPS на loopback random port;
- schema: production migrator, expected/actual version 7;
- secrets/state: только `%LOCALAPPDATA%/TaskE2ERuntime`, вне Git и package.

## Фактические сценарии

| Сценарий | Результат |
|---|---|
| Реальный administrator login | PASS |
| Session capabilities содержат Calendar.Read/Create/Update | PASS |
| Создание двух пересекающихся CalendarEvent через HTTPS | PASS — HTTP 201/201 |
| Schedule range читает оба persisted event из PostgreSQL | PASS |
| Event details возвращает version и ETag | PASS |
| Conflict endpoint возвращает ожидаемую blocking pair | PASS |
| PATCH события с актуальным strong If-Match | PASS — HTTP 200 |
| WPF открывает текущую неделю после реального входа | PASS |
| WPF показывает оба event и текстовые conflict indicators | PASS |
| WPF inspector загружает server description/timezone/project | PASS |
| WPF create editor доступен при capability | PASS |
| Real PostgreSQL store round-trip/tenant/concurrency tests | PASS — 2/2, 0 skipped |

После проверки runtime очищается штатной фазой Cleanup с восстановлением сохранённого Desktop app-data.
