# DATA-05 — validation report

Версия пакета: `1.0.0`
Дата проверки: `2026-09-08T12:35:44+03:00`
Итог: **PASS**

## Реализованный контур

- `outbox.publish` раз в секунду забирает bounded batch через `FOR UPDATE SKIP LOCKED`, выдаёт 30-секундную lease и публикует domain event в durable `sync.change_feed`.
- Change feed соответствует каноническому Stage 2.2 projector-контракту: единственный writer — `sync.project_domain_event_change`, а unique key `(organization_id, source_event_id, object_id, operation)` исключает повторную проекцию.
- Проекция event и перевод outbox message в `published` выполняются в одной транзакции. Worker продлевает активную lease heartbeat-ом перед доставкой; stale token не может завершить или продлить перехваченную работу.
- `reminders.dispatch` материализует due reminders с уникальным `(reminder_id, due_at)`, забирает occurrences через lease и атомарно создаёт пользовательское уведомление.
- Ошибки получают capped exponential backoff до 3600 секунд; после 10 попыток запись переходит в `dead_letter`.
- Просроченная lease повторно захватывается после падения worker-а. Если падение произошло на последней попытке, следующая проверка переводит запись в `dead_letter`, не оставляя её навечно в `processing/claimed`.
- Существующие `ExpiredSessionMaintenanceWorker`, `RecurrenceHorizonWorker` и базовый Windows Service host сохранены и прошли регрессию.

## Фактические проверки

| Проверка | Результат |
|---|---:|
| `dotnet build Task.sln -c Release --no-restore` | PASS, 0 ошибок |
| `Task.Tests` Release | PASS, 792 executed/796 total; 4 внешних PostgreSQL-теста пропущены штатными guards |
| `Task.ServiceHosts.Tests` Release | PASS, 562/562 |
| `Task.Desktop.Tests` Release | PASS, 269/269 |
| Targeted background-worker tests | PASS, 4/4 |
| Targeted migration contract tests | PASS, 9/9 |
| Отдельный PostgreSQL 16 DATA-05 gate | PASS, 1/1 без пропусков |
| Форматирование изменённых C# файлов | PASS |
| `npm run dashboard:validate` | PASS |
| `git diff --check` | PASS |
| `node work/production/verification/Build-Data05Package.mjs` | PASS, 25 artifacts и все SHA-256 перепроверены |

PostgreSQL gate выполнен на временном `postgres:16-alpine` и подтвердил миграцию v14, tenant-safe reminder relations, отсутствие прямого INSERT-доступа runtime role к change feed, канонические upsert/tombstone projections, конкурентный claim, heartbeat, recovery просроченной lease, идемпотентность change feed, reminder → notification и dead-letter на 10-й попытке. Контейнер после проверки остановлен и удалён.

## Ограничения и замечания

- Четыре общих интеграционных теста `Task.Tests` имеют штатные external-environment guards и были пропущены в полном прогоне. Специализированный DATA-05 PostgreSQL 16 тест выполнен отдельно без пропуска.
- Общий `dotnet format Task.sln --verify-no-changes` фиксирует существующий whitespace debt в не затронутых DATA-05 файлах. Проверка только изменённых файлов проходит.
- В полном Release rebuild остаются 8 существующих предупреждений: ASP.NET deprecation в старых service-host tests и xUnit blocking-call warning в desktop test. Новые файлы предупреждений не добавляют.

## Заключение

Критерий `DATA-05` подтверждён фактическим исполнением worker-сценариев. Roadmap обновлён с 50% до 100% и статусом `done`.
