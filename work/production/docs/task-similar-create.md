# Создать похожую задачу — 1.0.0

Действие в командной области карточки открывает существующий `TaskEditorViewModel` в режиме Create. До нажатия «Сохранить» выполняются только GET исходной задачи и GET вариантов связей. Новый create flow, DTO, endpoint, миграции и побочные объекты не добавлены. Контекстного меню списка в текущем UI нет; отдельный shortcut не вводится. Обычная Button доступна через Tab и Enter/Space; форма использует существующий перевод фокуса на название и Escape с подтверждением потери черновика.

Поля сверены с каноническими `Task`, `TaskCreate`, `dto_field_catalog.csv`, OpenAPI этапа 2.2 и текущими `TaskCardContent`, `TaskEndpoints`, `DesktopTasksApiClient.CreateTaskAsync`. Production использует свой существующий C# adapter; сгенерированный TypeScript SDK в канонических материалах не подключён к WPF. Actor/author определяется обычным серверным create use case.

| Поле TaskCreate | Предзаполнение |
| --- | --- |
| title, description, priority | Копируются |
| plannedDurationMinutes | Копируется |
| projectId | Только из серверных вариантов с эффективным task.create |
| primaryCounterpartyObjectId | Только из доступных серверных вариантов; прямое поле, без ObjectLink |
| assigneeIds, watcherIds | Только активные доступные участники и при Task.Assign / Task.Watch |
| authorUserId, requesterUserId | Не копируются; автор новой задачи — текущий пользователь |
| parentTaskId | Очищается; похожая подзадача становится самостоятельной задачей |
| scheduledDate, startTimeLocal, scheduleTimeZone, deadlineAt | Очищаются, как и startAtUtc текущего adapter |
| status | Не переносится; обычное серверное начальное состояние new |

Identity, ETag/version, audit, timestamps, завершение, lifecycle, recurrence, exceptions, reminders, comments, checklist, subtasks, links и файлы отсутствуют в предзаполнении. Mapping построен по allowlist, без копирования aggregate или изменения исходного объекта.

`Task.Read` и `Task.Create` проверяются существующими capabilities; роли клиент не вычисляет. Source перечитывается при выборе действия. Потеря доступа, смена выбора, завершение активации и отзыв capability не открывают форму из устаревшего ответа. Новое действие не заменяет уже открытый черновик.

Список проектов существующего `/tasks/options` для пользователей с Task.Create теперь дополнительно использует тот же `work.task_project_writable(...,'task.create')`, что и POST validation. Read-only callers сохраняют прежние варианты. Текущие выбранные поля редактора изменения сохраняются через существующий SetOptions. Формат ответа и параметры endpoint неизменны.

Недоступные или неподтверждённые relations очищаются до открытия формы, с постоянным пояснением. Options ограничены 200 результатами: значение вне первой страницы также не переносится без подтверждения; пользователь может найти и выбрать его обычным поиском. При ошибке загрузки options переносятся только скалярные поля. Удалённые значения не возвращаются при последующем поиске.

Save остаётся обычным POST `/api/v1/tasks`, DesktopCreateTaskCommand / TaskCreateModel и Task.Create с Idempotency-Key, server validation и текущими 403/422/network/error handlers. Двойной Save блокируется AsyncCommand; при неопределённом сетевом результате повтор неизменённого draft сохраняет ключ. Offline не отправляет запросы автоматически.

Текущий canonical GET задачи доступен только для active lifecycle. Completed/cancelled active source поддерживаются. Archived/trashed source не показывают это действие, поскольку их карточка не может быть прочитана этим endpoint. Это существующая граница чтения; разрешения и lifecycle исходной задачи не расширяются. В mapper нет lifecycle-зависимости: при появлении разрешённого чтения таких карточек новый объект по-прежнему будет обычной active задачей.

Автотесты: SimilarTaskTests, DesktopTasksApiClientTests, WindowsUxAccessibilityTests, PostgresTaskCardWorkflowTests плюс существующий solution gate. Отдельный native UI keyboard walkthrough не заменяется статическими assertions; его статус указывается в validation report.
