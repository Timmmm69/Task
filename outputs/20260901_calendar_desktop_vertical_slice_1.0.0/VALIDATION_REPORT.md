# Validation report — Calendar Desktop vertical slice 1.0.0

Дата проверки: 2026-09-01

## Итог

PASS для недельного read/write vertical slice. Production Desktop build компилируется, календарные client/ViewModel tests проходят без skip, server Calendar endpoints проходят, а PostgreSQL persistence и production HTTPS API проверены на изолированном PostgreSQL 16.

## Автоматизированные проверки

| Проверка | Результат |
|---|---|
| Calendar Desktop tests | PASS — 12/12, 0 skipped |
| Full Desktop tests | PASS — 232/232, 0 skipped |
| Calendar service-host tests | PASS — 33/33, 0 skipped |
| Real PostgreSQL Calendar stores | PASS — 2/2, 0 skipped |
| Full solution gate | PASS — 1290/1290, 0 skipped (Task.Tests 755; ServiceHosts 303; Desktop 232) |
| Release build | PASS — 0 errors, 0 warnings |
| `verification/Test-DesktopShell.ps1` | PASS |
| `dotnet format Task.sln --no-restore` | Изменённый scope отформатирован; известный unrelated xUnit1031 не имеет auto-fix |
| `git diff --check` | PASS |

## Исправленный regression defect

Миграция CalendarEvent permissions повысила migration catalog до version 7, а реальный PostgreSQL test task writer продолжал hard-code version 6. Assertion заменён на `TaskPersistenceMigrationCatalog.LatestVersion`, после чего реальный тест прошёл.

## Security/correctness

- organization не принимается от WPF и определяется серверной сессией;
- 401 refresh выполняется максимум один раз существующим executor;
- `Calendar.Read`/write capabilities применяются fail-closed;
- create отправляет `Idempotency-Key`, update — strong `If-Match`;
- ETag обязан совпасть с version event details;
- malformed/unsupported page, enum, timing и cursor дают controlled protocol failure;
- logout/revocation отменяет requests и очищает calendar data.
