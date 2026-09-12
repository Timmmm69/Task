# API-05 — Audit и capabilities: безопасная эксплуатационная картина

## Цель

Действия в организации отслеживаются (append-only audit journal, безопасное чтение), а
desktop-клиент получает версию и возможности сервера без утечки данных. Задача закрывает
пункт roadmap API-05: расширяет audit trail на новые product-модули и проверяет отсутствие
утечек.

## Канонический контракт (spec 2.2)

| Endpoint | Доступ | Ответ |
| --- | --- | --- |
| `GET /api/v1/audit` | `Audit.ReadAll` (`audit.entry.read`) | `AuditEntryPage` |
| `GET /api/v1/capabilities` | Authenticated | `ServerCapabilities` |
| `GET /api/v1/admin/server-capabilities` | `System.Configure` | `ServerCapabilities` |
| `GET /api/v1/system/version` | Authenticated | `SystemVersion` |

Фильтры audit по контракту: `actor`, `action`, `object`, `outcome`, `from`, `to`, `cursor`.
`ServerCapabilities` = `{ capabilities[], minimumApiVersion, minimumDesktopVersion }`.
`SystemVersion` = `{ serverVersion, apiVersion, databaseSchemaVersion, minimumDesktopVersion }`.

## Изменения

1. **Audit read surface** (`Task.Application.Audit`, `Task.Infrastructure/Postgres/PostgresAuditEntryStore`,
   `Task.Api/Audit/AuditEndpoints`):
   - `AuditEntryRecord` и `AuditQuery` дополнены `ObjectId`/`ObjectType` и фильтром `ObjectId`.
   - `PostgresAuditEntryStore.ReadAsync` читает `object_id`/`object_type` и фильтрует по `object_id`.
   - `GET /api/v1/audit` принимает `actor` (uuid) и `object` (uuid), валидирует (422 на некорректный
     UUID) и возвращает `objectId`/`objectType` в каждом элементе.
2. **Capabilities канонической формы** (`Task.Application/System/ServerCapabilitiesService`):
   публичный payload теперь `{ capabilities, minimumApiVersion, minimumDesktopVersion }` без
   операционных деталей (`schemaVersion`, `recommendedDesktopVersion` выведены из payload —
   они доступны через `/api/v1/system/version` и заголовки compatibility-middleware).
3. **`GET /api/v1/system/version`** (`Task.Api/Server/SystemVersionEndpoints`): authenticated;
   `serverVersion` из конфигурации `Task:Server:Version` с fallback на informational version сборки.
4. **`GET /api/v1/admin/server-capabilities`** (`Task.Api/Capabilities/CapabilitiesEndpoints`):
   тот же payload, но с разрешением администратора. Канонический `System.Configure` отображается
   на production-capability `organization.manage` (политика `permission.organization.manage`).
5. **Проверка отсутствия утечек** (endpoint-тесты): audit-элемент отдаёт ровно безопасные поля,
   никогда не содержит `metadata`/`oldState`/`newState`; capabilities/system-version отдают только
   публичные поля.

## Карта audit trail по модулям

| Модуль | Action codes | object_id/object_type | Redaction |
| --- | --- | --- | --- |
| Задачи | `task.create`, `task.update`, `task.changestatus` | task id | standard (safe payload) |
| Auth | `UserLoggedIn`, `LoginFailed`, `SessionRefreshed`, `RefreshTokenReuse` | — | standard |
| Product API (проекты, CRM, файлы, уведомления, настройки, архив/корзина) | `<type>.<operation>` | да | standard; пути и контактные значения не попадают в payload |
| Recurrence | `recurrence.*` | `recurrence_series` | standard |
| Пользователи | user-события | `user_account` | `restricted` |
| Устройства | device-события | `device` | `restricted` |
| Административное чтение | `authorization.administrative_read` | — | standard (metadata: operation) |
| Bootstrap | bootstrap-события | — | restricted |

Журнал неизменяемый: триггер `trg_audit_entries_append_only` отклоняет UPDATE/DELETE; `occurred_at`
присваивается серверными часами. Записи не содержат паролей, токенов и секретов.

## Границы

- `GET /api/v1/audit/export` (асинхронный защищённый экспорт + `AuditExportRequested`) вне scope
  этого инкремента; требует отдельной задачи (background-джоба, модель `ExportJob`).
- Мутации событий календаря пишутся в `governance.domain_events` (история объекта), но не в
  `governance.audit_entries`; выравнивание календаря с audit-журналом — follow-up.
- Пагинация audit использует production-конвенцию `pageToken`/`pageSize` (keyset) вместо
  канонического `cursor`; семантика эквивалентна.
- Фильтр `object` ограничен uuid-идентификаторами `object_id` (production-семантика).

## Проверка

- Release-сборка решения: PASS, 0 ошибок.
- Полный прогон на локальном PostgreSQL 16.14 (`TASK_POSTGRES_TEST_ADMIN_CONNECTION`):
  `Task.Tests` 804/804 passed (включая реальный gate `PostgresAuditEntryStoreTests`:
  append/read roundtrip, keyset-пагинация, фильтры, изоляция организаций, append-only триггер,
  readback и фильтр по `object_id`); `Task.ServiceHosts.Tests` 581/581 passed. Без
  env-переменной PostgreSQL-интеграционные тесты штатно пропускаются.
- Новые/обновлённые тесты: `AuditEndpointsTests`, `CapabilitiesEndpointsTests`,
  `SystemVersionEndpointsTests`, `ClientVersionCompatibilityMiddlewareTests`,
  `ServerCapabilitiesServiceTests`, `PostgresAuditEntryStoreTests`.
