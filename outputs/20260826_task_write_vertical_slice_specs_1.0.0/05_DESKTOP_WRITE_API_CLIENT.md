# ПРОМТ ДЛЯ НОВОГО ЧАТА — 05. Типизированный Desktop API client для Task write

Ты работаешь в `C:\Users\novik\Таск`. Реализуй transport/client слой WPF для создания, изменения и смены статуса задачи. Это пятое последовательное задание; начинай только когда все три write endpoints присутствуют и проверены в актуальном `origin/main`.

## Начальная проверка

Прочитай `AGENTS.md` и `work/delegation/README.md`. Fetch origin, проверь чистый synchronized `main`, fast-forward при необходимости. По endpoint code/tests зафиксируй точные body, headers, status codes, problem codes и response DTO. Если серверные create/PATCH/transition contracts расходятся между собой или не имеют ETag/idempotency/concurrency semantics, остановись; не маскируй server defect в Desktop.

Изучи `DesktopTasksApiClient`, `IDesktopTasksApiClient`, `DesktopAuthenticatedGetExecutor`, `SessionService`, `DesktopAuthApiClient`, security tests и current task DTO validation. Используй тот же certificate/session/refresh/logout behavior, что read-only client. Не создавай второй независимый session stack и не сохраняй tokens в UI layer.

## Цель

Расширь typed Desktop Task API boundary следующими операциями:

- create task: title, priority, nullable start/deadline, новый idempotency key;
- patch task: id, expected version, presence-aware editable fields, новый idempotency key;
- transition task: id, expected version, target status, optional reason, новый idempotency key.

DTO должны быть immutable records с validation на Desktop boundary. Idempotency key создаётся один раз на одну пользовательскую попытку и повторно используется при безопасном transport retry; новая сознательная команда пользователя получает новый key. `If-Match` формируется только из validated positive version как strong ETag `"vN"`.

## HTTP и session behavior

- Используй bearer access token через `SessionService`.
- При первом `401` выполни существующий single-flight refresh и повтори request ровно один раз с теми же body, correlation id, If-Match и Idempotency-Key.
- Если refresh невозможен, очисти сессию по существующему contract и верни typed AuthenticationFailure.
- Cancellation должна отменять request и не конвертироваться в failure result.
- Не делай automatic retry на `409`, `412`, `422` или неизвестный response.
- Не логируй request body, access/refresh tokens или credentials.
- JSON serialization должна быть deterministic enough для повторов: повтор одной команды отправляет byte-equivalent body.

## Typed results

Добавь исчерпывающие результаты, не заставляя ViewModel разбирать status code или raw JSON:

- `Succeeded<T>` с Task DTO, ETag/version и `WasReplayed`;
- `AuthenticationFailure`;
- `Forbidden`;
- `NotFound`;
- `ValidationFailure` с безопасным user-facing message и field errors, если сервер их даёт;
- `VersionConflict` для `412 VERSION_CONFLICT`;
- `PreconditionRequired` для `428` как protocol defect outcome;
- `IdempotencyConflict` и `RequestInProgress`;
- `InvalidTransition`;
- `ServerUnavailable`;
- `MalformedResponse`.

Не возвращай raw exception или server detail в UI. Любой success response повторно проходит существующую строгую Task DTO validation. Проверяй ETag: он должен соответствовать response version; mismatch означает MalformedResponse.

## Разрешённые изменения

- `work/production/src/Task.Desktop/Tasks/DesktopTasksApiClient.cs`;
- при необходимости один новый файл `DesktopAuthenticatedRequestExecutor.cs` рядом с security classes, который обобщает authenticated request без регрессии GET;
- минимальные изменения существующего GET executor только если повторно используется проверенный общий код;
- `work/production/tests/Task.Desktop.Tests/Tasks/DesktopTasksApiClientTests.cs`;
- при новом executor — один соответствующий security test file.

Не изменяй XAML, ViewModels, server, migration, sources, outputs, dependencies и visual resources. Не более 5 production/test files и 400 changed lines. Не добавляй стороннюю HTTP library.

## Обязательные тесты

Покрой точные method/path/body/headers для трёх operations; ETag serialization; idempotency key stability across refresh retry; новый key для новой команды; success/replayed; all typed error mappings; problem code parsing; ETag/body mismatch; malformed JSON; 401 refresh success/failure; single-flight compatibility; cancellation; no duplicate send beyond one post-refresh retry; URI validation; no token/body leakage.

## Проверки и acceptance

Выполни `dotnet format`, полный `Task.Desktop.Tests`, targeted task/security tests, `dotnet test Task.sln -c Release`, Release build, existing `verification/Test-DesktopShell.ps1`, `git diff --check` и secret scan diff.

Готово, когда ViewModel может пользоваться только typed interface и никогда не разбирает HTTP; read GET behavior не регрессировал; session refresh и idempotency headers доказаны tests.

Остановись без push при несовместимом server contract, необходимости менять server/XAML/dependencies, scope overflow, conflict или failing gate.

После PASS commit только scope с сообщением `feat(desktop): add typed task write api client`; fetch, rebase `origin/main`, повтор tests/build и push `origin HEAD:main`. Финальный ответ: файлы, tests, commit SHA, push.
