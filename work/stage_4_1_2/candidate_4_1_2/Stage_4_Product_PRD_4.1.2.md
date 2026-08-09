# Stage 4. Product PRD

**Статус:** Candidate для независимого аудита 4.2  
**Версия:** 4.1.2-candidate.1  
**Дата:** 2026-07-26  
**Продукт:** Windows desktop-органайзер одной компании

## 1. Product summary

Система объединяет задачи, расписание, проекты, каталог ссылок на рабочие файлы, контакты и совместную работу в локальной инфраструктуре одной компании. Windows desktop-клиент работает с локальным application server; PostgreSQL является хранилищем структурированных данных, а физические рабочие файлы остаются на локальных дисках, SMB/NAS и файловых серверах.

Целевые пользователи: системный администратор, руководитель, сотрудник и наблюдатель. Основная ценность: связать в одном рабочем контексте задачу, дату, проект, человека и доступные файлы, сохранив данные внутри инфраструктуры компании.

Ключевые ограничения: server-authoritative writes, online-only изменение общих данных, encrypted disposable read-cache, hybrid authorization, optimistic locking, metadata-only file model и OS/SMB ACL для физического доступа.

## 2. Цели MVP

### 2.1. Пользовательские цели

- Войти по отдельной учётной записи и работать только с разрешёнными объектами.
- Планировать, назначать, выполнять и проверять задачи в Today и Calendar.
- Управлять проектами, контактами и ссылками на файлы без потери связи между объектами.
- Получать напоминания/уведомления и безопасно выполнять действия из Windows toast.
- Восстанавливать ошибочно удалённые metadata и видеть историю изменений.

### 2.2. Операционные цели

- Развернуть один локальный сервер и Windows-клиенты без внешнего cloud dependency.
- Дать администратору bounded UI для пользователей, прав, health, backup status и restore verification.
- Обеспечить диагностируемое поведение при outage, storage full, maintenance, file/SMB failures.

### 2.3. Технические цели

- Сохранить сервер единственным источником истины и исключить silent last-write-wins.
- Поддержать incremental sync/realtime invalidation и авторизационную очистку кэша.
- Реализовать все 244 операции OpenAPI 1.2.0-stage2.3 без изменения 91 permissions и 44 stable errors.

### 2.4. Критерии успешного внедрения

Успех определяется прохождением сквозного сценария концепции: администратор создаёт сотрудника; руководитель создаёт проект и задачу; сотрудник видит и изменяет Task; обновление появляется у руководителя; Task виден в Calendar/Today; reminder создаёт notification; контакт и файл связываются с рабочим объектом; history фиксирует изменения; Trash restore работает; outage отображается и после reconnect cache становится актуальным. Бизнес-метрики adoption/ROI не задаются, поскольку их нет в канонических источниках.

## 3. Не входит в MVP

Cloud/SaaS, web/mobile clients, публичная регистрация, биллинг, AI, Gantt, встроенный мессенджер и почтовый клиент, видеозвонки, полноценная CRM/deal funnel, automation builder, встроенный редактор Office, содержимое/синхронизация рабочих файлов, автоматический поиск перемещённых файлов, real-time coauthoring, полноценное offline-редактирование и client write queue, микросервисы/Kubernetes, внешние массовые интеграции.

## 4. Пользователи и роли

| Роль | Обязанности | Основные сценарии | Ограничения | Риски неправильного использования |
| --- | --- | --- | --- | --- |
| Администратор | Учётные записи, роли, отделы, устройства, health/backup/audit | Создать/блокировать пользователя; управлять permissions; запускать allowlisted admin jobs | Только explicit capabilities; без shell/SQL/secrets | Privilege escalation, ошибочный restore/purge |
| Руководитель | Планирование и контроль работы | Проекты, участники, назначения, review, сроки | Только доступные проекты/отделы/relations | Избыточное назначение прав, массовые изменения |
| Сотрудник | Выполнение и фиксация работы | Собственные/доступные Task, files, comments, contacts | Нет доступа к скрытым объектам/admin actions | Ошибочная смена статуса/пути/связи |
| Наблюдатель | Контроль доступных объектов | Read-only проекты, задачи, файлы, comments/history | Нет edit capabilities | Попытка трактовать visibility как edit right |

## 5. Общая функциональная модель

| Module | Название | Объекты/ценность | Пользователи | Связи | Ключевые FLOW |
| --- | --- | --- | --- | --- | --- |
| MOD-001 | Авторизация и сессии | Безопасный вход, поддержание и завершение пользовательских сессий. | Все пользователи; администратор для сброса пароля и расследования входов. | MOD-002, MOD-018, MOD-019, MOD-020 | FLOW-001,FLOW-002,FLOW-003 |
| MOD-002 | App shell и навигация | Единая desktop-оболочка, безопасная навигация и глобальные команды. | Все аутентифицированные пользователи. | Все модули | FLOW-002,FLOW-005,FLOW-020 |
| MOD-003 | Сегодня | Главный рабочий экран текущего дня с ограниченными секциями задач, событий и напоминаний. | Все пользователи с Calendar.Read в разрешённом scope. | MOD-005, MOD-008, MOD-009, MOD-015, MOD-020 | FLOW-005,FLOW-007,FLOW-008,FLOW-020,FLOW-021 |
| MOD-004 | Входящие | Быстрый захват информации и последующая транзакционная классификация. | Владелец InboxItem. | MOD-002, MOD-005, MOD-011, MOD-012, MOD-017 | FLOW-034 |
| MOD-005 | Задачи | Управление личными и командными задачами, статусами, сроками, назначениями и связями. | Администратор, руководитель, сотрудник, наблюдатель по capabilities. | MOD-003, MOD-006, MOD-007, MOD-008, MOD-009, MOD-010, MOD-013, MOD-021 | FLOW-004,FLOW-005,FLOW-006,FLOW-007,FLOW-008,FLOW-025,FLOW-033 |
| MOD-006 | Подзадачи и чек-листы | Декомпозиция Task на один уровень подзадач и простые checklist items. | Пользователи с Task.Read/Task.Update/Task.ChangeStatus. | MOD-005, MOD-021 | FLOW-009 |
| MOD-007 | Повторяющиеся задачи | Создание и изменение серий повторяющихся Task с независимыми occurrence. | Пользователи с Task.ManageRecurrence; System.JobRun для административной генерации. | MOD-005, MOD-009, MOD-019, MOD-020 | FLOW-010,FLOW-011,FLOW-012 |
| MOD-008 | Напоминания | Планирование, snooze, dismiss и reschedule напоминаний пользователя. | Получатель с Reminder.ManageOwn. | MOD-003, MOD-005, MOD-009, MOD-015, MOD-018 | FLOW-021 |
| MOD-009 | Календарь | Диапазонное представление Task и CalendarEvent в режимах день, неделя и месяц. | Пользователи с Calendar.Read и объектными capabilities. | MOD-003, MOD-005, MOD-007, MOD-008, MOD-010, MOD-020 | FLOW-031,FLOW-032 |
| MOD-010 | Проекты | Управление проектами, участниками, ролями, задачами и связанными объектами. | Владелец, руководитель, редактор, исполнитель, наблюдатель по capabilities. | MOD-005, MOD-009, MOD-011, MOD-012, MOD-013, MOD-021 | FLOW-013,FLOW-014,FLOW-035 |
| MOD-011 | Файловый каталог | Виртуальный каталог метаданных и безопасное открытие локальных/сетевых файлов по нескольким location. | Пользователи с FileCatalog/FileReference/FileLocation capabilities. | MOD-005, MOD-010, MOD-012, MOD-017, MOD-019, MOD-020 | FLOW-015,FLOW-016,FLOW-017,FLOW-036 |
| MOD-012 | Контакты и компании | Карточки физических лиц и контрагентов с каналами, адресами и связями. | Пользователи с Contact.* capabilities. | MOD-005, MOD-010, MOD-011, MOD-013, MOD-021 | FLOW-018 |
| MOD-013 | Комментарии и взаимодействия | Обсуждение объектов, ручная история взаимодействий, связи и теги. | Пользователи с Comment, Interaction, ObjectLink и Tag capabilities. | MOD-005, MOD-010, MOD-011, MOD-012, MOD-021 | FLOW-037 |
| MOD-014 | Глобальный поиск | Авторизационно-фильтруемый поиск, включая employee как отдельный result type/group, по поддержанным типам и метаданным. | Все пользователи с Search.Use. | Все предметные модули; MOD-020 | FLOW-019 |
| MOD-015 | Уведомления | Центр уведомлений, Windows toast и безопасные действия над target object. | Получатель уведомления. | MOD-005, MOD-008, MOD-009, MOD-010, MOD-013, MOD-018 | FLOW-020 |
| MOD-016 | Архив | Отдельное read-only представление архивных объектов и возврат в active lifecycle. | Пользователи с History.Read и Archive.Restore/object capability. | Предметные модули, MOD-017, MOD-021 | FLOW-026 |
| MOD-017 | Корзина и восстановление | Обратимое удаление metadata, restore и контролируемый purge после retention. | Пользователи с Trash.Read/Restore/Purge и объектными capabilities. | Все удаляемые модули, MOD-016, MOD-021 | FLOW-027,FLOW-028 |
| MOD-018 | Настройки | Профильные, календарные, notification, файловые, device-local settings и organizational urgency scale. | Пользователь; администратор для organization settings. | MOD-001, MOD-002, MOD-008, MOD-015, MOD-019, MOD-020 | FLOW-002,FLOW-003 |
| MOD-019 | Администрирование | Ограниченное управление пользователями, отделами, ролями, устройствами, ресурсами и эксплуатационными операциями. | Администратор и пользователи с конкретными administrative capabilities. | MOD-001, MOD-010, MOD-011, MOD-018, MOD-020, MOD-021 | FLOW-029,FLOW-030 |
| MOD-020 | Синхронизация, read-only и конфликты | Поддержание авторизованного read-cache, восстановление связи и безопасное разрешение optimistic conflicts. | Все аутентифицированные пользователи. | Все модули | FLOW-022,FLOW-023,FLOW-024,FLOW-025,FLOW-030 |
| MOD-021 | Аудит и история | Пользовательская история доступных объектов и защищённый технический аудит. | Пользователи с History.Read; администратор с Audit.ReadAll/SecurityAudit.Read. | Все модули | FLOW-025,FLOW-029,FLOW-030 |

Lifecycle каждого общего объекта отделяет active, archived, trashed и purged; business statuses остаются внутри предметного модуля. Permissions вычисляются сервером по global role, department, project membership/role, ownership/assignee relation, explicit allow/deny и object state. Domain events, outbox, change feed, notifications, history и audit создаются только после успешной транзакции.

## 6. Общие бизнес-правила

| BR ID | Правило | Источник | Verification |
| --- | --- | --- | --- |
| BR-001 | Организация одна; каждая общая запись ограничена organization boundary. | Концепция §3.1; Архитектура A1-11 | AC-001 |
| BR-002 | UserAccount отвечает за аутентификацию, EmployeeProfile — за рабочий профиль. | Этап 2.2 C-03 | AC-002 |
| BR-003 | Сервер является единственным источником истины; локальный cache disposable и read-only при outage. | Архитектура A1-01/A1-03; DEC-006 | AC-003 |
| BR-004 | Любое действие разрешается сервером по RBAC/ReBAC/ABAC, explicit deny сильнее allow, default deny. | ADR-002; DEC-004 | AC-004 |
| BR-005 | Business status, derived state, archive, trash и UI state не смешиваются. | DEC-005 | AC-005 |
| BR-006 | Archived object по умолчанию read-only; Trashed object доступен только для restore/purge. | ADR-011; DEC-023 | AC-006 |
| BR-007 | Удаление metadata рабочего файла не удаляет физический файл. | Концепция §22.2; DEC-018 | AC-007 |
| BR-008 | Физический доступ к файлу определяется Windows/SMB ACL после metadata permission. | Архитектура A1-06; GAP-004 | AC-008 |
| BR-009 | Versioned write требует If-Match; stale version не перезаписывается молча. | ADR-004; DEC-038 | AC-009 |
| BR-010 | Safe retry допускается только для safe/idempotent operation с сохранением request hash/key. | Этап 2.2 idempotency | AC-010 |
| BR-011 | Audit append-only; secrets и неразрешённые sensitive paths редактируются или не записываются. | ADR-009; архитектура | AC-011 |
| BR-012 | Realtime signal является invalidation; durable recovery выполняется через change feed. | ADR-005 | AC-012 |
| BR-013 | Search фильтруется по authorization и filters до pagination; client post-filter запрещён. | DEC-040; Search Contract | AC-013 |
| BR-014 | UI hidden/disabled state не заменяет server-side permission check. | DEC-030 | AC-014 |
| BR-015 | PATCH omitted означает unchanged; explicit null очищает только nullable field. | DEC-037 | AC-015 |

## 7. Общие состояния

| STATE ID | State | Trigger/semantics | UI behavior | Recovery | Stable error | Origin |
| --- | --- | --- | --- | --- | --- | --- |
| STATE-001 | Initial | Route/application context has not started loading | Shell chrome only; no fake data | Start auth/bootstrap | — | Restored from Stage 3.0 |
| STATE-002 | Loading | No usable data; read pending | Skeleton matching final layout | Wait/cancel/retry safe read | — | Restored from Stage 3.0 |
| STATE-003 | Refreshing | Usable data; background read pending | Keep data and selection; subtle progress | Atomic apply or retain old data | — | Restored from Stage 3.0 |
| STATE-004 | Loaded | Authorized usable data loaded | Normal surface and allowed commands | Normal navigation/actions | — | Restored from Stage 3.0 |
| STATE-005 | Empty | Authorized unfiltered result is empty | Purpose-specific empty state | Create/change scope | — | Restored from Stage 3.0 |
| STATE-006 | FilteredEmpty | Query/filter returns no results | Keep filters visible; no hidden counts | Reset/change filters/query | — | Restored from Stage 3.0 |
| STATE-007 | ValidationError | DTO/business validation failed | Keep draft; inline canonical field errors | Correct and resubmit | VALIDATION_FAILED; REQUEST_TOO_LARGE; compatible domain validation errors | Stage 3.5 retained |
| STATE-008 | Forbidden | Server denies known action | Undo preview; disable/hide by capability without disclosure | Reload capabilities/navigate away | FORBIDDEN | Restored from Stage 3.0 |
| STATE-009 | ObjectUnavailable | Object hidden, purged, removed or no longer visible | Neutral unavailable view; remove sensitive detail | Back/search/refresh | OBJECT_NOT_VISIBLE | Restored from Stage 3.0 |
| STATE-010 | ServerUnavailable | Server/readiness/dependency unavailable | Persistent banner; cached read only; writes disabled | Reconnect and sync | DATABASE_UNAVAILABLE; DEPENDENCY_UNAVAILABLE | Restored from Stage 3.0 |
| STATE-011 | ReadOnlyCache | Only authorized local cache is usable | Show freshness timestamp; disable shared writes | Reconnect and sync | — | Restored from Stage 3.0 |
| STATE-012 | Reconnecting | Transport restored but auth/sync incomplete | Remain read-only until verified | Refresh session and incremental/bootstrap sync | — | Restored from Stage 3.0 |
| STATE-013 | SyncPending | Startup/invalidation catch-up pending | Show current data with sync indicator | Apply feed and acknowledge cursor | — | Restored from Stage 3.0 |
| STATE-014 | Conflict | Versioned command uses stale state | Keep draft; compare/reapply/discard | GET current and resend with new ETag | VERSION_CONFLICT | Stage 3.5 retained |
| STATE-015 | StaleData | Displayed data may be outdated | Show freshness and limit claims of completeness | Refresh when online | — | Restored from Stage 3.0 |
| STATE-016 | PartialAccess | Surface contains only currently permitted subset | Show neutral partial-access indication; no hidden counts | Continue or external access request | Filtered response/FORBIDDEN | Restored from Stage 3.0 |
| STATE-017 | Archived | Lifecycle is archived | Read-only banner; lifecycle actions only | Unarchive if allowed | OBJECT_ARCHIVED | Restored from Stage 3.0 |
| STATE-018 | Trashed | Lifecycle is trashed | Tombstone; restore/purge actions only | Restore if allowed | OBJECT_DELETED | Restored from Stage 3.0 |
| STATE-019 | BackgroundOperation | Accepted operation continues asynchronously | Show immutable request/status/progress | Poll/event refresh | 202/background | Restored from Stage 3.0 |
| STATE-020 | RecoverableFailure | Failure allows safe user recovery | Keep usable data/draft and show bounded retry | Retry only when safe | RATE_LIMITED; TIMEOUT; selected domain errors | Restored from Stage 3.0 |
| STATE-021 | UnrecoverableFailure | Current action cannot continue safely | Sanitized error and traceId; no blind retry | Change input/context or contact admin | INTERNAL_ERROR; DATABASE_CONSTRAINT_FAILED; MALFORMED_JSON | Restored from Stage 3.0 |
| STATE-022 | Maintenance | Server reports maintenance | Cached reads allowed; writes blocked | Retry after server instruction | MAINTENANCE_MODE | Restored from Stage 3.0 |
| STATE-023 | ClientUnsupported | Client below supported version | Blocking update route | Install signed supported client | CLIENT_VERSION_UNSUPPORTED | Restored from Stage 3.0 |
| STATE-024 | AccessScopeChanged | Authorization scope version changed | Purge sensitive projections before rendering | Bootstrap new scope | SYNC_SCOPE_CHANGED | Restored from Stage 3.0 |
| STATE-025 | PreconditionRequired | Required If-Match/precondition missing | Block blind repeat and refresh current object | GET current ETag | PRECONDITION_REQUIRED | Stage 3.5 retained |
| STATE-026 | SearchCursorInvalid | Search cursor does not match normalized filters | Discard cursor; keep query/filters | Restart page 1 | SEARCH_CURSOR_INVALID | Stage 3.5 retained |
| STATE-027 | SearchCursorExpired | Search snapshot/scope cursor expired | Discard cursor; explain result refresh | Restart page 1 | SEARCH_CURSOR_EXPIRED | Stage 3.5 retained |
| STATE-028 | ExplicitNull | Nullable field deliberately cleared | Serialize explicit null | Save | — | Stage 3.5 retained |
| STATE-029 | PatchFieldOmitted | PATCH field unchanged | Omit property and preserve server value | Save | — | Stage 3.5 retained |
| STATE-030 | FieldRedacted | Individual field/relation redacted within otherwise usable card | Neutral marker; no hidden value/count | Continue or external access request | FORBIDDEN/filtered response | Stage 3.5 retained; narrower than STATE-016 |
| STATE-031 | FileUnavailable | No usable location or OS/path/resource blocks open | Do not invoke shell; categorized recovery | Alternative/relink/retry/external ACL fix | FILE_NO_LOCATION; FILE_NOT_FOUND; FILE_ACCESS_DENIED; NETWORK_RESOURCE_UNAVAILABLE; UNSAFE_PATH; UNSAFE_FILE_TYPE | Stage 3.5 retained and normalized to all file-unavailable variants |
| STATE-032 | SessionExpired | Session/access token can no longer authorize commands | Stop commands; preserve only allowed in-memory draft | Refresh session or login | AUTHENTICATION_REQUIRED; SESSION_EXPIRED | New: unique security recovery semantics |
| STATE-033 | SessionRevoked | Session/token family revoked or reuse detected | Clear secure credentials and prohibited cache; no automatic resume | Explicit login | SESSION_REVOKED; REFRESH_TOKEN_REUSE | New: unique forced-revocation semantics |
| STATE-034 | DeviceRevoked | Current device is revoked | Clear credentials/cache; block further use on device | Administrator/device remediation | DEVICE_REVOKED | New: unique device-level denial |
| STATE-035 | StorageFull | Server storage condition blocks writes | Critical global write-block banner | Administrator frees storage | STORAGE_FULL | New: unique global operational state |
| STATE-036 | SyncCursorExpired | Durable sync cursor compacted/expired | Clear disposable projections without exposing stale scope | Full bootstrap | SYNC_CURSOR_EXPIRED | New: distinct from search cursor |
| STATE-037 | AccountBlocked | Account disabled/blocked | Do not disclose sensitive reason; block session creation | Administrator reactivates account | ACCOUNT_BLOCKED | New: unique account lifecycle state |
| STATE-038 | AccountTemporarilyLocked | Login temporarily locked after security policy trigger | Show bounded retry timing without account enumeration | Retry after lock interval | ACCOUNT_LOCKED_TEMPORARILY | New: unique timed security state |
| STATE-039 | AuthenticationFailed | Credentials rejected | Generic login error; no field-specific disclosure | Correct credentials/retry under rate limit | INVALID_CREDENTIALS | New: unique pre-session authentication state |

Rules:

- `STATE-016` describes a surface-level permitted subset; `STATE-030` describes field/relation redaction inside a usable object.
- Business status such as completed and nonblocking warnings such as calendar overlap are not separate technical UI states.
- Module-specific errors reuse the nearest stable state above; new IDs were added only for unique session, device, storage and sync-cursor semantics.
- `STATE-001…024` are restored from the Stage 3.0 registry; `STATE-025…031` are retained from Stage 3.4 without reuse. `OQ-002` is closed.

## 8. Общие нефункциональные требования

| NFR | Area | Requirement | Target | Measurement | Source |
| --- | --- | --- | --- | --- | --- |
| NFR-001 | Platform | Desktop client supports corporate Windows 10/11 editions approved by the company. | Windows 10/11; supported .NET LTS build | Installation/smoke matrix on approved images | Architecture §0.5/technology profile |
| NFR-002 | Desktop | All primary and destructive actions have keyboard-only paths. | 100% critical flows keyboard-completable | Accessibility test scripts for SCR/FLOW | Stage 3 accessibility |
| NFR-003 | Accessibility | Focus is visible, order is deterministic, controls have accessible names and states. | No critical WCAG/Windows accessibility blocker | UIA/Screen Reader + manual audit | Stage 3 accessibility |
| NFR-004 | High DPI | Layouts remain usable at 100–200% Windows scaling and multiple monitors. | No clipped critical control at 200% | Visual/interaction matrix | Architecture/Stage 3 |
| NFR-005 | Color | Status/urgency/error is never communicated by color alone. | Text/icon/state alternative for every colored signal | Accessibility review | Stage 3 |
| NFR-006 | Performance | Large lists use server pagination and client virtualization. | No full dataset load; stable interaction on source scale | Profile with production-like fixtures | Architecture scale; DEC-034 |
| NFR-007 | Calendar | Calendar reads are range-bounded and virtualized. | No unbounded multi-year query | Contract/UI tests | Architecture §3.12 |
| NFR-008 | Search | Filtering and cursor pagination execute server-side before result delivery. | No client post-filter of paged results | Contract tests with filter-bound cursor | Search Contract; DEC-040 |
| NFR-009 | Resilience | Server outage changes application to honest read-only cache mode. | 0 accepted business writes while API unavailable | Fault injection desktop tests | ADR-015; DEC-006 |
| NFR-010 | Recovery | After reconnect client authenticates, applies change feed, and bootstraps when cursor/scope is invalid. | No stale unauthorized projection after recovery | Fault injection/integration | ADR-005; DEC-032 |
| NFR-011 | Concurrency | Versioned writes require If-Match and reject stale/missing version. | 100% versioned writes covered by 412/428/409 tests | Generated contract test suite | DEC-038 |
| NFR-012 | Idempotency | Idempotent commands do not duplicate side effects for same key/hash. | Exactly one business result/event for duplicate delivery | Integration/replay tests | Stage 2.2 |
| NFR-013 | Security | Server evaluates authentication, capability and relation on every request. | No authorization decision based solely on UI | BOLA/permission tests | ADR-002; DEC-004 |
| NFR-014 | Security | Tokens, passwords, secrets and sensitive full paths are absent from logs/analytics. | 0 secret findings in log scan | Automated redaction/security test | Architecture audit rules |
| NFR-015 | Transport | Client-server traffic uses approved TLS inside local network. | No plaintext API endpoint | Deployment/security test | Architecture |
| NFR-016 | Local data | User-specific cache is encrypted, disposable and cleared on logout/revoke/scope changes as specified. | No readable cache after cleanup | Desktop storage/security test | ADR-015; DEC-032 |
| NFR-017 | Errors | Client displays stable recovery message and traceId without stack trace. | All stable errors mapped; no raw exception | Catalog coverage test | errors.csv; DEC-035 |
| NFR-018 | File safety | Application never deletes or moves physical working files as a metadata side effect. | 0 physical mutations in delete/move/purge tests | Filesystem sandbox tests | DEC-017/018 |
| NFR-019 | File access | OS/SMB ACL remains authoritative and failures are categorized. | No ACL bypass; differentiated error | Windows/SMB integration tests | Architecture A1-06 |
| NFR-020 | Compatibility | Unsupported client is blocked with 426/update route. | No writable shell below min version | Version compatibility tests | ADR-012; CLIENT_VERSION_UNSUPPORTED |
| NFR-021 | Audit | Audit/history is append-only and authorization/redaction aware. | No update/delete path for audit; current-right filtering | DB/API/security tests | Architecture; ADR-009 |
| NFR-022 | Backup UX | Admin UI shows backup result, last success and verification status; backup bytes are not exposed. | No success claim without completed result/verification metadata | Admin integration test | Architecture backup agent |
| NFR-023 | Scale | Candidate is designed for up to 300 active employees, 100 concurrent connections and approximately 2M tasks/events. | Architecture target; validate with load fixtures | Load test report | Architecture §0.5 assumption |
| NFR-024 | Availability | Working-hours target 99.5%, RPO ≤15 min, RTO ≤4 h are architecture assumptions, not contractual SLA. | Measure and confirm before production baseline | Operations validation | Architecture §0.5 assumption |
| NFR-025 | Request limits | Requests, text and batch sizes respect OpenAPI/Stage 2.3.1 limits. | 0 client request beyond contract limit | Generated boundary tests | OpenAPI/Stage 2.2 |

## 9. MVP boundaries и открытые блокеры

Candidate не придумывает отсутствующие поля или операции. После нормализации `OQ-002` закрыт как документационный дефект. `OQ-001` остаётся High: концепция прямо требует настраиваемые пороги/цветовые интервалы (§17.3, §23.2, §27.1 item 20), а writable contract отсутствует. `OQ-003` остаётся High: концепция прямо включает сотрудников в область и группы глобального поиска (§20.1–20.2), а Search Contract не содержит employee/user result type. OpenAPI/DTO не изменялись. Независимый аудит 4.2 не запускается до нормативного закрытия этих двух High gaps.

## 10. Definition of Done продукта

- Все 21 module DoD выполнены.
- 241/241 OpenAPI operations имеют FR и AC.
- Critical flows имеют happy, validation, permission, conflict/read-only и recovery cases.
- Нет действий без permission, полей без DTO или stable error без UX recovery.
- Архив, корзина, business status и offline/read-only не смешиваются.
- `Critical=0`, `High=0` после независимого аудита 4.2/исправлений 4.3.

## 11. Самопроверка общего PRD

Проверяется автоматически файлом manifest/validation summary: количество модулей, operations, IDs, FR→AC, permission/error references, coverage SCR/FLOW и отсутствие непроверенных маркеров. Результат Candidate не называется Final до Этапа 4.3.


## 14. Нормативное точечное обновление 4.1.2

Stage 2.3.1 (`OpenAPI 1.2.0-stage2.3`, 244 operations, 237 schemas, 91 permissions, 44 stable errors) является текущим technical contract. Stage 3.5 является текущим UX baseline. Stage 2.2 и 3.4 используются только как historical/backward-compatibility evidence.

### 14.1. Изменённые области

| Area | Normative result |
| --- | --- |
| Organizational urgency scale | Единственный owner — organization; CMP-001 в SCR-153; GET/PUT/reset; четыре semantic intervals 0–100; ETag/If-Match; audit; no user override |
| Employee global search | `employee` — distinct type и группа «Сотрудники»; DTO-only fields; no avatar; server filtering/redaction/blocked policy before pagination; no client post-filter |
| Notifications | Current organization mapping влияет на presentation существующих и будущих notifications, не меняя semantic urgency |
| Accessibility | Urgency и employee status/redaction доступны без зависимости только от цвета; keyboard, focus order и screen-reader semantics обязательны |
| Privacy | Product analytics, diagnostics и security audit разделены; query/PII/paths/secrets/notification content не записываются |

### 14.2. Affected modules

`MOD-002`, `MOD-014`, `MOD-015`, `MOD-018`, `MOD-019`, `MOD-020`, `MOD-021`. Остальные 14 module PRDs сохранены без изменения бизнес-scope.

### 14.3. Identifier normalization

Stage 3.5 input содержит два разных определения `FLOW-035`. Согласно правилу сохранения существующих ID исторический `FLOW-035` остаётся «Завершение и архивирование проекта», а новый urgency-scale flow получает следующий свободный `FLOW-038`. Решение фиксируется `DEC-060`; исходный Stage 3.5 архив не изменяется.

### 14.4. Product DoD 4.1.2

- 21 modules; 244/244 API operations mapped.
- 279 FR, 113 BR, 1824 AC, 25 NFR.
- FR without AC, unknown permissions/errors/UX IDs, unverified, provisional, duplicate IDs и lost references: 0.
- `OQ-001` и `OQ-003`: Fixed с сохранённой историей.
- MVP не расширен; Critical/High validation findings: 0.
