# Stage 4. Product PRD

**Статус:** Remediated Candidate для повторного независимого аудита 4.4  
**Версия:** 4.5-candidate.1  
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

## 7. State reference policy (Stage 4.5)

The candidate does not create or republish Stage 3.5 states. Active PRD references use the published State Matrix behavior name, or a stable-error/UI condition when the old numeric token was not a state. The historical resolution ledger is `Stage_4_5_STATE_Resolution.csv`.

| Reference form | Addressable source | Rule |
| --- | --- | --- |
| Published numeric contract state | `STATE-007`, `STATE-014`, `STATE-025`–`STATE-031` | Retained only where Stage 3.5 publishes the numeric ID. |
| Published named behavior | Stage 3.5 State Matrix row | Used for Initial, Loading, Refreshing, Empty, Forbidden, ObjectUnavailable, ServerUnavailable, Reconnecting, Maintenance, StorageFull, ClientUnsupported, SyncPending, CursorExpired, AccessScopeChanged, PartialAccess, Archived, Trashed and BackgroundOperation. |
| Error/UI condition | Stable error and State Matrix rule | Used instead of the withdrawn synthetic IDs for auth/session, account, generic failure, loaded and freshness conditions. |
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
| NFR-012 | Idempotency | Idempotent commands do not duplicate side effects for same key/hash. | Exactly one business result/event for duplicate delivery | Integration/replay tests | Stage 2.3.1 |
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
| NFR-024 | Operational policy boundary | Product baseline defines honest outage/read-only/recovery behavior but does not set numeric availability, RPO or RTO. Before production deployment the organization must approve a separate operational availability/backup/recovery contract and verify it by outage/restore exercises. | 0 unapproved numeric SLA targets in product PRD; approved deployment policy is a production gate | Document gate plus fault-injection and restore exercise evidence | Architecture §0.5 boundary; OQ-008 closure |
| NFR-025 | Request limits | Requests, text and batch sizes respect OpenAPI/Stage 2.3.1 limits. | 0 client request beyond contract limit | Generated boundary tests | OpenAPI/Stage 2.3.1 |

## 9. MVP boundaries и открытые блокеры

Candidate не придумывает отсутствующие поля или операции. `OQ-001`, `OQ-002` и `OQ-003` сохранены в истории и закрыты только нормативными решениями. `OQ-001` закрывается контрактом Stage 2.3.1 для организационной шкалы срочности и UX Stage 3.5 для `SCR-153/CMP-001`; `OQ-003` закрывается контрактом Stage 2.3.1 для `employee`/`EmployeeSearchResult` и UX Stage 3.5 для `SCR-133/134/135/CMP-002`. Исходные Stage 2.3.1 и Stage 3.5 не изменялись. Статус закрытия в candidate 4.5 подлежит подтверждению повторным независимым аудитом 4.4.

## 10. Definition of Done продукта

- Все 21 module DoD выполнены.
- 244/244 OpenAPI operations имеют FR и AC.
- Critical flows имеют happy, validation, permission, conflict/read-only и recovery cases.
- Нет действий без permission, полей без DTO или stable error без UX recovery.
- Архив, корзина, business status и offline/read-only не смешиваются.
- `Critical=0`, `High=0`, `Medium=0` по remediation-precheck; окончательное подтверждение выполняется независимым аудитом 4.4.

## 11. Самопроверка общего PRD

Проверяется автоматически файлами manifest/validation summary: количество модулей, operations, IDs, FR→AC, permission/error references, coverage SCR/FLOW и отсутствие непроверенных маркеров. Candidate не называется Final до успешного повторного независимого аудита 4.4.


## 14. Нормативная модель после remediation 4.5

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

### 14.5. Identifier normalization

Stage 3.5 input содержит два разных определения `FLOW-035`. Согласно правилу сохранения существующих ID исторический `FLOW-035` остаётся «Завершение и архивирование проекта», а новый urgency-scale flow получает следующий свободный `FLOW-038`. Решение фиксируется `DEC-060`; исходный Stage 3.5 архив не изменяется.

### 14.4. Product DoD 4.5

- 21 modules; 244/244 API operations mapped.
- 279 FR, 113 BR, 1911 AC, 25 NFR.
- FR without AC, unknown permissions/errors/UX IDs, unverified, provisional, duplicate IDs и lost references: 0.
- `OQ-001` и `OQ-003`: Fixed in candidate 4.5 с сохранённой историей; независимое подтверждение ожидается на Этапе 4.4.
- MVP не расширен; remediation устраняет известные противоречия, но не заменяет независимый аудит.

### 14.5. Сквозная модель OQ-001

- Область настройки — только `organization`; владелец — организация; personal/user override отсутствует.
- Шкала содержит ровно четыре обязательных semantic level: `low`, `normal`, `high`, `critical`, каждый ровно один раз и в этом порядке.
- Inclusive-интервалы полностью покрывают 0–100 без gaps и пересечений; defaults: 0–24, 25–49, 50–74, 75–100.
- `displayToken` задаётся контрактом и не заменяет semantic level; текст/иконка/label обязательны, зависимость только от цвета запрещена.
- Чтение использует `GET /api/v1/settings/notification-urgency-scale` и `Settings.ReadOwn`; полная атомарная замена и reset используют `System.Configure`, `ETag/If-Match` и `Idempotency-Key`.
- Missing/stale version не допускает overwrite; validation/conflict сохраняют draft и требуют refresh/compare/reapply либо discard. При outage запись блокируется; offline queue не используется.
- Успешные PUT/reset и permission-sensitive denials аудитируются событием `notification_urgency_scale.changed` с actor, outcome, correlationId и redacted diff.
- Текущая шкала меняет presentation существующих и будущих notifications, но не их semantic urgency; клиент поколения Stage 2.2 сохраняет встроенный mapping.

### 14.6. Сквозная модель OQ-003

- `employee` — самостоятельный result type и отдельная доступно озвучиваемая группа «Сотрудники» в employee-only и mixed search.
- DTO — только `EmployeeSearchResult`: `userId`, `displayName`, nullable `departmentId`/`departmentName`/`jobTitle`, `accountStatus`, `deepLink`, `isRedacted`; avatar, email, phone и произвольная роль не добавляются.
- `userIds`, contacts и административный список пользователей не заменяют employee search.
- Authorization, relation filtering, redaction, blocked-user policy, ranking и grouping выполняются сервером до cursor pagination. Client post-filter и восстановление скрытых данных запрещены.
- Blocked employee исключается сервером, кроме caller с существующей capability `User.Block`; partial/redacted данные не раскрывают скрытые значения или counts.
- Cursor связан с нормализованными filters, authorization scope, index snapshot и employee visibility policy version; invalid/expired cursor перезапускает page 1 с теми же filters.
- Employee `deepLink` открывается только после повторной server-side проверки; stale/unavailable target показывает нейтральное состояние без раскрытия объекта.
- Keyboard navigation, active descendant, Enter, Esc/focus return, screen-reader group/status/redaction semantics и non-color status обязательны.

### 14.7. Operational SLA и telemetry retention

- Product PRD не устанавливает неподтверждённые numeric availability, RPO или RTO. Эти значения задаются отдельным company-approved operational contract до production deployment и проверяются outage/backup/restore exercises; это deployment-policy gate, а не открытый product requirement.
- Внешняя analytics platform и отдельное долговременное product-analytics storage не предполагаются. Минимизированные product/diagnostic events пишутся только в server-side structured application logs.
- Application-log retention находится в нормативном диапазоне 30–90 дней по Stage 1 §6.10; точное значение выбирает компания в deployment configuration, документирует и проверяет rotation/expiration test. Candidate не фиксирует произвольный срок.
- Security/business audit остаётся отдельным permission-controlled контуром Stage 2.3.1 и не наследует product-log access/retention. Новые API, DTO и permissions не вводятся.
