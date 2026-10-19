# Карточка проекта — приёмка 13.1–13.4

Дата: 2026-10-19. Версия пакета: 13.4.0. Ветка: `feature/project-detail-sections`.
База этапа: `d5afec2`; общая база расширения: `dcbaca2` (актуальный origin/main при начале работы).

История реализована и целевые проверки прошли. Полная финальная приёмка остаётся открытой:
нет работающего production-контракта проектных комментариев; один ранее существующий
нативный тест не может активировать окно в текущей Windows-сессии. Он воспроизведён на
неизменённом `d5afec2`. Live-server ручной walkthrough не выполнялся.

| Этап | Статус | Реализовано | Тесты текущего прогона | Коммит |
|---|---|---|---|---|
| 13.1 Структура | Реализован; регрессия проверена | Независимые состояния и жизненные циклы разделов, обзор/участники/задачи | Входит в 250 PASS Projects; полный desktop: 1780 PASS, 1 существующий FAIL | `283bcd4` |
| 13.2 Файлы | Реализован; автоматическая регрессия проверена | Каталог, Object Links, разрешённое открытие файла, optimistic locking | ProjectFiles unit/integration/native и transport/file-policy проверки запущены | `4accd66` |
| 13.3 Контакты | Реализован; автоматическая регрессия проверена | Контакты/компании, привязка/удаление, навигация, контроль доступа | ProjectContacts unit/integration/native и transport/API проверки запущены | `d5afec2` |
| 13.4 История | Реализована; общая приёмка с ограничениями | Чтение, пагинация, авторы, права, cancellation, безопасные состояния | Итоговый targeted gate: 250 desktop + 14 domain/Postgres + 4 HTTP = 268 PASS | `63babc1` |

## Реализация истории

- Раздел запрашивается при раскрытии, только после подтверждения выбранного проекта.
- `Project.Read` + `History.Read` обязательны; без них раздел скрыт и запросов нет.
- Единственный источник — `GET /api/v1/projects/{id}/history?limit=50&page=N`.
- Показаны локальная дата/время с UTC offset, действие, версия и `changedFields`.
  Распознанные действия имеют русские подписи; неизвестный тип сохраняет серверное название.
  Старые и новые значения не моделируются и не выдумываются.
- Имя автора разрешается только через существующий `GET /users/{id}`, при `User.Read`.
  Нет права, имени, доступного пользователя или каталога — нейтральная подпись без UUID.
  При серверном 403 каталога очищаются также имена на уже загруженных страницах.
- Следующая страница добавляется с дедупликацией по event ID. Ошибка страницы сохраняет
  предыдущие события и номер страницы для retry. Короткая страница завершает загрузку;
  ровно полная последняя страница требует ещё одного пустого запроса, поскольку total/hasMore нет.
- Смена проекта/организации, отзыв прав, logout, collapse, unload, deactivate, dispose и offline
  отменяют запросы. Проверка владельца запроса и identity выбранной карточки отвергает поздние
  ответы, даже если транспорт игнорирует cancellation. Старые строки очищаются.
- Empty выставляется только после успешного пустого ответа. Ошибки и 403/404 имеют отдельное
  безопасное состояние и не ломают обзор, задачи, файлы или контакты. 401 завершает сессию.
- Постоянного кэша, очереди записей, параллельного журнала и обращений к admin audit нет.

Клиентские records являются проекциями существующего ответа, а не новыми серверными DTO.
Сервер, миграции, нормативный OpenAPI, permissions и бизнес-правила не изменены.

## ISSUE-13.4-COMMENTS — production-контракт не доведён до проекта

Проверка не ограничивалась поиском `/projects/{id}/comments`:

| Уровень | Обнаружено | Недостающая часть |
|---|---|---|
| Концепция и архитектура | Комментарии и история доступных объектов; неизменяемая история и текущий read scope | Новые бизнес-правила не требуются и не добавлялись |
| Нормативный контракт 2.2 | `GET/POST /objects/{objectId}/comments`, операции чтения/изменения/удаления/restore/versions; Comment/CommentPage, Comment.Read/Create и lifecycle/version policy | Полноценная production-реализация этого контракта для project target |
| UX 3.4 | SCR-067 — комментарии проекта; SCR-068 — история; объектные комментарии SCR-035 | Наличие UX не делает production endpoint доступным |
| PRD candidate 4.1.1 | История проекта FR-239, read-scope/security acceptance; общие comments requirements | Не заменяет отсутствующий серверный маршрут |
| Production routes | `POST /tasks/{id}/comments`, `Comment.Create`, versioned + idempotent; чтение task workspace содержит комментарии задачи | Нет read/create project comments или generic object comments маршрутов |
| Production storage/DTO | `work.task_comments` и task-only операция; нормативные общие модели существуют отдельно | Нет production project/generic comment DTO + операций и lifecycle/version enforcement для проекта |

Нельзя утверждать, что нормативного контракта комментариев вообще нет. Блокер — именно
разрыв между существующей общей нормативной моделью и исполняемым production API.
Для закрытия необходим отдельный согласованный backend-инкремент generic object comments
с project/object-level authorization, чтением, записью, версиями и lifecycle по нормативу.
В этом этапе endpoints/БД не добавлялись. Нет формы, вкладки-заглушки, task comments proxy
или хранения комментариев в Description/History/Links. История этим блокером не блокируется.

## Проверки и фактические результаты

| Запуск | Результат | Назначение |
|---|---|---|
| Первый Projects Debug | 246 PASS / 1 FAIL / 0 SKIP | Найден null event в некорректном payload, исправлен до итогового gate |
| History Debug после исправления | 33 PASS / 0 FAIL / 0 SKIP | Unit, client и native history |
| Полная Release-сборка решения | PASS | Все проекты; предупреждения сохранены в build.log |
| Полный Release Task.ServiceHosts.Tests | 644 PASS / 0 FAIL / 0 SKIP | HTTP policies, Project API, Object Links, каталог, контакты и существующие endpoints |
| Полный Release Task.Tests на изолированном PostgreSQL 16 | 879 PASS / 1 FAIL / 0 SKIP | В том числе существующие permissions, links, file catalog и CRM; единственный FAIL — ошибочное ожидание в новом history тесте |
| Полный Release Task.Desktop.Tests | 1780 PASS / 1 FAIL / 0 SKIP | Все desktop unit/integration и доступные native WPF тесты; один существующий foreground activation failure |
| Повтор native TaskContextActions на текущем коде | 0 PASS / 1 FAIL | `company.Activate()` вернул false, строка 62 |
| Тот же native тест на чистом исходном `d5afec2` | 0 PASS / 1 FAIL | Та же строка и причина; подтверждено, что сбой существовал до 13.4 |
| Итоговая Release-сборка + targeted gate после всех исправлений | 268 PASS / 0 FAIL / 0 SKIP | 250 Projects desktop, 14 ProjectHistory/Projects domain/Postgres, 4 ProjectHistory HTTP |

Исправление теста: глобальные `Project.Read`/`History.Read` проверяются HTTP-слоем, а не
подстановкой списка capabilities в trusted store fixture. Добавлены HTTP-тесты обоих запретов
до вызова store. Postgres-тест отдельно проверяет реальные события, paging и object visibility.
Невалидный page возвращает фактический server status 422. Сервер не изменялся ради тестов.

Полный набор после последней правки повторно целиком не запускался: изменённые сценарии и
исправленные тесты покрыты итоговым targeted gate. Общий gate не объявляется полностью зелёным.
Первый запуск DB harness был остановлен initdb из-за кириллицы в пути; test runtime перенесён
в ASCII temp. Первый полный запуск также прерван PowerShell обработкой native stderr;
исправлен harness, фактический полный запуск приведён в таблице. Изолированный PostgreSQL остановлен.

## Регрессионная матрица и security review

| Область | Проверено автоматически / по коду | Оставшееся ограничение |
|---|---|---|
| Обзор | Выбор, имя/описание/статус/даты, owner/manager, редактор, save/conflict/archive | Ручной live API walkthrough не выполнен |
| Участники | Employee Picker, add/role/remove, permissions, версии, owner invariant, directory denial | Физическая пользовательская сессия отдельно не принималась |
| Задачи | Paging, cancel, stale responses, Task API failures, контекст создания | Существующий native task-create тест упирается в отказ Windows foreground activation |
| Файлы | Просмотр, link/unlink, directory grants/resolution, недоступные пути, раздельные metadata и OS storage | Реальные NAS/ACL/network failures не воспроизводились вручную |
| Контакты/компании | Read/link/unlink/navigation, target revalidation, partial access, имена скрытых целей | Live CRM walkthrough не выполнен |
| История | AC-1–AC-7: lazy read, page dedup/retry, права, stale response, независимая ошибка, fields-only, empty | Snapshot paging сервером не предоставлен |
| Общие переходы | Selection/organization switch, offline/reconnect, read-only, logout, revoke, reopen, async disposal | Реальная смена аккаунта проверяется существующим scope/session lifecycle, без ручного end-to-end входа |
| Keyboard/WPF | Реальные Window/Dispatcher/ExpandCollapse provider, focus/Next, list rows, collapse/reopen, reconnect button state, capability visibility | Ручной Narrator, физические DPI/HC и ручной запуск production с live server не выполнены |

Независимый read-only review `dcbaca2..d5afec2` проверил разделы 13.1–13.3 и не нашёл
конкретных P1/P2. Отдельный review 13.4 подтвердил null-payload дефект и его исправление;
ошибочное замечание о reconnect было отозвано после проверки уже существующего NotifyHistory
в NotifyCommands. После исправлений конкретных нерешённых дефектов в diff не выявлено.
Review сам по себе не считается запуском тестов.

Security: только разрешённые существующие маршруты; object scope остаётся на сервере;
имена целей читаются только через соответствующий read API. If-Match и idempotency прежних
мутаций сохранены, optimistic updates без подтверждения нет. История read-only, поэтому
не посылает If-Match/idempotency. Source changes не добавляют пути, shell-команды или audit API.
Файловые пути открываются существующим проверенным resolver; raw path не переносится в историю.
Очистка на capability/session/organization transitions проверена. Параметры аккаунта/организации
не меняются внутри одного VM; существующий host создаёт/закрывает соответствующий session scope.

## Windows UI и ограничения приёмки

Windows доступна. Нативные WPF-тесты проектов выполнены, в том числе новая история;
сохранены скриншоты, визуально проверены loaded/revoked состояния. Это automated UI fixture,
а не ручной обход работающего production приложения с живым сервером. Такой ручной обход
не выполнен: в этой сессии нет доступного native desktop control. Нельзя заменять его PASS
нативного fixture-теста. Foreground-отказ существующего теста остаётся отдельным UI gate issue.

Технические риски: offset paging при новых событиях между запросами не даёт server snapshot;
повторы подавляются по ID, для актуального начала есть refresh, но абсолютная полнота при
конкурентных изменениях не гарантируется контрактом. Имена optional, поэтому directory failure
может оставить нейтральную подпись. Различие исторической нормативной HistoryEntry модели
и действующего production response не исправлялось: используется проверенный runtime response
в границах задания, без изменения источников.

Ветка подготовлена для PR/review: отдельный source commit, список файлов, отчёт, manifest и
SHA-256 в outputs. Не опубликована и не слита в main. Готовность к безусловной финальной
приёмке/merge не заявляется до решения comments gap и проверки оставшегося UI gate.


## Изменённые файлы и review scope

- `DesktopProjectsApiClient.cs` — существующий клиент реализует дополнительный read-only history interface.
- `DesktopProjectHistoryApiClient.cs` — HTTP history, клиентские проекции, bounds/payload validation.
- `ProjectsViewModel.History.cs` — paging/state/author resolution/request lifetime и presentation rows.
- `ProjectsViewModel.cs`, `ProjectsViewModel.Sections.cs` — интеграция контекста и lifecycle.
- `ProjectsView.xaml`, `ProjectsView.xaml.cs` — capability-aware Expander и команды.
- `DesktopProjectHistoryApiClientTests.cs`, `DesktopProjectsApiClientTests.cs` — клиентские transport/validation tests.
- `ProjectHistoryTests.cs`, `ProjectHistoryWindowsTests.cs`, `ProjectsViewModelTests.cs`,
  `ProjectDetailSectionsTests.cs` — unit fixtures, state transitions, paging, native WPF.
- `PostgresProjectHistoryTests.cs`, `ProjectHistoryEndpointsTests.cs` — реальный PostgreSQL и HTTP policy/response.
- Этот отчёт, `.project-dashboard/roadmap.json` (только PROD-04 evidence/note/time) и
  `work/project_history_13_4/Run-Gate.ps1` — проверяемая документация и воспроизводимый gate.

Полный перечень путей находится в `changed-files.txt` итогового пакета.
