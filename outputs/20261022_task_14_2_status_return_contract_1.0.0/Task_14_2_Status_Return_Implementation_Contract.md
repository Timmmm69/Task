# Task 14.2 — согласованный контракт «Вернуть на доработку»

Версия: **1.0.0**. Дата: **22.10.2026**, Europe/Minsk.
Baseline: `Timmmm69/Task`, `origin/main` **fa504bc01584547afc50398ba6c1735391c4173c**.

## 1. Решение и полномочия

Основание: аудит `Task_14_1_Status_Return_Contract_Audit.md` версии 1.0.0 и последующее поручение пользователя: «сделай всё лучшим образом, я доверяю». Это поручение принимается как разрешение выбрать и зафиксировать способ устранения противоречия, без изменения бизнес-требований. Ограничение исходной задачи на изменения production-кода сохраняется: настоящий пакет является документарным контрактом следующего этапа, а не реализацией функции.

Применяется явная каноническая стрелка **review → in_progress: returned**. Она присутствует в техническом контракте 2.2, сохранена в финальном 2.3.1 и подтверждена утверждённым PRD 4.5 (BR-035, FR-250). Спецификация ограниченного production-среза от 26.08.2026 не отменяет это бизнес-требование.

**DEC-14-RETURN-001:** для будущей реализации возврата настоящее уточнение заменяет только запрет `review → in_progress` в `outputs/20260826_task_write_vertical_slice_specs_1.0.0/04_SERVER_TASK_TRANSITION.md`, строка 33. Исторический пакет остаётся неизменным. Остальные переходы, условия и противоречия того пакета не пересматриваются автоматически.

Новое решение о составе бизнес-статусов, ролей или API не принимается. Для согласования этой уже нормативной стрелки не нужны новый endpoint, новый permission, migration или расширение DTO.

## 2. Закрытие вопросов 14.1

| Вопрос аудита | Принятое решение | Граница |
|---|---|---|
| OQ-14.1-01: запрет в implementation prompt | Применить каноническую стрелку и DEC-14-RETURN-001 | Документарный блокер снят. Код и тесты остаются отстающей реализацией до отдельного этапа. |
| OQ-14.1-02: причина/комментарий | Причина необязательна, отдельный комментарий не обязателен | Новая кнопка возврата использует минимальный запрос с `reason: null`; отдельный ввод причины в этом действии не добавляется. Это избегает обещания сохранённого текста, которого текущая история не обеспечивает. Общий API по-прежнему принимает optional reason по существующей схеме. |
| OQ-14.1-03: уведомления | Использовать существующие TaskStatusChanged, outbox и механизм обновления данных | Новый тип уведомления о возврате, получатели и шаблон не вводятся. Общий notification projector продолжает существующее поведение. Факт изменения статуса должен попасть в историю и данные клиента. |
| OQ-14.1-04: кто принимает/возвращает | Существующие Task.ChangeStatus + object scope + deny-wins | Не добавляются reviewer identity, обязательная роль руководителя, запрет self-acceptance или право по одному лишь просмотру. |
| OQ-14.1-05: остальные расхождения | Сохранить как отдельные открытые вопросы | Reopening, cancelled→new, direct completion, TaskPatch.status и общий history DTO drift не входят в этот инкремент. |

Опциональная причина в общем API не означает гарантированного сохранения/отображения: аудит установил, что сейчас она валидируется и входит в request hash, но не передаётся в business event payload. Исправление этого общего поведения не маскируется под добавление возврата. Сотрудник при необходимости может отдельно добавить обычный комментарий при наличии Comment.Create; это самостоятельное действие, не prerequisite и не атомарная часть возврата.

## 3. Бизнес-инварианты

1. Только активная задача со статусом `review` может выполнить действие «Вернуть на доработку»; результат — `in_progress`.
2. Набор статусов остаётся `new`, `in_progress`, `review`, `completed`, `cancelled`. Просрочка вычисляется по прежним правилам.
3. Проверка возврата не меняет правила new→in_progress и in_progress→review. Другие действующие переходы не расширяются.
4. Возврат не является повторным открытием выполненной задачи, восстановлением из архива/корзины или изменением постановщика/исполнителя.
5. Сохраняются title, description, schedule, deadline, priority, project, participants, checklist, связи и остальные данные карточки. Поведение вычисляемых признаков следует существующим правилам.
6. Version увеличивается ровно на один; updatedAt/updatedBy фиксируют server-derived actor/time. У review и результата in_progress отсутствует факт завершения; completedAt/completedBy остаются null.
7. UI меняет подтверждённый статус только по authoritative server response. Повторная отправка при неопределённом результате не создаёт второго изменения.

Нормативные источники: технический контракт 2.2/2.3.1 §9.2; OpenAPI 1.2.0-stage2.3; UX 3.5 FLOW-008; PRD 4.5 BR-035/FR-250. Точные пути, SHA-256 и строки приведены в `source-evidence.json` пакета 14.1; этот контракт связывается с ним хэшами в `manifest.json`.

## 4. HTTP, DTO, права и ошибки

```http
POST /api/v1/tasks/{id}/transition
Content-Type: application/json
Authorization: Bearer <session>
If-Match: "v7"
Idempotency-Key: <opaque-key>
X-Correlation-ID: <uuid>

{"targetStatus":"in_progress","reason":null}
```

- operationId: `POST_api_v1_tasks_id_transition`.
- Required targetStatus использует существующий enum; optional reason сохраняет тип `string|null` и maxLength 2000. Новых свойств нет.
- Success: `200 Task`, `ETag: "v8"`, `Idempotency-Replayed: false`; exact replay — сохранённый ответ и `true`.
- Permission: публичное `Task.ChangeStatus`, runtime `task.changestatus`, policy `permission.task.change-status`; дополнительно существующие organization/session, visibility, object relation, scope, expiry и deny checks.
- Просмотр требует Task.Read; право просмотра само по себе возврат не разрешает. Comment.Create для `reason: null` не требуется. History.Read управляет чтением истории, не разрешением перехода.
- If-Match защищает задачу из URL; idempotency scope = organization/user/operation/key. Key 8–200 ASCII `!`–`~`. Request hash содержит taskId, expectedVersion и body. Retry неопределённого результата сохраняет исходные key/body/version.

| Ситуация | Существующий ответ |
|---|---|
| Missing If-Match | 428 PRECONDITION_REQUIRED |
| Malformed If-Match / invalid DTO | 400 VALIDATION_FAILED; field-length violations — по существующей границе 422 |
| Stale version | 412 VERSION_CONFLICT |
| Недопустимый текущий статус / same-state | 409 INVALID_STATE_TRANSITION |
| Archived / trashed | 409 OBJECT_ARCHIVED / OBJECT_DELETED |
| Нет capability | 403 FORBIDDEN |
| Hidden / cross-tenant / object scope denial | 404 OBJECT_NOT_VISIBLE по существующему fail-closed mapping |
| Key с другим hash | 409 IDEMPOTENCY_KEY_REUSED |
| Команда выполняется | 409 IDEMPOTENCY_REQUEST_IN_PROGRESS + Retry-After |
| Сессия недействительна / сеть недоступна | Существующие authentication/network branches; локальный успех не показывается |

Новые stable error codes не нужны. Известное расхождение документации дополнительных runtime error branches с OpenAPI остаётся зарегистрированным в аудите 14.1.

## 5. Технические точки реализации

Production-изменения в этом этапе не выполняются. Следующая реализация должна быть одним согласованным инкрементом, а не отдельной кнопкой поверх отклоняющего запрос сервера.

| Файл/символ | Требуемый результат следующего этапа |
|---|---|
| `work/production/src/Task.Domain/TaskRules.cs` / CanTransition | Добавить ровно review→in_progress; сохранить остальные 24 комбинации согласно текущему baseline этого инкремента. |
| `work/production/src/Task.Domain/TaskAggregate.cs` | Предпочтительно явный доменный метод ReturnForRework для review→in_progress с active guard и RecordVisibleChange. Start должен сохранить смысл «начать новую»: после расширения общей матрицы требуется явная проверка source New, иначе он случайно начнёт принимать review. |
| `work/production/src/Task.Application/TaskUpdateCommandService.cs` / TaskStatusTransitionCommandService.ApplyTransition | Добавить review→in_progress через доменный метод; сохранить существующий transaction executor, AuditAction, EventType, hash и permission revalidation. |
| `work/production/src/Task.Desktop/ViewModels/TasksViewModel.cs` | Отдельный CanReturnForRework только для review; CanStart только для new. Возврат использует существующий TransitionCommand с target InProgress, корректное подтверждение и reason=null. Состояние доступности обновляется при selection/status/capability/network/busy changes. |
| `work/production/src/Task.Desktop/MainWindow.xaml` | Кнопка «Вернуть на доработку», доступная по CanReturnForRework; подтверждение «Вернуть задачу на доработку?» и «Вернуть». Для этого действия не показывать поле причины. Не переименовывать normal Start в возврат. |
| История / TaskWorkspace | После успеха перечитать данные и историю существующим механизмом; событие TaskStatusChanged отображается с актуальной версией. Не заявлять отображение причины или полную совместимость HistoryEntry DTO. |
| Тесты Domain, ServiceHosts, Desktop, PostgreSQL | Согласованно обновить матрицу, покрыть guards, security/concurrency, transport и UI state. |

Расширение TaskRules используется также PersonalTaskStore и PersonalTasksViewModel. До изменения необходимо проверить все call sites общей матрицы: canonical review→in_progress может стать доступен там автоматически. Нельзя случайно изменить semantics Start, обходить ownership или расширить другие переходы; покрыть соответствующую регрессию. Не создавать параллельную несовместимую state machine только ради UI.

## 6. Транзакция и защита от гонок

Существующий PostgresTaskWriteCommandExecutor должен сохранить порядок: authorization lock/revalidation → idempotency acquire/replay → actor/session validation → tenant load/version check → visibility/object-action check → domain mutation → aggregate save → audit + event + outbox → durable HTTP result → commit. Replay повторно проверяет права и не выполняет mutation.

Для возврата changedFields содержит `status`; audit action `task.change_status`; event `TaskStatusChanged`; payload фиксирует taskId, fromStatus=review, targetStatus=in_progress, aggregateVersion, actorId, correlationId. Полезная нагрузка не включает необещанный текст причины. Исключение до commit не оставляет версии, success audit/event/outbox или ложного завершённого idempotency result.

Если два actor одновременно принимают и возвращают одну review-задачу с одной версией, ровно один новый intent может изменить задачу; второй получает VERSION_CONFLICT. Realtime события не подменяют authoritative HTTP response и не должны переигрывать локальную команду.

## 7. Acceptance criteria следующего этапа

| ID | Проверка / требуемый результат |
|---|---|
| AC-14-01 | Active review → in_progress: version+1, updated actor/time, completion metadata null, прочие поля карточки сохранены. |
| AC-14-02 | ReturnForRework отвергает new, in_progress, completed, cancelled, archived и trashed. Start продолжает отвергать review. |
| AC-14-03 | Endpoint matrix: к 8 текущим allowed pairs добавляется только review→in_progress; итого 9 allowed и 16 forbidden комбинаций. Same-state не разрешается. |
| AC-14-04 | Syntax/header/body errors и lifecycle/tenant denial сохраняют existing stable codes, без частичного успеха. |
| AC-14-05 | Read-only/watcher/requester без status grant не может вернуть; assignee с effective grant и допустимым scope может; explicit deny, revoked/expired grant и cross-tenant закрывают доступ. Проверить реальную object policy, а не только mock capability. |
| AC-14-06 | Exact replay даёт прежний ответ/ETag, единственную version mutation и один набор audit/event/outbox. Изменение body/reason при прежнем key даёт KEY_REUSED. |
| AC-14-07 | Конкурирующие review→completed и review→in_progress: один победитель, второй 412. Повтор одинаковой команды не создаёт второй event. Ошибка записи event/outbox полностью откатывает команду. |
| AC-14-08 | Desktop: кнопка только для review, отсутствует для остальных статусов; read-only/offline/session/busy правила действуют; normal Start и возврат имеют разные тексты. |
| AC-14-09 | На подтверждение отправляется target in_progress и reason null, актуальные task id/version и один idempotency key. Cancel не отправляет HTTP. До success статус карточки не меняется. |
| AC-14-10 | 412 предлагает актуализировать данные; network uncertainty сохраняет command key; forbidden блокирует запись; INVALID_STATE_TRANSITION даёт понятную ошибку без ложного успеха. |
| AC-14-11 | После server success карточка/список и существующая история обновлены; правила приоритетов, сроков, файлов, назначений не изменились. Новый тип уведомления не появляется. |
| AC-14-12 | Shared-rule callers и personal ownership проверены на регрессию. Полный security/build gate и targeted suites проходят; реальные PostgreSQL проверки возврата нельзя засчитать при skip. |

Проверка будущей реализации должна включать targeted Domain/ServiceHosts/Desktop tests, isolated PostgreSQL transaction/permission tests и WPF acceptance. Текущие 252 passed из аудита 14.1 подтверждают старый baseline и не засчитываются как проверка новой функции.

## 8. Версии, артефакты и следующий шаг

Исторический аудит 14.1 v1.0.0 и его checksums сохраняются. Этот пакет содержит явное решение вместо их заднего редактирования. Текущий результат аудита для выбранного минимального сценария уточняется до **READY по бизнес- и API-контракту**: противоречащая implementation instruction заменена DEC-14-RETURN-001.

READY не означает, что функция реализована, протестирована в PostgreSQL/WPF или опубликована в main. Проверенный main по-прежнему отклоняет возврат. Готовность продукта и dashboard не повышаются.

Следующий шаг — отдельный этап реализации и его acceptance по настоящему контракту. До этого production/source/test файлы не меняются согласно исходной границе задачи. Изменения общих бизнес-правил, обязательной причины, reviewer policy, схемы или notification taxonomy требуют отдельного обоснования, а не попутного расширения scope.

**Статус контракта: READY.**
