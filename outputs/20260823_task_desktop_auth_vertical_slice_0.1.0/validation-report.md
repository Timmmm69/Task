# Validation report

## Статус

- Версия: `0.1.0`.
- Scope: только Инкремент A — client/session foundation.
- Implementation commit: `4a5f72355f45b84f4df70496e3db092ba98bc9eb`.
- Публикация: **BLOCKED**, commit не отправлен в `origin/main`.
- Gate A: **PASS**.
- Полный vertical slice / E2E: **NOT COMPLETE**.

## Изменённые файлы

Production:

- `work/production/src/Task.Desktop/Security/DesktopServerConnection.cs`
- `work/production/src/Task.Desktop/Security/DesktopAuthApiClient.cs`
- `work/production/src/Task.Desktop/Security/SessionService.cs`

Tests:

- `work/production/tests/Task.Desktop.Tests/Security/DesktopServerConnectionTests.cs`
- `work/production/tests/Task.Desktop.Tests/Security/DesktopAuthApiClientTests.cs`
- `work/production/tests/Task.Desktop.Tests/Security/SessionServiceTests.cs`

## Реализованные сценарии

- Валидация абсолютного HTTPS URL; user-info, query и fragment запрещены.
- Нормализация адреса и атомарное хранение в `%LocalAppData%\Task`.
- Повреждённая настройка не падает, а изолируется как corrupt-файл.
- Probe последовательно проверяет `/health/live` и `/health/ready` и различает
  invalid URL, TLS failure, недоступность, not-ready и unexpected response.
- Смена сервера очищает persisted refresh token, in-memory access token и полное
  состояние `SessionService`; refresh не может вернуть старые credentials после очистки.
- `POST /api/v1/auth/change-password` использует Bearer token и типизированные outcomes.
- Добавлены `VALIDATION_FAILED`, `SESSION_REVOKED` и `AUTHENTICATION_REQUIRED`.
- Login/refresh/restore подтверждают `/auth/session` до ready-состояния.
- Неизвестный или malformed session response блокирует переход в main shell.
- Restore различает ready, must-change-password, retryable failure и terminal sign-out.
- Terminal refresh очищает vault и доставляет UI причину выхода вне внутреннего lock.
- Retryable refresh сохраняет vault.
- Password change снимает `mustChangePassword` только после повторного server confirmation.
- Logout во время refresh сериализован; поздняя ротация не восстанавливает vault.
- Dispose идемпотентен; фоновые refresh exceptions наблюдаются и не уходят в dispatcher.

## Фактические проверки

1. Baseline до изменений:
   `dotnet test tests/Task.Desktop.Tests/Task.Desktop.Tests.csproj -c Release --no-restore`
   — PASS, 81/81.
2. Финальный Gate A:
   `dotnet test tests/Task.Desktop.Tests/Task.Desktop.Tests.csproj -c Release --no-restore`
   — PASS, 119/119.
3. Server auth regression:
   `dotnet test tests/Task.ServiceHosts.Tests/Task.ServiceHosts.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~Auth"`
   — PASS, 69/69.
4. Release build:
   `dotnet build Task.sln -c Release --no-restore`
   — PASS, 0 errors, 0 warnings.
5. Полный solution test:
   `dotnet test Task.sln -c Release --no-restore`
   — BLOCKED нестабильными существующими тестами
   `ExpiredSessionMaintenanceWorkerTests` вне разрешённого auth scope. Наблюдались
   лишние timer ticks в `EmptyPass_LogsDebug_AndDoesNotPurgeAnything`,
   `DatabaseUnavailable_LogsWarning_AndNextPassRetries` и
   `PurgeRunsInBatches_UntilBatchComesBackNonFull_WithRetentionCutoff`.
   При этом каждый прогон сохранял PASS для desktop (119) и core (718), а отдельный
   повтор первого упавшего maintenance-теста прошёл 1/1. Код этих тестов не менялся.
6. Secret/prototype scan:
   `rg -n -i "task2026|password.*=|accessToken.*=|refreshToken.*=|deviceKey.*=" work/production/src/Task.Desktop`
   — совпадения вручную проверены: только имена DTO, полей, параметров и присваивания;
   literal credentials и prototype password отсутствуют.
7. `git diff --check` — PASS.
8. Изменённые C# файлы отформатированы `dotnet format` с `--include`.

## Manual smoke и accessibility

- Реальный API/PostgreSQL E2E: **не выполнялся**.
- Новый профиль, TLS endpoint, offline, login, mustChangePassword, restore, logout,
  server-side revocation: **не выполнялись вручную**; это требует Инкрементов B/C.
- Keyboard-only, UI Automation, 125/150/200% DPI и длинные русские сообщения:
  **не применимо к Инкременту A и не проверено**, поскольку WPF UI входит в Инкремент C.

## Известные ограничения и следующий шаг

- Workflow/ViewModels и WPF composition отсутствуют; main shell ещё не подключён к foundation.
- Полный solution gate остаётся publication blocker из-за timer-test flakiness вне scope.
- Следующий production-шаг: стабилизировать/подтвердить общий gate отдельным разрешённым
  изменением, опубликовать Инкремент A и затем реализовать Инкремент B.
