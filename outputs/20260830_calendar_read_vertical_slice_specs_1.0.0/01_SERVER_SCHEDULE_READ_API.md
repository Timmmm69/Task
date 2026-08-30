# ПРОМТ ДЛЯ НОВОГО ЧАТА — 01. Production API объединённого календарного расписания

Ты работаешь в репозитории `C:\Users\novik\Таск`. Реализуй первый production increment Calendar read vertical slice: защищённый `GET /api/v1/calendar`, permission/capability `Calendar.Read` и DI для существующего schedule read model. Это первое из пяти последовательных заданий.

## Обязательная начальная процедура

Полностью прочитай корневой `AGENTS.md` и `work/delegation/README.md`. Выполни `git fetch origin`; проверь `git status --short --branch`, текущую ветку и divergence `origin/main...HEAD`. Работай только на чистом синхронизированном `main`; чистый отстающий `main` синхронизируй fast-forward. При dirty tree, diverged history или невозможности fast-forward остановись.

Изучи существующие `TaskEndpoints`, `TaskApiProblemResponse`, authenticated request context, permission authorization, capabilities mapping, `ScheduleQueryService`, `IScheduleStore`, `PostgresScheduleStore`, `TaskPersistenceRuntime` и тестовые host fixtures. Проверь migration 003 и существующие real-PostgreSQL schedule tests. Используй текущие endpoint/serialization/security patterns. Не переписывай application/domain/persistence, если endpoint может использовать их напрямую.

Канонические reference files:

- `sources/stage_2_2/Organizer_Stage2_Technical_Specification_2.2/openapi/openapi.yaml` — `/api/v1/calendar`, `SchedulePage`, `ScheduleItem`;
- `sources/stage_4_1_1/Organizer_Stage4_PRD_Candidate_4.1.1.zip`;
- `work/production/src/Task.Application/Calendar/ScheduleQueryService.cs`;
- `work/production/src/Task.Application/Calendar/IScheduleStore.cs`;
- `work/production/src/Task.Application/Calendar/ScheduleContracts.cs`;
- `outputs/20260817_task_calendar_store_schedule_read_model_0.1.0/VALIDATION_REPORT.md`.

При конфликте действуют канонические источники в порядке из `AGENTS.md`, но не меняй business requirements самостоятельно.

## Цель и публичный контракт

Добавь `GET /api/v1/calendar` с named policy публичного смысла `Calendar.Read`. До появления отдельного granular permission разрешено fail-closed связать его с уже существующим backing permission `task.read`; capability response при этом обязан явно возвращать `Calendar.Read` только если это разрешение реально выдано. Не используй безымянную проверку и не разрешай доступ только по факту authentication.

Organization берётся исключительно из `AuthenticatedRequestContext`. Query contract:

- `from` и `to` обязательны для этого production slice, несмотря на optional в общем OpenAPI; RFC 3339 UTC instants с явным `Z`;
- диапазон half-open `[from,to)`, `from < to`, максимум 366 дней;
- `timezone` optional, 1–64, default `UTC`, должен разрешаться системным `TimeZoneInfo`;
- repeated `users` и `projects`, максимум по 100 UUID, пустые/дубликаты/невалидные UUID отклоняются;
- `status` optional, trim, 1–40; передаётся существующему read service без выдумывания новых enum;
- `departments` и `cursor`: отсутствие допустимо, любое непустое значение возвращает `400 VALIDATION_FAILED`, потому что текущий persistence contract их не поддерживает;
- неизвестные query parameters возвращают `400 VALIDATION_FAILED`, а не игнорируются.

Успех `200 application/json`: точное отображение `SchedulePage` — `items`, `nextCursor`, `rangeStart`, `rangeEnd`. Каждый item содержит `objectId`, snake-case `itemType` (`task`, `calendar_event`), `title`, `localDate`, nullable `startAtUtc`, nullable `endAtUtc`, `isAllDay`, nullable `projectId`, `status`, nullable snake-case `priority`. Даты/время сериализуются однозначно; UTC instants с `Z`. `nextCursor` в этом slice всегда `null`. Добавь/сохрани `X-Correlation-ID`; ETag не фабрикуй, если нет корректного snapshot token.

## Ошибки, security и устойчивость

- missing/malformed/duplicate scalar input → `400 VALIDATION_FAILED`;
- валидный синтаксис, но invalid range/timezone/domain invariant → стабильный `422` либо текущий согласованный validation mapping; закрепи выбор тестами и не смешивай случайно 400/500;
- unauthenticated/session/device failures остаются в существующем pipeline;
- нет `Calendar.Read` → `403 FORBIDDEN`;
- store/database unavailable → безопасный retryable `503`, без SQL, stack trace, connection string или внутреннего exception text;
- cancellation должна достигать endpoint boundary; если текущий sync store не поддерживает token, не создавай fake asynchronous wrapper и зафиксируй честную границу;
- tenant isolation обязательна: query никогда не принимает organization id и не возвращает другой tenant.

Ограничь materialized response каноническим `maxItems: 500`. Если существующий store/service способен вернуть больше, endpoint обязан fail-safe ограничить запрос/результат без изменения сортировки; предпочтительно добавить bounded contract на самом узком слое только если это требуется для correctness. Не делай cursor pagination в этом задании.

## Разрешённые изменения

Разрешено менять только необходимое:

- новый `work/production/src/Task.Api/Calendar/CalendarEndpoints.cs`;
- `work/production/src/Task.Api/Program.cs` для DI/wiring;
- `work/production/src/Task.Api/Security/TaskPermissionAuthorization.cs` для named policy/capability;
- один новый `work/production/tests/Task.ServiceHosts.Tests/CalendarScheduleEndpointsTests.cs`;
- существующие permission/capability tests только для новой capability;
- `work/production/src/Task.Application/Calendar/**` и соответствующие tests только если нужен узкий bounded-read fix, доказанный тестом.

Не меняй migrations, database schema, Calendar domain/write services, Desktop, deployment, canonical sources, outputs или dependencies. Ориентир — до 8 файлов и 400 changed lines. Если требуется новый storage contract, schema или архитектурная переработка security, остановись.

## Обязательные тесты

Покрой: 200 empty page; task + timed event + all-day event mapping; deterministic ordering; requested range echo; timezone default/validation; boundary inclusion/exclusion; max 366 days; invalid/missing timestamps; repeated UUID filters and max 100; unsupported departments/cursor; unknown query; 500-item bound; authenticated tenant derivation/isolation; 401/403; `Calendar.Read` capability; safe 503; correlation id; no secret leakage.

Добавь real PostgreSQL integration assertion через штатные fixtures: migration применяется, task и event одного tenant попадают в окно, другой tenant не попадает. Critical test не должен skip.

## Проверки

```powershell
cd work/production
dotnet format Task.sln --no-restore
dotnet test tests/Task.ServiceHosts.Tests/Task.ServiceHosts.Tests.csproj -c Release --filter "FullyQualifiedName~CalendarScheduleEndpoints"
dotnet test tests/Task.Tests/Task.Tests.csproj -c Release --filter "FullyQualifiedName~Schedule"
dotnet test Task.sln -c Release
dotnet build Task.sln -c Release --no-restore
git diff --check
```

Выполни также штатный real-PostgreSQL test command проекта с настроенной `TASK_POSTGRES_TEST_ADMIN_CONNECTION`; отсутствие локальной БД — blocker для объявления завершения.

## Критерии приёмки и stop conditions

Endpoint отдаёт реальные unified schedule rows, tenant/permission fail-closed, unsupported filters не имитируются, response строго соответствует DTO, ошибки безопасны, тесты и build проходят. Не push при необходимости менять business contract/schema, failing или skipped critical test, dirty tree, конфликте или выходе за scope.

## Commit и публикация

Проверь итоговый diff и stage только scope-файлы. Commit: `feat(calendar): add schedule read endpoint`. Затем `git fetch origin`, `git rebase origin/main`, повтори targeted tests, полный Release gate, build и `git diff --check`; только после PASS выполни `git push origin HEAD:main`. В финале перечисли изменённые файлы, проверки, real-PostgreSQL result, commit SHA и факт push.
