# Validation report — Calendar read API 1.0.0

Дата проверки: 2026-09-01

## Итог

PASS. Реализация собирается, HTTP contract tests проходят, application/persistence calendar tests проходят, а реальные PostgreSQL round-trip и tenant-boundary сценарии подтверждены на изолированном временном PostgreSQL 18.

## Проверки

| Проверка | Результат |
|---|---|
| Targeted Calendar HTTP contract tests | PASS — 25/25 |
| Calendar/Schedule application tests | PASS — 230/230 |
| Authorization/capability regression | PASS после обновления ожидаемого `Calendar.Read` |
| Full `dotnet test Task.sln -c Release` | PASS — 1268 passed, 2 unrelated existing PostgreSQL command tests skipped |
| `dotnet build Task.sln -c Release --no-restore` | PASS — 0 warnings, 0 errors |
| `dotnet format Task.sln --no-restore` | PASS для изменённого scope; существующий xUnit1031 не имеет code fix |
| `git diff --check` | PASS |
| Dashboard order + validation | PASS — 40 items, 8 categories, 6 gates |
| Real PostgreSQL schedule store | PASS — `RealPostgres_ScheduleWindowTenantBoundaryFiltersAndDiCoverage` |
| Real PostgreSQL calendar event store | PASS — `RealPostgres_CalendarEventRoundTripTenantBoundaryAndConcurrency` |

## Примечания

Два skip в полном solution gate относятся к существующим `TaskUpdateTests.RealPostgres_PatchIsAtomicDurableAndConcurrencySafe` и `TaskCreateCommandTests.RealPostgres_CreateIsAtomicDurableAndReadable`; они не входят в calendar read scope. Calendar persistence gate был отдельно запущен с реальным подключением и прошёл без skip.

Для real PostgreSQL проверки был создан отдельный локальный кластер на loopback. После теста он штатно остановлен и удалён; пользовательские базы и службы не изменялись.
