# Validation report

## Статус

- Версия: `0.1.0`.
- Scope: `PRODUCTION-TASK-READ-VS-01`, increment D — WPF task screen.
- Implementation commit: `868906a857c37044e8dd31641705933b19f74f22`.
- Automated gate: **PASS**.
- Реальный PostgreSQL 16 + HTTPS API + WPF E2E: **PASS**.
- Secret scan изменённых desktop/API файлов: **PASS**.

## Реализация

- Добавлен `TasksViewModel` с типизированными screen/detail states, атомарной
  заменой первой страницы, добавлением следующей страницы, защитой от
  параллельных одинаковых запросов и отменой при уходе из раздела.
- Выбор задачи загружает реальную карточку через `GET /api/v1/tasks/{id}`.
- Ошибка следующей страницы сохраняет уже показанные данные; invalid cursor
  очищает continuation и предлагает обновить первую страницу.
- 401/session-ended и 403 очищают ранее доступные task data; object disappeared
  отображается в detail area без раскрытия внутренних данных.
- Статусы и приоритеты локализованы по контракту; UTC DTO переводятся в локальное
  время только в presentation projection.
- Existing `SessionService` безопасно передаётся только composition root; vault
  не попадает во ViewModel, отдельный auth stack не создан.
- Раздел `tasks` активирует загрузку, уход из раздела отменяет её. Code-behind
  содержит только Enter-to-focus UI bridge.
- WPF показывает название, статус, приоритет, срок, обновление и основные детали;
  project, assignee, watcher, description и другие отсутствующие данные не
  выдумываются. «Новая задача» disabled с доступным пояснением.

## API contract и ограничения первого read increment

- Рабочие маршруты: `GET /api/v1/tasks`, `GET /api/v1/tasks/{id}`.
- Permission policy имеет пользовательский смысл `Task.Read`, backing permission
  временно остаётся `task.manage`; новые права ролям не выдавались.
- Непустые `filter`/`sort` и `page > 1` отклоняются стабильной validation problem;
  UI их не отправляет. Continuation выполняется только opaque cursor.
- Increment остаётся read-only: create, PATCH, DELETE и status transitions не
  реализованы.

## Изменённые production/test файлы

- `work/production/src/Task.Desktop/App.xaml.cs`
- `work/production/src/Task.Desktop/MainWindow.xaml`
- `work/production/src/Task.Desktop/MainWindow.xaml.cs`
- `work/production/src/Task.Desktop/ViewModels/AuthWorkflowViewModel.cs`
- `work/production/src/Task.Desktop/ViewModels/MainWindowViewModel.cs`
- `work/production/src/Task.Desktop/ViewModels/TasksViewModel.cs`
- `work/production/tests/Task.Desktop.Tests/MainWindowViewModelTests.cs`
- `work/production/tests/Task.Desktop.Tests/Tasks/TasksViewModelTests.cs`

## Автоматические проверки

1. `dotnet format Task.sln --no-restore --include <изменённые C# файлы>` — PASS.
2. Targeted ViewModel/shell gate — PASS, 26/26.
3. `dotnet test tests/Task.Desktop.Tests/Task.Desktop.Tests.csproj -c Release` —
   PASS, 174/174. При clean rebuild выводится один существовавший до increment
   warning `xUnit1031` в `DesktopCredentialVaultTests.cs`; scope-файл не менялся.
4. `dotnet test tests/Task.Tests/Task.Tests.csproj -c Release --filter
   "FullyQualifiedName~Task"` с real PostgreSQL 16 — PASS, 724/724.
5. `dotnet test tests/Task.ServiceHosts.Tests/Task.ServiceHosts.Tests.csproj -c
   Release --filter "FullyQualifiedName~Task"` — PASS, 156/156. При clean rebuild
   выводятся существующие `ASPDEPR004` в старых service-host test fixtures.
6. `dotnet test Task.sln -c Release` с real PostgreSQL gate — PASS, 1054/1054:
   core/integration 724, ServiceHosts 156, Desktop 174.
7. `dotnet build Task.sln -c Release --no-restore` — PASS, 0 errors, 0 warnings.
8. `git diff --check` — PASS.
9. Secret scan по private-key/JWT literals и password/accessToken/refreshToken/
   deviceKey literal assignments в изменённых desktop/API файлах — PASS,
   совпадений нет.

## Реальный E2E

Окружение размещалось в `work/tmp/task-read-e2e`: изолированный PostgreSQL 16.14
на отдельном loopback-порту, новая БД, synthetic administrator/session и
доверенный localhost HTTPS development certificate. Секреты и token values в
логи и пакет не включались.

- Миграции: expected/actual version 4.
- `/health/live`: `Alive`; `/health/ready`: `Ready`, persistence compatible.
- Fixtures: 55 active tasks целевой организации, 1 archived, 1 trashed и 1 active
  task другой организации.
- Desktop session создана существующим `SessionService` и сохранена существующим
  DPAPI vault; приложение выполнило реальный restore перед открытием main shell.
- Раздел «Задачи» показал 50 строк первой страницы и карточку выбранной задачи.
  Archived, trashed и foreign task в UI не появились.
- «Загрузить ещё» довела видимое количество до 55 и исчезла после последней
  страницы; refresh атомарно вернул первую страницу и continuation.
- Стрелка вниз сменила выбранную задачу и карточку; Enter дал видимый focus detail
  area. Automation tree подтвердил IDs списка, строк, refresh, load more, detail,
  logout и live/status regions.
- После server-side revoke refresh получил terminal auth failure, общий workflow
  закрыл main shell и открыл login с сообщением «Срок действия сессии истёк».
- После визуальных исправлений повторный restore/smoke подтвердил актуальный
  connection notice и компактную кнопку пагинации.

## Accessibility, размеры и непроверенное

- Keyboard-only list selection и Enter-to-details: **PASS**.
- AutomationId/Name/HelpText/LiveSetting для обязательных областей: **PASS** по
  реальному UI Automation tree.
- Декларативный минимум окна `800x480`, адаптивные grid/star размеры и scroll
  containers проверены по XAML и Release build; физическое уменьшение окна до
  минимума отдельно не выполнялось.
- Системная DPI-матрица 125/150/200%: **не проверена**, системный DPI не менялся.
- Реальный E2E synthetic и loopback-only; production credentials/DB не
  использовались.

## Следующий increment

MOD-005 не завершён. Следующий отдельный vertical slice: создание задачи, PATCH
и status transitions с `If-Match`, `Idempotency-Key`, audit и outbox.
