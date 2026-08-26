# ПРОМТ ДЛЯ НОВОГО ЧАТА — 02. Production endpoint создания задачи

Ты работаешь в репозитории `C:\Users\novik\Таск`. Реализуй production-код для создания задачи. Это второе из шести последовательных заданий. Оно начинается только после успешного слияния серверной транзакционной основы из задания `TASK-WRITE-FOUNDATION-01` в `origin/main`.

## Обязательная начальная процедура

Прочитай `AGENTS.md` и `work/delegation/README.md`. Выполни `git fetch origin`; проверь чистоту дерева, ветку `main` и divergence с `origin/main`; при чистом отстающем `main` сделай fast-forward. При dirty tree, diverged history или невозможности fast-forward остановись. Убедись по коду и тестам, что новая migration содержит durable idempotency, domain events и outbox, а Task write transaction executor действительно существует. Не полагайся на рассказ пользователя о предыдущем чате. Если foundation отсутствует или не обеспечивает одну транзакцию, остановись и не создавай временный обход.

Перед изменениями изучи текущие `TaskEndpoints`, authentication request context, permission policies, problem response, `TaskAggregate`, `TaskLifecycleService`, Task read response mapping, transaction executor и endpoint test fixtures. Используй существующие паттерны проекта. Не добавляй dependencies.

## Цель и контракт

Реализуй `POST /api/v1/tasks` для первого production write vertical slice. Endpoint обязан использовать authenticated organization и authenticated actor из server-derived request context. Клиент не может выбрать другую organization или выдать себя за другого автора.

Запрос этого slice содержит только:

- `title`: обязательная строка после trim, длина 1–500;
- `priority`: необязательное значение `low`, `normal`, `high`, `critical`, default `normal`;
- `startAtUtc`: необязательный RFC 3339 UTC instant с явным `Z`;
- `deadlineAt`: необязательный RFC 3339 UTC instant с явным `Z`; если обе даты заданы, deadline не раньше start.

Для совместимости с каноническим контрактом разрешается принять `authorUserId` только если он в точности равен authenticated user id; расхождение возвращает `403 FORBIDDEN`. Если текущая agreed foundation уже зафиксировала отсутствие этого body field, следуй принятому контракту и объясни это тестом/комментарием. Любые остальные Stage 2.2 TaskCreate properties должны приводить к `400 VALIDATION_FAILED`, а не молча игнорироваться. Это намеренно ограниченный vertical slice; не добавляй фиктивное хранение description, project, parent, assignees, watchers или recurrence.

Обязательные headers:

- `Idempotency-Key`, printable ASCII, 8–200 characters;
- optional `X-Correlation-ID`; существующий middleware создаёт его при отсутствии.

Успех: `201`, JSON task response в той же форме, которую уже используют GET endpoints, `ETag: "v1"`, `Idempotency-Replayed: false`. Повтор того же нормализованного запроса с тем же scope/key: тот же сохранённый status/body/ETag, `Idempotency-Replayed: true`, без новой задачи, audit, event или outbox.

## Права и ошибки

Добавь отдельную named policy с публичным смыслом `Task.Create`. До появления granular database permissions она может fail-closed использовать существующий backing permission `task.manage`, как текущий Task.Read bridge. Нельзя использовать read policy для write endpoint.

Стабильное отображение ошибок:

- malformed JSON или неизвестные/невалидные properties → `400 VALIDATION_FAILED` либо существующий `MALFORMED_JSON` pattern;
- отсутствующий/невалидный Idempotency-Key → `400 VALIDATION_FAILED`;
- authentication/session errors остаются в существующем security pipeline;
- нет write permission → `403 FORBIDDEN`;
- тот же key с другим hash → `409 IDEMPOTENCY_KEY_REUSED`;
- активная параллельная команда того же key → `409 IDEMPOTENCY_REQUEST_IN_PROGRESS` и разумный `Retry-After`;
- domain conflict → `409 INVALID_STATE_TRANSITION` или другой уже канонический code по причине;
- business validation → `422 VALIDATION_FAILED` только если request синтаксически корректен, но нарушает domain invariant;
- database unavailable → безопасный retryable `503`, без SQL/exception leakage.

## Atomic side effects

В одной foundation-транзакции:

1. acquire idempotency;
2. создать `TaskAggregate` с серверными UUID и UTC time;
3. применить priority/schedule через доменные методы или decision-complete create path без обхода invariants;
4. записать aggregate;
5. append audit action `task.create`, outcome `success`, object type `task`;
6. записать domain event `TaskCreated` с aggregate version 1;
7. записать outbox messages для минимально необходимых destinations, принятых foundation, без публикации внутри request;
8. сохранить точный HTTP response для replay;
9. commit.

Audit/event payload должен содержать только идентификаторы и безопасные changed fields; не копируй произвольный title в audit, если canonical redaction pattern этого не требует.

## Разрешённые изменения

Разрешено изменять только необходимые файлы:

- `work/production/src/Task.Api/Tasks/**`;
- `work/production/src/Task.Api/Security/TaskPermissionAuthorization.cs`;
- `work/production/src/Task.Api/Program.cs` только для DI/endpoint wiring, если foundation не использует существующую регистрацию;
- `work/production/src/Task.Application/**` только для create command service/contracts;
- `work/production/src/Task.Domain/TaskAggregate.cs` только если требуется атомарный create с поддерживаемыми initial fields;
- `work/production/tests/Task.ServiceHosts.Tests/TaskEndpointsTests.cs` и при необходимости один новый create-specific test file;
- `work/production/tests/Task.Tests/**` только для create application/domain tests.

Не меняй migration/schema, read endpoint semantics, Desktop, sources, outputs, deployment или dependencies. Ориентир: не более 8 production/test files и 400 changed lines. Если foundation требует переработки или лимит существенно превышается, остановись.

## Обязательные тесты

Покрой: 201 response и ETag; server-derived tenant/actor; default и явный priority; schedule validation; unknown property rejection; отсутствующий и неверный idempotency key; exact replay; hash mismatch; concurrent duplicate; forbidden write при разрешённом read; tenant isolation; audit/event/outbox exactly once; rollback при injected side-effect failure; отсутствие secret leakage; cancellation; сохранение совместимости GET созданной задачи.

Запусти targeted endpoint/application tests, реальный PostgreSQL create integration test, полный Release test suite и build. PostgreSQL test нельзя считать PASS при skip.

## Проверки

```powershell
cd work/production
dotnet format Task.sln --no-restore
dotnet test tests/Task.ServiceHosts.Tests/Task.ServiceHosts.Tests.csproj -c Release --filter "FullyQualifiedName~TaskEndpoints"
dotnet test tests/Task.Tests/Task.Tests.csproj -c Release --filter "FullyQualifiedName~Task"
dotnet test Task.sln -c Release
dotnet build Task.sln -c Release --no-restore
git diff --check
```

## Критерии приёмки и stop conditions

Endpoint действительно создаёт и затем читается через GET. Replay не создаёт дублей. Atomicity подтверждена PostgreSQL test. Permission fail-closed. Ошибки стабильны и не раскрывают детали. Все тесты проходят.

Не push при отсутствии foundation, необходимости менять migration/OpenAPI/business requirements, необходимости поддержать неподдерживаемые Task fields, конфликте, failing check, dirty tree или выходе за scope.

## Commit и публикация

Проверь diff, stage только scope, commit `feat(tasks): add idempotent task creation endpoint`. Затем `git fetch origin`, `git rebase origin/main`, повтори targeted/full tests, build и `git diff --check`, после чего `git push origin HEAD:main`. При любой ошибке ничего не push. В финальном ответе перечисли файлы, проверки, commit SHA и факт push.
