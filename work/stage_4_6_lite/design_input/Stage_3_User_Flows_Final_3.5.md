# Stage 3. User Flows

## Нормативная база и границы проверки

Этап 3.1 использует результат Этапа 3.0 и не повторяет полный аудит упаковки. Приоритет источников:

1. `architecture_organizer.md` — финальная концепция и бизнес-состав.
2. `01_core_domain_and_data.md` — ограничения Этапа 1: Windows/WPF, server-authoritative, online-only writes, read-only cache, metadata-only files, OS/SMB ACL, optimistic locking.
3. `06_stage_2_1_normative_corrections.md` — данные, lifecycle, права, recurrence, time model, file locations.
4. `traceability.csv`, `00_MANIFEST.md`, `STAGE_3_SOURCE_INDEX.md`, `02_api_and_concurrency.md` — API, permissions, ошибки, события, sync и сценарии.
5. `Старт UX архитектуры.txt` — решения и идентификаторы Этапа 3.0.

Этап 3.4 использует нормативный OpenAPI 3.1 `1.2.0-stage2.2` (241 операций, 232 schemas, 1322 DTO fields), `dto_field_catalog.csv`, `Search_Contract.md`, catalogs permissions/errors и validation/codegen reports. Field-level проверка завершена; нормативная трассировка находится в `Stage_3_Field_Traceability.csv`.

**Всего flows:** 37. Для каждого определён основной путь, альтернативы, API, permission и результат.

## Каталог

| ID | Flow | Роли | Permission | API | Outcome |
| --- | --- | --- | --- | --- | --- |
| FLOW-001 | Первый вход | Все роли | Anonymous → Authenticated → Sync.Read | auth/login; system/version; capabilities; sync/bootstrap/changes/ack; realtime/negotiate | Active session, authorized cache, Today |
| FLOW-002 | Обычный вход | Все роли | Authenticated; Sync.Read | auth/refresh; auth/session; sync/changes/ack | Shell restored without stale unauthorized data |
| FLOW-003 | Истечение сессии | Все роли | Authenticated | auth/refresh; auth/session; logout | Secure return to login |
| FLOW-004 | Создание задачи | Admin/Manager/Employee with capability | Task.Create + related capabilities | POST /tasks + optional related endpoints | Versioned Task created |
| FLOW-005 | Быстрое создание задачи | Admin/Manager/Employee | Task.Create | POST /tasks | Minimal valid task |
| FLOW-006 | Назначение исполнителя | Task editor with permission | Task.Assign | PUT /tasks/{id}/assignees | Authoritative assignment |
| FLOW-007 | Завершение задачи | Assignee/Manager/Admin with capability | Task.ChangeStatus | POST /tasks/{id}/transition; notification action | Business completion, active lifecycle |
| FLOW-008 | Изменение статуса | Allowed editor | Task.ChangeStatus | POST /tasks/{id}/transition | Status changed without overwriting fields |
| FLOW-009 | Создание подзадачи | Task editor | Task.Create/Task.Update | POST /tasks | One-level child Task |
| FLOW-010 | Повторяющаяся задача | Allowed editor | Task.ManageRecurrence | POST /recurrence-series; POST preview | Active series + deterministic occurrences |
| FLOW-011 | Изменить один occurrence | Allowed editor | Task.ManageRecurrence | POST /recurrence-series/{id}/apply-change | Single occurrence safely diverged |
| FLOW-012 | Изменить future/all series | Allowed editor | Task.ManageRecurrence | POST /recurrence-series/{id}/apply-change | Series changed without silent exception overwrite |
| FLOW-013 | Создание проекта | Admin/Manager/allowed Employee | Project.Create | POST /projects | Project created |
| FLOW-014 | Добавление участника | Owner/Manager | Project.ManageMembers | Project member/role/override endpoints | Membership and authorization updated |
| FLOW-015 | Открытие файла | Metadata reader with open permission | FileReference.Open | GET locations; POST resolve-location; POST check-result | OS opened file or explicit diagnosis |
| FLOW-016 | Недоступный файл | File reader | FileReference.Open; optional FileLocation.Update | GET locations; POST check-result | No destructive change; clear recovery |
| FLOW-017 | Перепривязка файла | Location editor | FileLocation.Update | PATCH location or POST alternative; check-result | Metadata updated; physical file untouched |
| FLOW-018 | Создание контакта | CRM editor | Contact.Create/Update | POST /contacts + channels/company links | Contact created in allowed scope |
| FLOW-019 | Глобальный поиск | Все роли | Search.Use | GET /search/suggestions; GET /search | Allowed target or explicit zero/partial result |
| FLOW-020 | Действие из notification | Recipient | Notification.ManageOwn + target permission | POST /notifications/{id}/action + target API | Action confirmed or safely rejected |
| FLOW-021 | Snooze reminder | Recipient | Reminder.ManageOwn | POST /reminders/{id}/snooze | New due time persisted |
| FLOW-022 | Потеря сервера | Все logged-in | Authenticated | Health/sync/realtime; no business writes | Honest read-only/degraded mode |
| FLOW-023 | Работа в read-only cache | Все роли | Last-known read only | Local cache; Windows file access | Inspect prior data without freshness claim |
| FLOW-024 | Восстановление соединения | Все роли | Sync.Read | auth/session; sync/changes/ack/bootstrap; capabilities | Current authorized cache and online writes |
| FLOW-025 | Optimistic conflict | Any editor | Original command permission | Target GET + original command | No silent overwrite |
| FLOW-026 | Архивирование | Authorized user | Object archive/update permission | Object archive endpoint | Archived lifecycle, not deleted |
| FLOW-027 | Перемещение в корзину | Authorized user | Object Delete permission | Object DELETE endpoint | Trashed with retention |
| FLOW-028 | Восстановление из корзины | Authorized user | Trash.Restore/object Restore | POST /trash/{id}/restore or object-specific | Active object restored |
| FLOW-029 | Блокировка пользователя | Admin | User.Block | POST /users/{id}/block + session effects | Account blocked, sessions revoked, audit |
| FLOW-030 | Изменение permissions | Admin/Project owner/manager | Role.Manage/Project.ManageMembers | Role/project member override APIs; sync/capabilities | No stale unauthorized data/actions |
| FLOW-031 | Создание CalendarEvent | Authorized user | CalendarEvent.Create/Update | POST /calendar-events; attendees; reminders | CalendarEvent created, distinct from Task |
| FLOW-032 | Drag/resize календаря | Authorized editor | Relevant update permission | POST task move; PATCH event | Explicit time model updated |
| FLOW-033 | Массовая смена статуса | Manager/Employee with capability | Task.ChangeStatus | POST /tasks/bulk-transition | Transparent batch result |
| FLOW-034 | Inbox capture и conversion | Admin/Manager/Employee | Inbox.ManageOwn + target create | Inbox create/update/convert | Auditable target conversion |
| FLOW-035 | Завершение и архивирование проекта | Owner/Manager | Project.Update/Archive | Project update/transition/archive | Completion and archive remain distinct |
| FLOW-036 | Добавить альтернативный FileLocation | Location editor | FileLocation.Update | POST locations; resolve/check | Additional path without replacing old |
| FLOW-037 | Создать Interaction | CRM editor | Interaction.Create/Update | POST /interactions; PUT participants | Manual interaction history |

## FLOW-001. Первый вход

**Роли:** Все роли  
**Предусловия:** Installed client; account active; server address known  
**Permission:** `Anonymous → Authenticated → Sync.Read`  
**API:** `auth/login; system/version; capabilities; sync/bootstrap/changes/ack; realtime/negotiate`  
**Состояния/ошибки:** INVALID_CREDENTIALS; ACCOUNT_BLOCKED; CLIENT_VERSION_UNSUPPORTED; SYNC errors  
**Результат:** Active session, authorized cache, Today

### Основной путь

1. Проверить server/version
2. Ввести login/password/device name
3. Создать session/device и получить capabilities
4. Bootstrap cache
5. Открыть Today и realtime

### Альтернативные и негативные пути

- Server unavailable → SCR-002
- Blocked/locked → safe message
- Unsupported client → SCR-007
- Bootstrap failure → retry, no writable empty shell

## FLOW-002. Обычный вход

**Роли:** Все роли  
**Предусловия:** Secure refresh token; device active  
**Permission:** `Authenticated; Sync.Read`  
**API:** `auth/refresh; auth/session; sync/changes/ack`  
**Состояния/ошибки:** SESSION_EXPIRED; SESSION_REVOKED; SYNC_CURSOR_EXPIRED; SYNC_SCOPE_CHANGED  
**Результат:** Shell restored without stale unauthorized data

### Основной путь

1. Refresh session
2. Check account/device/capabilities
3. Apply changes after cursor
4. Restore last safe route

### Альтернативные и негативные пути

- Refresh revoked → Login
- Cursor expired → bootstrap
- Scope changed → purge + bootstrap

## FLOW-003. Истечение сессии

**Роли:** Все роли  
**Предусловия:** Open app; session invalidated  
**Permission:** `Authenticated`  
**API:** `auth/refresh; auth/session; logout`  
**Состояния/ошибки:** SESSION_EXPIRED; SESSION_REVOKED; REFRESH_TOKEN_REUSE; DEVICE_REVOKED  
**Результат:** Secure return to login

### Основной путь

1. Block new commands
2. Cancel pending requests
3. Clear tokens/cache according to reason
4. Show SCR-006 and login

### Альтернативные и негативные пути

- Refresh succeeds after idle expiry → continue
- Device revoked → full device cleanup
- Dirty draft stays only in memory; never queued

## FLOW-004. Создание задачи

**Роли:** Admin/Manager/Employee with capability  
**Предусловия:** Online; Task.Create in selected scope  
**Permission:** `Task.Create + related capabilities`  
**API:** `POST /tasks + optional related endpoints`  
**Состояния/ошибки:** VALIDATION_FAILED; FORBIDDEN; TIMEOUT; IDEMPOTENCY_KEY_REUSED  
**Результат:** Versioned Task created

### Основной путь

1. Open full editor
2. Fill core/time/people/project groups
3. Validate distinct date/start/duration/deadline/reminder
4. POST with Idempotency-Key
5. Open confirmed version

### Альтернативные и негативные пути

- Forbidden project → choose allowed target
- Validation → focus field
- Timeout → same-key safe retry/read
- Offline → save disabled, no queue

## FLOW-005. Быстрое создание задачи

**Роли:** Admin/Manager/Employee  
**Предусловия:** Online; Task.Create  
**Permission:** `Task.Create`  
**API:** `POST /tasks`  
**Состояния/ошибки:** VALIDATION_FAILED; FORBIDDEN; DATABASE_UNAVAILABLE  
**Результат:** Minimal valid task

### Основной путь

1. Ctrl+N
2. Enter title and context defaults
3. POST minimal task
4. Return focus; optionally open card

### Альтернативные и негативные пути

- Empty title → inline error
- Complex fields → open full editor
- Current context forbidden → clear or cancel

## FLOW-006. Назначение исполнителя

**Роли:** Task editor with permission  
**Предусловия:** Task visible; online  
**Permission:** `Task.Assign`  
**API:** `PUT /tasks/{id}/assignees`  
**Состояния/ошибки:** FORBIDDEN; VERSION_CONFLICT; OBJECT_NOT_VISIBLE  
**Результат:** Authoritative assignment

### Основной путь

1. Open people dialog
2. Load eligible scoped users
3. Choose assignees/primary
4. PUT with If-Match
5. Refresh history/capabilities

### Альтернативные и негативные пути

- Inactive/hidden target → remove
- Conflict → people diff
- No capability → hidden/disabled by state

## FLOW-007. Завершение задачи

**Роли:** Assignee/Manager/Admin with capability  
**Предусловия:** Task nonterminal  
**Permission:** `Task.ChangeStatus`  
**API:** `POST /tasks/{id}/transition; notification action`  
**Состояния/ошибки:** INVALID_STATE_TRANSITION; VERSION_CONFLICT  
**Результат:** Business completion, active lifecycle

### Основной путь

1. Invoke Complete/transition
2. Send narrow command
3. Server sets completed_at/version
4. Reposition item by current filter; do not archive

### Альтернативные и негативные пути

- Already completed → refresh current
- Review required → only review transition
- Conflict → current allowed actions

## FLOW-008. Изменение статуса

**Роли:** Allowed editor  
**Предусловия:** Allowed transition  
**Permission:** `Task.ChangeStatus`  
**API:** `POST /tasks/{id}/transition`  
**Состояния/ошибки:** FORBIDDEN; INVALID_STATE_TRANSITION; VERSION_CONFLICT  
**Результат:** Status changed without overwriting fields

### Основной путь

1. Open status menu with server transitions
2. Choose target/reason if required
3. Send narrow command
4. Refresh row/card/history

### Альтернативные и негативные пути

- Stale capability → server deny and refresh
- Concurrent state → show current transitions
- Offline → disabled

## FLOW-009. Создание подзадачи

**Роли:** Task editor  
**Предусловия:** Parent active; depth 0  
**Permission:** `Task.Create/Task.Update`  
**API:** `POST /tasks`  
**Состояния/ошибки:** SUBTASK_DEPTH_EXCEEDED; OBJECT_ARCHIVED; FORBIDDEN  
**Результат:** One-level child Task

### Основной путь

1. Add subtask
2. Enter minimal fields
3. POST with parentTaskId
4. Refresh parent children/history

### Альтернативные и негативные пути

- Parent is subtask → create sibling
- Parent archived/trashed → disabled
- Child scope forbidden → explain

## FLOW-010. Повторяющаяся задача

**Роли:** Allowed editor  
**Предусловия:** Task.ManageRecurrence; online  
**Permission:** `Task.ManageRecurrence`  
**API:** `POST /recurrence-series; POST preview`  
**Состояния/ошибки:** RECURRENCE_RULE_INVALID; TIMEOUT  
**Результат:** Active series + deterministic occurrences

### Основной путь

1. Create/choose template
2. Set local rule/timezone/termination
3. Preview occurrences
4. Create series
5. Show materialized horizon

### Альтернативные и негативные пути

- Invalid rule/DST → correct preview
- Missing termination if required → validation
- Timeout → idempotent retry/read

## FLOW-011. Изменить один occurrence

**Роли:** Allowed editor  
**Предусловия:** Occurrence visible  
**Permission:** `Task.ManageRecurrence`  
**API:** `POST /recurrence-series/{id}/apply-change`  
**Состояния/ошибки:** VERSION_CONFLICT; RECURRENCE_OCCURRENCE_EXISTS  
**Результат:** Single occurrence safely diverged

### Основной путь

1. Edit occurrence
2. Choose «Только эту»
3. Create/update exception and task
4. Mark occurrence as exception

### Альтернативные и негативные пути

- Occurrence independently changed → conflict
- Deleted/completed → current lifecycle actions only

## FLOW-012. Изменить future/all series

**Роли:** Allowed editor  
**Предусловия:** Series active/paused  
**Permission:** `Task.ManageRecurrence`  
**API:** `POST /recurrence-series/{id}/apply-change`  
**Состояния/ошибки:** VERSION_CONFLICT; RECURRENCE_RULE_INVALID  
**Результат:** Series changed without silent exception overwrite

### Основной путь

1. Edit series/occurrence
2. Choose current+future or all
3. Show affected instances/exceptions
4. Apply; split series for future scope
5. Track background materialization

### Альтернативные и негативные пути

- Modified exceptions preserved
- Conflict → reload
- Large operation → background status

## FLOW-013. Создание проекта

**Роли:** Admin/Manager/allowed Employee  
**Предусловия:** Project.Create; online  
**Permission:** `Project.Create`  
**API:** `POST /projects`  
**Состояния/ошибки:** DUPLICATE_RESOURCE; VALIDATION_FAILED; FORBIDDEN  
**Результат:** Project created

### Основной путь

1. Open project editor
2. Enter name/owner/manager/dates
3. POST with idempotency
4. Open overview
5. Optionally add members

### Альтернативные и негативные пути

- Duplicate → open/rename
- Inactive owner → validation
- Offline → disabled

## FLOW-014. Добавление участника

**Роли:** Owner/Manager  
**Предусловия:** Project.ManageMembers  
**Permission:** `Project.ManageMembers`  
**API:** `Project member/role/override endpoints`  
**Состояния/ошибки:** DUPLICATE_RESOURCE; FORBIDDEN; SYNC_SCOPE_CHANGED  
**Результат:** Membership and authorization updated

### Основной путь

1. Open Members
2. Search eligible user
3. Choose project role/overrides
4. POST membership
5. Refresh scope/capabilities

### Альтернативные и негативные пути

- Duplicate → open existing
- Sole owner cannot be removed
- Blocked user rejected
- Current user loses access → navigate out after purge

## FLOW-015. Открытие файла

**Роли:** Metadata reader with open permission  
**Предусловия:** CatalogItem visible  
**Permission:** `FileReference.Open`  
**API:** `GET locations; POST resolve-location; POST check-result`  
**Состояния/ошибки:** FILE_NO_LOCATION; FILE_NOT_FOUND; FILE_ACCESS_DENIED; NETWORK_RESOURCE_UNAVAILABLE; UNSAFE_FILE_TYPE  
**Результат:** OS opened file or explicit diagnosis

### Основной путь

1. Request authorized locations
2. Desktop resolve current-device candidate
3. Probe path
4. Show selected path and Shell open
5. Send redacted check result

### Альтернативные и негативные пути

- No location → recovery
- Not found/denied/network → categorized recovery
- Unsafe path/type → do not launch
- Choose alternative

## FLOW-016. Недоступный файл

**Роли:** File reader  
**Предусловия:** Probe/open failed  
**Permission:** `FileReference.Open; optional FileLocation.Update`  
**API:** `GET locations; POST check-result`  
**Состояния/ошибки:** FILE_NOT_FOUND; FILE_ACCESS_DENIED; NETWORK_RESOURCE_UNAVAILABLE  
**Результат:** No destructive change; clear recovery

### Основной путь

1. Classify failure
2. Show path scope/owner and alternatives
3. Retry or choose alternative
4. Offer relink only with capability

### Альтернативные и негативные пути

- Access denied → external ACL owner
- Network unavailable → preserve record
- Other-device local path → show device, redact path

## FLOW-017. Перепривязка файла

**Роли:** Location editor  
**Предусловия:** FileLocation.Update; online  
**Permission:** `FileLocation.Update`  
**API:** `PATCH location or POST alternative; check-result`  
**Состояния/ошибки:** UNSAFE_PATH; VERSION_CONFLICT; FORBIDDEN  
**Результат:** Metadata updated; physical file untouched

### Основной путь

1. Open relink
2. Choose path via native picker
3. Choose replace/add alternative
4. Validate path + If-Match
5. Probe and update history/ranking

### Альтернативные и негативные пути

- Wrong device scope → correct
- Unsafe path → preserve old
- Conflict → manual path/priority resolution

## FLOW-018. Создание контакта

**Роли:** CRM editor  
**Предусловия:** Contact.Create; online  
**Permission:** `Contact.Create/Update`  
**API:** `POST /contacts + channels/company links`  
**Состояния/ошибки:** DUPLICATE_RESOURCE; VALIDATION_FAILED; OBJECT_NOT_VISIBLE  
**Результат:** Contact created in allowed scope

### Основной путь

1. Open editor
2. Enter identity/channels/company
3. Create Contact
4. Add child relations as API permits
5. Open card and optional link

### Альтернативные и негативные пути

- Duplicate warning → inspect existing
- Validation → focus field
- Hidden company → create without link/cancel

## FLOW-019. Глобальный поиск

**Роли:** Все роли  
**Предусловия:** Search.Use  
**Permission:** `Search.Use`  
**API:** `GET /search/suggestions; GET /search`  
**Состояния/ошибки:** DATABASE_UNAVAILABLE; OBJECT_NOT_VISIBLE  
**Результат:** Allowed target or explicit zero/partial result

### Основной путь

1. Ctrl+Shift+F
2. Debounced grouped results
3. Keyboard select
4. Open deep link
5. Back restores query/selection

### Альтернативные и негативные пути

- Zero → refine
- Offline → partial cache banner
- Target becomes hidden → neutral unavailable

## FLOW-020. Действие из notification

**Роли:** Recipient  
**Предусловия:** Notification own; target action capability  
**Permission:** `Notification.ManageOwn + target permission`  
**API:** `POST /notifications/{id}/action + target API`  
**Состояния/ошибки:** SESSION_EXPIRED; FORBIDDEN; VERSION_CONFLICT; INVALID_STATE_TRANSITION  
**Результат:** Action confirmed or safely rejected

### Основной путь

1. Click toast action
2. Activate single instance
3. Server rechecks permission/version/state
4. Execute target command
5. Show result and update target/notification

### Альтернативные и негативные пути

- Session expired → login then open, no blind replay

## Stage 3.5 — FLOW-019 уточнён: сотрудники в глобальном поиске

**Trigger:** Ctrl+Shift+F или shell search.  
**Предусловия:** authenticated; `Search.Use`.  
**API/DTO:** `GET /api/v1/search`; `SearchPage`, `SearchSuggestion`, `EmployeeSearchResult`.  
**Результат:** отдельная permission-safe Employees group либо явный empty/filtered/partial state.

### Employee-only и mixed path

1. При employee-only клиент отправляет `types=employee` с допустимыми `q`, `departments`, `cursor`, `limit`.
2. Сервер авторизует, редактирует, сортирует и фильтрует до pagination.
3. UI показывает `displayName`, optional department/job title, `accountStatus`; avatar отсутствует.
4. Up/Down выбирают результат, Enter открывает `deepLink`; next page использует только server cursor.
5. Mixed request с employee показывает отдельную Employees group, сохраняя server order.

### Ошибки и восстановление

- `isRedacted=true` → nullable fields скрыты нейтрально (`STATE-030`).
- Blocked employee → сервер опускает запись без disclosure, если нет `User.Block`.
- `FORBIDDEN` → операция недоступна; stale data не выдаётся за fresh.
- `SEARCH_CURSOR_INVALID/EXPIRED` → сохранить filters, удалить cursor, повторить page 1 (`STATE-026/027`).
- Empty/filtered empty сохраняют корректный query/filter context.
- Server unavailable → cache-only banner + Retry; клиент не дополняет страницы постфильтрацией.
- Target стал недоступен → neutral unavailable, sensitive details удалены, focus возвращён в выдачу.

## FLOW-035. Управление организационной шкалой срочности

**Trigger:** SCR-153 → «Шкала срочности».  
**Предусловия:** GET — `Settings.ReadOwn`; изменение/reset — `System.Configure`.  
**API:** GET/PUT `/api/v1/settings/notification-urgency-scale`; POST `/reset`.  
**DTO:** `NotificationUrgencyScale`, `NotificationUrgencyScalePatch`, `UrgencyScaleInterval`, `UrgencyLevel`.  
**Результат:** server-confirmed organization scale и новый ETag.

### Основной путь и reset

1. GET загружает четыре interval rows и ETag.
2. UI показывает level, inclusive min/max, `displayToken`, non-color preview и read-only metadata.
3. Клиент проверяет 0–100, order, contiguity и overlap.
4. Save отправляет полный массив через PUT с `If-Match` и новым `Idempotency-Key`.
5. Reset подтверждает defaults 0–24/25–49/50–74/75–100 и вызывает POST `/reset` с теми же concurrency headers.
6. Успех атомарно заменяет draft ответом/ETag; presentation всех notifications обновляется, semantic urgency неизменна.

### Ошибки и восстановление

- Overlap/order/gap/out-of-range → `STATE-007`, focus first invalid, draft сохранён.
- Нет `System.Configure` → read-only с объяснением; server `FORBIDDEN` authoritative.
- `VERSION_CONFLICT` → `STATE-014`: preserve draft, load current, compare/reapply/discard.
- Нет `If-Match` → `STATE-025`: GET current, no blind retry.
- Server unavailable → cached read-only, Retry, без offline write queue.
- Старый client 2.2 использует встроенный mapping.
- Target changed → current state
- Offline → read-only open, no queue

## FLOW-021. Snooze reminder

**Роли:** Recipient  
**Предусловия:** Reminder.ManageOwn; online  
**Permission:** `Reminder.ManageOwn`  
**API:** `POST /reminders/{id}/snooze`  
**Состояния/ошибки:** VALIDATION_FAILED; INVALID_STATE_TRANSITION; DATABASE_UNAVAILABLE  
**Результат:** New due time persisted

### Основной путь

1. Choose preset/custom time
2. POST snooze with idempotency
3. Close toast after confirmation
4. Refresh upcoming reminders

### Альтернативные и негативные пути

- Invalid/past time → correct
- Already dismissed/expired → current state
- Offline → no server snooze; local toast may close only locally

## FLOW-022. Потеря сервера

**Роли:** Все logged-in  
**Предусловия:** App online then network/server lost  
**Permission:** `Authenticated`  
**API:** `Health/sync/realtime; no business writes`  
**Состояния/ошибки:** DATABASE_UNAVAILABLE; DEPENDENCY_UNAVAILABLE; MAINTENANCE_MODE; STORAGE_FULL  
**Результат:** Honest read-only/degraded mode

### Основной путь

1. Detect bounded timeout/readiness failure
2. Show persistent status and last sync
3. Disable all business writes
4. Keep cached navigation and available local file open

### Альтернативные и негативные пути

- Realtime only lost but REST works → polling degraded, writes allowed
- Maintenance/storage full/DB down → specific state
- No cache → unavailable, not empty

## FLOW-023. Работа в read-only cache

**Роли:** Все роли  
**Предусловия:** Server unavailable; cache exists  
**Permission:** `Last-known read only`  
**API:** `Local cache; Windows file access`  
**Состояния/ошибки:** ReadOnlyCache; StaleData  
**Результат:** Inspect prior data without freshness claim

### Основной путь

1. Navigate cached routes
2. Show stale timestamp globally
3. Search cache with partial label
4. Open local/reachable file without metadata write

### Альтернативные и негативные пути

- Object not cached → unavailable
- Permissions revalidated after reconnect
- User may copy draft text, but no durable pending command

## FLOW-024. Восстановление соединения

**Роли:** Все роли  
**Предусловия:** Read-only/degraded; server returns  
**Permission:** `Sync.Read`  
**API:** `auth/session; sync/changes/ack/bootstrap; capabilities`  
**Состояния/ошибки:** SYNC_CURSOR_EXPIRED; SYNC_SCOPE_CHANGED; SESSION_REVOKED  
**Результат:** Current authorized cache and online writes

### Основной путь

1. Authenticate/refresh
2. Fetch changes after durable cursor
3. Apply batch atomically incl. tombstones
4. Refresh capabilities/visible routes
5. Enable writes only after sync

### Альтернативные и негативные пути

- Cursor expired → bootstrap
- Scope changed → purge first
- No offline write conflicts because queue absent
- Target unavailable → neutral state

## FLOW-025. Optimistic conflict

**Роли:** Any editor  
**Предусловия:** Stale If-Match  
**Permission:** `Original command permission`  
**API:** `Target GET + original command`  
**Состояния/ошибки:** VERSION_CONFLICT; OBJECT_DELETED; OBJECT_ARCHIVED; INVALID_STATE_TRANSITION  
**Результат:** No silent overwrite

### Основной путь

1. Keep local draft in memory
2. Fetch current object/changed fields
3. Compare base/server/local
4. User reloads/manual resolves/reapplies
5. Retry with new If-Match

### Альтернативные и негативные пути

- Different fields → suggest, require confirmation
- Same field/participants/path/status → never auto-merge
- Completed/archived/trashed/deleted → state-specific actions
- Already applied idempotent command → recognize current state

## FLOW-026. Архивирование

**Роли:** Authorized user  
**Предусловия:** Object active; archive capability  
**Permission:** `Object archive/update permission`  
**API:** `Object archive endpoint`  
**Состояния/ошибки:** INVALID_STATE_TRANSITION; VERSION_CONFLICT  
**Результат:** Archived lifecycle, not deleted

### Основной путь

1. Explain read-only/exclusion effect
2. Confirm archive with version
3. Move to Archive projection
4. Keep links/history according to access

### Альтернативные и негативные пути

- Invalid business state → blockers
- Concurrent change → conflict
- Project active work → warning, no cascade

## FLOW-027. Перемещение в корзину

**Роли:** Authorized user  
**Предусловия:** Delete capability; active/archived object  
**Permission:** `Object Delete permission`  
**API:** `Object DELETE endpoint`  
**Состояния/ошибки:** VERSION_CONFLICT; FORBIDDEN  
**Результат:** Trashed with retention

### Основной путь

1. Explain metadata consequences/links
2. Catalog message says physical file unaffected
3. DELETE with version/idempotency
4. Remove from active; show in Trash

### Альтернативные и негативные пути

- Nonempty virtual folder → descendant metadata summary
- Conflict/permission change → preserve state
- UserAccount uses block/deactivate, not trash

## FLOW-028. Восстановление из корзины

**Роли:** Authorized user  
**Предусловия:** Object trashed; restore capability  
**Permission:** `Trash.Restore/object Restore`  
**API:** `POST /trash/{id}/restore or object-specific`  
**Состояния/ошибки:** DUPLICATE_RESOURCE; OBJECT_NOT_VISIBLE; FORBIDDEN  
**Результат:** Active object restored

### Основной путь

1. Open restore dialog
2. Server validates parent/uniqueness/relations/permission
3. Resolve name/parent conflict
4. Restore and navigate

### Альтернативные и негативные пути

- Parent purged/hidden → choose allowed parent
- Duplicate → rename/open existing
- Policy/legal restriction → no restore

## FLOW-029. Блокировка пользователя

**Роли:** Admin  
**Предусловия:** User.Block; target active  
**Permission:** `User.Block`  
**API:** `POST /users/{id}/block + session effects`  
**Состояния/ошибки:** INVALID_STATE_TRANSITION; FORBIDDEN; VERSION_CONFLICT  
**Результат:** Account blocked, sessions revoked, audit

### Основной путь

1. Show immediate session impact
2. Confirm block reason if required
3. Block account and revoke sessions
4. Affected client shows session interruption

### Альтернативные и негативные пути

- Self/last-admin guard
- Already blocked → current state
- Deactivate selected explicitly as different lifecycle

## FLOW-030. Изменение permissions

**Роли:** Admin/Project owner/manager  
**Предусловия:** Role.Manage or Project.ManageMembers  
**Permission:** `Role.Manage/Project.ManageMembers`  
**API:** `Role/project member override APIs; sync/capabilities`  
**Состояния/ошибки:** SYNC_SCOPE_CHANGED; FORBIDDEN; VERSION_CONFLICT  
**Результат:** No stale unauthorized data/actions

### Основной путь

1. Edit role/membership permissions
2. Show impacted scope
3. Save with version
4. Server increments scope version
5. Clients purge/bootstrap and rerender

### Альтернативные и негативные пути

- Self critical permission guard
- User loses open-object access → neutral unavailable
- Stale action → server deny + scope refresh

## FLOW-031. Создание CalendarEvent

**Роли:** Authorized user  
**Предусловия:** CalendarEvent.Create  
**Permission:** `CalendarEvent.Create/Update`  
**API:** `POST /calendar-events; attendees; reminders`  
**Состояния/ошибки:** VALIDATION_FAILED; VERSION_CONFLICT  
**Результат:** CalendarEvent created, distinct from Task

### Основной путь

1. Select time slot and Event
2. Enter title/start/end/timezone/attendees/reminders
3. Save
4. Refresh range and schedule notifications

### Альтернативные и негативные пути

- Choose Task instead → Task editor
- Overlap → warn but allow
- Invalid end/timezone → field error

## FLOW-032. Drag/resize календаря

**Роли:** Authorized editor  
**Предусловия:** Task.Update or CalendarEvent.Update  
**Permission:** `Relevant update permission`  
**API:** `POST task move; PATCH event`  
**Состояния/ошибки:** VERSION_CONFLICT; FORBIDDEN  
**Результат:** Explicit time model updated

### Основной путь

1. Immediate preview + overlap warning
2. Send narrow move/update
3. Commit position/version on success
4. Conflict → rollback and resolver

### Альтернативные и негативные пути

- Keyboard reschedule dialog
- Offline → disabled
- Date-only dropped on timeline gains explicit start/duration

## FLOW-033. Массовая смена статуса

**Роли:** Manager/Employee with capability  
**Предусловия:** 2..100 selected; common transition  
**Permission:** `Task.ChangeStatus`  
**API:** `POST /tasks/bulk-transition`  
**Состояния/ошибки:** REQUEST_TOO_LARGE; INVALID_STATE_TRANSITION; VERSION_CONFLICT  
**Результат:** Transparent batch result

### Основной путь

1. Multi-select
2. Compute capability/transition intersection
3. Show count and exclusions
4. POST batch
5. Update after response

### Альтернативные и негативные пути

- Mixed states → common actions only
- Too large → user reduces selection; no hidden loops
- Conflict → failed items + refresh

## FLOW-034. Inbox capture и conversion

**Роли:** Admin/Manager/Employee  
**Предусловия:** Inbox.ManageOwn  
**Permission:** `Inbox.ManageOwn + target create`  
**API:** `Inbox create/update/convert`  
**Состояния/ошибки:** VALIDATION_FAILED; FORBIDDEN; TIMEOUT  
**Результат:** Auditable target conversion

### Основной путь

1. Ctrl+Shift+N capture
2. Later choose target type
3. Set missing project/date/assignee/location
4. Transactional conversion creates target and marks converted

### Альтернативные и негативные пути

- Invalid URL/path → keep editable
- Target forbidden → choose another
- Timeout → idempotent retry/read target

## FLOW-035. Завершение и архивирование проекта

**Роли:** Owner/Manager  
**Предусловия:** Project Update/Archive  
**Permission:** `Project.Update/Archive`  
**API:** `Project update/transition/archive`  
**Состояния/ошибки:** INVALID_STATE_TRANSITION; VERSION_CONFLICT  
**Результат:** Completion and archive remain distinct

### Основной путь

1. Set business status completed
2. Review active task warnings
3. Keep project visible/readable
4. Archive separately when appropriate

### Альтернативные и негативные пути

- Invariant blocks completion → show blockers
- Reopen completed with permission
- Trash remains separate

## FLOW-036. Добавить альтернативный FileLocation

**Роли:** Location editor  
**Предусловия:** FileLocation.Update  
**Permission:** `FileLocation.Update`  
**API:** `POST locations; resolve/check`  
**Состояния/ошибки:** DUPLICATE_RESOURCE; UNSAFE_PATH; NETWORK_RESOURCE_UNAVAILABLE  
**Результат:** Additional path without replacing old

### Основной путь

1. Open location manager
2. Choose local/network path and scope
3. Validate/store priority
4. Probe current-device availability

### Альтернативные и негативные пути

- Duplicate → open existing
- Local selected as global → force device scope
- Root not allowlisted → admin action, no bypass

## FLOW-037. Создать Interaction

**Роли:** CRM editor  
**Предусловия:** Interaction.Create  
**Permission:** `Interaction.Create/Update`  
**API:** `POST /interactions; PUT participants`  
**Состояния/ошибки:** VALIDATION_FAILED; OBJECT_NOT_VISIBLE  
**Результат:** Manual interaction history

### Основной путь

1. From Contact/Company choose Add Interaction
2. Set type/occurred_at/summary/participants/next step
3. Save
4. Insert in timeline

### Альтернативные и негативные пути

- Hidden participant → remove/cancel
- Past occurred_at allowed; next step validated
- No email/message sent automatically

## Самопроверка flows

| Критерий | Статус |
| --- | --- |
| Первый/обычный вход и session expiry | PASS |
| Task create/quick/assign/status/complete/subtask/recurrence | PASS |
| Project/member/lifecycle | PASS |
| File open/unavailable/relink/multipath | PASS |
| Contact/search/notification/snooze | PASS |
| Server loss/read-only/reconnect/conflict | PASS |
| Archive/trash/restore/block user/permissions | PASS |


## Stage 3.4. Contract-dependent flow amendments

### FLOW-004 / FLOW-005. Task create/edit

- Create serializes `TaskCreate`; `title` is required, 1–500 characters.
- Edit serializes only dirty properties of `TaskPatch`; omitted properties remain unchanged, nullable `null` clears.
- Author and requester/assigner are separate fields; assignees/watchers use their typed operations when edited independently.
- Inline validation uses server field paths. A 412 opens `STATE-014`; 428 opens `STATE-025`.

### FLOW-010–012. Recurrence

- Rule fields come from `RecurrenceSeriesCreate/Patch` and nested `RecurrenceTaskTemplate`.
- Scope is exactly `this_occurrence`, `this_and_future`, `entire_series`.
- Exclusion is the `skip/{{occurrenceKey}}` command with reason/expectedSeriesVersion; no invented exclusions array.

### FLOW-021. Reminder snooze/dismiss/cancel

- Snooze sends `SnoozeRequest.until` and expectedVersion.
- Reschedule uses the operation request DTO from OpenAPI.
- Dismiss and delete/cancel are separate commands; neither changes Task date/time.

### FLOW-031 / FLOW-032. Calendar create, drag and resize

- CalendarEvent create/edit uses all-day/date-time fields exactly as DTO defines.
- Drag/resize is a narrow PATCH with If-Match. UI performs visual rollback on 412 and opens conflict compare.
- Overlap remains a warning; server validation may reject invalid time ordering, not overlap itself.

### FLOW-017 / FLOW-036. FileLocation

- New alternative path creates another `FileLocation`; it does not overwrite other locations.
- Local paths require device binding; UNC/mapped-drive paths use contract locationType/networkResource semantics.
- Redacted rawPath is never reconstructed on the client.

### FLOW-018. Contact/Company

- Core cards, channels, addresses and contact-company roles use separate DTO/operations.
- Primary values are represented by typed fields; duplicate/hidden target errors retain the draft.

### FLOW-019. Search

1. Serialize `q`, `types`, `projectIds`, `userIds`, `departments`, `contactIds`, `hasFiles`, `lifecycle`, `from`, `to`, `limit`.
2. Any filter change clears `cursor` before the request.
3. Append only the server-returned authorized page.
4. On `SEARCH_CURSOR_INVALID` or `SEARCH_CURSOR_EXPIRED`, keep query/filters, clear results/cursor as appropriate, and restart from page 1.
5. Client-side post-filtering of paged results is prohibited.

### FLOW-029 / FLOW-030. Administration

- User create/edit, activation/block/deactivation, role replacement, device/session revoke, departments and network resources use distinct permissions and DTOs.
- Read-only IDs/versions/timestamps are never editable; write-only security values are never redisplayed.
