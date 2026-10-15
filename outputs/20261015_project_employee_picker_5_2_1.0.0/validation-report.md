# Task — Employee Picker в проектах, этап 5.2

Версия пакета: **1.0.0**, 15 октября 2026. Ветка: `feature/employee-picker`.
База: `1cfb6c2` (этап 5.1). `origin/main` при подготовке: `5465c4e`;
ветка включает три сохранённых коммита общего picker. В main этот инкремент не публиковался.
Незакоммиченные изменения исходной рабочей копии сохранены без изменений.

## Результат

Реализованы добавление участника и выбор необязательного руководителя через общий компонент
5.1 для пользователей с User.Read. UUID-ввод сохранён для ролей без доступа к каталогу.
Существующие API/DTO, роли, membership schema, version handling, If-Match и idempotency сохранены.
Задание не включает календарь, передачу владения, расширение permissions или изменение сервера.

Изменённые production-файлы: `Modes/CorporateApplicationContext.cs`,
`ViewModels/ProjectsViewModel.cs`, новый `ViewModels/ProjectsViewModel.Employees.cs`,
`Views/ProjectsView.xaml`. Тесты: `ProjectsViewModelTests.cs`, новые
`ProjectEmployeePickerTests.cs` и `ProjectPickerWindowsTests.cs`.
Документация: `PROD-04-projects-and-members.md`, `PROJECT-EMPLOYEE-PICKER-5.2.md`.
Dashboard: evidence/note только PROD-04 и DESK-03, без повышения progress/status;
`dashboard:order` и `dashboard:validate` выполнены.
Полные пути и хеши входят в manifest.

## Acceptance

| Критерий | Проверено |
|---|---|
| AC-1 | Поиск имени в настоящем WPF control, выбор конкретной учётной записи по ID/login |
| AC-2 | AddMemberAsync получает подтверждённый ID, роль, project version, idempotency key |
| AC-3 | Authoritative reload участников, имя и сохранение выбранного объекта проекта |
| AC-4 | Предзаполнение/смена руководителя в существующем DTO; owner/version сохранены |
| AC-5 | Создание без руководителя и явное снятие назначения |
| AC-6 | Нет ManageMembers, read-only, offline, session ended: команды закрыты |
| AC-7 | HTTP 412 не даёт ложного успеха; черновик/руководитель сохраняются для явного retry |
| AC-8 | Известный участник блокирует add; server duplicate/state conflict не создаёт UI-строку |

Дополнительно: деактивация после выбора участника/руководителя, одинаковые имена,
403/404/authentication failure/server unavailable, недоступный выбранный аккаунт,
смена проекта во время поиска и повторной проверки, отсутствие User.Read с нулём directory calls.
Запоздалые ответы не переносят выбранного человека или имена на другой проект.

## Validation receipts

- Release solution build: **PASS**, 0 errors; финальный incremental build — 0 warnings.
  Первоначальный полный build содержал 11 прежних analyzer / ASP.NET deprecation warnings
  из тестов; новые файлы предупреждений не добавили.
- Полный serial desktop regression: **923 PASS / 0 FAIL / 0 SKIP**.
- Профильные Projects + Employees: **77 PASS / 0 FAIL / 0 SKIP** (подмножество desktop suite).
- User / Capabilities / PermissionAuthorization / Product endpoint contracts:
  **251 PASS / 0 FAIL / 0 SKIP**.
- Production/test architecture boundaries: **PASS**.
- Dashboard validation: **PASS**, progress не повышен; общий `handoff_ready` остаётся false.
- `git diff --check`: **PASS**.
- Пакет проверяется по каждому SHA-256 и повторно по содержимому ZIP; результат записывается
  в `.zip.validation.json`.

Команды:

```powershell
dotnet build work/production/Task.sln -c Release --nologo
dotnet test work/production/tests/Task.Desktop.Tests/Task.Desktop.Tests.csproj -c Release --no-build -- xUnit.ParallelizeTestCollections=false xUnit.MaxParallelThreads=1
dotnet test work/production/tests/Task.Desktop.Tests/Task.Desktop.Tests.csproj -c Release --no-build --filter 'FullyQualifiedName~ProjectsViewModelTests|FullyQualifiedName~Projects.DesktopProjectsApiClientTests|FullyQualifiedName~Employees' -- xUnit.ParallelizeTestCollections=false xUnit.MaxParallelThreads=1
dotnet test work/production/tests/Task.ServiceHosts.Tests/Task.ServiceHosts.Tests.csproj -c Release --no-build --filter 'FullyQualifiedName~UserEndpointsTests|FullyQualifiedName~CapabilitiesEndpointsTests|FullyQualifiedName~TaskPermissionAuthorizationTests|FullyQualifiedName~ProductEndpointsTests'
```

Native Windows test показывает настоящее ProjectsView в WPF-окне: поиск через TextBox binding,
подтверждение через button command, общий control в редакторе руководителя, UUID visibility,
узкая форма, прокрутка и focus кнопки «Сохранить», форма без User.Read.
Пять кадров входят в evidence; проверены визуально. Обычная форма и 900 px viewport используют
существующий scroll layout. Это автоматизированный native probe, а не физическая клавиатура,
screen-reader listening audit или ручной walkthrough развёрнутой организации.

## Permissions и ограничения

`GET /users` и `/users/{id}` требуют самостоятельный `user.read` / `User.Read`.
`Project.ManageMembers` и `Employee.Read` не дают его. Проверены текущие endpoint policies,
capability mapping, migration 012, bootstrap administrator и серверная active-account validation.
Project manager/member editor без User.Read сохраняет UUID-сценарий; сервер проверяет реальный
object scope и active account. Полный отказ от UUID для этой роли **BLOCKED** отдельным решением
по контракту прав. Ни grants, ни обходные endpoints, ни общий межаккаунтный кэш не добавлены.
Конкретные effective grants/denies production-ролей не перечислялись против live DB.

Существующий сервер проверяет active user транзакционно для membership и manager write.
Отдельная клиентская проверка уменьшает ошибки выбора, но не заменяет серверную авторизацию.
403 каталога очищает загруженные имена, не расширяя project permissions. Недоступные имена
остаются нейтральными. После offline/access перехода необходимо подтвердить руководителя снова.

Реальная сеть/каталог организации, большой live PostgreSQL-каталог и реальные production-аккаунты
в этой задаче не проверялись. Server paths не менялись; HTTP/endpoint suites и native WPF fixture
не выдаются за live deployment acceptance. Отправленный до переключения проекта write может
завершиться на сервере; исходный проект перечитывается при последующем открытии.

**Готовность:** инкремент проектов готов к интеграции для User.Read с сохранённой совместимостью
UUID. Блокер полного role coverage документирован; весь продукт/следующий этап завершённым не объявлен.
