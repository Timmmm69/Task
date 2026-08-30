# ПРОМТ ДЛЯ НОВОГО ЧАТА — 02. Production API деталей события и конфликтов расписания

Ты работаешь в `C:\Users\novik\Таск`. Реализуй второй server increment Calendar read vertical slice: `GET /api/v1/calendar-events/{id}` и `GET /api/v1/calendar/conflicts`. Начинай только после успешного появления задания `CALENDAR-READ-01` в актуальном `origin/main`.

## Обязательное начало

Прочитай `AGENTS.md` и `work/delegation/README.md`. Выполни `git fetch origin`, проверь чистый `main` и divergence, fast-forward при необходимости. По коду и тестам убедись, что `/api/v1/calendar`, named policy/capability `Calendar.Read` и DI `IScheduleStore` уже существуют и работают. Если prerequisite отсутствует, расходится с каноническим contract или дерево не чистое — остановись.

Изучи принятый `CalendarEndpoints`, problem responses, request context, `CalendarEventQueryService`, `CalendarEventDetails`, attendee value objects, `ScheduleQueryService.GetConflicts`, PostgreSQL stores и endpoint fixtures. Reference files:

- `sources/stage_2_2/Organizer_Stage2_Technical_Specification_2.2/openapi/openapi.yaml` — event by id, `CalendarEvent`, conflicts;
- `work/production/src/Task.Application/Calendar/CalendarEventQueryService.cs`;
- `work/production/src/Task.Application/Calendar/CalendarEventDetails.cs`;
- `work/production/src/Task.Application/Calendar/ScheduleQueryService.cs`;
- `outputs/20260817_task_calendar_event_read_model_0.1.0/VALIDATION_REPORT.md`;
- `outputs/20260817_task_calendar_store_schedule_read_model_0.1.0/VALIDATION_REPORT.md`.

## Endpoint 1: детали события

Добавь `GET /api/v1/calendar-events/{id}` под той же named policy `Calendar.Read`. `id` — непустой UUID; organization только из authenticated context. Успех `200` возвращает строгий CalendarEvent DTO:

- `id`, `organizationId`, nullable `projectId`;
- `title`, nullable `description`;
- `eventDate`, `isAllDay`, nullable `startAtUtc`, nullable `endAtUtc`, `timeZone`;
- snake-case `status` (`scheduled`/`cancelled`) и lifecycle state в форме канонического schema;
- `version`, `createdAtUtc`, `updatedAtUtc`;
- user/contact attendee arrays со всеми каноническими role/response fields в сохранённом порядке.

Проверь точное имя lifecycle property и attendee DTO по OpenAPI, не угадывай. Добавь `ETag: "v{version}"`. Event другого tenant должен выглядеть как `404 OBJECT_NOT_FOUND`, а не раскрывать существование. Archived/trashed visibility должна следовать текущему store/query contract и каноническому GET; не добавляй скрытый lifecycle transition.

## Endpoint 2: конфликты

Добавь `GET /api/v1/calendar/conflicts` под `Calendar.Read`:

- `from`, `to` обязательны в этом slice, UTC с `Z`, half-open range, не более 366 дней;
- repeated `userIds`, максимум 100 UUID, пустые/дубликаты/invalid отклоняются;
- optional `excludeObjectId`, непустой UUID;
- unknown query parameters отклоняются;
- `timezone` не является параметром канонического endpoint. Для all-day boundary используй согласованное поведение existing service: если endpoint должен передать timezone, используй `UTC` и закрепи тестом; не добавляй новый публичный query parameter.

Успех `200` — максимум 500 `ScheduleConflict`: `leftObjectId`, `rightObjectId`, UTC `overlapStart`, UTC `overlapEnd`, snake-case `severity` (`info`, `warning`, `blocking`). Сохрани deterministic ordering существующего service. Point tasks/zero-duration intervals не конфликтуют; half-open touching intervals не конфликтуют; overlap меньше 30 минут — warning, от 30 минут — blocking. `excludeObjectId` исключает все пары с объектом.

## Errors/security

Используй тот же validation mapping, что принят в задании 01. 401/403 остаются security pipeline; invalid route/query — safe problem response; другой tenant/not found — 404; unavailable database — retryable 503 без leakage. Не принимать organization/user identity из body/header/query. Не возвращать details объектов, которые caller не может читать.

## Разрешённые изменения

- существующий `work/production/src/Task.Api/Calendar/CalendarEndpoints.cs` и узкие DTO/mapping files в этой папке;
- `work/production/src/Task.Api/Program.cs` только для `CalendarEventQueryService` DI;
- новый `CalendarEventDetailsEndpointsTests.cs` и `CalendarConflictsEndpointsTests.cs` в `Task.ServiceHosts.Tests` либо один объединённый calendar details/conflicts test file;
- application Calendar tests только для найденного correctness defect, без расширения public port.

Не менять permission semantics из задания 01, schema/migrations, write services, Desktop, sources, outputs, dependencies или `/calendar-events` list endpoint. Ориентир — до 8 файлов и 400 changed lines. Если выясняется, что attendee response невозможно сериализовать без изменения domain contract, остановись и опиши точное расхождение.

## Обязательные тесты

Для details: full scalar/timing/status/lifecycle mapping; all-day null instants; both attendee kinds; ETag/version; invalid/empty UUID; missing; cross-tenant 404; 401/403; safe store failure.

Для conflicts: none/one/multiple; warning/blocking threshold; touching intervals; point tasks; all-day event; filters; exclude; deterministic order; max 500; invalid range/UUID/too many users/unknown parameter; tenant isolation; 401/403/503; correlation id. Real PostgreSQL test обязан доказать чтение persisted event details и conflict projection между persisted objects, без skip.

## Проверки

```powershell
cd work/production
dotnet format Task.sln --no-restore
dotnet test tests/Task.ServiceHosts.Tests/Task.ServiceHosts.Tests.csproj -c Release --filter "FullyQualifiedName~CalendarEventDetailsEndpoints|FullyQualifiedName~CalendarConflictsEndpoints"
dotnet test tests/Task.Tests/Task.Tests.csproj -c Release --filter "FullyQualifiedName~Calendar"
dotnet test Task.sln -c Release
dotnet build Task.sln -c Release --no-restore
git diff --check
```

Запусти штатный real-PostgreSQL gate с `TASK_POSTGRES_TEST_ADMIN_CONNECTION`; critical scenario не может быть skipped.

## Завершение и публикация

Acceptance: оба endpoints защищены одной правильной capability, details/ETag и conflict semantics соответствуют canonical/application contracts, tenant isolation доказана, реальные persistence paths пройдены, полный gate зелёный.

Не push при отсутствии задания 01, необходимости менять OpenAPI/business/schema, test skip/failure, dirty tree, конфликте или scope overflow. Commit: `feat(calendar): expose event details and conflicts`. После commit выполни fetch + rebase `origin/main`, повтори проверки и push `HEAD:main`. В финале укажи scope, команды/results, real database result, SHA и факт push.
