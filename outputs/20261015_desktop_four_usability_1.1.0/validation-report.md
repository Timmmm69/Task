# Task desktop: четыре исправления, версия 1.1.0

Все четыре задачи реализованы и проверены. Прежний блокер преобразования записи только с названием устранён. Этот отчёт заменяет выводы версии 1.0.0 о неполном сценарии и непроверенном PostgreSQL; старый пакет сохранён как исторический.

Дата: 2026-10-15. Ветка: `fix/desktop-four-usability-improvements`.
Проверенный код: `f8fc9d5467710b9e80bdb412035a4ef418ff3656`. База origin/main: `c583f200e0e60db3fe3d2b33bf798897d0d99c46`.

| Задача | Статус | Основные изменённые файлы | Тесты | Коммиты |
|---|---|---|---|---|
| 1. Необязательный срок | Выполнено, включая только название | InboxViewModel, DesktopInboxApiClient, TaskEditorViewModel, PostgresProductApiInbox, migration 017, ProductEndpoints | InboxViewModelTests, DesktopInboxConversionTests, InboxRetargetTests, DesktopInboxWindowsTests, PostgresInboxConversionTests, InboxEndpointsTests | 2ba6552, 20a4399, 7f19b53, 653ef92, 06c34c7, f8fc9d5 |
| 2. Пагинация | Выполнено | DesktopWorkApiClient, WorkHubPagination, WorkHubSearch, CommandPaletteViewModel, WorkHubView, CommandPaletteView | DesktopWorkPaginationTests, WorkHubPaginationTests, SettingsWindowsTests | 0ee8e93, 594bff6, 20a4399 |
| 3. Навигация поиска | Выполнено для существующих экранов; явное сообщение для остальных типов | MainWindowViewModel, WorkHubSearch, WorkHubCatalogViewModel | SearchNavigationTests, WorkHubNavigationTests | 2965993, 20a4399 |
| 4. Настройки | Выполнено | WorkHubSettingsPresentation, WorkHubViewModel, WorkHubView | DesktopSettingsPresentationTests, SettingsWindowsTests | 594bff6 |

## Подтверждённые ошибки и результат

1. Сохранение входящего действительно требовало срок. Дополнительно production вычислял входящие из обычных задач без постоянного признака преобразования, поэтому изменение только названия не убирало запись из входящих. Восстановлены предусмотренные Stage 2.2 inbox routes, DTO и permissions, отдельное хранение входящих и атомарное преобразование с association. Название обязательно, срок остаётся null, фиктивных дат нет. Явно введённый срок сохраняется существующим task PATCH после преобразования. Если PATCH не удался, форма сохраняется, сообщает об уже созданной задаче и повторяет только нужную фазу. Idempotency защищает от двойного Save и потери ответа. Существующие heuristic capture-only задачи переносятся миграцией с внутренней ссылкой: при преобразовании используется исходная задача, копия не создаётся. Состояние источника и audit-связь сохраняются в БД.
2. Клиент действительно терял nextCursor для contacts, archive, trash и search. Добавлена управляемая догрузка одной страницы по нажатию «Загрузить ещё» на экране и в палитре. Сохраняются фильтры и порядок; IDs устраняют дубликаты, повторяющийся cursor прекращает догрузку. Проверены 50/101/201 записей, отмена, stale responses, сетевая ошибка на следующей странице и частичная выборка. При authentication/forbidden защищённая выборка очищается; при истечении search snapshot выборка прекращается и очищается.
3. При открытии результата ObjectId мог ошибочно заменяться ParentObjectId; company переключал раздел без открытия объекта. Теперь parent используется только для file_location. Task, project, calendar_event, catalog_item/file_location и contact открывают нужный объект через существующую навигацию, включая контакт после первой страницы и повторную проверку доступа. Company, employee_profile, interaction и неизвестные типы показывают явное сообщение об отсутствии экрана. Новые экраны не создавались. Повторная навигация и устаревшие ответы не оставляют неверный selected object.
4. Технический ввод языка, дня недели и формата времени заменён понятным выбором. Язык — только реально реализованный русский; дни подписаны названиями и сохраняются как 1–7. Время показывается как ЧЧ:ММ с сохранением существующих секунд; API получает прежний HH:mm:ss. Неверное время не отправляется, ошибки сохранения не выглядят успехом. Проверены reload, bindings, доступные имена, keyboard navigation и tab order.

## Контракт, безопасность и миграция

Канонический источник: `sources/stage_2_2/Organizer_Stage2_Technical_Specification_2.2/openapi/openapi.yaml`, схема InboxConvertTask (около строки 26502). POST `/api/v1/inbox-items/{id}/convert-to-task` требует expectedInboxVersion, но не срок. Восстановлены существующие GET/POST/PATCH inbox routes и Inbox.ReadOwn/Inbox.ManageOwn; новые публичные DTO-поля, permissions, статусы и stable error codes не выдумывались. `legacy_task_id` — внутреннее поле хранения, клиенту не раскрывается. Task.Read и видимость объекта повторно проверяются, включая idempotent replay; owner-only доступ и контроль версий проверены негативными тестами.

Миграция 017 использует штатный migration catalog. Runtime SQL grants обновлены в существующем deployment script. Перед установкой нового корпоративного клиента требуется штатно обновить сервер и выполнить миграцию до версии 17. Продакшен-БД и установленное приложение в рамках этой работы не обновлялись. Файлы sources не изменены. Personal mode использует прежний отдельный local data path.

## Фактически выполненная проверка

Финальный gate выполнен на Windows из чистого Git archive коммита `f8fc9d5467710b9e80bdb412035a4ef418ff3656`, отдельно от пользовательских незакоммиченных правок. Содержимое 44 source-overlay файлов сверено с этим снимком. Выполнены locked restore и Release build/test всей Task.sln. Сборка без ошибок; в тестовых проектах остаются существующие предупреждения ASPDEPR004 и xUnit1031/xUnit2031.

```powershell
dotnet restore Task.sln --locked-mode
# Run-Gate.ps1 создаёт временный PostgreSQL 16 внутри work/,
# задаёт TASK_POSTGRES_TEST_ADMIN_CONNECTION и останавливает свой кластер после тестов.
dotnet test Task.sln -c Release --no-restore --logger trx --results-directory <release-gate>
```

| Suite | Passed | Failed | Skipped |
|---|---:|---:|---:|
| task.desktop.tests.dll | 859 | 0 | 0 |
| task.servicehosts.tests.dll | 620 | 0 | 0 |
| task.tests.dll | 858 | 0 | 0 |
| Всего | 2337 | 0 | 0 |

PostgreSQL 16 действительно запущен и использован. Проверены атомарный rollback при injected SQL failure, конкурентное преобразование, replay, доступ и обновление схемы v16 → v17 без клонирования старых задач. Повтор полного gate после исправления тестовой гонки прошёл.

Оговорка к формальному счётчику VSTest: один старый тест RealPostgres15_IsRejectedBeforeBootstrapDdl выводит NOT RUN и возвращается с Passed, поскольку PostgreSQL 15 не установлен и TASK_POSTGRES15_TEST_ADMIN_CONNECTION отсутствует. Поэтому реально подтверждены 2336 выполненных проверок, а не 2337. Это отдельная проверка отказа на неподдерживаемой версии сервера; все новые тесты и весь PostgreSQL 16 gate выполнены. Иных NOT RUN и явных skips нет.

Нативные WPF-тесты открыли настоящие Window: преобразование без срока, сохранение настроек, focus/keyboard и поиск с двумя страницами. Снимки ui-final и ui-four-final просмотрены; title-only сохраняется, источник исчезает после refresh. UI-тесты используют HTTP fixture, а DB/endpoint поведение проверяется отдельными тестами. Ручная проверка установленного клиента с действующим корпоративным сервером не выполнялась; её нельзя приравнивать к этим автоматизированным проверкам.

Статические проверки: git diff --check production PASS; Test-ProjectBoundaries.ps1 PASS; dashboard:order и dashboard:validate PASS. Изменены только notes/evidence PROD-01 и PROD-05; progress/status не повышены. Все девять исходных пользовательских tracked изменений сохранены, их доказательство — preservation.json. Непрофильные untracked файлы не добавлены.

## Обнаруженные и исправленные регрессии

Исправлены mutation gate после no-op; CanExecute у догрузки после поиска; refresh при отмене старой загрузки; stale selected contact и search snapshot после потери доступа/410. После восстановления inbox устранены SQL-предположение p.name в archive/trash discovery, фикстуры предыдущей версии миграции и прежнее сообщение TaskCreate о попадании обычной задачи во входящие. Новый render-тест сначала перехватывал keyboard focus у другого WPF-теста; окно сделано пассивным, без Activate. Финальный прогон содержит все исправления и не имеет падений.

## Готовность к интеграции

Известных оставшихся кодовых блокеров по четырём задачам нет. Ветка готова к Pull Request с требованием применения миграции 017 при deployment. Push, PR, merge в main и установка не выполнялись. Dashboard overall readiness не является результатом этих четырёх задач и автоматически не повышался. Полный список файлов ниже и в validation.json; source-overlay содержит только закоммиченный код. Manifest и SHA256SUMS покрывают содержимое пакета, внешний ZIP имеет отдельный SHA-256.

## Полный список файлов по задачам

### Задача 1

- `work/production/deployment/containers/sql/grant-runtime.sql`
- `work/production/src/Task.Api/ProductData/ProductEndpoints.cs`
- `work/production/src/Task.Application/ProductData/ProductApiContracts.cs`
- `work/production/src/Task.Desktop/Modes/CorporateApplicationContext.cs`
- `work/production/src/Task.Desktop/Tasks/DesktopInboxApiClient.cs`
- `work/production/src/Task.Desktop/Tasks/DesktopTasksApiClient.cs`
- `work/production/src/Task.Desktop/ViewModels/InboxViewModel.cs`
- `work/production/src/Task.Desktop/ViewModels/TaskCardEditor.cs`
- `work/production/src/Task.Desktop/ViewModels/TaskEditorViewModel.cs`
- `work/production/src/Task.Desktop/ViewModels/TasksViewModel.cs`
- `work/production/src/Task.Infrastructure/Persistence/Migrations/.gitattributes`
- `work/production/src/Task.Infrastructure/Persistence/Migrations/017_inbox_conversion.sql`
- `work/production/src/Task.Infrastructure/Persistence/PostgresProductApiInbox.cs`
- `work/production/src/Task.Infrastructure/Persistence/PostgresProductApiQueries.cs`
- `work/production/src/Task.Infrastructure/Persistence/PostgresProductApiStore.cs`
- `work/production/src/Task.Infrastructure/Persistence/TaskPersistenceMigrationCatalog.cs`
- `work/production/tests/Task.Desktop.Tests/Tasks/InboxRetargetTests.cs`
- `work/production/tests/Task.Desktop.Tests/Tasks/InboxViewModelTests.cs`
- `work/production/tests/Task.Desktop.Tests/Tasks/JourneyTaskTests.cs`
- `work/production/tests/Task.Desktop.Tests/Work/DesktopInboxConversionTests.cs`
- `work/production/tests/Task.Desktop.Tests/Work/DesktopInboxWindowsTests.cs`
- `work/production/tests/Task.ServiceHosts.Tests/InboxEndpointsTests.cs`
- `work/production/tests/Task.ServiceHosts.Tests/ProductEndpointsTests.cs`
- `work/production/tests/Task.Tests/PostgresAutomaticDeadlineReminderTests.cs`
- `work/production/tests/Task.Tests/PostgresInboxConversionTests.cs`
- `work/production/tests/Task.Tests/TaskPersistenceMigrationHistoryTests.cs`

### Задача 2

- `work/production/src/Task.Desktop/ViewModels/CommandPaletteViewModel.cs`
- `work/production/src/Task.Desktop/ViewModels/WorkHubPagination.cs`
- `work/production/src/Task.Desktop/ViewModels/WorkHubSearch.cs`
- `work/production/src/Task.Desktop/ViewModels/WorkHubViewModel.cs`
- `work/production/src/Task.Desktop/Views/CommandPaletteView.xaml`
- `work/production/src/Task.Desktop/Views/WorkHubView.xaml`
- `work/production/src/Task.Desktop/Work/DesktopWorkApiClient.cs`
- `work/production/tests/Task.Desktop.Tests/Work/DesktopWorkApiClientTests.cs`
- `work/production/tests/Task.Desktop.Tests/Work/DesktopWorkPaginationTests.cs`
- `work/production/tests/Task.Desktop.Tests/Work/WorkHubPaginationTests.cs`

### Задача 3

- `work/production/src/Task.Desktop/ViewModels/MainWindowViewModel.cs`
- `work/production/src/Task.Desktop/ViewModels/WorkHubCatalogViewModel.cs`
- `work/production/src/Task.Desktop/ViewModels/WorkHubSearch.cs`
- `work/production/src/Task.Desktop/ViewModels/WorkHubViewModel.cs`
- `work/production/tests/Task.Desktop.Tests/SearchNavigationTests.cs`
- `work/production/tests/Task.Desktop.Tests/Work/WorkHubNavigationTests.cs`

### Задача 4

- `work/production/src/Task.Desktop/ViewModels/WorkHubSettingsPresentation.cs`
- `work/production/src/Task.Desktop/ViewModels/WorkHubViewModel.cs`
- `work/production/src/Task.Desktop/Views/WorkHubView.xaml`
- `work/production/tests/Task.Desktop.Tests/SettingsWindowsTests.cs`
- `work/production/tests/Task.Desktop.Tests/Work/DesktopSettingsPresentationTests.cs`
