# Validation report

- Версия: `1.0.0`
- Дата: `2026-08-29`
- Итог: `PASS`

## Проверено

| Gate | Результат |
|---|---|
| `dotnet test tests/Task.Desktop.Tests/Task.Desktop.Tests.csproj -c Release --no-restore` | PASS, 210/210 |
| Task endpoint tests | PASS, 128/128 |
| `dotnet test Task.sln -c Release --no-restore` | PASS, 1227 passed, 2 expected real-Postgres skips |
| `dotnet build Task.sln -c Release --no-restore` | PASS, 0 warnings, 0 errors |
| `verification/Test-DesktopShell.ps1` | PASS |
| `verification/Test-TaskWriteE2E.ps1 -Phase Verify` | PASS |
| `git diff --check` | PASS |
| Проверка package manifest/SHA-256 | PASS |

## Покрытие

Проверены валидация полей, UTC-конверсия расписания, create/PATCH/transition контракты, idempotency, optimistic concurrency, сохранение черновика при сетевой ошибке, cancellation/generation guards, права Task.Read/Create/Update/ChangeStatus, read-only UX и durable audit/domain event/outbox след.

В раннем запуске gate Release-бинарник был занят живым E2E-экземпляром WPF. После его закрытия весь gate повторён и прошёл.
