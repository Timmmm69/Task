# Этап 2.1. Нормативные инженерные коррекции

## 1. Статус и приоритет

Этот документ фиксирует только изменения, необходимые по результатам независимого аудита. Он является нормативным дополнением к документам Этапа 2. При конфликте применяются в порядке приоритета:

1. исполняемые миграции `db/001...004`;
2. `openapi/openapi.yaml`;
3. каталоги `catalogs/*.csv`;
4. этот документ;
5. документы `01...05` в части, не переопределённой Этапом 2.1.

Канонический продуктовый источник — `sources/product_concept.txt`. Архитектурная база — `sources/architecture_stage1.md`. Их SHA-256 фиксируются в `qa/traceability_report.md` и `MANIFEST.json`.

## 2. Исполняемая схема и миграции

Целевая версия — PostgreSQL 16. Миграции применяются строго последовательно:

1. `001_initial_schema.sql`;
2. `002_seed_authorization.sql`;
3. `003_audit_corrections.sql`;
4. `004_stage_2_1_foundation.sql`.

`001` не является повторно применяемой миграцией. Повторный запуск предотвращает migration ledger прикладного migrator. `002` является идемпотентным canonical seed: обновляет 91 разрешение, удаляет отсутствующие в каталоге и проверяет итоговое количество. `iam.bootstrap_first_administrator` и `iam.seed_organization_authorization` допускают безопасный повтор с теми же идентификаторами.

Чистое развёртывание, повторный seed и контрактные проверки выполняет `qa/run_validation.ps1`. Лог PostgreSQL сохраняется в `qa/reports/postgresql_validation.log`.

## 3. Каноническая авторизация

### 3.1. Каталог

Канонический каталог содержит 91 permission. Три разрешения введены для устранения выявленных privilege-escalation и disclosure дефектов:

- `Archive.Restore` — команда возврата из архива; дополнительно проверяется доменное право изменения объекта;
- `Trash.Restore` — команда возврата из корзины; дополнительно проверяется доменное право восстановления конкретного типа;
- `FileLocation.ReadSensitivePath` — просмотр полного физического пути, если пользователь не является владельцем привязанного устройства.

`History.Read` и `Trash.Read` не разрешают изменение lifecycle.

### 3.2. Системные и проектные роли

При bootstrap организации создаются четыре системные роли:

- `system_admin`;
- `manager`;
- `employee`;
- `observer`.

И пять проектных ролей:

- `project_owner`;
- `project_manager`;
- `project_editor`;
- `project_executor`;
- `project_observer`.

Точные матрицы задаёт `iam.seed_organization_authorization`. `system_admin` получает все 91 permission. Остальные матрицы заданы явными списками кодов; добавление нового permission не расширяет их автоматически. Bootstrap первой организации в одной транзакции создаёт организацию, настройки, профиль, активную учётную запись первого администратора, обе группы ролей, матрицы и назначение `system_admin`.

Обычный `UserCreate` создаёт `account_status=pending_activation`. Состояние `active` задаёт только подтверждённая activation-команда или bootstrap первого администратора.

## 4. Lifecycle matrix

| Тип | Источник lifecycle | Delete semantics | Restore/activate semantics | Участие в `core.objects` / universal trash |
| --- | --- | --- | --- | --- |
| UserAccount | `account_status` | `deactivate`; запись и история сохраняются | `activate/reactivate` | `core.objects`, но не universal trash |
| Role | `iam.roles.status` | деактивация пользовательской роли; системная роль не удаляется | `/roles/{id}/activate` | нет |
| ProjectRole | `projects.project_roles.status` | деактивация; системная роль защищена | повторная активация через role management | нет |
| Checklist / ChecklistItem | Task aggregate | физическое удаление дочерней строки внутри Task transaction | нет отдельного restore | нет |
| RecurrenceSeries | `status` | `cancelled`; шаблон и ledger сохраняются | `/recurrence-series/{id}/resume` только из `paused` | нет |
| Reminder | `status` | `cancelled` | `/reminders/{id}/reschedule` создаёт новый trigger plan | нет |
| Comment | `status=deleted`, `deleted_at/by` | moderation tombstone; body недоступен обычной проекции | dedicated `/comments/{id}/restore` с `Comment.Moderate` | нет |
| Project, Task, Contact, Company, Interaction, CatalogItem, Tag, CalendarEvent, Department | `core.objects.lifecycle_state` | archive или universal trash согласно типу | доменное restore + `Archive.Restore`/`Trash.Restore` для универсальных endpoints | да |

Универсальные archive/trash endpoints отклоняют типы, не включённые в последнюю строку. Restore всегда проверяет текущую версию, parent/unique constraints, доступ к объекту и доменное write/restore permission.

## 5. Idempotency contract

`iam.idempotency_records` хранит scope `(organization_id, user_account_id, operation_id, idempotency_key)`, SHA-256 нормализованного request, состояние, lease и сериализованный HTTP result.

Алгоритм mutating-команды:

1. сервер валидирует формат ключа и вычисляет request hash;
2. в той же PostgreSQL transaction выполняется insert idempotency row;
3. конфликтующий insert ожидает текущую transaction и затем блокирует существующую строку;
4. другой hash для того же scope возвращает `409 IDEMPOTENCY_KEY_REUSED`;
5. `completed` возвращает сохранённые status/headers/body и `Idempotency-Replayed: true`;
6. активный чужой lease возвращает `409 IDEMPOTENCY_REQUEST_IN_PROGRESS` с `Retry-After`;
7. бизнес-записи, audit, history, domain event, outbox и completed idempotency response фиксируются одним commit;
8. rollback удаляет и бизнес-изменения, и незавершённую idempotency row;
9. crash после commit безопасен: следующий запрос получает сохранённый response;
10. cleanup удаляет только истёкшие записи, не имеющие активного lease.

Ключ обязателен для команд, помеченных в API-каталоге `Idempotency-Key`. Для login ключ опционален. Single-use refresh token остаётся самостоятельным механизмом replay protection.

## 6. HTTP concurrency contract

`If-Match` содержит strong ETag `"v<positive-int64>"` и защищает агрегат, указанный в OpenAPI extension `x-if-match-target`.

- отсутствующий обязательный header → `428 PRECONDITION_REQUIRED`;
- stale ETag корневого агрегата → `412 PRECONDITION_FAILED`;
- конфликт доменного состояния, uniqueness или secondary expected version → `409`;
- текущая версия и ETag возвращаются в `ProblemDetails`.

Для команд с несколькими версиями header защищает один агрегат, остальные версии находятся в DTO:

| Команда | Header target | Secondary version |
| --- | --- | --- |
| FileLocation patch | location | `expectedCatalogItemVersion` |
| ProjectMember patch | member | `expectedProjectVersion` |
| Recurrence scoped change | series | `expectedTaskVersion` |
| Bulk task transition | нет общего header | `expectedVersion` каждого item |
| Project ownership transfer | project | `expectedNewOwnerMembershipVersion` |

Обновление выполняется условным `UPDATE ... WHERE id=:id AND version=:expected`, затем version увеличивается ровно на 1. Domain event и history используют полученную версию.

## 7. Конкурентные графы

Task hierarchy, file catalog hierarchy и task dependency graph используют единый порядок блокировок:

1. собрать organization ID и все старые/новые node ID;
2. удалить `NULL` и дубликаты;
3. отсортировать UUID;
4. получить `pg_advisory_xact_lock` в этом порядке;
5. перечитать затронутые строки после получения advisory locks без дополнительной блокировки родительской строки: целевая строка уже блокируется самим `UPDATE`, а ожидание встречной parent-row lock внутри row-trigger создаёт цикл ожидания;
6. проверить tenant, parent type, project scope, depth/cycle;
7. выполнить запись.

Такой порядок обязателен для create/move/reparent/dependency add/remove. Обход без advisory locks запрещён. Ошибка serializable/deadlock retry допускает не более трёх попыток с jitter и только при наличии idempotency key.

## 8. Tenant isolation

Organization scope берётся из authenticated session. Поле `organizationId` отсутствует в mutating DTO.

`004_stage_2_1_foundation.sql` создаёт database guards для всех single-column FK, где child и parent имеют `organization_id`. Для relation tables без собственного organization ID создаётся inferred-tenant trigger: организации всех tenant-owned parents должны совпасть. Composite `(organization_id,id)` FK сохраняются там, где они уже являются естественным ключом.

Application authorization не заменяет эти ограничения. Ошибка cross-organization reference имеет SQLSTATE `23514`, транслируется в `422 TENANT_REFERENCE_MISMATCH` и записывается в security audit без раскрытия чужого ID.

## 9. Audit, history и purge

`governance.audit_entries`, `governance.object_history` и `governance.history_redactions` являются append-only. Runtime role имеет только `SELECT`; специализированный writer — `INSERT, SELECT`; `UPDATE`, `DELETE`, `TRUNCATE` отозваны. Дополнительные triggers отклоняют mutation даже при ошибочном grant.

`governance.object_history_version_keys` гарантирует уникальность `(organization_id, object_id, object_version)` независимо от partition key. Повтор версии отклоняется.

Purge:

1. проверяет retention, legal hold, текущую версию и permission;
2. создаёт `ObjectPurgeRequested`;
3. worker очищает доменные PII и создаёт `object_tombstone`;
4. history остаётся append-only;
5. PII скрывается append-only записью `history_redactions`, которую применяет read projection;
6. UUID и последняя версия никогда не переиспользуются.

## 10. Change feed и bootstrap snapshot

Канонический механизм — асинхронная idempotent projection из `governance.domain_events`.

Business transaction записывает domain state, audit/history, domain event и outbox. Она не пишет `sync.change_feed`. Projector вызывает `sync.project_domain_event_change`; unique key `(organization_id, source_event_id, object_id, operation)` исключает дубли при at-least-once delivery.

Bootstrap protocol:

1. сервер открывает repeatable-read transaction;
2. фиксирует `cutSequence=max(change_feed.sequence)` и authorization `scopeVersion`;
3. создаёт `snapshot_session` с expiry;
4. авторизованные datasets материализуются в `snapshot_session_items` со стабильными `(dataset,ordinal)`;
5. страницы читаются только по session/dataset/ordinal, поэтому concurrent writes не меняют snapshot;
6. после последней страницы клиент атомарно заменяет SQLite projections;
7. catch-up выполняется `/sync/changes` строго после `cutSequence`;
8. scope change прекращает session и требует новый bootstrap;
9. `410` означает истечение cursor/session, после чего partial snapshot удаляется.

Dataset order: organization/settings → users/departments → projects/members → tasks/checklists/recurrence → calendar/reminders → CRM → file catalog → tags/links → notifications. Page size: 1–500. Payload snapshot discriminated by `objectType`.

## 11. Worker lease protocol

Outbox, background jobs и reminder occurrences имеют `lock_token`, `lease_expires_at`, heartbeat, attempt count и next attempt.

Claim выполняется `FOR UPDATE SKIP LOCKED`, переводит запись в processing/running/claimed и возвращает token. Heartbeat продлевает только активный token. Complete/fail требует точного token; stale worker получает `40001 ..._LEASE_LOST` и не может завершить перехваченную работу. Истёкший lease допускает reclaim. Retry использует capped exponential backoff. После лимита запись переходит в `dead_letter`.

## 12. Файлы и desktop

- `local_path` всегда связан с owner user и конкретным device;
- `mapped_drive` связан с device и approved network resource;
- `unc_path` не связан с device, но обязан ссылаться на approved network resource;
- normalized path не содержит `..` и находится внутри approved normalized root;
- retired resource не принимается;
- обычная API projection возвращает `displayPath`, а не `rawPath`;
- полный чужой path требует `FileLocation.ReadSensitivePath`;
- desktop probe принимается только от authenticated device текущего пользователя;
- availability хранится в `file_location_device_states` отдельно для каждого device;
- физические файлы не копируются, не удаляются и не читаются сервером.

## 13. Reminder state machine

| Состояние | Требуемые данные | Допустимые переходы |
| --- | --- | --- |
| `scheduled` | `next_trigger_at` | `due`, `snoozed`, `cancelled`, `expired` |
| `due` | due occurrence | `delivered`, `snoozed`, `cancelled`, `expired` |
| `snoozed` | `snoozed_until`, новый `next_trigger_at` | `due`, `cancelled`, `expired` |
| `delivered` | `delivered_at` | terminal |
| `cancelled` | `cancelled_at` | `/reschedule` создаёт новый plan/version |
| `expired` | `expired_at` | terminal |

Occurrence имеет состояния `created → claimed → delivered`; failure возвращает `failed` с next attempt, а десятая ошибка переводит в `dead_letter`. Все deliver/complete операции проверяют lease token и idempotency key.

## 14. Дополнительные исправления

- `/api/v1/today` является отдельным read-model по локальной дате пользователя;
- notification preferences являются единственным владельцем sound/quiet-hours; user settings их не дублируют;
- calendar event поддерживает internal user attendees и CRM contact attendees;
- comment поддерживает `parentCommentId`;
- для contact-company действует один текущий `isPrimary` relation на contact;
- high-volume operational tables имеют retention policy; audit/history partitioned monthly;
- API query/page/limit/string constraints заданы в OpenAPI;
- missing leading FK indexes создаются migration-time только при отсутствии эквивалентного prefix index;
- Project status использует единое значение `planning`.

## 15. ADR Этапа 2.1

### ADR-016. Durable command idempotency

**Decision:** PostgreSQL idempotency record в одной transaction с бизнес-командой.  
**Reason:** безопасный retry после timeout/crash и дедупликация concurrent requests.  
**Consequence:** response должен быть сериализуем и ограничен retention.

### ADR-017. Lifecycle ownership

**Decision:** universal trash только для `core.objects` типов из lifecycle matrix; status-owned и aggregate-child типы имеют отдельные команды.  
**Reason:** исключить orphan rows и фиктивные restore endpoints.  
**Consequence:** desktop capabilities строятся по object type.

### ADR-018. Change feed as event projection

**Decision:** единственный writer change feed — idempotent domain-event projector.  
**Reason:** устранить двойную запись и расхождение последовательностей.  
**Consequence:** realtime latency зависит от outbox lag, который мониторится.

### ADR-019. Stable bootstrap snapshot

**Decision:** materialized short-lived snapshot session с fixed cut и ordinal pages.  
**Reason:** согласованный initial cache при concurrent writes.  
**Consequence:** требуется cleanup и bounded page size.

### ADR-020. Database tenant guards

**Decision:** application scope дополняется composite FK или generated tenant triggers.  
**Reason:** cross-organization связь не должна зависеть от корректности handler.  
**Consequence:** migration validation проверяет покрытие каждого tenant FK.

### ADR-021. Tokenized worker leases

**Decision:** claim/heartbeat/complete используют непереиспользуемый lock token.  
**Reason:** stale worker не должен подтверждать работу после reclaim.  
**Consequence:** worker обязан прекращать side effects при lease loss.

### ADR-022. Per-device file availability

**Decision:** доступность path хранится по `(location,device)`, физический path redacted по умолчанию.  
**Reason:** один device не определяет доступность для других и не должен раскрывать локальные пути.  
**Consequence:** desktop передаёт authenticated probe results.
