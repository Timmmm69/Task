# ПРОМТ ДЛЯ НОВОГО ЧАТА — 03. Production endpoint изменения задачи

Ты работаешь в `C:\Users\novik\Таск`. Реализуй production `PATCH /api/v1/tasks/{id}` для уже принятого ограниченного Task write vertical slice. Это третье последовательное задание; начинай только после того, как транзакционная foundation и `POST /api/v1/tasks` присутствуют в актуальном `origin/main`.

## Начальная процедура и prerequisites

Полностью прочитай `AGENTS.md` и `work/delegation/README.md`. Выполни fetch, проверь чистый `main`, синхронизируй только fast-forward. Остановись при dirty/diverged state. По коду и тестам проверь наличие durable idempotency transaction executor и реально работающего create endpoint с ETag/replay; без них не продолжай.

Изучи `TaskAggregate`, `TaskSchedule`, `TaskLifecycleService`, aggregate store, Task read mapping, create endpoint, permission policies, problem response и endpoint/integration tests. Не меняй зависимости и нормативные источники.

## Цель и request semantics

Endpoint изменяет только уже хранимые editable fields:

- `title`: non-null string, trim, 1–500;
- `priority`: `low`, `normal`, `high`, `critical`;
- `startAtUtc`: UTC instant либо explicit `null` для очистки;
- `deadlineAt`: UTC instant либо explicit `null` для очистки.

PATCH использует presence-aware DTO: omitted property остаётся неизменной; explicit null очищает только nullable schedule property; null для title/priority недопустим. Пустой object и неизвестные properties отклоняются. Поле `status` запрещено в PATCH: статус меняется только отдельным transition endpoint. Все остальные пока неподдерживаемые Stage 2.2 properties отклоняются, а не игнорируются.

Одним PATCH можно изменить несколько полей, но aggregate version должна увеличиться ровно один раз. Добавь в domain/application один атомарный update operation, который валидирует итоговое состояние целиком и вызывает `RecordVisibleChange` один раз. Не вызывай последовательно Rename, ChangePriority и Reschedule, если это увеличит version несколько раз. No-op, при котором итоговые значения совпадают с текущими, должен вернуть текущий representation без ложного domain event; выбери и зафиксируй idempotent behavior в тесте, сохраняя корректный replay response.

## Headers и ответы

- Обязателен strong `If-Match` строго вида `"v<positive-int64>"`.
- Для этого production slice обязателен `Idempotency-Key` 8–200 printable ASCII, чтобы retry PATCH был crash-safe; это допустимое усиление канонического `Conditional` idempotency.
- `X-Correlation-ID` поддерживается существующим middleware.
- Success: `200`, актуальный Task JSON, новый `ETag`, `Idempotency-Replayed: false`.
- Exact replay: сохранённый response и `Idempotency-Replayed: true` без новой версии/side effects.

## Concurrency, visibility и ошибки

- Missing If-Match → `428 PRECONDITION_REQUIRED`.
- Malformed/weak/multiple/wildcard If-Match → `400 VALIDATION_FAILED`; не угадывай версию.
- Stale version → `412 VERSION_CONFLICT`; не перезаписывай данные.
- Невалидный/отсутствующий Idempotency-Key → `400 VALIDATION_FAILED`.
- Key reuse с другим normalized request hash → `409 IDEMPOTENCY_KEY_REUSED`.
- Активная duplicate request → `409 IDEMPOTENCY_REQUEST_IN_PROGRESS` и `Retry-After`.
- Чужая organization или невидимая task → одинаковый `404 OBJECT_NOT_VISIBLE`, без oracle.
- Archived/trashed/terminal update → canonical `409` с подходящим `OBJECT_ARCHIVED`, `OBJECT_DELETED` или `INVALID_STATE_TRANSITION`.
- Field/domain violation → `422 VALIDATION_FAILED`.
- Нет Task.Update permission → `403 FORBIDDEN`.

Добавь отдельную Task.Update named policy, временно backed by `task.manage`, но не используй read/create policy.

## Atomic side effects

Успешное фактическое изменение одной транзакцией сохраняет aggregate, audit action `task.update`, domain event `TaskUpdated`, outbox и completed idempotency response. Event включает version и список реально изменённых полей. Stale, forbidden, invalid, not found и no-op не создают success audit/event/outbox. Security-denial logging можно оставить существующему security pipeline; не создавать ложный object-level audit, раскрывающий наличие чужой task.

## Разрешённые файлы

- `work/production/src/Task.Domain/TaskAggregate.cs`;
- `work/production/src/Task.Application/TaskLifecycleService.cs` либо новый узкий Task update service/contract;
- `work/production/src/Task.Api/Tasks/**`;
- `work/production/src/Task.Api/Security/TaskPermissionAuthorization.cs`;
- `work/production/src/Task.Api/Program.cs` только при обязательном wiring;
- соответствующие `TaskAggregateTests`, `TaskLifecycleServiceTests`, `TaskEndpointsTests` либо один новый узкий test file.

Не изменяй migration, transaction foundation, create behavior, Desktop, sources, outputs, deployment и dependencies. Не более 8 production/test files и примерно 400 changed lines. При необходимости переделать foundation остановись.

## Тесты и проверки

Покрой single-field и multi-field patch, presence/null semantics, one-version increment, no-op, invalid final schedule, terminal/lifecycle guards, missing/malformed/stale If-Match, tenant-safe 404, permission denial, exact replay, different hash, concurrent request, rollback, exactly-once audit/event/outbox, GET after PATCH и отсутствие status mutation через PATCH.

Выполни formatting, targeted domain/application/endpoint tests, реальный PostgreSQL PATCH gate, полный `dotnet test Task.sln -c Release`, Release build, `git diff --check` и secret scan собственного diff. Skip реального DB test не является PASS.

## Stop conditions, commit и push

Остановись без push при отсутствующих prerequisites, необходимости менять migration/OpenAPI/business requirements, выходе за scope, конфликте или failing check.

После успешной проверки stage только scope, commit `feat(tasks): add concurrency-safe task patch endpoint`; затем fetch, rebase `origin/main`, повтор всех relevant checks и `git push origin HEAD:main`. Финальный ответ должен назвать commit SHA, изменённые файлы, результаты tests и подтвердить push.
