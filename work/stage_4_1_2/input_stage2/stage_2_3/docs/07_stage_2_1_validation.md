# Этап 2.1. Протокол исполняемой валидации

## 1. Статус

Финальный единый gate завершён со статусом `STAGE_2_1_VALIDATION_PASSED`. Проверка выполнена на фактических артефактах пакета, а не на выдержках из документации.

Команда воспроизведения из корня пакета:

```powershell
powershell -ExecutionPolicy Bypass -File qa\run_validation.ps1
```

## 2. Среда

| Компонент | Проверенная версия / режим |
| --- | --- |
| PostgreSQL | `16.10`, Docker image `postgres:16.10-alpine` |
| OpenAPI | `3.1.0`, JSON Schema dialect 2020-12 |
| Python validators | `openapi-spec-validator 0.7.2`, PyYAML |
| OpenAPI lint | Redocly CLI `2.40.0`, recommended rules |
| Server contract | `openapi-typescript 7.9.1` |
| Desktop SDK | `openapi-typescript-codegen 0.29.0`, Fetch client |
| Compilation | TypeScript `5.8.3`, `strict`, `noEmit` |

## 3. PostgreSQL

На каждом полном прогоне удаляются container, network и volume, после чего миграции применяются в последовательности:

1. `db/001_initial_schema.sql`;
2. `db/002_seed_authorization.sql`;
3. `db/003_audit_corrections.sql`;
4. `db/004_stage_2_1_foundation.sql`;
5. повторный `db/002_seed_authorization.sql` для проверки идемпотентности seed;
6. `qa/database_contract_tests.sql`;
7. `qa/concurrency_tests.py`.

Live catalog после миграций:

| Метрика | Значение |
| --- | ---: |
| Прикладные схемы | 14 |
| Tables/partitions | 90 |
| Views | 3 |
| Indexes | 396 |
| Пользовательские triggers | 187 |
| Канонические permissions | 91 |

Контрактные тесты проверяют bootstrap и повторный bootstrap администратора, точный permission catalog, системные и проектные роли, default account state, tenant-negative relations, acquire/complete/replay idempotency, другой request hash, неизменяемость history, уникальность версии объекта, dedupe change feed и lease-token completion.

Три конкурентных теста выполняются реальными параллельными транзакциями:

| Граф | Ожидаемый результат | Фактический результат |
| --- | --- | --- |
| Task parent `A → B` против `B → A` | один commit, один `23514`, без deadlock | Pass |
| Catalog parent `A → B` против `B → A` | один commit, один `23514`, без deadlock | Pass |
| Task dependency `A → B` против `B → A` | один commit, один `23514`, без deadlock | Pass |

Полный журнал: `qa/reports/postgresql_validation.log`. Отдельный журнал гонок: `qa/reports/concurrency_validation.log`. Live inventory: `qa/reports/postgresql_schema_inventory.json`.

## 4. OpenAPI и каталоги

Автоматически подтверждено:

- OpenAPI version строго `3.1.0`; legacy keyword `nullable` отсутствует;
- 241 уникальная операция совпадает с API catalog без расхождений;
- 232 конкретные schema; пустые/unbounded object schemas и `additionalProperties: true` запрещены;
- все `$ref` разрешаются;
- `If-Match`, `Idempotency-Key`, `409`, `412`, `428`, security и refresh contract проверяются;
- 91 permission существует в catalog и seed, используется или явно предусмотрен моделью;
- lifecycle paths, Project `planning`, secondary expected-version fields, Today, recurrence template и sync snapshot проверяются;
- 172 event и 18 background-job contracts согласованы с API и DDL.

Отчёт: `qa/validation_report.json`. Логи: `qa/reports/artifact_validation.log` и `qa/reports/openapi_lint.log`.

## 5. Генерация

- `schema.d.ts` генерируется из OpenAPI 3.1;
- `handlers.ts` генерируется для всех 241 `operationId`;
- desktop Fetch SDK содержит типизированные models/services;
- весь server contract и desktop SDK компилируется одним TypeScript strict gate.

Отчёт: `qa/reports/codegen_report.md`. Полный лог: `qa/reports/codegen_validation.log`.

## 6. Источники и трассировка

В пакет включены фактические входные документы и их SHA-256. Semantic traceability содержит 38 проверяемых требований, marker failures — 0. Старый hash концепции, источник которого отсутствовал в исходной поставке, сохранён как историческое расхождение и не используется для заявления о соответствии.

Отчёт: `qa/traceability_report.md`. Матрица: `catalogs/traceability.csv`.

## 7. Ограничения доказательства

Gate доказывает исполняемость и согласованность спецификации перед началом разработки. Он не подменяет будущие проверки реализации:

- query-plan и load tests на объёмах production profile;
- benchmark параметров Argon2id на целевом сервере;
- restore drill фактической backup/WAL chain;
- end-to-end contract tests реального backend и desktop.

Эти проверки являются обязательными gate Этапа 3, но не требуют изменения фундаментальных решений Этапа 2.1.
