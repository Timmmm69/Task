# Catalog Tree 1.0.0 — validation report

Дата: 2026-10-07, Europe/Minsk. Репозиторий: Timmmm69/Task.
Базовый HEAD: f6689a57f83ce5b050c626e11850a399a2006fca.
Результат: иерархический Каталог реализован и проверен локально. Commit/push не выполнялись.

## Что реализовано

- Нативный WPF TreeView с presentation-моделью CatalogNodeViewModel, Children, ParentId, Version, IsExpanded и IsSelected.
- Папки визуально отличаются от записей; сохранён существующий AutomationId CatalogList.
- Corporate-клиент загружает корень и каждую виртуальную папку через catalog/tree с depth=1, limit=200 и всеми cursor pages. Ограничение размера одного серверного запроса соблюдается; общего ограничения в 200 записей или 8 уровней в клиенте нет. Порядок соседних элементов сохраняется; дубликаты и некорректные страницы отклоняются.
- Создание виртуальной папки в корне и в выбранной папке; создание file_reference/folder_reference/virtual_folder с выбранным parent.
- UX-решение: при выборе обычной записи создание происходит в её родительской папке. Кнопка «Корень каталога» сбрасывает выбор и позволяет создавать в root.
- Drag & drop на virtual_folder; drop на кнопку корня; доступная с клавиатуры команда «Переместить в…» с выбором папки или корня и полными путями папок.
- Защита Desktop от parent=self, parent=descendant, обычного элемента в роли parent и бесполезного move в текущую папку. Backend повторно проверяет ограничения.
- FileCatalog.Read/Create/Update и существующие FileLocation.Update/FileReference.Open; новые permissions не добавлены.
- If-Match использует актуальную версию выбранного элемента. После create/move читается актуальное дерево; после сохранения расположения версия перечитывается в обоих режимах. Ошибка refresh отключает дальнейшие записи до успешного чтения. Неопределённый результат mutation также требует обновления.
- Настоящий VERSION_CONFLICT вызывает refresh и предупреждение; операция не объявляется успешной. CATALOG_CYCLE и validation ошибки показываются понятным русским сообщением; Forbidden/NotFound/ServerUnavailable используют существующий feedback.
- При потере связи уже загруженное дерево сохраняется, writes отключаются. После восстановления связи доступен Refresh.
- Refresh сохраняет выбранный Id и раскрытые папки; если запись исчезла, используется её доступный родитель либо корень.
- SelectCatalogItemAsync находит элемент по Id, раскрывает родителей и выделяет узел.
- PersonalWorkClient подключён к тому же UX; существующий SQLite parent_id читается в DTO, CreateCatalog и MoveCatalog используются без миграции БД.

## Фактический контракт и архитектура

Desktop ранее использовал catalog-items?limit=200 и не передавал parent при создании.
Runtime-контракт проверен по ProductApiContracts.cs, ProductEndpoints.cs, PostgresProductApiQueries.cs и PostgresProductApiStore.cs.

Существующие маршруты: GET /api/v1/catalog/tree, POST /api/v1/catalog-items, POST /api/v1/catalog-items/{id}/move.
Runtime использует parentItemId в create/move, If-Match в move, items/hasMore/nextCursor в tree. Tree принимает parentId/depth/limit/cursor; depth одного запроса ограничен 8, размер страницы 200, многоуровневое раскрытие одного запроса ограничено общим бюджетом 1000 узлов.

Архивный work/stage_3_5_delta/openapi/openapi.yaml содержит иную форму CatalogTree (rootItems/item/childIds), CatalogMoveRequest (parentCatalogItemId/expectedParentVersion) и depth до 10. Desktop реализован против существующего работающего runtime, который эти архивные поля не принимает. Новые endpoints, backend DTO, permission, миграции и backend-изменения не вводились.

Разделение ответственности: DesktopCatalogItem — API/local-storage model; CatalogNodeViewModel — presentation; WorkHubCatalogViewModel — сценарии и команды; WorkHubView code-behind — только WPF selection/drag/drop events. Серверные hierarchy writes уже сериализуются tenant advisory locks; backend остаётся источником авторизации и cycle validation.

## Ключевые файлы

Production, всего 10 файлов:

- src/Task.Desktop/Work/DesktopWorkApiClient.cs, DesktopCatalogApiClient.cs.
- src/Task.Desktop/ViewModels/WorkHubViewModel.cs, WorkHubCatalogViewModel.cs, CatalogNodeViewModel.cs.
- src/Task.Desktop/Views/WorkHubView.xaml и WorkHubView.xaml.cs.
- src/Task.Desktop/Personal/PersonalWorkClient.cs, PersonalWorkspaceStore.cs, PersonalWorkspaceViewModel.cs.

Tests, всего 4 файла:

- tests/Task.Desktop.Tests/Work/CatalogTreeViewModelTests.cs и DesktopCatalogApiClientTests.cs — 23 новых test cases.
- tests/Task.Desktop.Tests/Work/WorkHubViewModelTests.cs — fake client теперь отражает mutation state.
- tests/Task.Desktop.Tests/Work/DesktopWorkApiClientTests.cs — ожидаемый tree route.

Все пути выше относительны к work/production. В source/ находятся копии всех изменённых файлов; отдельный WPF probe и Verify-CatalogTree.ps1 находятся в work/catalog_tree_1. Dashboard обновлён только для PROD-05/DESK-03: note, evidence, updated_at; прежние пользовательские изменения, status и progress сохранены, dashboard:order выполнен.

## Проверки

| Проверка | Результат |
|---|---|
| dotnet restore Task.sln | PASS |
| Dependency audit, direct + transitive, blocking high/critical | PASS |
| dotnet build Task.sln -c Release --no-restore | PASS |
| dotnet format whitespace Task.sln --verify-no-changes --no-restore | PASS |
| Project boundaries | PASS |
| Task.Tests, полный прогон с PostgreSQL 16 | 819 PASS / 0 SKIP / 0 FAIL |
| Task.ServiceHosts.Tests | 590 PASS / 0 SKIP / 0 FAIL |
| Task.Desktop.Tests | 520 PASS / 0 SKIP / 0 FAIL |
| Всего solution tests | 1929 PASS / 0 SKIP / 0 FAIL |
| Existing PostgresProductApiTests + PostgresProductStoresTests на реальном PG16 | 18 PASS |
| Дополнительная проверка keyboard move после уточнения теста | 11 PASS |
| WPF event/layout probe | PASS |
| Independent security review, без дублирования build/tests | PASS |
| Production secrets/TLS contract, PowerShell 7 | PASS |
| git diff --check, production | PASS |
| dashboard:order и dashboard:validate | PASS |

Полный gate выполнен с отдельным временным PostgreSQL 16.14 на 127.0.0.1:63451. Существующие пользовательские базы и службы не менялись. Временный сервер остановлен.
При пересборке видны существующие warnings xUnit1031 в DesktopCredentialVaultTests и ASPDEPR004 в service-host tests; новых warnings, связанных с Каталогом, нет. Итоговая инкрементальная solution-сборка: 0 warnings / 0 errors.

WPF probe загружает настоящую WorkHubView и Theme, создаёт реальные SQLite-объекты, проверяет TreeView container generation, selection/expansion binding, bubbling DragOver/Drop на вложенную папку и root, actual move/version refresh, создание записью/папкой через кнопки и root selection. Итоговый render визуально проверен; catalog-tree-wpf.png включён в пакет.

## Самопроверка Definition of Done

- Tree, empty catalog, root/child/multiple levels: PASS.
- Folder in root, folder in folder, regular item in folder: PASS.
- Item and folder between folders, root move: PASS.
- Cycle/self/regular-parent protection, backend concurrency: PASS.
- Permissions and read-only on server unavailable: PASS.
- Fresh Version after mutation, VERSION_CONFLICT recovery: PASS.
- Selection/expansion and disappeared-selection fallback: PASS.
- Personal Mode, reopen persistence, Corporate HTTP client: PASS.
- Pagination beyond 200 and 12-level tree: PASS.
- Existing open-file scenario and all existing Catalog tests: PASS.
- Build, automated tests, formatting and standard security gates: PASS.

## Реальные ограничения проверки

Интерактивное перетаскивание физической мышью в установленном portable-клиенте не проводилось. Реальные WPF events/layout и команды проверены автоматизированно; отдельная ручная проверка установленной сборки этим отчётом не объявляется.

Автоматическая проверка запретила рекурсивное удаление временного PostgreSQL-каталога вне workspace с причиной «blocked by policy». Сервер остановлен; тестовые данные остались в C:\Users\novik\AppData\Local\Temp\TaskCatalogTreePostgres-724c95785372411789b08af830862f0a.

Это пакет исходных изменений и evidence. Он не заменяет ранее выданные portable-релизы и не опубликован в origin/main.

## Воспроизведение

В актуальном workspace выполнить PowerShell 7 script work/catalog_tree_1/Verify-CatalogTree.ps1, передав PostgresConnection для отдельного тестового PostgreSQL 16, где разрешены CREATE DATABASE/ROLE. Script выполняет restore/audit, format, boundaries, build, solution tests, WPF probe и security/TLS gates, сохраняя evidence/recheck. Не использовать рабочую корпоративную БД для integration fixtures.

Manifest, VERSION, SHA256SUMS и validation.json фиксируют состав и результаты. Отдельный ZIP и его .sha256 сопровождают пакет.
