# Calendar read API — release 1.0.0

## Результат

Реализован production read slice календаря:

- `GET /api/v1/calendar` — объединённый диапазон Task + CalendarEvent;
- `GET /api/v1/calendar-events/{id}` — детали события и `ETag`;
- `GET /api/v1/calendar/conflicts` — детерминированные пересечения расписания.

Все маршруты защищены named policy `Calendar.Read`. В текущем согласованном slice capability fail-closed использует существующее backing permission `task.read`; session capability response сообщает `Calendar.Read` только при фактическом grant этого permission.

## Контрактные границы

- `from` и `to` обязательны, принимаются только как UTC RFC 3339 с `Z`.
- Диапазон half-open, `from < to`, максимум 366 дней.
- UUID-фильтры принимаются repeated-параметрами, максимум 100, без пустых значений и дубликатов.
- `departments` и `cursor` честно отклоняются до появления соответствующего persistence contract.
- Schedule/conflict responses ограничены 500 элементами.
- Organization всегда берётся из authenticated request context.
- Details другого tenant и отсутствующий/невалидный UUID возвращают одинаковый `404 OBJECT_NOT_VISIBLE`.
- Необработанные store failures преобразуются в безопасный retryable `503 INTERNAL_ERROR`.

## Изменённые production surfaces

- `work/production/src/Task.Api/Calendar/CalendarEndpoints.cs`
- `work/production/src/Task.Api/Program.cs`
- `work/production/src/Task.Api/Security/TaskPermissionAuthorization.cs`
- `work/production/tests/Task.ServiceHosts.Tests/CalendarReadEndpointsTests.cs`
- `work/production/tests/Task.ServiceHosts.Tests/AuthSessionEndpointsTests.cs`
- `.project-dashboard/roadmap.json`

Канонические файлы в `sources/` не изменялись. Миграции, schema, write services и Desktop не менялись.
