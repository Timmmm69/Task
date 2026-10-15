# Task — финальный review Employee Picker 5.1–5.3

Версия **1.0.1**, 2026-10-15. Ветка `feature/employee-picker`, общая база `5465c4e`.
Исходный код финальной проверки: **fa8a15cf93da166040741c869d8b761d72a8eaf3**.
Публикация в main явно разрешена пользователем после исправления найденных ошибок и проверки.
Пакет заменяет прежнюю оценку merge readiness из отчёта 5.3, сохраняя предыдущие receipts как историю.

## Результат review

Проверены общий EmployeePickerViewModel/View, existing User API adapter, проектные участники и
руководитель, календарные сотрудники/контакты, permission gating, DTO round-trip, cancellation,
late completions, save/add races и результаты всех трёх этапов.
Независимый read-only review 5.1/5.2 выявил P2: открытая без User.Read форма проекта не переключалась
на picker руководителя при выдаче права. Исправлен в fa8a15c: используется dormant access-gated
picker, без запросов до выдачи права. После grant та же форма включает picker и проверяет ранее
сохранённый ManagerId; revoke отменяет запросы, очищает directory data и сохраняет хранимый ID.
Два unit-сценария покрывают создание и существующий проект с назначенным руководителем; native
WPF probe проверяет смену видимости на той же форме. Повторный независимый review не нашёл
блокирующих регрессий в исправлении.

| Этап | Реализация | Коммиты |
|---|---|---|
| 5.1 | Один общий bounded Employee Picker, подтверждение UserAccountId, cursor, cancellation | bccc86c, 9a1bddc, 1cfb6c2 |
| 5.2 | Участник и необязательный руководитель проекта, имена по User.Read, active check, live-grant fix | ed80529, fa8a15c |
| 5.3 | Сотрудники календаря через тот же picker, contacts отдельно, роли/ответы/версии/timezone сохранены | 5ab9abf, пакет 5aa3cac |

Других новых функциональных дефектов в проверенном scope не выявлено. API, DTO, backend,
миграции, grants/roles и уведомления не менялись. Чужие локальные изменения не включены.

## Итоговые проверки

| Проверка | Результат |
|---|---|
| Release solution build | PASS, 0 errors / 0 warnings |
| Полный serial desktop regression | **959 PASS / 0 FAIL / 0 SKIP** |
| Scoped Employees + Projects + Calendar | **206 PASS / 0 FAIL / 0 SKIP**, подмножество desktop |
| Unit + реальная PostgreSQL 16 (Calendar, Product API/stores, authorization) | **233 PASS / 0 FAIL / 0 SKIP** |
| Calendar/User/Capabilities/PermissionAuthorization/Product HTTP endpoint integration | **259 PASS / 0 FAIL / 0 SKIP** |
| Точечный grant fix + native Projects WPF | **3 PASS / 0 FAIL / 0 SKIP**, подмножество desktop |
| Architecture boundaries, dashboard order/validation, diff whitespace | PASS |

Общий независимый объём desktop + domain/Postgres + endpoints: **1451 PASS**, без прибавления
повторных профильных проверок. Проверена реальная WPF binding/command/visibility/focus цепочка
в Windows-окнах, включая ранее падавший TaskContextActionsUiTests.

Исторический Window.Activate flake не скрыт: промежуточный прогон дал 958 PASS / 1 FAIL на том же
existing activation assertion. Сохранены desktop-window-activation-failure.trx/log. Ранее ошибка
воспроизводилась на неизменённой базе ed80529 (пакет 5.3). Финальный прогон выполнен отдельно,
без второго набора native UI одновременно, и все 959 прошли. Тест не отключён и не изменён.

PostgreSQL запускался отдельным временным кластером из установленного PostgreSQL 16, только на
127.0.0.1 с динамическим портом. Test connection задана непосредственно перед тестами; receipts
и postgres-runtime.json подтверждают запуск. Кластер штатно остановлен, существующие сервисы
и пользовательские базы не затрагивались. Начальная попытка initdb в Unicode-пути workspace
отвергнута Windows PostgreSQL; повтор использовал стандартный ASCII TEMP fixture path.

## Сохранённые ограничения контракта

- User.Read — самостоятельное право. Без него остаётся прежний UUID-ввод участников/руководителя
  проекта и сотрудников календаря. UUID контакта сохранён, Contact Picker вне scope.
- Picker календаря требует Calendar.Read и соответствующего CalendarEvent.Create/Update плюс
  User.Read; проектные права не дают доступ к каталогу автоматически.
- Calendar backend не проверяет активность attendee транзакционно. Клиент повторно читает нового
  сотрудника перед добавлением и перед сохранением, но гонка деактивации после GET остаётся
  пределом действующего серверного контракта. Backend не расширялся без отдельного согласования.
- Ручной walkthrough установленного production-клиента, реальная сеть организации, её effective
  role assignments и нагрузочная проверка большого каталога не выполнялись.

Текущие APIs: GET /api/v1/users и /users/{id}; calendar schedule/conflicts/details и POST/PATCH
/calendar-events; существующие projects/members API. UUID→name replacements и permissions
подробно описаны в отчётах 5.1–5.3. Существующие IDs/roles/response timestamps не мигрировались.

В пределах утверждённого scope блокирующих ошибок не осталось; ветка готова к публикации этапов
5.1–5.3 в main. Общая готовность всего продукта не повышалась: dashboard изменён только для
PROD-03, PROD-04 и DESK-03, progress/status сохранены.

Пакет содержит VERSION, manifest, SHA256SUMS, актуальные исходники всех трёх этапов, TRX,
build/check logs, WPF screenshots и этот отчёт. ZIP и сохранённый в Git snapshot проверяются
по содержимому и SHA-256; факт успешного push отдельно проверяется по origin/main.
