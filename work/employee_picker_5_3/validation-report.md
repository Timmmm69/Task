# Task — Employee Picker, финальная проверка этапов 5.1–5.3

Версия пакета **1.0.0**, дата **2026-10-15**, ветка `feature/employee-picker`.
Календарь: **5ab9abfc0c57c364dc7659f844c870f0e4eb77c4**.
База 5.3: `ed80529`. Общая база и актуальный при подготовке origin/main: `5465c4e`.
Работа выполнена в существующей рабочей копии `work/employee_picker_5_1/repo`.
Незакоммиченные изменения основной папки сохранены. Push/merge/создание PR не выполнялись.

## Итог трёх этапов

| Этап | Статус | Реализовано | Тесты | Коммиты |
|---|---|---|---|---|
| 5.1 Employee Picker | Реализован для User.Read | Один общий WPF/MVVM-компонент; bounded search, cursor, подтверждённый ID, preselect, cancellation, auth/offline | На этапе: 42 профильных, 901 desktop, 13 server-contract PASS. Сейчас перепроверен в общем профильном прогоне 204 PASS | bccc86c, 9a1bddc; пакет 1cfb6c2 |
| 5.2 Проекты | Реализован для User.Read | Участник и необязательный руководитель, разрешённые имена, revalidation, сохранённые DTO и версии | На этапе: 77 профильных, 923 desktop, 251 server-contract PASS. Сейчас проекты и их native WPF probe входят в 204 PASS | ed80529 |
| 5.3 Календарь | Реализован; профильный gate PASS, общий desktop gate с замечанием | Общий picker только для сотрудников; контакты отдельно; лимиты, роли/ответы, edit round-trip, permissions, stale guards, повторная проверка нового сотрудника при сохранении | 204 профильных PASS, 174 domain PASS, 259 HTTP/permission integration PASS. Итоговый desktop: 956 PASS / 1 FAIL / 0 SKIP | 5ab9abf |

## Изменения и acceptance

Используется существующий `EmployeePickerView` / `EmployeePickerViewModel`, переданный через
существующий `IDesktopEmployeeDirectory` из `DesktopAdministrationApiClient`. Второго каталога,
поискового компонента или постоянного кэша не создано. Shared picker и проекты в 5.3 не переписывались.

- AC-1/2: поиск по имени, результаты с именем/login/должностью, подтверждение конкретного account ID,
  затем существующий AddAttendeeCommand. Перед добавлением проверяется текущая активность аккаунта.
- AC-3: повтор `(IsUser, Id)` не добавляется; одинаковый UUID сотрудника и контакта допустим.
- AC-4/5: контакт вводится через прежний UUID; employee/contact сериализуются отдельно.
  Реальный HTTP-client test проверяет POST и PATCH, оба массива и round-trip response/timestamp.
- AC-6: ID, IsUser, Role, ResponseStatus, RespondedAtUtc существующих участников копируются без изменений.
  Недоступные/деактивированные старые участники не удаляются и не меняют ответ при name lookup.
  Удаление и роли required/optional/observer сохранены. Лимит — 500 сотрудников и 500 контактов.
- AC-7: без User.Read ноль directory calls и нейтральные имена. Серверный 403 очищает ранее загруженные
  имена/поиск; новые неподтверждённые picker-участники блокируют отправку, пока их не удалить или
  не восстановить доступ. Старые участники и контакты остаются доступны по календарным правам.
- AC-8: 412 сохраняет черновик и обновляет серверную версию по прежнему сценарию, без ложного успеха.
  404/недоступность/validation/auth/403 также не объявляют успешное сохранение.

Смена типа очищает UUID и employee confirmation. Закрытие формы, деактивация, смена capabilities,
сессии и подключения отменяют запросы, удаляют directory data и отвергают late responses.
Деактивация/blocked/not found/неверный ID после поиска блокируют добавление.
Новые picker-участники повторно проверяются непосредственно перед записью события.
Во время add-revalidation нельзя сохранить неполный список. Во время save отключена вся форма;
команда заново строится после directory validation. Гонка потери правок найдена независимым review,
исправлена и покрыта тестом с ожидающим GetUserAsync.

DTO/серверный сериализатор, If-Match, event version, idempotency, timezone/DST/time rules,
уведомления и приглашения не изменены. Проверка diff всей ветки от 5465c4e подтверждает отсутствие
изменений API/Application/Domain/Infrastructure/OpenAPI. Во всех трёх этапах backend не менялся.

## UUID: что заменено и что сохранено

Заменены при доступном User.Read:
1. Проект: NewMemberUserId — выбор участника по имени.
2. Проект: необязательный ManagerId — выбор руководителя по имени, явное снятие назначения.
3. Календарь: AttendeeId при IsUser=true — общий Employee Picker.

Сохранены:
- UUID участников и руководителя проекта без User.Read: действующие API требуют отдельного права
  для каталога, а project write не даёт его. Права ролей не расширялись.
- UUID сотрудника календаря без User.Read: сохранён ранее разрешённый сценарий редактирования.
- UUID контакта календаря: Employee Picker не предназначен для контактов; Contact Picker вне scope.
- Техническое отображение IDs и другие UUID-поля вне задачи №5 не удалялись. ProjectId — другая сущность,
  владение/передача владения и другие модули не менялись.

## Permissions и API

| Комбинация | Поведение |
|---|---|
| Calendar.Read + CalendarEvent.Create + User.Read | Picker в новом событии |
| Calendar.Read + CalendarEvent.Update + User.Read | Picker в существующем событии |
| Calendar.Read + соответствующее Create/Update без User.Read | Прежний UUID-ввод, без directory reads |
| Calendar.Read + User.Read без соответствующего write | Нет редактируемой формы участников |
| Нет Calendar.Read, logout, offline, неактивная поверхность | Picker queries и attendee mutations закрыты |

Effective permissions проверяются отдельно; роли/наборы grants реальной организации не опрашивались.
В проектах сохраняются Project.Read, Project.ManageMembers и соответствующие Project.Create/Update.

Используются существующие endpoints:
- GET /api/v1/users?sort=id&filter=...&cursor=..., GET /api/v1/users/{id} — User.Read.
- GET /api/v1/calendar, GET /api/v1/calendar/conflicts, GET /api/v1/calendar-events/{id} — Calendar.Read.
- POST /api/v1/calendar-events — CalendarEvent.Create.
- PATCH /api/v1/calendar-events/{id} — CalendarEvent.Update и существующая object-scope проверка.

## Фактические проверки

- Release solution build — PASS, 0 errors, 2 прежних analyzer warnings в существующих тестах.
- Профильная регрессия Employees + Projects + Calendar — **204 PASS / 0 FAIL / 0 SKIP**.
- Calendar domain/application unit tests — **174 PASS / 0 FAIL / 0 SKIP**.
- Calendar/User/Capabilities/PermissionAuthorization/Product HTTP endpoint integration —
  **259 PASS / 0 FAIL / 0 SKIP**.
- Полный serial desktop на финальном коде — **956 PASS / 1 FAIL / 0 SKIP**, всего 957.
- Production/test dependency boundaries, dashboard order/validation, diff check — PASS.
- Настоящий WPF UI на Windows — проверен автоматизированным окном: binding поиска, selection/confirm,
  host add, сотрудники/контакты, UUID visibility, прокрутка, фокус кнопки, узкое окно, сохранение/disablement.
  Повторно выполнены native probes shared picker и проектов. Кадры календаря визуально просмотрены.

Исключение общего desktop gate: существующий
`TaskContextActionsUiTests.NativeWindows_MouseKeyboardFocusRelationsAndSmallWindow` падает на строке 62,
`Assert.True(company.Activate())`, при показе окна Tasks. Отдельный повтор тоже FAIL. Для проверки
происхождения экспортирован неизменённый **ed80529**, отдельно собран, и этот же тест воспроизводит
тот же FAIL на Activate. Это подтверждённое ограничение native window activation в текущем сеансе,
не новый дефект интеграции календаря. Тест не отключался, его исходники не менялись; результаты FAIL
сохранены в TRX. Общий gate не объявляется зелёным. Предшествующий прогон до добавления последних
пяти тестов был 952 PASS; финальным доказательством считаются приложенные 957 результатов.

В evidence входят точные команды/вывод build/test, final TRX, baseline/recheck receipts и screenshots.
Полный продукт не объявляется принятым по одному профильному набору.

## Не выполнено, ограничения, готовность PR

Не выполнялись: ручной walkthrough установленного клиента с физической клавиатурой/мышью;
проверка live организации/сети/большого каталога и её effective role grants; новый live PostgreSQL gate;
production deployment и ручная проверка уведомлений/приглашений. Environment variable
TASK_POSTGRES_TEST_ADMIN_CONNECTION не задана; 174 unit и 259 endpoint tests не выдаются за real-DB gate.

Известный предел API: CalendarEventLifecycleService/PostgresCalendarEventStore и migration 003 не
реализуют транзакционную проверку активности учётной записи attendee. Две клиентские проверки
снижают риск, но не исключают деактивацию между последним GET и commit. UUID compatibility path
сохраняет прежнюю серверную валидацию. Исправление этой гонки требует отдельного согласованного
backend-контракта. Новые календарные права/grants или серверные сущности не вводились.

Новых известных функциональных дефектов в проверенном scope не выявлено. Открытые ограничения:
полный охват ролей без User.Read; серверная гонка деактивации; inherited native activation FAIL.

**Ветка готова к review/Draft PR** с перечисленными ограничениями; **безусловная готовность к merge
не заявляется** до разрешения общего native gate. Push и PR не создавались, изменений в main нет.
Dashboard обновлён только для PROD-03/DESK-03, без повышения progress/status; handoff_ready=false.

Пакет содержит VERSION, manifest с SHA-256 каждого файла, SHA256SUMS, исходники, evidence и этот
отчёт. Отдельная проверка ZIP сверяет все файлы, manifest и контрольные суммы. Успешная проверка
целостности пакета не равна успешному прохождению всех regression gates.
