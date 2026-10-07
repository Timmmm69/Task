# Location Manager 1.0.0 — validation report

Дата: 2026-10-08. Область: корпоративный «Каталог» Task, существующий LAN API.
Baseline `main` / `origin/main`: `06443765750ccea544c136f8d22551324afed667`.
Изменения реализованы в локальной рабочей копии; GitHub push не выполнялся.

## Что изменено

- `Work/DesktopFileLocationsClient.cs`: существующие GET locations, GET
  CatalogItem, POST/PATCH/DELETE locations и paginated GET network-resources.
  Используются общий authenticated executor, ETag и idempotency. Новые поля
  соответствуют действующему серверному контракту, server-controlled поля не
  отправляются.
- `Work/DesktopWorkApiClient.cs`: модель расположения теперь включает primary,
  enabled, priority, resource/device и допускает отсутствие rawPath. Русские
  подписи и доступное имя не содержат UUID, Version или внутренних API-полей.
- `ViewModels/FileLocationsViewModel.cs`: отдельная observable collection,
  команды и форма, загрузка/ошибки/partial access, подтверждение удаления,
  блокировка параллельных mutations, generation/cancellation при смене записи,
  обновление агрегата и восстановление после конфликта.
- `ViewModels/WorkHubLocationsViewModel.cs`, `WorkHubViewModel.cs`,
  `WorkHubCatalogViewModel.cs`, `CatalogNodeViewModel.cs`: связь выбора в дереве
  с расположениями, обновление версии/описания родителя, взаимная блокировка
  catalog mutations и location mutations, повторное чтение новой версии
  выбранной записи, очистка чувствительного состояния при изменении прав.
- `Views/FileLocationsView.xaml` и `.xaml.cs`, `WorkHubView.xaml`: карточки
  нескольких путей, форма, выбор ресурса по имени, native WPF file/folder
  pickers, primary/enabled, подтверждение удаления только ссылки. Code-behind
  обслуживает только native picker; бизнес-операции остаются в ViewModel.
- `Administration/DesktopAdministrationApiClient.cs`: доступное имя существующей
  модели ресурса — человеческое название, без технического DTO.
- Три новых test files: `FileLocationsViewModelTests.cs`,
  `DesktopFileLocationsClientTests.cs`, `PostgresFileLocationManagerTests.cs`.
- `verification/Test-Qa03Gate.ps1`, `Test-TaskWriteE2E.ps1` и
  `docs/QA-03-critical-e2e-matrix.md`: существующий QA-03 расширен новым реальным
  UIA сценарием; отдельный LocationsOnly runtime, диагностика ошибок и cleanup.
- Dashboard: только evidence/note/date в PROD-05, DESK-03, QA-03. Прежние
  пользовательские изменения сохранены; progress/status не повышались.

Production backend, PostgreSQL schema, FileLocationPolicy, permissions и
OpenAPI не изменялись. Рабочие файлы других задач и `sources/` не затронуты.

## Пользовательский сценарий

При выборе файла/папки загружаются все зарегистрированные расположения. Список
прежней записи сразу очищается при смене выбора. Видны тип, доступный путь,
основное/дополнительное расположение, включённость и сведения о доступности на
компьютере, которые допускают серверные данные. Пользователь добавляет путь,
меняет его параметры, назначает основным, отключает/включает или удаляет ссылку
с подтверждением. После записи перечитываются родитель и список; перезапуск
экрана не нужен. Путь не обязан существовать сейчас, автоматического удаления
не найденного файла нет. Физические файлы не изменяются.

## Permissions и sensitive paths

- Чтение каталога/ресурсов: `FileCatalog.Read`.
- Чтение locations и существующее открытие: `FileReference.Open` вместе с
  доступом к записи каталога.
- POST/PATCH/DELETE: `FileLocation.Update` и readable object scope; доступные
  действия зависят также от session/network и подтверждённого snapshot.
- `FileLocation.ReadSensitivePath` оценивает backend. Его действующее правило
  отдельно допускает owner/user/device path read. Клиент показывает только
  возвращённый сервером rawPath, принимает его отсутствие и не реконструирует
  скрытый путь. Скрытый путь невозможно редактировать, но разрешённые metadata
  PATCH не отправляют вместо него пустую строку или placeholder.
- На обновлении capabilities очищаются и path draft, и cached resource roots.
  Backend продолжает проверять объектные права и владельца локального пути.
  DTO-идентификаторы не попадают в accessibility names.

## Concurrency

`If-Match` содержит версию CatalogItem, а не location row. ETag ответа mutation
принимается как версия агрегата; затем GET CatalogItem обновляет запись дерева,
и GET locations перечитывает primary/версии всех строк. Локального Version++
нет. При отсутствии list ETag используется поддерживаемый GET родителя.

Настоящий HTTP 412 / VERSION_CONFLICT перечитывает родителя/список и сохраняет
черновик. Автоматического overwrite или повторного mutation нет: пользователь
проверяет форму и повторяет действие явно. При невозможности подтвердить
обновление snapshot кнопки записи блокируются до успешного refresh.

## UNC и local scope

UNC-форма загружает все страницы существующих разрешённых active
NetworkResource и выбирает ресурс по имени. UUID используется только внутри
request. Без такого выбора UNC POST не отправляется. Backend сохраняет
проверку активности/видимости ресурса и допустимого UNC root через неизменённый
FileLocationPolicy. Redacted root остаётся скрытым. Local/mapped requests не
передают ownerUserId/deviceId; backend связывает их с текущим пользователем и
устройством и проверяет allowLocalPaths.

## Фактическая проверка

Окончательный прогон: `evidence/accepted/`.

| Проверка | Результат |
|---|---|
| Release `dotnet build Task.sln --no-restore` | PASS, 0 errors; одно существующее xUnit1031 warning в DesktopCredentialVaultTests |
| Task.Desktop.Tests | 546 PASS, 0 FAIL, 0 SKIP |
| Task.ServiceHosts.Tests | 590 PASS, 0 FAIL, 0 SKIP |
| Task.Tests с TASK_POSTGRES_TEST_ADMIN_CONNECTION | 822 PASS, 0 FAIL, 0 SKIP |
| Всего solution tests | 1958 PASS, 0 FAIL, 0 SKIP |
| Новые regressions относительно baseline | 29: 26 desktop/client и 3 PostgreSQL |
| Targeted whitespace verification | PASS |
| Production/test project boundaries | PASS |
| git diff --check | PASS |
| dashboard:order, dashboard:validate | PASS; общая готовность проекта не объявлена |
| QA-03 real Release WPF + HTTPS API + PostgreSQL 16 | PASS |

Новые automated regressions покрывают selection, смену записи и late response,
empty/loading, local/UNC add, edit, primary, delete/последний путь, read/write
permissions, redaction/partial access, network/timeout, настоящую stale version
в PostgreSQL, вторую mutation с актуальной версией, resource roots/status,
device/user scope и local settings. Есть regression отзыва session/read access
во время загрузки ресурсов и same-item новой версии.

Реальный UIA сценарий создаёт запись, сохраняет local и UNC пути, редактирует и
отключает путь, получает 412 после конкурентного API PATCH родителя, сохраняет
черновик и явно повторяет запись с включением, меняет primary, останавливает
API, проверяет read-only/cache, восстанавливает API и удаляет обе ссылки. SQL
проверяет persistence каждого шага. SHA-256 физического sentinel-файла после
удаления последней ссылки совпал. Cleanup собственного стенда завершился.

Скриншоты реального WPF просмотрены. Устранены повторные подписи; ListBox
сохраняет доступность вложенных действий после перечитывания, имена строк
человеческие. Предыдущие диагностические прогоны сохранены отдельно в
`archive/quarantine/location_manager_attempts_1.0.0/` и не считаются финальным
подтверждением.

## Self-review и границы

Проверены source diff, безопасность path/owner/device fields, permission
revocation, отсутствие физических file operations и ложного offline success,
mutual write lock и отсутствие stale response при смене выбора. Независимый
read-only review обнаружил два recovery race; исправления закреплены тестами.
Дополнительно реальный UIA обнаружил и помог исправить refresh accessibility
и технические DTO names. Новых непроверенных обязательных функций в целевом
Corporate сценарии нет; его Definition of Done 1–18 выполнен.

Реальные границы: Personal — отдельный SQLite-контракт с одним путём, он не
переведён на API Location Manager; соответствующий schema/backup migration не
входит в эту LAN/API-задачу. Реальные SMB/ACL и чужая корпоративная сеть не
проверялись, используется синтетический разрешённый UNC root. Windows pickers
скомпилированы на установленном WPF; выбор файла в native dialog отдельно не
автоматизировался. Fallback/open redesign, новая offline synchronization и
управление NetworkResource не добавлялись. Остальные QA-03 UI сценарии этим
прогоном не повторялись; весь solution test suite повторён.

Пакет содержит source/evidence для review, а не установщик/деплой на компьютеры
компании. Изменения upstream `main` пока не опубликованы.
