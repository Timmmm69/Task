# Calendar write API — release 1.0.0

Дата: 2026-09-01

## Результат

Реализован защищённый HTTP write slice для `CalendarEvent`:

- `POST /api/v1/calendar-events`;
- `PATCH /api/v1/calendar-events/{id}`;
- `POST /api/v1/calendar-events/{id}/archive`;
- `POST /api/v1/calendar-events/{id}/unarchive`;
- `DELETE /api/v1/calendar-events/{id}`;
- `POST /api/v1/calendar-events/{id}/restore`.

Создание защищено capability `CalendarEvent.Create`, редактирование и архив — `CalendarEvent.Update`, операции корзины — `CalendarEvent.Delete`. Новые permission codes добавлены миграцией БД №7 и публикуются в server-derived capabilities текущей сессии.

## Контракт изменения

- Mutating endpoints существующего события требуют один strong `If-Match` вида `"v<positive-integer>"`.
- Отсутствующий precondition возвращает `428 PRECONDITION_REQUIRED`, устаревшая версия — `412 VERSION_CONFLICT`.
- Успешные ответы возвращают актуальный `ETag`.
- Multi-field PATCH применяется одной доменной операцией и повышает версию не более одного раза.
- Tenant boundary применяется до изменения: объект другой организации возвращается как `404 OBJECT_NOT_VISIBLE`.
- Create/archive/restore проверяют обязательный `Idempotency-Key` согласно HTTP surface контракта.
- JSON parsing отклоняет неизвестные поля, malformed JSON и невалидные даты, UTC instants, UUID, enum и attendee collections стабильными problem codes.

## Затронутые production-компоненты

- `Task.Api/Calendar/CalendarWriteEndpoints.cs` — HTTP adapter, request validation, permissions, ETag/If-Match и problem responses.
- `Task.Domain/Calendar/CalendarEvent.cs` — атомарный `ApplyPatch`.
- `Task.Application/Calendar/CalendarEventLifecycleService.cs` — optimistic-concurrency orchestration.
- `Task.Api/Security/TaskPermissionAuthorization.cs` — policies и session capabilities.
- `Task.Infrastructure/Persistence/Migrations/007_calendar_event_capability_permissions.sql` — permission catalog/grant compatibility migration.

## Проверки

Полный перечень и результаты приведены в `VALIDATION_REPORT.md`.
