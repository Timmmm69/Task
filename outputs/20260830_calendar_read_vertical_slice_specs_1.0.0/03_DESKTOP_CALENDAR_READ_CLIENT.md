# ПРОМТ ДЛЯ НОВОГО ЧАТА — 03. Типизированный Desktop API client календаря

Ты работаешь в `C:\Users\novik\Таск`. Реализуй production typed WPF client для calendar read API. Это третье из пяти последовательных заданий; начинай только после успешного push обоих server increments в `origin/main`.

## Обязательное начало

Прочитай `AGENTS.md` и `work/delegation/README.md`; fetch origin, проверь чистый synchronized `main`, fast-forward. Подтверди server code/tests для `GET /api/v1/calendar`, `GET /api/v1/calendar-events/{id}` и `GET /api/v1/calendar/conflicts`, включая точные JSON/error contracts. Если endpoints или `Calendar.Read` отсутствуют, остановись.

Изучи `DesktopTasksApiClient`, `DesktopAuthenticatedGetExecutor`, `SessionService`, `DesktopServerConnection`, current error/result types, cancellation/disposal tests и capabilities flow. Переиспользуй authentication refresh/retry: не создавай второй token manager, сырой `HttpClient` lifecycle или competing error model.

## Публичный Desktop contract

Создай isolated calendar client namespace/folder, например `Task.Desktop/Calendar`. Нужны:

- `IDesktopCalendarApiClient`;
- `DesktopCalendarApiClient`;
- immutable DTO/result types, достаточные для следующего WPF screen;
- query value object для schedule range/filters, который строит URI deterministically.

Методы:

1. `GetScheduleAsync(fromUtc, toUtc, timezoneId, users, projects, status, cancellationToken)`;
2. `GetEventAsync(eventId, cancellationToken)`;
3. `GetConflictsAsync(fromUtc, toUtc, userIds, excludeObjectId, cancellationToken)`.

Не добавляй write methods. DTO обязаны сохранить object/event identity, type, title, local date, nullable instants/project/priority, status, page range/cursor; event details/timing/version/ETag/attendees; conflicts/severity. Enum strings parse строго; unknown item type/status/priority/severity, missing required property, invalid UUID/date/version или structurally inconsistent all-day/timed event дают controlled protocol failure, а не exception leakage и не partially valid object.

## HTTP и security behavior

- Используй existing authenticated GET executor, включая single refresh on 401 и session invalidation semantics.
- URI строится через escaped query values; repeated arrays имеют stable order, duplicates normalized/rejected согласно server contract; UTC instants отправляются с явным `Z`.
- Не логируй access/refresh token, password, raw sensitive response или connection string.
- Обрабатывай: success, 400/422 validation, 401/session/device revocation, 403 forbidden, 404 details missing, retryable 503/network/TLS, malformed/problem JSON, cancelled request.
- Сохраняй server `X-Correlation-ID`; для details сохрани/parse ETag и проверь соответствие version. Schedule/conflicts не должны придумывать ETag.
- Один вызов пользователя не должен бесконечно retry. Cancellation и dispose запрещают позднее применение результата.

## DI и capabilities

Подключи client к production Desktop composition root тем же способом, что tasks client. Не создавай Calendar ViewModel в этом задании. Убедись, что `Calendar.Read` capability доступна в существующей capability model; если generic set уже сохраняет code, не дублируй booleans без нужды. Любое изменение auth/session service должно быть минимальным и сопровождаться regression tests.

## Разрешённые изменения

- новые файлы `work/production/src/Task.Desktop/Calendar/**`;
- минимальный Desktop composition wiring (`App.xaml.cs` или существующий composition root);
- существующий authenticated GET executor только для reusable correctness fix;
- новый `work/production/tests/Task.Desktop.Tests/Calendar/DesktopCalendarApiClientTests.cs`;
- узкие existing security/client tests при изменении shared executor.

Не менять server/domain/migrations, WPF layout/ViewModels, visual resources, canonical sources, outputs, dependencies или Tasks semantics. Ориентир — до 8 файлов и 400 changed lines. Если shared auth abstraction требует крупного refactor, остановись.

## Обязательные тесты

Покрой exact request paths/query encoding; UTC `Z`; repeated filters; successful empty/mixed schedule mapping; all-day/timed mapping; details + ETag + attendees; conflicts; every enum; malformed/unknown/missing fields; invalid dates/UUID/version; 400/401-refresh/403/404/422/503; problem payload/correlation id; refresh only once; session revoked; cancellation; disposal; no secret leakage; no write methods. Сохрани все existing Tasks/security tests.

## Проверки

```powershell
cd work/production
dotnet format Task.sln --no-restore
dotnet test tests/Task.Desktop.Tests/Task.Desktop.Tests.csproj -c Release --filter "FullyQualifiedName~DesktopCalendarApiClient"
dotnet test tests/Task.Desktop.Tests/Task.Desktop.Tests.csproj -c Release
dotnet test Task.sln -c Release
dotnet build Task.sln -c Release --no-restore
git diff --check
```

## Acceptance, stop и публикация

Typed client полностью покрывает три read endpoints, использует единый session/auth pipeline, строго валидирует server payload, корректно классифицирует ошибки и не содержит write/offline mutation behavior. Все tests/build PASS.

Не push при server workaround, расширении public API сверх согласованного, dependency change, failing check, dirty tree, конфликте или scope overflow. Commit: `feat(desktop): add calendar read api client`. Затем fetch, rebase `origin/main`, повторный gate и `git push origin HEAD:main`. В финале перечисли contract/files/tests/SHA/push.
