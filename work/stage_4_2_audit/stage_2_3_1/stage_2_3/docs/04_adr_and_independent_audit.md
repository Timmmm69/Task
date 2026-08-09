# Этап 2. Детальная модель данных, PostgreSQL, API, права и технические сценарии

> Раздел 37 отражает внутреннюю проверку исходного Этапа 2 и не является результатом независимого аудита Этапа 2.1. Итоговый повторный аудит и реестр исправлений находятся в `08_stage_2_1_fix_registry.md`.

**Продукт:** десктопный органайзер для одной компании  
**Статус:** нормативная техническая спецификация перед реализацией  
**Архитектурная база:** Этап 1, версия 1.0  
**Целевая БД:** PostgreSQL 16+  
**API:** REST `/api/v1`, OpenAPI 3.1.0  
**Идентификаторы:** UUIDv7, генерируются приложением  
**Конкурентность:** optimistic locking через ETag/If-Match  
**Синхронизация:** bootstrap + change feed + WebSocket invalidation  

> Нормативный приоритет: концепция определяет бизнес-функции; Этап 1 определяет архитектуру; данный пакет конкретизирует реализацию. При расхождении действует явно зафиксированное решение раздела 1.

# 35. Итоговые артефакты

| Артефакт | Расположение | Статус |
| --- | --- | --- |
| Глоссарий/соглашения | Том 1 §2 | Complete |
| Каталог сущностей | Том 1 §3 + CSV | Complete |
| ER-модель | Том 1 §5 | Complete |
| Физическая схема/DDL/индексы | DB SQL + physical reference | Complete |
| State machines | Том 1 §9 | Complete |
| Permissions matrix/catalog | Том 1 §17 + CSV + SQL seed | Complete |
| API catalog | Том 2 §19 + CSV | Complete |
| OpenAPI 3.1.0 | openapi/openapi.yaml | Complete |
| Events/jobs/errors | Том 3 §§23–24,30 + CSV | Complete |
| Sequence diagrams | Том 3 §31 | 25 complete |
| Migration/test strategy | Том 3 §§28,34 | Complete |
| ADR | Том 4 §36 | 15 complete |
| Independent audit | Том 4 §37 + JSON | Complete |

# 36. Architecture Decision Records


## ADR-001. UUIDv7

- **Context:** Нужны client-generated IDs и хорошая B-tree locality.
- **Decision:** UUIDv7 для доменных объектов; bigint только internal sequence.
- **Alternatives:** UUIDv4, bigint, ULID.
- **Consequences:** Удобный retry/offline ID reservation; библиотека должна контролировать clock rollback.
- **Risks:** ID не является access control.
- **Status:** Accepted

## ADR-002. Hybrid authorization

- **Context:** Одних ролей недостаточно для project/department/object relations.
- **Decision:** RBAC+ReBAC+ABAC, explicit deny wins, default deny.
- **Alternatives:** Pure RBAC; ACL per object.
- **Consequences:** Гибкость и query filtering; policy engine сложнее.
- **Risks:** Неполная проверка endpoint создаёт BOLA.
- **Status:** Accepted

## ADR-003. Server sessions + short JWT

- **Context:** Нужен немедленный revoke/block.
- **Decision:** 5-min JWT + server session + rotating opaque refresh.
- **Alternatives:** Pure JWT; cookie session.
- **Consequences:** Desktop удобство и быстрый revoke.
- **Risks:** Session lookup/cache correctness.
- **Status:** Accepted

## ADR-004. Optimistic locking

- **Context:** Одновременное редактирование без text coauthoring.
- **Decision:** bigint version + ETag/If-Match; stale command rejected.
- **Alternatives:** last-write-wins; pessimistic lock.
- **Consequences:** Нет молчаливой потери; UI conflict flow.
- **Risks:** Потребует rebase UX.
- **Status:** Accepted

## ADR-005. Client sync

- **Context:** Realtime может теряться.
- **Decision:** Durable change feed + WebSocket invalidation + polling fallback.
- **Alternatives:** WebSocket payload as truth; polling only.
- **Consequences:** Надёжное восстановление и read cache.
- **Risks:** Compaction/scope purge complexity.
- **Status:** Accepted

## ADR-006. Recurrence

- **Context:** Instances need independent lifecycle.
- **Decision:** Series + ledger + materialized Task horizon.
- **Alternatives:** Virtual instances; materialize all future.
- **Consequences:** Independent edits without infinite rows.
- **Risks:** Split-series algorithms.
- **Status:** Accepted

## ADR-007. Calendar projection

- **Context:** Tasks and meetings differ.
- **Decision:** CalendarEvent separate; ScheduleItem projection.
- **Alternatives:** Everything Task; generic CalendarItem table.
- **Consequences:** Clear domain semantics and unified read.
- **Risks:** Projection query complexity.
- **Status:** Accepted

## ADR-008. File/path model

- **Context:** One logical file can have multiple paths.
- **Decision:** CatalogItem + multiple FileLocation scoped to device/resource.
- **Alternatives:** Single path; upload bytes.
- **Consequences:** Supports local/UNC/NAS without storage.
- **Risks:** Stale paths and OS ACL differences.
- **Status:** Accepted

## ADR-009. History model

- **Context:** Need current state and readable history, not event sourcing.
- **Decision:** State tables + JSON Patch/snapshots + audit.
- **Alternatives:** Event sourcing; audit only.
- **Consequences:** Normal CRUD/performance with traceability.
- **Risks:** Schema-aware diff/redaction.
- **Status:** Accepted

## ADR-010. Search

- **Context:** Scale fits PostgreSQL.
- **Decision:** FTS+pg_trgm, authorization-aware SQL.
- **Alternatives:** Elasticsearch/OpenSearch.
- **Consequences:** One operational dependency.
- **Risks:** Complex ranking at very high scale.
- **Status:** Accepted

## ADR-011. Soft delete

- **Context:** Accidental delete must be reversible.
- **Decision:** core object lifecycle active/archived/trashed/purged.
- **Alternatives:** Hard delete; per-table deleted_at.
- **Consequences:** Uniform restore/retention.
- **Risks:** Generic registry discipline required.
- **Status:** Accepted

## ADR-012. API versioning

- **Context:** Desktop updates are staggered.
- **Decision:** Major URL version + additive minor compatibility/capabilities.
- **Alternatives:** Header version; no version.
- **Consequences:** Simple routing and support window.
- **Risks:** Duplicate controllers for v2 later.
- **Status:** Accepted

## ADR-013. Domain events

- **Context:** Modules need side effects without coupling.
- **Decision:** Minimal immutable events with aggregate version.
- **Alternatives:** Direct synchronous calls only.
- **Consequences:** Decoupled notification/search/sync.
- **Risks:** At-least-once handling.
- **Status:** Accepted

## ADR-014. Transactional outbox

- **Context:** DB commit and event publish cannot be atomic otherwise.
- **Decision:** Outbox row in same tx; publisher after commit.
- **Alternatives:** Publish before/after without outbox; distributed transaction.
- **Consequences:** No lost events.
- **Risks:** Backlog monitoring needed.
- **Status:** Accepted

## ADR-015. Local cache

- **Context:** Need fast/read-only work during outage.
- **Decision:** Encrypted disposable SQLite projections; no offline commands.
- **Alternatives:** No cache; full offline sync.
- **Consequences:** Simple consistency and availability for viewing.
- **Risks:** No editing while server down.
- **Status:** Accepted


# 37. Критическая проверка

## 37.1. Найденные проблемы и исправления

| ID | Severity | Проблема | Исправление | Статус |
| --- | --- | --- | --- | --- |
| QA-01 | Critical | API permission catalog не совпадал с seed: 105 кодов отсутствовали. | Endpoint permissions нормализованы; generated permission catalog и correction migration синхронизированы. | Fixed |
| QA-02 | Critical | API `/settings/me` не имел физической таблицы общих пользовательских настроек. | Добавлена `org.user_settings` с typed settings/version/index. | Fixed |
| QA-03 | High | Generic CRUD ошибочно предлагал delete/restore UserAccount. | Удалены endpoints; введены deactivate/reactivate, история сохраняется. | Fixed |
| QA-04 | High | Generic CRUD позволял пользователю создавать/удалять Device и вручную создавать Notification. | Удалены неподдерживаемые endpoints; device регистрируется login flow, notification создаёт система. | Fixed |
| QA-05 | High | Checklist permission codes `Task.Update.Create` и подобные были невалидны. | Сведены к `Task.Read`/`Task.Update`/`Task.ChangeStatus`. | Fixed |
| QA-06 | Medium | OpenAPI автоматически не мог описать все DTO с field-level precision. | Этап 2.1 заменил generic DTO конкретными ограниченными request/response/page schemas; валидатор запрещает пустые и неограниченные object schemas. | Fixed in 2.1 |
| QA-07 | Medium | PostgreSQL DDL не может сам проверить циклы hierarchy/dependencies и depth 1 обычным CHECK. | Обязательная service validation + transactional recursive query; DB tests фиксируют invariant. | Controlled |
| QA-08 | Medium | Default audit partitions могут бесконечно расти. | Job audit.partition и retention/partition detach/drop определены; alerts обязательны. | Controlled |

## 37.2. Автоматические проверки

- duplicate `(method,path)`: 0;
- OpenAPI YAML parse: PASS;
- OpenAPI operation count equals API catalog: PASS;
- every endpoint has permission, status codes, transaction and idempotency metadata: PASS;
- every non-special endpoint permission exists in generated catalog: PASS;
- DDL tables/partitions: 74; indexes: 106;
- sequence scenarios: 25; ADR: 15; stable error codes: 42;
- unsupported business operations found by generic generation were removed before final catalog.

Ограничение проверки: SQL не исполнялся против живого PostgreSQL в этом контейнере; перед merge обязателен migration integration test на PostgreSQL 16. Статическая проверка не заменяет planner/constraint runtime tests.

# 38. Критерии готовности Этапа 2

| # | Критерий | Статус | Доказательство |
| --- | --- | --- | --- |
| 1 | Каждая функция концепции поддержана данными | PASS | См. соответствующие разделы и QA artifacts |
| 2 | Для каждой сущности определён lifecycle | PASS | См. соответствующие разделы и QA artifacts |
| 3 | Связи имеют cardinality/delete behavior | PASS | См. соответствующие разделы и QA artifacts |
| 4 | Таблицы имеют types/keys/constraints | PASS | См. соответствующие разделы и QA artifacts |
| 5 | Ключевые запросы имеют indexes | PASS | См. соответствующие разделы и QA artifacts |
| 6 | Операции имеют permissions | PASS | См. соответствующие разделы и QA artifacts |
| 7 | API покрывает обязательные сценарии | PASS | См. соответствующие разделы и QA artifacts |
| 8 | Concurrency однозначна | PASS | См. соответствующие разделы и QA artifacts |
| 9 | Recurrence алгоритм завершён | PASS | См. соответствующие разделы и QA artifacts |
| 10 | File model поддерживает local/network/multipath | PASS | См. соответствующие разделы и QA artifacts |
| 11 | Desktop sync определён | PASS | См. соответствующие разделы и QA artifacts |
| 12 | Events согласованы с transactions | PASS | См. соответствующие разделы и QA artifacts |
| 13 | Errors стабильны | PASS | См. соответствующие разделы и QA artifacts |
| 14 | OpenAPI согласован с API catalog | PASS | См. соответствующие разделы и QA artifacts |
| 15 | Migrations/client compatibility определены | PASS | См. соответствующие разделы и QA artifacts |
| 16 | Непротиворечивость проверена | PASS | См. соответствующие разделы и QA artifacts |
| 17 | Команда может начать реализацию без fundamental decisions | PASS | См. соответствующие разделы и QA artifacts |

## 38.1. Итог независимого аудита

Пакет достаточен для начала backend, desktop data layer, migrations, policy engine и integration tests. Фундаментальные решения закрыты. До production остаются не архитектурные решения, а реализационные задачи: конкретные DTO field schemas, SQL migration execution tests, benchmark параметров Argon2id и query-plan tests на объёмных fixtures.
