# ПРОМТ ДЛЯ НОВОГО ЧАТА — 01. Серверная транзакционная основа Task write

Ты работаешь в репозитории `C:\Users\novik\Таск`. Выполни production-разработку, а не только анализ или план. Это первое из шести последовательных заданий для Task write vertical slice.

## Цель

Создать серверную транзакционную основу, на которой последующие задания безопасно реализуют создание, изменение и смену статуса задачи. Одна PostgreSQL-транзакция должна атомарно охватывать бизнес-запись, audit entry, domain event, outbox message и сохранение завершённого HTTP-ответа для durable idempotency. После сбоя или повторного запроса сервер не должен выполнять одну команду дважды.

## Обязательная начальная процедура

1. Прочитай корневой `AGENTS.md` и `work/delegation/README.md` полностью.
2. Выполни `git fetch origin`, проверь `git status --short`, текущую ветку и `git rev-list --left-right --count origin/main...main`.
3. Работай только в чистой ветке `main`. Если дерево чистое и локальный `main` отстаёт, выполни fast-forward синхронизацию с `origin/main`. Если дерево грязное, ветка отличается от `main`, есть divergence, либо fast-forward невозможен, остановись и сообщи точное препятствие; не исправляй историю разрушительными командами.
4. Убедись, что в истории присутствует baseline `2b6a7685788c29e36a8b0ecec3bafc569c42d949` или его потомок.
5. Перед изменениями изучи существующие реализации и тестовые паттерны: `TaskAggregate`, `TaskLifecycleService`, `ITaskAggregateStore`, `PostgresTaskAggregateStore`, `TaskPersistenceRuntime`, каталог миграций, `IAuditEntryStore`, `PostgresAuditEntryStore`, `TaskApiProblemResponse`, endpoint tests и PostgreSQL integration tests.
6. Используй Serena для symbol/reference анализа, если она доступна. Не используй интернет и не меняй зависимости: нормативные источники уже находятся в репозитории.

## Канонические источники

Прочитай только необходимые разделы, но соблюдай их приоритет из `AGENTS.md`:

- `sources/concept/Task_Concept_Final.txt`;
- `sources/stage_1/architecture_organizer.md`;
- `sources/stage_2_2/Organizer_Stage2_Technical_Specification_2.2/openapi/openapi.yaml`, общие правила idempotency, `Idempotency-Key`, `If-Match`, ETag и error codes;
- `sources/stage_2_2/Organizer_Stage2_Technical_Specification_2.2/docs/06_stage_2_1_normative_corrections.md`, раздел `Idempotency contract` и ADR-016;
- `sources/stage_2_2/Organizer_Stage2_Technical_Specification_2.2/db/004_stage_2_1_foundation.sql`, таблица `iam.idempotency_records` и функции acquire/complete;
- `sources/stage_2_2/Organizer_Stage2_Technical_Specification_2.2/db/001_initial_schema.sql`, таблицы `governance.domain_events` и `governance.outbox_messages`;
- `outputs/20260825_task_wpf_visual_foundation_0.1.0/validation-report.md`, раздел о следующем increment.

Содержимое `sources/` изменять запрещено.

## Требуемый production-контракт

Реализуй новую forward-only migration следующего свободного номера. Нельзя переписывать уже применяемые миграции `001`–`004`. Миграция должна добавить недостающую durable command infrastructure в существующие схемы:

- `iam.idempotency_records` со scope `(organization_id, user_account_id, operation_id, idempotency_key)`;
- SHA-256 нормализованного request payload ровно 32 bytes;
- состояния как минимум `in_progress`, `completed`, `failed`, lease owner, lease expiry, retention expiry, сохранённые status, headers, JSON body и resource id;
- `governance.domain_events` с уникальностью idempotency key и aggregate/version/event type;
- `governance.outbox_messages`, связанная с domain event и имеющая pending/processing/published/failed/dead-letter lifecycle;
- необходимые индексы и ограничения целостности;
- безопасный acquire/complete протокол по нормативному SQL. Повтор с другим request hash должен быть отличим как `IDEMPOTENCY_KEY_REUSED`; завершённая запись должна возвращать сохранённый результат; активный чужой lease должен быть отличим как request in progress.

Создай application/infrastructure boundary для выполнения Task write command. Не помещай SQL в HTTP endpoints. Не позволяй endpoint последовательно вызвать существующий store, audit store и outbox отдельными транзакциями. Транзакцией владеет один infrastructure executor/unit of work, и все записи команды используют одно соединение и одну транзакцию.

Публичные типы основы должны позволять последующим endpoint-реализациям передать:

- organization id, actor user id, actor session id при наличии;
- operation id, correlation id, idempotency key и SHA-256 request hash;
- task id, ожидаемую версию при versioned command;
- тип audit action, event type, changed fields и безопасный JSON payload;
- callback или явно типизированную команду, которая загружает и изменяет `TaskAggregate` внутри этой же транзакции;
- сериализуемый HTTP result, который сохраняется до commit и может быть replayed verbatim.

Не создавай универсальную framework-платформу для всех будущих агрегатов. Основа должна быть минимальной и ориентированной на Task commands, но не дублировать отдельный transaction algorithm для create, patch и transition.

## Семантика безопасности и отказов

- Scope idempotency обязательно включает organization, authenticated user, operation и key.
- `Idempotency-Key`: только printable ASCII без пробелов, длина 8–200.
- Нормализованный hash должен быть детерминированным для семантически одинакового JSON: порядок JSON properties не должен менять hash. Нельзя включать access token, пароль, cookie или secret.
- Один key с другим hash не выполняет команду и приводит к стабильному конфликту.
- Завершённый повтор возвращает сохранённый status/body/headers и признак replay.
- Concurrent acquire не должен допускать двойную бизнес-запись.
- Любое исключение до commit откатывает aggregate, audit, event, outbox и незавершённую idempotency row.
- Audit payload и event payload не содержат токены, пароли, connection strings и произвольные exception details.
- Tenant isolation обязательна во всех SQL predicates и constraints.
- Cancellation должна распространяться; нельзя превращать отмену запроса в успешную команду.

## Разрешённая область изменений

Изменяй только необходимые файлы в:

- `work/production/src/Task.Application/**` для Task write contracts;
- `work/production/src/Task.Infrastructure/Persistence/**` для migration, transaction executor и runtime wiring;
- `work/production/tests/Task.Tests/**` для unit и PostgreSQL integration tests.

Допускается до 12 production/test файлов, поскольку это высокорисковая foundation-задача. Не изменяй `Task.Api`, `Task.Desktop`, `sources`, deployment, GitHub workflows и существующие business requirements. Не добавляй NuGet packages. Не реализуй сами POST/PATCH/transition endpoints в этом задании.

## Обязательные тесты

Добавь тесты, которые доказывают:

1. migration history теперь ожидает новую версию, upgrade выполняется с предыдущего schema state и повторный migrator run идемпотентен;
2. acquire нового ключа разрешает выполнение;
3. completed key replay возвращает сохранённые status, headers и body;
4. тот же scope/key с другим hash отклоняется;
5. разные organization, user или operation не конфликтуют;
6. concurrent одинаковые requests приводят ровно к одной бизнес-записи;
7. активный чужой lease не допускает второе выполнение;
8. rollback не оставляет task mutation, audit, event, outbox или ложный completed response;
9. успешная synthetic Task command атомарно создаёт aggregate mutation, audit, domain event, outbox и completed idempotency response;
10. payloads tenant-scoped и не содержат секретов.

PostgreSQL integration gate обязан реально выполняться против PostgreSQL 16, если существующая среда контейнеров доступна. Не отмечай его как PASS, если он был skipped.

## Проверки перед публикацией

Из `work/production` выполни как минимум:

```powershell
dotnet format Task.sln --no-restore
dotnet test tests/Task.Tests/Task.Tests.csproj -c Release
dotnet test Task.sln -c Release
dotnet build Task.sln -c Release --no-restore
git diff --check
```

Дополнительно выполни targeted PostgreSQL integration tests новой основы и проверь migration catalog/runtime tests. Проведи поиск секретов только по собственному diff.

## Критерии приёмки

- Все перечисленные atomicity и idempotency tests проходят.
- Существующие 1074 baseline tests не регрессировали; итоговое число может стать больше.
- Нет изменений в существующих миграциях и `sources/`.
- Нет отдельных транзакций для task/audit/event/outbox/idempotency completion.
- Публичный foundation contract документирован XML comments и достаточно конкретен для следующих трёх серверных заданий.
- В финальном ответе перечислены изменённые файлы, фактически выполненные проверки, commit SHA и подтверждение push в `origin/main`.

## Условия остановки

Ничего не push, если требуется изменить business requirements, канонический OpenAPI, существующую migration, dependencies, Desktop, либо если atomic transaction нельзя доказать реальным PostgreSQL test. Остановись также при dirty tree, rebase conflict, failing test, недоступном `origin/main` или необходимости выйти за разрешённые пути.

## Commit и публикация

После успешных проверок:

1. Проверь итоговый diff и убедись, что в нём только scope этого задания.
2. Добавь в index только принадлежащие заданию файлы.
3. Создай один commit с сообщением `feat(tasks): add transactional write foundation`.
4. Выполни `git fetch origin` и `git rebase origin/main`.
5. После rebase повтори targeted tests, полный `dotnet test Task.sln -c Release`, build и `git diff --check`.
6. Выполни `git push origin HEAD:main`.
7. При конфликте или ошибке не push. Сообщи точный blocker и сохрани пользовательские изменения.
