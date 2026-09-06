# Validation report

## Статус

- Версия: `0.1.0`.
- Scope: `PRODUCTION-AUTH-E2E-02` — continuity текущей desktop-сессии после смены пароля.
- Implementation commit: `90603472bd90c48f82299f7f202e2a2b95462bab`.
- Automated gate: **PASS**.
- Реальный WPF + HTTPS API + PostgreSQL 16 E2E: **PASS**.
- Secret scan: **PASS**.

## Причина дефекта

После смены пароля сервер увеличивал `iam.user_accounts.credential_version`, но
оставлял прежнюю версию у текущей `iam.sessions`. Desktop после ответа 204 сразу
вызывал `/api/v1/auth/session` со старым access token. Проверка версии корректно
возвращала `SESSION_EXPIRED`, хотя refresh token текущей сессии должен был
оставаться рабочим.

## Исправление

- `PasswordChangeService` использует единый `CommitPasswordChangeAsync` вместо
  независимых persistence-операций.
- PostgreSQL commit выполняется в одной транзакции с optimistic guard по старому
  hash, параметрам и credential version.
- Транзакция обновляет credential и `must_change_password`, архивирует прежний
  hash, переводит текущую активную сессию на новую credential version и отзывает
  остальные активные сессии с refresh tokens.
- Чужая, отозванная, истёкшая или уже изменившая версию текущая сессия приводит
  к rollback всей операции.
- Desktop после 204 выполняет refresh внутри уже удерживаемого gate, затем
  `/auth/session`; main shell разрешается только после подтверждённого
  `mustChangePassword=false`.
- Terminal refresh очищает vault и возвращает login. Retryable refresh сохраняет
  vault, выставляет readiness `Unavailable` и не открывает main shell.
- Публичные API routes и JSON DTO, миграции, схема БД, JWT lifetime, hashing и TLS
  validation не изменялись; новые зависимости не добавлялись.

## Изменённые файлы

Production:

- `work/production/src/Task.Application/Security/IAccountCredentialStore.cs`
- `work/production/src/Task.Application/Security/PasswordChangeService.cs`
- `work/production/src/Task.Desktop/Security/SessionService.cs`
- `work/production/src/Task.Infrastructure/Postgres/PostgresAccountCredentialStore.cs`

Tests:

- `work/production/tests/Task.Desktop.Tests/AuthWorkflowViewModelTests.cs`
- `work/production/tests/Task.Desktop.Tests/Security/SessionServiceTests.cs`
- `work/production/tests/Task.ServiceHosts.Tests/AuthSessionEndpointsTests.cs`
- `work/production/tests/Task.Tests/Postgres/PostgresAccountCredentialStoreTests.cs`
- `work/production/tests/Task.Tests/Security/PasswordChangeServiceTests.cs`
- `work/production/tests/Task.Tests/Postgres/PostgresDeviceRegistrationStoreTests.cs`
- `work/production/tests/Task.Tests/Postgres/PostgresSessionListTests.cs`

Последние два файла исправляют выявленные реальным PostgreSQL gate существующие
дефекты тестовых данных и чтения `timestamptz`; изменение scope явно разрешено
пользователем.

## Автоматические проверки

1. `dotnet format Task.sln --no-restore --include <все изменённые C# файлы>` — PASS.
2. `dotnet test tests/Task.Desktop.Tests/Task.Desktop.Tests.csproj -c Release` — PASS, 152/152.
3. `dotnet test tests/Task.ServiceHosts.Tests/Task.ServiceHosts.Tests.csproj -c Release --filter "FullyQualifiedName~Auth"` — PASS, 69/69.
4. `dotnet test Task.sln -c Release` с включённым реальным PostgreSQL integration gate — PASS, 1006/1006: Desktop 152, ServiceHosts 135, core/integration 719.
5. Точечный password-change/PostgreSQL gate — PASS, 20/20.
6. `dotnet build Task.sln -c Release --no-restore` — PASS, 0 errors, 0 warnings.
7. `git diff --check` — PASS.
8. Desktop secret scan по `task2026`, password/accessToken/refreshToken/deviceKey assignments — PASS. Совпадения проверены вручную: только UI-тексты, имена полей, свойств, параметров и присваивания; secret literals отсутствуют.

## Реальный E2E

Окружение размещалось только в `work/tmp/desktop-auth-e2e/`: отдельный PostgreSQL
16, новая временная БД, synthetic credentials и локальный HTTPS API endpoint.
Секретные значения в логи и пакет не включены.

- Миграции: ожидаемая и фактическая версия 4.
- `/health/live`: Alive; `/health/ready`: Ready, persistence Ready.
- TLS: использован доверенный действующий localhost development certificate.
  Доверие уже присутствовало; `dotnet dev-certs https --trust` не запускался.
- First server setup сохранил HTTPS endpoint; login с
  `mustChangePassword=true` открыл обязательный экран смены пароля.
- Финальное нажатие «Изменить пароль» выполнил пользователь согласно Computer Use policy.
- Наблюдаемый порядок API: change-password 204 без body → refresh 200 → session 200.
- Main shell открылся только после подтверждённого `mustChangePassword=false`.
- В БД credential version аккаунта и текущей сессии совпали; текущий refresh token
  остался активным, другая сессия и её refresh token были отозваны.
- Старый access token другой сессии получил 401 `SESSION_EXPIRED`.
- Restart/restore подтвердил текущую сессию и открыл main shell.
- После серверного revoke перезапуск вернул login, vault был очищен, server
  settings сохранились.
- Повторный login новым паролем открыл main shell без обязательной смены.
- Logout вернул login, пользователь выполнил финальное нажатие; локальный vault
  отсутствует, активных server-side сессий не осталось.
- API и Desktop после проверки остановлены. Исходный пользовательский desktop
  profile junction восстановлен.

## Accessibility, keyboard и размеры

- AutomationId проверены для server setup, login, password change, main shell и logout.
- Keyboard Tab navigation на login проверена визуально; focus переходил к полю пароля.
- Auth window при попытке уменьшения сохранил минимальный размер и доступность
  login surface; декларативный минимум — 900x620.
- Main window имеет декларативный минимум 800x480, но отдельное физическое
  перетаскивание main window до минимума не выполнялось.
- Системные DPI 125/150/200% и увеличение текста: **не проверены**, поскольку
  системный DPI по условию не изменялся.

## Непроверенное и ограничения

- Не выполнена отдельная ручная DPI-матрица 125/150/200%.
- Не выполнено отдельное физическое уменьшение main window до 800x480; значение
  проверено по XAML, а enforcement вручную наблюдался на auth window.
- E2E использовал synthetic локальное окружение, а не production credentials или
  production database.
