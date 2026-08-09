# Stage 2.1 source traceability and semantic diff

## Canonical sources

- Product concept: `sources/product_concept.txt`, SHA-256 `74482a0d463a4831228f96847496152d93887aece6f498e2cd1dec0054d5f7f4`.
- Stage 1 architecture: `sources/architecture_stage1.md`, SHA-256 `309492241dcd63bac970b467e729fb878ae265cca6f592cf43c3bb4ac0cdbe16`.
- Stage 2.1 acceptance criteria: `sources/stage_2_1_acceptance_criteria.txt`, SHA-256 `3898cfcf31c3c766cafede3861f813fd5e81dd34eb7b92aa1df795164c25843f`.
- The original Stage 2 report declared concept hash `fc28c77341aa9309aaa4b44311191ef81d641bb24bdf45f8c6fc8e135fb2ea86`, but did not include that source.
- Stage 2.1 removes the ambiguity by packaging the user-supplied source itself and making its current hash normative.

## Semantic diff

This is a requirement-level comparison, not a hash-only check. Each concept/architecture capability is mapped to an executable or contract artifact.

| ID | Requirement | Result | Stage 2.1 evidence |
| --- | --- | --- | --- |
| C-01 | Одна компания, локальный сервер, отсутствие внешнего облака | Pass | docs/01_core_domain_and_data.md; db/001_initial_schema.sql |
| C-02 | Пользователи, сотрудники, отделы, роли | Pass | db/001_initial_schema.sql; db/003_audit_corrections.sql |
| C-03 | Read-model «Сегодня» | Pass | db/004_stage_2_1_foundation.sql:calendar.today_read_model; GET /api/v1/today |
| C-04 | Входящие и конвертация | Pass | work.inbox_items; /api/v1/inbox-items |
| C-05 | Задачи, подзадачи и чек-листы | Pass | work.tasks/checklists/checklist_items; graph locks |
| C-06 | Повторяющиеся задачи | Pass | work.recurrence_task_templates; recurrence API |
| C-07 | Календарь и конфликты | Pass | calendar schema; /api/v1/calendar |
| C-08 | Проекты, участники и проектные роли | Pass | projects schema; five canonical project roles |
| C-09 | Файловый каталог и несколько путей | Pass | files schema; approved roots; per-device availability |
| C-10 | Контакты, контрагенты и взаимодействия | Pass | crm schema and API |
| C-11 | Напоминания и desktop-уведомления | Pass | calendar.reminders; notify; lease protocol |
| C-12 | Совместная работа и optimistic concurrency | Pass | If-Match/412/428; comments and replies |
| C-13 | Работа без сервера без offline writes | Pass | docs/03 runtime; snapshot/cache contract |
| C-14 | Глобальный поиск | Pass | search.search_documents; bounded query parameters |
| C-15 | История изменений | Pass | append-only history; global object/version uniqueness |
| C-16 | Архив, корзина и purge | Pass | lifecycle matrix; tombstones/redactions |
| C-17 | Настройки пользователя и уведомлений | Pass | org.user_settings + notify.notification_preferences without duplicate ownership |
| C-18 | Авторизация и server-derived scope | Pass | 91 permissions; roles; tenant guards |
| A-01 | Desktop + modular local server | Pass | Stage 2 bounded schemas and module tags |
| A-02 | Transactional outbox | Pass | governance.domain_events/outbox_messages |
| A-03 | Desktop sync coordinator and cache | Pass | snapshot sessions + projected change feed |
| A-04 | Authorization policy module | Pass | canonical permission/role matrices and database tenant guards |
| A-05 | Background worker and backup agent | Pass | token leases; background job catalog |
| S21-01 | PostgreSQL 16 clean deploy | Pass | db/001...004; qa/database_contract_tests.sql |
| S21-02 | Canonical authorization | Pass | 91 permissions; bootstrap; role matrices |
| S21-03 | Concrete OpenAPI | Pass | openapi/openapi.yaml; validation/codegen reports |
| S21-04 | Durable idempotency | Pass | iam.idempotency_records; API headers |
| S21-05 | Lifecycle matrix | Pass | docs/06 section 4 |
| S21-06 | Recurrence task template | Pass | work.recurrence_task_templates and children |
| S21-07 | Concurrent graph locks | Pass | core.lock_graph_nodes; concurrency tests |
| S21-08 | Tenant boundaries | Pass | generated database tenant guards; negative tests |
| S21-09 | Append-only audit/history | Pass | runtime roles, triggers, version key, tombstones |
| S21-10 | Canonical change feed and snapshots | Pass | event projector, source dedupe, snapshot sessions |
| S21-11 | Worker leases | Pass | claim/heartbeat/complete/fail functions |
| S21-12 | File security | Pass | owner/device binding, approved roots, redaction, per-device state |
| S21-13 | Reminder state machine | Pass | strict state dates and occurrence lease/retry |
| S21-14 | Source traceability | Pass | sources directory, hashes, semantic matrix |
| S21-15 | Medium audit findings | Pass | Today, settings ownership, relations, retention, indexes, limits |

## Semantic changes introduced by Stage 2.1

- No product capability was removed.
- Lifecycle endpoints were changed only where the original endpoint could not be represented by its physical model: role restore → activate, recurrence restore → resume, reminder restore → reschedule.
- The new Today endpoint exposes a concept capability that previously had no explicit API contract.
- Change feed remains an architecture capability, but its writer is now uniquely the post-commit domain-event projector.
- File paths remain references to existing files; Stage 2.1 only restricts disclosure and cross-device availability semantics.
- Offline behavior remains read-only cache usage; durable offline business writes are still out of scope.

Automated marker failures: 0.
