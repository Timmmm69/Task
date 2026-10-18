# Управление сотрудниками — проверка 11.1–11.4

Версия 11.4.0, 2026-10-18. Ветка `feature/admin-user-management`.
База 11.4: `1f42e49`; общая история `origin/main`: `2f50889` на начало работы.

**Фактический статус: безопасная часть реализована; задача №11 целиком не закрыта.**
Открыт контрактный блокер **OQ-ADMIN-ROLES-READ**. Клиент не отправляет PUT назначений.
API, БД, определения ролей, permissions и нормативные `sources/` не изменены.

## Результаты этапов

| Этап | Результат | Основные файлы относительно `work/production/` | Коммит реализации |
|---|---|---|---|
| 11.1 API | Реализован типизированный клиент; повторно проверяется общим gate | `Administration/DesktopUserManagementContracts.cs`, `Administration/DesktopUserManagementApiClient.cs` в `src/Task.Desktop/`; `docs/USER-MANAGEMENT-API.md`; API-client tests | `4c737b2` |
| 11.2 Создание и редактирование | Реализованы pending_activation, профиль, минимальный PATCH, сохранение черновика и защита версии | `ViewModels/AdminUserEditor.cs`, `ViewModels/AdministrationUsers.cs`, `Views/AdministrationView.xaml`; editor/Windows tests | `ec39281` |
| 11.3 Состояния | Реализованы пять подтверждаемых переходов; server rules сохранены | `ViewModels/AdministrationUserTransitions.cs`, `Views/AdministrationView.xaml`; transition/Windows, UserEndpoints, PostgresIdentityLifecycle tests | `1f42e49` |
| 11.4 Роли и итоговая проверка | Частично: самостоятельный безопасный экран ограничения, защита чтения и улучшение каталога; назначения заблокированы | `ViewModels/AdministrationViewModel.cs`, `AdministrationUsers.cs`, `AdministrationUserTransitions.cs`, `Administration/DesktopAdministrationApiClient.cs`, `Views/AdministrationView.xaml`; AdministrationAccess, API-client, editor/Windows, PostgresAdministrationRole tests | `780bf72` |

Полный перечень изменённых файлов, точные результаты запусков и SHA-256 представлены в
`outputs/20261018_admin_user_management_11.4.0/`.

## Проверка контракта ролей

Источники: Stage 2.2 `openapi/openapi.yaml` (User, UserRole, UserRolesReplace и пути users/roles),
`ProductApiContracts.cs`, `ProductEndpoints.cs`, `PostgresProductApiRoles.cs`,
`TaskPermissionAuthorization.cs`, `Users/UserEndpoints.cs`, `IUserAccountReadStore.cs`.

1. `GET /api/v1/roles` возвращает определения ролей организации, **не назначения**.
   Требует `Role.Read`; пользователь с одним `User.ManageRoles` не получает каталог автоматически.
2. `User` в нормативном OpenAPI, серверной read projection и desktop DTO не содержит назначения.
   На `/api/v1/users/{id}/roles` в нормативном OpenAPI присутствует только PUT.
   Реестр работающего API также не регистрирует GET назначений.
   Effective permissions и история не позволяют восстановить исходные назначения со scopes/expiry.
3. PUT заменяет **весь** набор, до 100 уникальных roleId. Разрешены только `roleId`,
   `departmentId`, `validUntil`; body также содержит `expectedUserVersion`, совпадающий с If-Match.
   Role и department принадлежат текущей организации; department должен быть активным;
   validUntil — будущий instant с явным offset. Системные определения не редактируются.
4. Нужны одновременно `User.ManageRoles` и серверное `organization.manage`.
   Право вкладки не является правом чтения пользователей или выполнения иной команды.
   Организационное администрирование не подставляется клиентом в capabilities.
5. Сервер допускает роли из текущей организации. Роли, выдающие organization.manage,
   identity.account.manage, identity.role.manage или user.manageroles, нельзя ограничивать
   departmentId или validUntil: требуется постоянное назначение на организацию.
6. После полной замены в транзакции должен остаться хотя бы один активный пользователь с
   organization.manage. Иначе `409 LAST_ADMINISTRATOR` и rollback. User lifecycle и role writes
   используют общий advisory lock организации, а не только разные блокировки записей.
7. Версия объекта пользователя увеличивается на 1. Authorization scope version увеличивается
   триггерами назначений и явным BumpScope; это монотонный маркер, не обязательно ровно +1.
   Повтор того же idempotency key не выполняет изменения и не увеличивает scope повторно.
   Есть audit/outbox; сервер повторно вычисляет права, включая проверку до replay.
8. PUT возвращает назначения с userAccountId, roleId, departmentId, validUntil, grantedAt,
   grantedBy и новый ETag. Нормативный `UserRole` описан иначе (id, roleCode, validFrom).
   Это дополнительное расхождение для согласования, а не основание изобретать новый DTO/GET.

Для снятия OQ нужен согласованный authoritative read полного набора с версией и правилами
авторизации. Также требуется согласовать нормативный response назначения с действующим сервером.
Конкретный новый endpoint, permission или миграция этим этапом не утверждены и не добавлены.

## Что работает в UI

- «Пользователи»: список, серверный текстовый поиск по displayName/login/workEmail, cursor pagination,
  карточка доступных сводных полей, создание pending_activation, редактирование профиля.
  Поиск применяется явно; следующая страница сохраняет применённый filter, новый поиск сбрасывает cursor.
- Активация — User.Create; блокировка, разблокировка, деактивация, восстановление — User.Block.
  Перед подтверждением читается свежая карточка; для block/deactivate обязательна причина.
- Каждая команда дополнительно требует User.Read, активную сессию и подключение.
  Read-only не превращается в write после выбора другой вкладки.
- «Роли и права»: только каталог по Role.Read; показаны все опубликованные permissions выбранной
  роли, включая обозначение deny. Каталог не называется effective permissions пользователя.
- «Управление доступом»: отдельная область Access по User.ManageRoles. Показано ограничение;
  refresh, account commands и чтение сотрудников не выполняются. Нет кнопки опасной замены.
- «Сетевые ресурсы»: прежний FileCatalog.Read и серверное скрытие чувствительных путей сохранены.

Отзыв User.Read, Role.Read, FileCatalog.Read очищает соответствующие данные и selection;
выход из сессии очищает все три области. Поздние ответы после отзыва/выхода/deactivate/dispose
не восстанавливают сведения. После смены вкладки устаревший результат игнорируется и загружается
последняя выбранная область. Forbidden при свежем чтении пользователя снимает локальный User.Read;
восстановление сети само по себе не возвращает это право. Нужен новый authoritative capability update.

## Отдельный критический проход и безопасность

Проведён повторный просмотр ранее реализованных этапов и итогового diff после реализации.
Внешний независимый reviewer не привлекался.

| Угроза/сценарий | Результат и проверяемое основание |
|---|---|
| Self escalation, delegated User.ManageRoles | PUT отсутствует в desktop surface; сервер требует organization.manage; существующий Authorization_RoleAssignment… проверяет 403 до изменения |
| Административная роль с expiry/department | Сервер возвращает 422; новые PostgreSQL негативные тесты проверяют rollback исходных назначений |
| Последний активный администратор | Существующий real-PG Authorization_RoleAssignment… проверяет запрет role removal и block; lifecycle matrix проверяет переходы |
| Потеря неизменённых ролей/scopes | Desktop replacement не включён. Store-тест проверяет полный receipt с departmentId/validUntil; это не означает готовность UI назначения |
| Другая организация | Серверные User read/command stores ограничивают organizationId; неизвестные/чужие target/role/department не принимаются. Runtime identity определяется сервером |
| Устаревшая версия, 412/428 | If-Match и expectedVersion по существующему контракту; черновик сохраняется, слепого повтора нет; editor/transition/API tests |
| Двойной клик и неопределённая запись | AsyncCommand сериализует команду; явный повтор сохраняет исходные bytes/key/version/reason; 401-refresh replay сохраняется в общем executor |
| Старые permissions | Scope version и серверная revalidation сохранены; поздние UI receipts проверяют поколение доступа; новые тесты очищают роли/ресурсы/пользователей |
| Offline/read-only/session expiry | Нет offline write queue; команды выключаются; подтверждённое состояние не меняется по локальному предположению |
| Смена выбранного пользователя | Детали проверяют id/generation; state confirmation блокирует смену selection; mutation использует захваченные id/version |

Исправлены: alias вкладки доступа на Users; удержание и поздняя публикация ролей/ресурсов после
потери доступа; удержание списка сотрудников после запрещённого чтения карточки; потерянный
refresh при смене вкладки; обрезание списка permissions и отсутствие обозначения deny;
неидемпотентный Dispose. Добавлен отсутствовавший UI серверного поиска.

Один существующий editor test уточнён из-за намеренного изменения security behavior: после
403 проверяется очистка и запрет повторного чтения, затем явно подтверждаются новые capabilities
и выполняется refresh. Проверки не удалены и не ослаблены ради зелёного результата.

## Acceptance criteria

| AC | Статус |
|---|---|
| AC-1 | BLOCKED: authoritative GET назначений не предусмотрен |
| AC-2 | PASS: отдельный экран ограничения, отсутствие replacement/скрытого users read |
| AC-3 | PASS в безопасном scope: capability gates, очистка защищённых проекций и server authorization; редактора назначения нет |
| AC-4 | BLOCKED для UI: replacement запрещён; store сохранение параметров покрыто отдельно |
| AC-5 | Покрыт существующими 412/428 client/editor/state/store tests; role UI не реализован |
| AC-6 | Server guard проверен; desktop state failure не показывает успех; role UI не реализован |
| AC-7 | BLOCKED для role UI; profile/state используют подтверждённые server receipts |

## Проверки и границы доказательств

Точные финальные счётчики — `validation-report.json`; TRX и build log сохранены рядом.
Целевой desktop gate включает 160 тестов; целевой PostgreSQL gate — 21.
Запуск всех трёх test projects выполняется последовательно, с изолированной PostgreSQL 16
на loopback и отдельными БД/ролями тестов. Runner: `work/verification/11_4/Run-Gate.ps1 -Full`.

Проверены доступные unit/VM/API-client, ASP.NET TestHost, authorization и PostgreSQL store tests.
Негативное покрытие включает validation, 401, 403, 404, 409, 412, 428, 503, отмену,
неопределённый результат, восстановление связи, выбор другого сотрудника и изменение capabilities.
Общий regression gate включает авторизацию, Projects, общий Employee Picker, роли,
сетевые ресурсы и прочие desktop sections; итог определяется фактическими TRX, не этим перечнем.

WPF evidence — автоматизированный запуск настоящих окон Windows с UIA/bindings/layout,
размерами 1120×900 и 800×600, fake API и PNG. Это не ручная приёмка установленного приложения
и не сквозной сценарий WPF → live HTTP API → PostgreSQL.

Остаются открытыми: OQ-ADMIN-ROLES-READ; согласование response UserRole; выдача сотруднику
учётных данных (отдельный процесс); department picker; ручная live Corporate acceptance,
Narrator/physical mixed-DPI. Каталог ролей и ресурсов наследует первую страницу до 200 записей;
он не является полным списком назначений. Физическое отключение сети и live race двух
администраторов не выполнялись; conflict/selection/idempotency проверены автоматическими сценариями.

Ветка может рассматриваться для PR безопасных изменений с явно открытым OQ. Нельзя оформлять
её как полностью завершённую задачу №11 или готовое управление назначениями ролей.

Первый полный desktop run: 1434 PASS / 2 FAIL (InboxZeroUiTests: Window.Activate, строка 85;
TaskContextActionsUiTests: Window.Activate, строка 62). Оба проходят отдельными запусками.
Контрольный полный прогон исходного `1f42e49`: 1415 PASS / 0 FAIL. Это не доказывает
унаследованность двух сбоев; первичный неуспешный receipt сохраняется. Результат повторного
полного текущего прогона отражён отдельно в validation-report.json и desktop-confirmation.trx.

Повторный обычный desktop run: 1435 PASS / 1 FAIL — PersonalShellViewModelTests,
Palette_UsesExistingPersonalSearchAndNavigation (ожидание результата за 300 ms); отдельно 1 PASS.
Первоначальные focus-тесты в этом повторе прошли.
Финальный desktop run с последовательными коллекциями: **1436 PASS / 0 FAIL / 0 SKIP**.
Итоговая Release solution build: 0 ошибок. Domain/real PostgreSQL: **875 PASS**;
API/TestHost: **631 PASS**. Все 2942 теста трёх проектов прошли в указанных конфигурациях;
первые нестабильные обычные desktop-прогоны не скрыты и не заменены в evidence.

Для воспроизведения Windows UI gate добавлен `desktop-serial.runsettings`:
ParallelizeTestCollections=false и MaxParallelThreads=1. Это настройки запуска всего набора,
а не отключение тестов или ослабление assertions. Параллельные коллекции разделяют Windows
desktop, где несколько тестов вызывают Activate/keyboard focus. Конфигурация проверена по
[официальному формату xUnit VSTest](https://github.com/xunit/xunit.net/blob/main/site/docs/config-runsettings.md).
Обычный параллельный режим остаётся нестабильным; его устойчивость этим этапом не объявлена.
