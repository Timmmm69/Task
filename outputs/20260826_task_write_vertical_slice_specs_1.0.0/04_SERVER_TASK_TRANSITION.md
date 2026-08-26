# ПРОМТ ДЛЯ НОВОГО ЧАТА — 04. Production endpoint смены статуса задачи

Ты работаешь в `C:\Users\novik\Таск`. Реализуй production `POST /api/v1/tasks/{id}/transition`. Это четвёртое последовательное задание. Начинай только после появления в актуальном `origin/main` транзакционной write foundation, create endpoint и concurrency-safe PATCH endpoint.

## Начало работы

Прочитай `AGENTS.md` и `work/delegation/README.md`. Fetch origin, проверь чистый synchronized `main`; только fast-forward, никаких destructive commands. Подтверди prerequisites кодом и тестами. Изучи `TaskAggregate`, `TaskWorkStatus`, `TaskLifecycleService`, write executor, Task response mapping, create/PATCH endpoints, permission/error patterns и test fixtures.

## Endpoint contract

Request body:

```json
{
  "targetStatus": "in_progress",
  "reason": null
}
```

`targetStatus` обязателен и допускает `new`, `in_progress`, `review`, `completed`, `cancelled`. `reason` необязателен, null либо строка после trim не длиннее 2000; reason может попасть только в redacted audit/event metadata согласно проектному паттерну, но не должен отображаться как поддерживаемое persisted task field. Unknown properties отклоняются.

Обязательны `If-Match: "vN"` и `Idempotency-Key` 8–200 printable ASCII. Success: `200`, актуальный Task JSON, новый ETag и `Idempotency-Replayed: false`; exact replay возвращает сохранённый ответ и true.

## Transition matrix

Используй только доменные методы, не присваивай status напрямую:

- `new → in_progress` через Start;
- `in_progress → review` через SubmitForReview;
- `new|in_progress|review → completed` через Complete;
- `new|in_progress|review → cancelled` через Cancel.

Нельзя придумывать reopening или возвращение к `new`: transitions из `completed`/`cancelled`, `review → in_progress`, same-state переход и любые другие комбинации возвращают `409 INVALID_STATE_TRANSITION`. Archive/trash/restore остаются отдельными endpoints и не реализуются здесь.

Completion обязан атомарно заполнить `completedAt` и `completedBy` server-derived actor. Остальные transitions не должны оставлять completion fields. Version увеличивается ровно на один.

## Security и concurrency

Добавь отдельную Task.ChangeStatus policy, временно fail-closed backed by `task.manage`. Нельзя использовать Task.Update/Read как замену.

- missing If-Match → `428 PRECONDITION_REQUIRED`;
- malformed If-Match → `400 VALIDATION_FAILED`;
- stale → `412 VERSION_CONFLICT`;
- hidden/cross-tenant → `404 OBJECT_NOT_VISIBLE`;
- invalid transition/lifecycle → `409` без mutation;
- invalid body → `400` или `422` по существующей согласованной границе;
- idempotency collision/in-progress → соответствующие стабильные `409`;
- forbidden → `403`.

## Atomic transaction

Успех в одной foundation transaction создаёт aggregate mutation, audit action `task.change_status`, domain event `TaskStatusChanged`, outbox и completed replay response. Payload фиксирует task id, from status, target status, aggregate version, correlation id и actor id; секретов и exception details нет. Любая ошибка откатывает всё. Повтор не создаёт новый event. Concurrent stale transitions дают ровно одного победителя.

## Разрешённая область

Разрешены только узкие изменения в `Task.Application` lifecycle/command service, `Task.Api/Tasks/**`, `Task.Api/Security/TaskPermissionAuthorization.cs`, `Task.Api/Program.cs` при необходимости и соответствующих domain/application/endpoint tests. Не меняй domain transition rules без явного противоречия каноническому источнику; не меняй migration, create/PATCH contracts, Desktop, sources, outputs, dependencies или deployment. Не более 8 production/test files и примерно 400 changed lines.

## Acceptance tests

Обязательно покрой каждый разрешённый переход; все запрещённые переходы; completion metadata; one-version increment; archived/trashed; permission denial; tenant-safe not found; missing/malformed/stale If-Match; exact replay; reused key with different hash; concurrent same/different transition; atomic rollback; exactly-one audit/event/outbox; response ETag; GET после transition.

Выполни formatting, targeted tests, реальный PostgreSQL transition integration test, полный Release suite/build, `git diff --check` и secret scan. Skip real database test не является PASS.

## Stop, commit, publication

Не push при отсутствии prerequisites, необходимости менять schema/business rules/contract foundation, выходе за scope, конфликте или failing test.

После PASS stage только собственные файлы, commit `feat(tasks): add idempotent status transition endpoint`; fetch, rebase `origin/main`, повтор relevant/full gates и push `origin HEAD:main`. В финальном ответе дай commit SHA, файлы, tests и факт push.
