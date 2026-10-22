# Задача 14.1 — аудит контракта «Вернуть на доработку»

Версия: **1.0.0**. Дата: **22.10.2026**, Europe/Minsk. Репозиторий: `Timmmm69/Task`.
Проверенный `origin/main`: **fa504bc01584547afc50398ba6c1735391c4173c**. Повторный fetch перед оформлением не изменил SHA.

## 1. Результат и границы вывода

**Бизнес-переход `review → in_progress` прямо предусмотрен каноническим техническим контрактом 2.2, сохранён в финальном 2.3.1 и подтверждён утверждённым PRD 4.5.** Он возвращает незавершённую работу исполнителю; новый статус не требуется. Существующий HTTP-контракт уже позволяет отправить `targetStatus: "in_progress"` через `POST /api/v1/tasks/{id}/transition`.

Однако в репозитории есть прямое противоречие: более поздний production implementation prompt `04_SERVER_TASK_TRANSITION.md` явно требует отклонять этот переход. Сервер, доменные правила, desktop и тестовая матрица реализуют именно ограниченный production-срез. Отдельного утверждённого бизнес-решения, отменяющего канонический возврат, в изученных decision logs и относящихся к сценарию документах не найдено. Наличие кода и успешных тестов не доказывает утверждение изменения бизнес-модели.

**Противоречие не разрешено этим аудитом.** Поэтому READY для перехода к непосредственной реализации не присваивается. Требуется утверждённое согласование технической спецификации реализации с нормативной моделью; расширение OpenAPI DTO, БД, permissions или enum для самого возврата не выявлено как необходимое. Итоговый статус указан в конце.

Проверка выполнена на отдельном detached worktree от указанного `origin/main` в `work/status_return_14_1/repo`. Исходная рабочая папка оставлена на ветке `codex/employee-picker-5-1-verification`; её неотслеживаемые материалы сохранены. Production-код, тесты, схемы, API, permissions, `sources/` и roadmap не изменены. Изменение готовности продукта не заявляется. Commit/push не выполнялись.

## 2. Источники и их применимость

Все пути ниже относительны корню проверенного репозитория. Для ZIP после `::` указан путь внутри архива. В `source-evidence.json` зафиксированы SHA-256, точные строки и выдержки ключевых правил; исходники привязаны к проверенному commit, а не к меняющемуся HEAD.

| ID | Источник | Использование |
|---|---|---|
| S01 | `sources/concept/Task_Concept_Final.txt`, §7, §11.2, §17, §18–19 | Бизнес-статусы, роли, совместная работа, уведомления и история. Концепция допускает сокращённый MVP-набор, но сама не перечисляет все стрелки state machine. |
| S02 | `sources/stage_1/architecture_organizer.md`, §10, §12.3 | Разделение permissions и field authorization; серверная валидация, версии, транзакция, история и конфликт. |
| S03 | `sources/stage_2_2/Organizer_Stage2_Technical_Specification_2.2/docs/01_core_domain_and_data.md`, §9.2, §10; `docs/02_api_and_concurrency.md`, §19 | Исходный канонический технический контракт. Указанный в старом AGENTS ZIP 2.2 в checkout отсутствует; доступен распакованный каталог 2.2. Использован именно он, без восстановления/изменения `sources/`. |
| S04 | `sources/stage_2_3/Organizer_Stage2_Technical_Specification_2.3_Final.zip` :: `stage_2_3/docs/01_core_domain_and_data.md`, `docs/06_stage_2_1_normative_corrections.md`, `catalogs/permissions.csv`, `catalogs/events.csv`, `catalogs/errors.csv` | Финальный technical package **2.3.1**; state machine, транзакции, права, события и ошибки. Более позднее изменение 2.3 относится к urgency scale и employee search и не отменяет возврат. |
| S05 | Тот же ZIP :: `stage_2_3/openapi/openapi.yaml` | Актуальный финальный нормативный OpenAPI **1.2.0-stage2.3**, SHA-256 `36c15dff5adba0041fcfd79f5a0d203835dac5cdd4ad24122bcd92177c13220d`. Изучена версия из проверенного ZIP, не случайная рабочая копия. |
| S06 | `sources/stage_3_5/Organizer_Stage3_Final_Baseline_3.5.zip` :: `Stage_3_User_Flows_Final_3.5.md`, FLOW-007/008; `Stage_3_Contract_Delta_3.5.md`; `Stage_3_Field_Traceability_Final_3.5.csv` | Финальный UX targeted update к 3.4; server-allowed commands, narrow transition, reason if required, refresh истории, offline/read-only, 412. Manifest 3.5 явно указывает baseline 2.3.1. |
| S07 | `work/stage_4_6_lite/final_baseline/Organizer_Stage4_PRD_Candidate_4.5.zip` :: `Stage_4_Module_PRDs_4.5.md`, MOD-005, FR-038/FR-250, BR-035, AC-035/AC-1412; business rules/AC catalogs; decision log | Последний найденный утверждённый PRD baseline. Имя архива содержит Candidate, но файл `Stage_4_Final_Baseline_Approval.md` рядом прямо говорит: «Approved: Stage 4.5 + Stage 4.6 Lite PASS». Поэтому PRD 4.1.1/4.1.2/4.3 не использованы как текущие нормы. |
| S08 | `outputs/20260826_task_write_vertical_slice_specs_1.0.0/04_SERVER_TASK_TRANSITION.md`, §Transition matrix; README, §Граница вертикального среза | Более позднее техническое задание ограниченного production-среза, **конфликтующий implementation artifact**, а не доказательство принятого изменения бизнес-требований. Его явный запрет сохранён в реестре противоречий. |
| S09 | `work/production/docs/task-card-workflow.md`, `SEC-02-authorization.md`, `task-product-api.md` | Последующие implementation increments: карточка, комментарии, отдельные granular permissions и object scope. Описывают фактический продукт; не подменяют S03–S07. Старое временное backing `task.manage` уже не актуально. |

ZIP 2.3.1, UX 3.5 и PRD 4.5 сверены с соответствующими `.sha256`: совпадение. Старые/промежуточные копии не выбраны нормативными по одному только номеру версии. Противоречащие материалы не перемещались в quarantine: это отслеживаемые доказательства аудита, а не разрешённая пользователем чистка репозитория.

## 3. Бизнес-модель

### 3.1. Статусы и значение review

MVP enum: `new` — новая; `in_progress` — в работе; `review` — на проверке; `completed` — выполнена; `cancelled` — отменена. Основание: S01 §11.2; S03/S04 §9.2; S05 `Task.status`; S06 contract delta §3.2. Просрочка вычисляется по сроку и незавершённости, а не добавляется в enum. Запланированность и ожидание из расширенной концепции не вводятся как новые MVP-статусы.

`review` — незавершённая задача после передачи результата на проверку: state machine ведёт из `in_progress` в `review`, затем допускает принятие `completed`, возврат `in_progress: returned` или отмену. Это бизнес-статус активной задачи, не archive/trash и не синоним completed. Основание: S03/S04 §9.2, S07 BR-035 и FR-250.

### 3.2. Таблица разрешённых переходов

Таблица показывает **явно заданные стрелки S03/S04 §9.2**, а не все комбинации enum. Любое действие дополнительно требует прав, допустимого lifecycle и версии. Условные terminal workflows не включены в scope возврата с проверки.

| Из | В | Бизнес-смысл / условие нормы | Фактический сервер main | Источник |
|---|---|---|---|---|
| `new` | `in_progress` | Начать | Разрешает | S03/S04 §9.2 |
| `in_progress` | `review` | Передать результат на проверку | Разрешает | S03/S04 §9.2 |
| **`review`** | **`in_progress`** | **Вернуть на доработку (`returned`)** | **Отклоняет, 409** | **S03/S04 §9.2; S07 BR-035, FR-250** |
| `review` | `completed` | Принять результат | Разрешает | S03/S04 §9.2; S07 BR-035 |
| `new` | `cancelled` | Отмена | Разрешает | S03/S04 §9.2 |
| `in_progress` | `cancelled` | Отмена | Разрешает | S03/S04 §9.2 |
| `review` | `cancelled` | Отмена | Разрешает | S03/S04 §9.2 |
| `completed` | `in_progress` | `reopen with permission`; отдельное условное повторное открытие | Отклоняет | S03/S04 §9.2; точная дополнительная permission не названа диаграммой |
| `cancelled` | `new` | `restore workflow`; отдельный условный workflow | Отклоняет | S03/S04 §9.2; не смешивать с восстановлением из корзины |

Инициализация `[*] → new` не является изменением статуса существующей задачи. Same-state и прочие стрелки не получают разрешения из наличия enum. В частности, `new → completed` и `in_progress → completed` фактически разрешены production-кодом и S08, но не нарисованы в нормативной state machine S03/S04. FLOW-007 допускает completion nonterminal с оговоркой «Review required → only review transition»; применимость обязательной проверки конкретной задачи требует уточнения. Эти расхождения не исправлялись и не используются как разрешение новых переходов.

### 3.3. Кто отправляет, принимает и возвращает

S01 §7.3 разрешает сотруднику менять статус собственных задач; руководитель контролирует задачи в своём scope. S04/S05 и S06 FLOW-008 задают точное право **`Task.ChangeStatus`**, а не один только статус роли. Следовательно:

- отправить на проверку может actor с `Task.ChangeStatus` в области задачи, при `in_progress → review`;
- принять результат может actor с тем же effective permission и допустимой связью с задачей, при `review → completed`;
- возврат использует тот же permission и object scope; отдельный `Task.Return`, `Task.Review` или `Task.Approve` не установлен;
- `requesterUserId` (постановщик), `authorUserId` (автор), фактический creator и assignee — разные отношения. Поле постановщика само по себе не даёт `Task.ChangeStatus`;
- точное бизнес-ограничение «только постановщик», «только руководитель» или запрет исполнителю принимать собственный результат не установлено найденными нормами. Его нельзя добавить самостоятельно.

### 3.4. Причина, комментарий, история и уведомления

S05 `TaskTransitionRequest` требует только `targetStatus`. `reason` — необязательная nullable строка максимум 2000 символов. Условной нормы «при returned причина/комментарий обязательны» не найдено. S06 FLOW-008 говорит «reason if required», не вводя требование именно для возврата. Поэтому обязательный комментарий не входит в подтверждённый минимальный контракт.

`reason` не тождественен объекту Comment: его отправка в transition не требует `Comment.Create`. Автоматически создавать Comment отдельной операцией нельзя считать атомарной частью возврата. Если продукт потребует обязательную сохраняемую причину или комментарий, необходимо отдельное согласование обязательности, хранения, видимости, ошибок и атомарности; текущий аудит этого правила не вводит.

Каждый успешный transition обязан сохранить состояние/время/версию, history/audit/domain event/outbox одним commit; change feed строится после commit. Ошибки не должны оставлять частичный успех. Основание: S02 §12.3, S04 §9.2 и normative corrections §5–6. Норма события — `TaskStatusChanged`.

S01 §17.1 явно перечисляет уведомления «задача отправлена на проверку», «завершение задачи», «комментарий». S05 transition имеет `x-side-effects: timestamps,notify,outbox`; каталог событий предусматривает notification/search/realtime **as applicable**. Отдельный тип/шаблон/набор получателей «возвращена на доработку» явно не определён. Из общего `notify` нельзя вывести новый обязательный тип уведомления. Этот вопрос остаётся открытым; существующие правила видимости и настроек получателей должны сохраняться.

## 4. API operations и DTO

### 4.1. Операции

| Назначение | Method / path | operationId | Permission / результат |
|---|---|---|---|
| Получить актуальную задачу и версию | `GET /api/v1/tasks/{id}` | `GET_api_v1_tasks_id` | `Task.Read`; `200 Task` + ETag |
| Изменить статус, включая нормативный возврат | `POST /api/v1/tasks/{id}/transition` | `POST_api_v1_tasks_id_transition` | `Task.ChangeStatus`; `200 Task` + новый ETag + `Idempotency-Replayed` |
| История задачи | `GET /api/v1/tasks/{id}/history` | `GET_api_v1_tasks_id_history` | `History.Read`; `200 HistoryEntryPage` |
| Общая история объекта | `GET /api/v1/objects/{objectId}/history` | `GET_api_v1_objects_objectId_history` | `History.Read`; `HistoryEntryPage` в S05; production route не подтверждён этим аудитом |
| Добавить самостоятельный комментарий, если отдельно нужен | `POST /api/v1/objects/{objectId}/comments` | `POST_api_v1_objects_objectId_comments` | `Comment.Create`; `201 Comment`, `Idempotency-Key`; S05 не требует If-Match для этой операции |
| Реализованный task-specific комментарий | `POST /api/v1/tasks/{id}/comments` | В S05 операции нет; runtime `ProductApiRoute`: resource `tasks`, operation `task-comment-add` | `Comment.Create`; runtime route versioned/idempotent, parent-task If-Match |
| Bulk, вне данного сценария | `POST /api/v1/tasks/bulk-transition` | `POST_api_v1_tasks_bulk_transition` | `Task.ChangeStatus`, `BulkTaskTransition → BulkOperationResult`; per-item expectedVersion, общий idempotency key. Runtime поддержка не подтверждена; не обход ограничения single transition. |

`PATCH /api/v1/tasks/{id}` / `PATCH_api_v1_tasks_id` требует `Task.Update` и не должен использоваться как обход `Task.ChangeStatus`. S05 содержит `TaskPatch.status`, а production PATCH его отвергает по S08-смежному заданию `03_SERVER_TASK_PATCH.md`; это отдельное существующее расхождение спецификаций. Для возврата предназначен narrow transition.

### 4.2. Запрос возврата по уже существующей схеме

```http
POST /api/v1/tasks/{id}/transition
Authorization: Bearer <действующая сессия>
Content-Type: application/json
If-Match: "v7"
Idempotency-Key: <opaque-key-8-to-200-chars>
X-Correlation-ID: <uuid>

{"targetStatus":"in_progress","reason":null}
```

Это пример **нормативного запроса**, а не утверждение о текущем успехе: main сейчас вернёт 409 для задачи в review при успешных auth/version checks.

| DTO/поле | Точный контракт S05 | Фактическая обработка |
|---|---|---|
| path `id` | Обязательный UUID | `Guid.TryParseExact(..., "D")`, empty недопустим; скрытый/чужой/некорректный id → OBJECT_NOT_VISIBLE |
| `TaskTransitionRequest.targetStatus` | Required string: `new`, `in_progress`, `review`, `completed`, `cancelled` | Принимает все 5 значений синтаксически, затем отдельная state machine |
| `TaskTransitionRequest.reason` | Optional `string|null`, `maxLength: 2000` | Принимает null/omitted/строку; длина после trim ≤2000 по S08. Не передаёт reason доменной мутации/истории |
| Неизвестные поля | `additionalProperties: false` | Отклоняет; также отклоняет duplicate properties |
| `TaskUpdateModel` | Внутренняя модель field PATCH, не transition DTO | `Title`, `Priority`, presence-aware `StartsAtUtc`, `DeadlineAt`, `CardPatch`; статуса/reason в ней нет |
| Transition application command | Внутренний `TaskWriteCommand` | actor/session/org server-derived; id/key/hash/expectedVersion, AuditAction, EventType, mutation |

`Task` в S05: required `id`, `organizationId`, `version`, `title`, `authorUserId`, `status`, `priority`, `assigneeIds`, `watcherIds`. `id/organizationId/version/createdAt/updatedAt/startAtUtc/recurrenceSeriesId` — read-only. UUID связи: nullable `projectId`, `parentTaskId`, `requesterUserId`, `primaryCounterpartyObjectId`, `recurrenceSeriesId`; authorUserId — UUID. Остальные поля: `description` nullable ≤50000, `title` 1–500, `priority` low/normal/high/critical; `scheduledDate`, `startTimeLocal`, `scheduleTimeZone`, `startAtUtc`, `plannedDurationMinutes` 1–10080, `deadlineAt`; `assigneeIds/watcherIds` до 100 уникальных UUID. Transition возвращает актуальную карточку; статус/версия берутся из ответа сервера, а остальные поля не перезаписываются входным DTO.

Фактический `TaskResponse` находится в `TaskEndpoints.cs`: дополнительно имеет `completedAt`, `deadlineDate`, `deadlineTimeZone`, `deadlineReminderOffsetMinutes`, `deadlineReminderTime` из последующих increments. `completedBy` сохраняется в агрегате при Complete, но отдельного поля в этом response нет. Эти расширения не нужны в запросе возврата; `reason` не является полем Task response.

Нормативный `HistoryEntry`: `id`, `objectId`, `objectVersion` int64≥1, `changedAt`, `changeType`, `changedFields`, `correlationId` — все required. `HistoryEntryPage`: required `items`, optional nullable `nextCursor/total`. Отдельного reason в этой DTO нет. Фактическая проекция истории описана ниже.

Нормативный `CommentCreate`: required `body` 1–20000; optional `targetObjectId`, nullable `parentCommentId`, `status` active/deleted. Реализованный task-specific маршрут принимает только `body`, trimmed 1–50000; не следует объявлять его тем же DTO/operationId. Для простого возврата ни один comment DTO не нужен.

### 4.3. Версии и идемпотентность

Основание: S05 headers/operation и S04 normative corrections §5–6.

- `If-Match` обязателен: один strong ETag `"v<positive-int64>"`; нет header → **428 PRECONDITION_REQUIRED**, malformed/weak/multiple → runtime **400 VALIDATION_FAILED**, stale → **412 VERSION_CONFLICT**. Заголовок защищает задачу из URL.
- `Idempotency-Key` обязателен, 8–200 символов ASCII `!`–`~` (без пробела). Scope: organization + user + operation + key. Хранится SHA-256 нормализованного запроса и сериализованный HTTP result.
- Runtime hash включает taskId, expectedVersion и request JSON. Exact replay использует прежние body/version/key и возвращает сохранённый ответ; новый intent/body/версия требует нового key. Изменение reason тоже меняет hash, хотя reason не сохраняется как бизнес-текст.
- Replay проверяется до stale-version mutation check; иначе подтверждённый повтор ошибочно конфликтовал бы со своей же новой версией. Права/видимость повторно проверяются и для replay.
- Другой hash с тем же key → **409 IDEMPOTENCY_KEY_REUSED**. In-progress → runtime **409 IDEMPOTENCY_REQUEST_IN_PROGRESS** + Retry-After, повтор тем же key.
- Успех увеличивает version ровно на 1; audit/event/outbox/idempotency response атомарны. Нельзя последовательно отправить comment и transition и назвать их одной атомарной командой.

## 5. Permissions и security review

| Действие | Публичная capability | Runtime permission/policy | Дополнительные условия |
|---|---|---|---|
| Просмотр карточки | `Task.Read` | `task.read` / `permission.task.read` | Org/session, scope и `work.task_visible`; не даёт права смены статуса |
| Любая смена статуса | `Task.ChangeStatus` | `task.changestatus` / `permission.task.change-status` | Effective grant, отсутствие deny, доступный объект, допустимые lifecycle/transition/version |
| Возврат с review | Тот же `Task.ChangeStatus` | Та же policy; отдельного return permission нет | Нормативная стрелка должна быть реализована; сейчас её блокирует state machine |
| Добавление отдельного комментария | `Comment.Create` | `comment.create`, Product route policy | Право комментировать видимый объект; не требуется для необязательного reason |
| Чтение комментариев | `Comment.Read` | Соответствующая product capability | Не заменяет Comment.Create |
| Чтение истории | `History.Read` | `history.read`, Product route policy | Object scope; для отображения истории после возврата |
| Общий технический аудит | В нормативном каталоге `Audit.ReadAll` | Текущий security audit adapter использует `audit.entry.read` / `SecurityAudit.Read` | Это отдельный административный доступ, не prerequisite возврата |

Точные проверки находятся в `TaskPermissionAuthorization.cs`, `PostgresTaskWriteCommandExecutor.cs`, `PostgresTaskCardValidation.cs` и migration `013_object_authorization.sql`:

1. Endpoint требует `task.changestatus`; внутри tenant transaction executor перечитывает effective capability после organization advisory lock.
2. `EnsureVisible` проверяет `work.task_visible`; `EnsureAction` — `iam.object_allowed(...,'task.changestatus')`; эти проверки выполняются также при replay.
3. Для задачи видимость может следовать из creator/requester/assignee/watcher или доступного проекта. Для записи действуют отдельные scope/deny/project capability checks; creator и assignee имеют соответствующую status-action relation, watcher/requester сами по себе — нет. Проектный grant либо административная relation также проверяются с deny-wins и scope.
4. System employee имеет `task.changestatus` в явном шаблоне; observer — чтение без этого business write grant. Имя роли не отменяет explicit deny, department/expiry и object checks. Роль не означает доступ ко всем задачам.
5. `Task.Update`, `Task.Read` и старый временный `task.manage` нельзя подставлять вместо действующей status capability. Остаточные комментарии/названия тестов с TaskManage устарели; проверен фактический constant `task.changestatus`.

Нормы не выделяют отдельную «приёмку только другим человеком». Фактическая авторизация не различает допустимые targetStatus по reviewer identity. Добавление такого ограничения было бы изменением бизнес-политики. Проверять permission только в UI или разрешать возврат всем видящим карточку недопустимо.

## 6. Stable error codes

| HTTP / code | Основание и применение |
|---|---|
| 400 `MALFORMED_JSON` | Некорректный JSON; S05 + runtime parser |
| 400/422 `VALIDATION_FAILED` | Поля, enum, unsupported/duplicate properties, reason >2000; runtime malformed headers →400, слишком длинный reason →422 |
| 401 `AUTHENTICATION_REQUIRED`, `SESSION_EXPIRED`, `SESSION_REVOKED`, `DEVICE_REVOKED` | S05 и security pipeline; выбирать по фактической причине сессии, не подменять ими доменный конфликт |
| 403 `FORBIDDEN` | Нет capability/relationship; runtime policy denial. При скрытом объекте/внутритранзакционном отказе возможен OBJECT_NOT_VISIBLE |
| 404 `OBJECT_NOT_VISIBLE` | Отсутствующий, чужой или скрытый объект; без раскрытия tenant/наличия |
| 409 `INVALID_STATE_TRANSITION` | Действующая runtime реакция на review→in_progress, same-state и остальные запрещённые текущей матрицей стрелки |
| 409 `OBJECT_ARCHIVED`, `OBJECT_DELETED` | Нужно восстановить lifecycle прежде изменения статуса |
| 428 `PRECONDITION_REQUIRED` | Нет If-Match |
| 412 `VERSION_CONFLICT` | Stale version; не путать со старым примером 409 в narrative docs: финальный OpenAPI и normative corrections задают 412 |
| 409 `IDEMPOTENCY_KEY_REUSED` | Тот же scoped key, другой request hash |
| 409 `IDEMPOTENCY_REQUEST_IN_PROGRESS` | Фактический executor disposition, Retry-After; **не перечислен в x-error-codes этой операции S05**, требует учёта при согласовании документации |
| 413 `REQUEST_TOO_LARGE`; 503 `INTERNAL_ERROR` | Фактические дополнительные ветви endpoint: body >1 MiB / недоступность write infrastructure; не названы в response map transition S05 |

S05 описывает `application/problem+json` / `ProblemDetails`: required `type,title,status,code,traceId,correlationId,fieldErrors`; optional `detail,instance,currentVersion,currentEtag,retryAfterSeconds`. Не вводить новые коды специально для возврата без необходимости. Операция имеет широкий общий x-error-codes list (включая unrelated calendar/file/recurrence errors); он не доказывает достижимость каждого из них для transition.

## 7. Реализация, история и конфликты

Оба предварительно указанных пользователем пути актуальны. `TaskStatusTransitionCommandService` расположен **в том же файле** `Task.Application/TaskUpdateCommandService.cs`, а не в отдельном одноимённом файле.

| Участок | Фактическое поведение |
|---|---|
| `Task.Api/Tasks/TaskEndpoints.cs` | `MapTaskEndpoints` регистрирует POST transition с отдельной policy. `TryParseTransitionRequest` допускает in_progress и reason. `TransitionTaskAsync` создаёт application command; ловит domain conflict→409, stale→412, hidden→404. |
| `Task.Application/TaskUpdateCommandService.cs` / `ApplyTransition` | Только new→in_progress; in_progress→review; из трёх nonterminal → completed/cancelled. review→in_progress попадает в default INVALID_STATE_TRANSITION. |
| `Task.Domain/TaskRules.cs` и `TaskAggregate.Start/SubmitForReview/Complete/Cancel` | Та же ограниченная матрица. Start не даёт войти из review. Отдельной доменной команды возврата нет. Расширения только UI недостаточно. |
| `Task.Infrastructure/Persistence/PostgresTaskWriteCommandExecutor.cs` | Durable idempotency, permission recheck, expectedVersion, scoped load, mutation/save, audit `task.change_status`, event `TaskStatusChanged`, realtime outbox и notification projector, completion response одним transaction. |
| Payload transition | taskId, fromStatus, targetStatus, aggregateVersion, correlationId, actorId; changedFields = status (при Complete дополнительно completedAt/completedBy). reason в mutation payload не передаётся. Тест явно ожидает отсутствие reason. |
| `PostgresProductApiRelations.cs`, `PostgresProductApiTaskWorkspace.cs` | История отображается из `governance.domain_events`; отдельный history endpoint отдаёт id/action/version/actorUserId/occurredAt/changedFields, workspace — последние 200 событий и флаг History.Read. Payload с from/target/reason не выводится этой проекцией. |
| `PostgresEventNotifications.cs` | TaskStatusChanged создаёт notification только для target completed/review; для target in_progress нет notice_type и уведомление не создаётся. Это факт, но не доказательство существования обязательной return-notification нормы. |
| `TasksViewModel.cs` | CanChangeStatus отдельно от Read/Update; CanStart/CanSubmitForReview/CanComplete/CanCancel; `IsAllowedTransition` вызывает `TaskRules.CanTransition`. Begin/ConfirmTransition не дают отправить review→in_progress. reason необязателен. |
| `Task.Desktop/MainWindow.xaml` | Фактические кнопки: «Начать», «На проверку», «Завершить», «Отменить задачу». Кнопки «Вернуть на доработку» нет. Подтверждение InProgress сейчас «Начать задачу?» — для возврата нужен согласованный текст. |
| `DesktopTasksApiClient.cs` | `DesktopTransitionTaskCommand`: Id, ExpectedVersion, TargetStatus, optional Reason, один UUID IdempotencyKey на команду; клиент передаёт narrow body/headers, получает authoritative task. |
| Desktop conflict handling | 412 очищает pending transition и открывает conflict editor с предложением загрузить актуальную версию. Network uncertainty сохраняет pending command/key; in-progress разрешает отложенный retry тем же key. Key collision сбрасывает command для нового подтверждения; forbidden блокирует запись. Неизвестные/InvalidTransition ветви дают общий текст ошибки и очищают pending confirmation. |

## 8. Подтверждённые расхождения и их трактовка

| ID | Расхождение | Доказательство / риск / граница |
|---|---|---|
| D01 — blocking specification conflict | Канонические S03/S04 и утверждённый PRD S07 разрешают возврат; S08 прямо требует 409 | S04 §9.2: `review --> in_progress: returned`; S07 BR-035/FR-250; S08 строка 33: явный запрет. Зафиксировано, **не закрыто**. Нужен утверждённый correction к implementation specification, а не молчаливое изменение бизнес-модели. |
| D02 — functional gap | Сервер/Domain/Desktop не реализуют подтверждённую каноническую стрелку | ApplyTransition, TaskRules, TasksViewModel; complete-matrix tests ожидают rejection. Исправлять согласованно в следующих этапах; одной кнопки недостаточно. |
| D03 — other matrix differences | Production допускает прямое completion из new/in_progress и запрещает условные terminal recovery arrows | Нормативная диаграмма, S08 и actual matrix; FLOW-007 допускает policy-dependent completion. Вне scope 14.1 исправлений нет; не расширять будущую задачу возврата на reopening. |
| D04 — history shape | Runtime GET task history возвращает event projection, а не полный HistoryEntryPage contract | `PostgresProductApiRelations.Related`: отсутствуют normative objectId/objectVersion/changedAt/changeType/correlationId, вместо части полей другие имена. Факт не означает отсутствия event/audit в БД; отображение reason/from-target также не реализовано. |
| D05 — reason limitation | reason принимается/участвует в hash, но не сохраняется как reason/comment и не показывается историей | Parser + service payload + endpoint test assert. S08 допускает только redacted metadata, не обещает persisted task field. Обязательное сохранение причины нормой не подтверждено; **не объявляется уже нарушенным обязательным требованием**. |
| D06 — notification limitation | У in_progress нет notification projection | S05 общее notify против конкретной runtime матрицы completed/review; отдельная норма возврата/получателей не установлена. Требует уточнения желаемого поведения, не самовольного нового notification type. |
| D07 — contract documentation drift | TaskPatch.status; history/comments DTO; runtime response extensions и дополнительные error branches расходятся с финальным OpenAPI | S05 против S08/S09 и исходников. Для возврата existing transition DTO достаточен; полный API alignment не выполнен и не объявлен. |

## 9. Тесты и фактическая верификация

Проверены существующие тесты, новые production/test файлы не создавались:

- `Task.ServiceHosts.Tests/TaskTransitionEndpointsTests.cs`: 8 allowed combinations; остальные 17 из 25, включая review→in_progress, ожидают 409. Success assertions проверяют version, ETag, actor/from/target metadata и отсутствие reason. Есть body/header validation, missing/malformed If-Match, stale, foreign organization, archived/trashed, denied, exact replay/key collision, independent policy.
- `Task.Tests/TaskAggregateTests.cs`: Start/SubmitForReview, completion/cancellation, terminal restrictions; `TaskWorkStatusTests.cs`: ровно пять MVP enum values, без overdue; `TaskUpdateTests.cs`: concurrency/field updates; `Security/PermissionDecisionServiceTests.cs`: permission engine.
- `Task.Desktop.Tests/Tasks/TasksViewModelTests.cs`: version/server response, revocation, read-only и mutation commands; `DesktopTasksApiClientTests.cs`: transport, reason, ETag/key/error mapping.
- Прочитаны `PostgresTaskWriteCommandExecutorTests.cs` (concurrency, replay, atomic rollback) и `PostgresAuthorizationScenarioTests.cs` (scope/private objects/roles/deny). Их реальные PostgreSQL сценарии **не запускались** в этом документарном аудите; не выдаются за PASS.

| Выполненная проверка | Результат |
|---|---|
| Release `Task.ServiceHosts.Tests`, filter `TransitionTask_ OR TransitionPolicy_ OR TaskPermissionAuthorizationTests` | **44 passed, 0 failed, 0 skipped** |
| Release `Task.Tests`, filter `TaskAggregateTests OR TaskWorkStatusTests OR TaskUpdateTests OR PermissionDecisionServiceTests` | **76 passed, 0 failed, 1 skipped**: RealPostgres_PatchIsAtomicDurableAndConcurrencySafe; нет PostgreSQL integration fixture |
| Release `Task.Desktop.Tests`, filter `TasksViewModelTests OR DesktopTasksApiClientTests` | **132 passed, 0 failed, 0 skipped** |
| Проверка SHA-256 трёх финальных ZIP и версии нормативного OpenAPI | **PASS**; baseline provenance воспроизводима |
| Проверка tracked diff worktree | **PASS**, изменений production/source/test нет |

Итого **252 passed, 0 failed, 1 skipped**. TRX включены в пакет. Первый desktop filter содержал несуществующий namespace и выбрал 0 тестов; он не засчитан как проверка и исправлен повторным запуском указанного рабочего filter. Build предупреждения ASPDEPR004 и xUnit1031/xUnit2031 в существующих файлах не исправлялись. Фактический success возврата, PostgreSQL transaction, real role/scope scenarios, realtime и визуальный WPF сценарий возврата остаются непроверенными; функции ещё нет. Действующая матрица протестирована, нормативная полнота — нет.

## 10. Открытые вопросы и переход к 14.2

| ID | Что требуется решить | Значение для следующего этапа |
|---|---|---|
| OQ-14.1-01 | Утвердить устранение явного запрета S08 для review→in_progress в пользу канонического возврата либо оформить явное изменение бизнес-модели | **Блокирует начало production-реализации**. Этот аудит не выбирает решение за владельца требований. Правка может быть узкой correction implementation spec, без изменения HTTP DTO. |
| OQ-14.1-02 | Нужна ли сохраняемая/показываемая причина возврата и где именно; нужна ли её обязательность | Базовый контракт optional. Нельзя обещать сохранённую причину или сделать Comment.Create prerequisite молча. Если обязательность нужна — согласовать отдельное правило и его атомарность/ошибки. |
| OQ-14.1-03 | Нужны ли отдельное уведомление о возврате и конкретные получатели | Сейчас нормы называют только общий notify/as applicable; runtime return notification отсутствует. Зафиксировать согласованное поведение до его acceptance tests. |
| OQ-14.1-04 | Требуется ли ограничить приём/возврат только постановщиком/руководителем и запретить self-acceptance | Текущая подтверждённая политика — Task.ChangeStatus + object scope; отдельного reviewer identity contract нет. Новую политику не добавлять без решения. |
| OQ-14.1-05 | Как учитывать terminal recovery, прямое completion, TaskPatch/history/response drift | Отдельные recorded gaps. Не включать их автоматом в узкую реализацию возврата и не считать закрытыми этим аудитом. |

К **14.2 можно перейти как к этапу согласования/подготовки точного implementation contract**, используя этот аудит. Если 14.2 подразумевает немедленное изменение production-кода, сначала требуется закрыть OQ-14.1-01 и зафиксировать scope решений по причине/уведомлению; optional reason и существующая permission остаются baseline до явного изменения.

Минимальный подтверждённый технический путь после согласования: та же операция POST transition, тот же enum и Task.ChangeStatus; согласованное доменное действие из review; версия +1; прежние lifecycle/security checks; атомарные audit/event/outbox/idempotency; desktop command с корректным текстом; обновление всех матричных тестов, включая real PostgreSQL/replay/concurrent stale/deny scenarios. Это описание границы следующей задачи, а не выполненная реализация.

**Итоговый статус: `CONTRACT_CHANGE_REQUIRED`.** Бизнес-требование подтверждено; HTTP-контракт уже выразителен, но противоречащая спецификация production-реализации требует утверждённого исправления/согласования. `BUSINESS_APPROVAL_REQUIRED` не выбран, поскольку нормативное подтверждение возврата существует; `NOT_ALLOWED` не выбран, поскольку канонический контракт его разрешает; `READY` не выбран из-за незакрытого явного противоречия.
