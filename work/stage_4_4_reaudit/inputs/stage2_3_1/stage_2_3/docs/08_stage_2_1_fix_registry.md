# Этап 2.1. Реестр исправлений и повторный независимый аудит

## 1. Правило статуса

`Fixed` присвоен только дефектам, для которых изменены все связанные нормативные/исполняемые артефакты и существует автоматическая либо исполняемая проверка. Простой текстовый комментарий не считается исправлением.

## 2. Реестр исправлений

| Audit ID | Severity | Root cause | Decision | Changed files | Tests | Status | Residual risk |
| --- | --- | --- | --- | --- | --- | --- | --- |
| CR-01 | Critical | Синтаксическая ошибка делала исходный DDL неисполняемым. | Исправить DDL и проверять полный clean deploy PostgreSQL 16. | `db/001_initial_schema.sql`; `docker-compose.yml`; `qa/run_validation.ps1` | Последовательное применение `001…004` с `ON_ERROR_STOP`; DB contracts | Fixed | Изменения будущих миграций обязаны проходить тот же gate. |
| CR-02 | Critical | OpenAPI содержал универсальные/пустые object schemas и не был полноценным контрактом. | OpenAPI 3.1 с конкретными bounded DTO, required, enum, format и limits. | `openapi/openapi.yaml`; `qa/build_openapi.py`; `qa/validate_artifacts.py` | spec-validator; Redocly; запрет empty/unbounded schemas; codegen compile | Fixed | Изменение DTO только через generator и contract gate. |
| CR-03 | Critical | Optimistic locking/idempotency были metadata, а не HTTP-параметрами; отсутствовали обязательные ответы. | Явные `If-Match`, `Idempotency-Key`, `409/412/428` и target aggregate. | `catalogs/api_catalog.csv`; `openapi/openapi.yaml`; `docs/02_api_and_concurrency.md` | Header/response/version validator по всем 241 operations | Fixed | Backend middleware должен точно реализовать контракт. |
| CR-04 | Critical | `/auth/refresh` наследовал bearer security и не мог работать с истёкшим access token. | Anonymous refresh contract с rotation request, без bearer requirement. | `catalogs/api_catalog.csv`; `openapi/openapi.yaml` | Специальная refresh-security regression | Fixed | Cookie/storage policy проверяется в desktop implementation. |
| CR-05 | Critical | Не существовало durable хранилища результатов idempotency и протокола crash recovery. | Scope `(organization,user,operation,key)`, request hash, lease, stored response, expiry, acquire/complete. | `db/004_stage_2_1_foundation.sql`; `docs/06_stage_2_1_normative_corrections.md`; OpenAPI/catalog/jobs | acquire→complete→replay; different hash rejection; scope collision; cleanup contract | Fixed | Business transaction и completion должны использовать один DB boundary. |
| CR-06 | Critical | Универсальные delete/restore endpoints противоречили физическим lifecycle Role, Checklist, Recurrence, Reminder, Comment. | Зафиксировать owner lifecycle каждого типа и заменить фиктивные restore на activate/resume/reschedule либо aggregate operation. | `docs/06_stage_2_1_normative_corrections.md`; DDL; API catalog; OpenAPI; events | Lifecycle path/permission validator; DB constraints | Fixed | Новые типы обязаны добавляться в lifecycle matrix. |
| CR-07 | Critical | Recurrence ссылалась на необязательную source task и не могла материализовать полноценную задачу. | Нормализованный versioned task template с actors, checklist/reminder/link templates, split/exceptions/ledger. | `db/004_stage_2_1_foundation.sql`; `docs/01_core_domain_and_data.md`; OpenAPI/events/jobs | Deferred required-template constraint; schema/API/traceability checks | Fixed | Worker implementation должна соблюдать ledger и series lock. |
| CR-08 | Critical | Permission seed и API расходились; системные/project roles и bootstrap были неполны. | Ровно 91 canonical permission; детерминированный seed; 4 system и 5 project roles на tenant; atomic first-admin bootstrap. | `catalogs/permissions.csv`; `db/002_seed_authorization.sql`; `db/003_audit_corrections.sql`; generator | Exact count; seed rerun; role/matrix/bootstrap DB tests; API coverage | Fixed | Изменение permission требует синхронного изменения catalog, seed, roles и API. |
| CR-09 | Critical | Restore/write операции использовали read permissions. | Ввести `Archive.Restore`, `Trash.Restore` и sensitive-path permission; запретить read-as-write. | permissions catalog/seed; API catalog; OpenAPI; role matrices | Permission action validator; lifecycle endpoint checks | Fixed | Custom roles могут не иметь restore по политике — это ожидаемо. |
| H-01 | High | Новый user по умолчанию сразу становился active. | Default `pending_activation`; bootstrap admin активируется явно. | `db/001_initial_schema.sql`; auth docs/OpenAPI | DB default introspection; bootstrap tests | Fixed | Invite/activation flow реализуется в Stage 3. |
| H-02 | High | Project status различался как `planned`/`planning`. | Каноническое значение `planning` во всех средах. | DDL; OpenAPI; API/domain docs | OpenAPI/DDL marker check | Fixed | Нет. |
| H-03 | High | Команды с несколькими изменяемыми ресурсами не определяли версии каждого объекта. | Header version для aggregate target и typed secondary expected-version fields. | API catalog; OpenAPI; `docs/06_stage_2_1_normative_corrections.md` | Secondary-version schema validator; TypeScript codegen | Fixed | Service обязан проверять secondary versions в одной транзакции. |
| H-04 | High | Иерархии/dependency graph были подвержены race и deadlock. | Stable advisory-lock ordering и повторное чтение без встречной parent row-lock; recursive invariant checks. | `db/004_stage_2_1_foundation.sql`; concurrency docs/tests | Три реальные двухтранзакционные гонки: один commit, один `23514`, без `40P01` | Fixed | Retry допускается только для явно transient SQLSTATE и идемпотентной команды. |
| H-05 | High | Single-column FK допускали cross-organization relations. | Composite guards либо generated constraint triggers для явного/выводимого tenant. | `db/004_stage_2_1_foundation.sql`; tenant docs | Cross-tenant project owner negative test; trigger inventory | Fixed | Trigger overhead требуется измерить на production profile. |
| H-06 | High | Lifecycle дочерних объектов и purge/cascade semantics были неоднозначны. | Aggregate child, status-owned и `core.object` lifecycle разделены; tombstone/redaction и child effects зафиксированы. | lifecycle matrix; DDL/FK; API/events/jobs | Lifecycle/permission validator; append-only/tombstone DB checks | Fixed | Purge worker implementation должна соблюдать legal hold. |
| H-07 | High | Audit/history можно было менять; версия объекта не была глобально уникальна между partitions. | Runtime roles без UPDATE/DELETE, immutable triggers, global version key, append-only redactions. | `db/004_stage_2_1_foundation.sql`; history docs | Duplicate version rejection; UPDATE/DELETE rejection; privilege introspection | Fixed | Redaction policy должна пройти security review на реальных payload. |
| H-08 | High | Два конкурирующих writer механизма change feed создавали дубли/разный порядок. | Единственный writer — idempotent projector из committed domain event/outbox. | DDL projector; API/event catalogs; sync docs | Unique source event projection; duplicate projection returns same sequence | Fixed | Outbox lag остаётся наблюдаемой эксплуатационной метрикой. |
| H-09 | High | Worker ownership не защищалось tokenized lease; stale worker мог завершить reclaimed work. | Claim/heartbeat/complete/fail с lock token, expiry, attempts, backoff и dead-letter. | `db/004_stage_2_1_foundation.sql`; background jobs; runtime docs | Wrong-token completion rejection; correct-token completion; schema checks | Fixed | Worker integrations должны использовать server time. |
| H-10 | High | Bootstrap sync не имел стабильного cut/session/page contract и обязательного catch-up. | Materialized snapshot session/items, fixed cut/scope version, typed pages, catch-up after cut. | DDL; OpenAPI; sync docs/events/jobs | Snapshot schema/API checks; source-event dedupe | Fixed | E2E concurrent-write bootstrap test выполняется с backend в Stage 3. |
| H-11 | High | File path/session ownership и path disclosure доверяли client data; probe мог менять global availability. | Session-derived user/device, approved active root, containment, sensitive-path permission, per-device state. | DDL/views; OpenAPI/API catalog; file docs | Tenant/path/state constraints; sensitive permission and redacted-view checks | Fixed | Windows canonicalization/ACL behavior требует platform integration tests. |
| H-12 | High | Reminder states/offset/snooze/dismiss/cancel расходились между DDL, API и worker. | Единая state machine и occurrence lease/dead-letter; строгие date/offset constraints. | DDL; lifecycle/reminder docs; OpenAPI/events/jobs | CHECK constraints; lifecycle API validation; lease schema checks | Fixed | OS notification delivery semantics проверяются на Windows. |
| H-13 | High | Заявленный hash концепции не соответствовал доступному источнику. | Упаковать фактические источники, вычислить hashes и выполнить semantic diff. | `sources/*`; `qa/generate_traceability.py`; traceability artifacts | 38 semantic markers; packaged-source hash validation; 0 failures | Fixed | Исторический отсутствующий source не восстановлен и явно не считается canonical. |
| M-01 | Medium | Концептуальный экран Today не имел типизированного API/read model. | Отдельный `/api/v1/today` и SQL read model. | API catalog/OpenAPI; `calendar.today_read_model`; docs | API/schema/DDL marker checks | Fixed | Оптимизация запроса проверяется на volume fixtures. |
| M-02 | Medium | Sound/quiet-hours дублировались в двух settings aggregates. | Notification preferences — единственный owner; general user settings не дублируют поля. | `db/003_audit_corrections.sql`; settings DTO/docs | Column/schema ownership checks | Fixed | Desktop migration старых local settings выполняется в Stage 3. |
| M-03 | Medium | Не хватало contact attendee, comment reply и единственной primary company relation. | Нормализованные relations и unique partial constraint. | `db/004_stage_2_1_foundation.sql`; DTO/API/docs | Table/index/schema existence checks | Fixed | Нет. |
| M-04 | Medium | Для высокообъёмных audit/telemetry не было исполняемой retention стратегии. | Monthly partitioning audit/history; bounded batched retention policies для operational telemetry; отдельный leased job. | DDL; `catalogs/background_jobs.csv`; runtime docs | Live partition/table/index inventory; policy/job validator | Fixed | Batch size и срок хранения калибруются по production profile. |
| M-05 | Medium | UNC path не был связан с разрешённым active network resource и containment root. | Обязательная approved root/network binding и canonical containment constraints. | DDL; file API/docs | Constraint/view/permission checks | Fixed | SMB/DFS edge cases требуют Windows integration suite. |
| M-06 | Medium | Один probe мог глобально объявить location недоступным для всех устройств. | Availability вынесена в `(file_location_id,device_id)` projection. | DDL/views; OpenAPI/file docs | Unique per-device state и schema checks | Fixed | Политика stale telemetry реализуется worker/service. |

## 3. Повторный независимый аудит

### 3.1. Метод

Повторный аудит выполнен от обязательных критериев и фактических gate outputs. Статус из таблицы выше не использовался как доказательство сам по себе. Проверены live PostgreSQL catalog/log, negative DB tests, результаты параллельных транзакций, parsed OpenAPI 3.1, codegen outputs, strict compilation, catalogs и semantic traceability.

### 3.2. Результаты по областям

| Область | Оценка | Фактическое основание |
| --- | ---: | --- |
| Архитектура и DDD boundaries | 93 | Normative precedence, aggregate/lifecycle ownership, single change-feed writer |
| Доменная модель и lifecycle | 94 | Реализуемые status models, recurrence template, typed relations |
| PostgreSQL/normalization/constraints/indexes | 92 | Clean PostgreSQL 16 deploy; 90 tables/partitions, 396 indexes, 187 triggers; negative tests |
| API/OpenAPI/REST | 94 | OpenAPI 3.1; 241 operations; 232 bounded schemas; lint/codegen/compile |
| Concurrency/transactions/idempotency | 92 | Durable records; version contracts; three deterministic race tests |
| RBAC/ReBAC/ABAC и tenant security | 93 | Exact permission model, role matrices, bootstrap, database tenant guards |
| Desktop/sync/cache/offline | 90 | Stable snapshot/cut/cursor/catch-up and read-only offline contract |
| Files/reminders/workers | 91 | Session-bound paths, per-device projection, strict states, token leases |
| Performance/scalability/operations | 86 | Index/retention/partition/job contracts complete; volume evidence deferred to implementation |
| Testability/maintainability/traceability | 94 | Reproducible single gate, generated catalogs, source hashes, 38 traceability markers |

**Итоговая готовность: 92/100.**

Оценка не равна 100, потому что пакет является спецификацией перед реализацией: planner/load evidence, backup restore drill и end-to-end tests реального backend/desktop объективно появятся только в Этапе 3.

### 3.3. Оставшиеся замечания

**Critical:** отсутствуют.

**High:** отсутствуют.

**Medium по артефактам Этапа 2.1:** отсутствуют.

Остаются обязательные implementation gates, не являющиеся открытыми дефектами спецификации:

1. query-plan/load tests на согласованном production data profile;
2. Argon2id benchmark на целевом сервере;
3. backup/WAL restore drill в изолированную среду;
4. Windows path/SMB/ACL integration suite;
5. backend/desktop end-to-end contract и bootstrap-under-write tests.

## 4. Решение

- Переход к реализации: **разрешён**.
- Переход к Этапу 3: **разрешён**.
- Разработку можно начинать по исправленному пакету; возвращаться к фундаментальному проектированию для закрытых `CR/H/M` не требуется.
- Перечисленные implementation gates должны войти в Definition of Done соответствующих вертикальных срезов Этапа 3 и не могут быть перенесены за production release.

## 5. Доказательства

- `qa/reports/validation_summary.log` — итог единого gate;
- `qa/reports/full_validation_console.log` — полный консольный прогон;
- `qa/reports/postgresql_validation.log` — миграции и DB contract tests;
- `qa/reports/concurrency_validation.log` — конкурентные транзакции;
- `qa/reports/openapi_lint.log` — OpenAPI lint;
- `qa/validation_report.json` — машинный отчёт согласованности;
- `qa/reports/codegen_report.md` и `qa/reports/codegen_validation.log` — server/client generation и compile;
- `qa/traceability_report.md` и `catalogs/traceability.csv` — источники, hashes и semantic coverage;
- `MANIFEST.json` — SHA-256 каждого файла поставки.
