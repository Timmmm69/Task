# Task 14.3 — WPF: «Вернуть на доработку»

Версия: 1.0.0. Дата: 22.10.2026. Репозиторий Timmmm69/Task.
Ветка: codex/status-return-14-3. Серверная база: 10774455af70c2a0d5c586bd6f5af26bbbc0a695.
Результат: функциональный WPF-инкремент проверен; кандидат для финального аудита с открытым compact-layout замечанием.

## Предварительные условия

Проверены Task_14_1_Status_Return_Contract_Audit.md, дополнение/контракт 14.2 и
отслеживаемое решение work/production/docs/task-14-2-return-contract-decision.md.
Исходный аудит запрещал реализацию до разрешения противоречия. Решение 14.2 применяет
канонический review→in_progress, Task.ChangeStatus и reason=null без нового уведомления.
Серверный коммит 1077445 реализует переход в Domain/Application. Его пакет
outputs/20261022_task_14_2_server_return_1.0.0 содержит успешные HTTP и реальные
PostgreSQL 16 проверки permissions, rollback, replay, версии и истории. Эти доказательства
прочитаны; PostgreSQL заново в 14.3 не запускался. HTTP gate повторён: 60 PASS.
origin/main на старте — fa504bc01584547afc50398ba6c1735391c4173c; серверный коммит
и данный WPF-инкремент локальные, публикация в main и deployment не выполнены.
Исходная ветка и неотслеживаемые материалы пользователя сохранены.

## Изменённые файлы

- TasksViewModel.cs: отдельный CanReturnForRework; CanStart только для New;
  подтверждение, busy, общий mutation guard, версия загруженной карточки,
  сохранение uncertain intent при offline/malformed response, доступные сообщения ошибок.
- MainWindow.xaml: кнопка в существующей панели действий карточки, отдельный текст
  подтверждения; поле причины скрыто для возврата; существующие стили и live region.
- TaskReturnTests.cs: 22 новых VM-сценария включая таблицы guards/errors.
- TaskReturnUiTests.cs: настоящее WPF-окно на STA dispatcher в дочернем процессе.
- DesktopTasksApiClientTests.cs: дополнительный transport case in_progress/reason=null
  с проверкой POST, If-Match и Idempotency-Key.
- .project-dashboard/roadmap.json: только evidence/note/date DESK-02, без повышения progress.

TaskWorkspaceView.xaml изучен; его action/комментарий/история остаются в текущей архитектуре.
Новая кнопка относится к MainWindow.xaml, где уже находятся остальные статусные действия.

## UX и выполнение

В карточке загруженной review-задачи при Task.ChangeStatus отображается «Вернуть на доработку».
Нет действия для других статусов, read-only, offline, завершённой сессии, открытого редактора,
загрузки/сохранения, комментария/чек-листа с несохранённым текстом или pending confirmation.
Проверяется enum, capability, session/connectivity, загруженная карточка и состояние операций.
«Начать» остаётся действием для New. Подтверждение: «Вернуть задачу на доработку?» → «Вернуть».
Причина не требуется и не предлагается: reason=null; фиктивный комментарий не создаётся.

Во время запроса показывается «Сохранение…» и live announcement. AsyncCommand и mutation gate
не допускают двойного запроса. Локальные status/version до ответа сервера не меняются.
Success DTO обновляет список/фильтры/карточку; создание нового Workspace перечитывает историю
существующим механизмом. Между другими экранами сохраняются существующие refresh/polling правила.

403 блокирует запись; 401 завершает сессию; 404 убирает недоступные детали; 409 invalid transition
предлагает обновление; validation показывает сообщение. 412 открывает существующий conflict editor
с действием загрузки актуальной версии. При offline, 5xx/сетевой неопределённости и malformed
ответе команда сохраняет key/body/expectedVersion для ручного retry; reconnect ничего не отправляет.
Поздний ответ после смены выбора/деактивации не обновляет закрытую карточку.
Отмена ожидания не означает отмену уже принятой сервером операции; при повторном открытии данные читаются заново.

## API и права

POST /api/v1/tasks/{id}/transition, body {"targetStatus":"in_progress","reason":null}.
If-Match: "vN"; стабильный Idempotency-Key; существующие bearer session/correlation headers.
Permission Task.ChangeStatus (runtime task.changestatus, policy permission.task.change-status).
Task.Read — чтение карточки, History.Read — доступная история. Task.Update и Comment.Create
для возврата не требуются. Endpoint, permissions, роли и серверные бизнес-правила не менялись.
Object visibility/scope/deny/lifecycle повторно проверяются сервером; DTO не предоставляет
allowed-actions для точного предварительного вычисления всех object grants на клиенте.
Клиент не угадывает reviewer policy по роли/автору и корректно обрабатывает server denial.

## Проверки

- Release Task.Desktop build: PASS, 0 warnings/errors (evidence/desktop-build.log).
- Финальный desktop gate: 166 PASS, 0 FAIL/SKIP (final-desktop.trx).
  Включает TasksViewModelTests со связанными partial suites, transport, accessibility и native WPF.
- Повторный server HTTP/matrix/permission gate: 60 PASS, 0 FAIL/SKIP (server-return.trx).
- Native WPF: окно, UIA Invoke, tab traversal/focus style, отсутствующая причина, single-flight
  busy UI, authoritative success и assertive live region; снимки 1100×900 и 800×800 проверены.
- dashboard:order, dashboard:validate, git diff --check: PASS; progress/status не повышены.
- Diff просмотрен; source overlay, manifest и SHA-256 проверены после формирования пакета.

Первая попытка общего guard требовала loaded details для всех старых переходов и сломала
7 completion/start regression tests: требование ограничено возвратом, регрессии проходят.
Один промежуточный запуск дал существующий нестабильный ViewPreferences order test
(равные даты/случайные ID); финальный прогон без изменения этого теста проходит.
Первый native fixture не раскрывал inspector; fixture исправлен. Неуспешные попытки
не засчитаны как PASS. Существующие xUnit1031/xUnit2031 и ASPDEPR004 предупреждения
в сторонних тестах не исправлялись; production Release build warning-free.

## Известные ограничения и финальный аудит

1. В 800×600 существующий compact inspector обрезается и подтверждение визуально недоступно
   (evidence/compact-800x600-limitation.png). Пробные layout-изменения убраны из diff;
   полная минимальная оконная UX-приёмка НЕ закрыта. Проверенные размеры — 1100×900 и 800×800.
2. Live WPF→network→реальный сервер/БД и физическое прослушивание screen reader не выполнялись.
   Native WPF использует typed fake client; transport и HTTP проверены отдельно.
3. Глобальные capabilities не содержат server-derived per-object allowed actions: сервер
   остаётся источником object authorization. Это ограничение существующего API, не новое право.
4. Не вводились причина в истории, reviewer policy, новый notification type или terminal recovery.

Функциональные AC-1–AC-7 подтверждены VM/transport/HTTP проверками. Можно проводить финальный
аудит инкремента на указанной серверной базе. Общий статус этапа не объявляется безусловным
PASS: compact 800×600 UX, live end-to-end и финальная публикация остаются открытыми.
