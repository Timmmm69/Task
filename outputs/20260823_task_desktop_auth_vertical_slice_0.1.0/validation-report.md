# Validation report

## Статус

- Версия: `0.1.0`.
- Scope: Инкременты A и B — client/session foundation и workflow/ViewModels.
- Implementation commit A: `4a5f72355f45b84f4df70496e3db092ba98bc9eb`.
- Implementation commit B: `0502c79f231446a96e2424455435ed2ee2ca60ba`.
- Gate A: **PASS**.
- Gate B: **PASS**.
- Полный vertical slice / реальный E2E: **NOT COMPLETE**.

## Изменённые файлы

Инкремент A, production:

- `work/production/src/Task.Desktop/Security/DesktopServerConnection.cs`
- `work/production/src/Task.Desktop/Security/DesktopAuthApiClient.cs`
- `work/production/src/Task.Desktop/Security/SessionService.cs`

Инкремент A, tests:

- `work/production/tests/Task.Desktop.Tests/Security/DesktopServerConnectionTests.cs`
- `work/production/tests/Task.Desktop.Tests/Security/DesktopAuthApiClientTests.cs`
- `work/production/tests/Task.Desktop.Tests/Security/SessionServiceTests.cs`
- `work/production/tests/Task.ServiceHosts.Tests/ExpiredSessionMaintenanceWorkerTests.cs`

Инкремент B, production:

- `work/production/src/Task.Desktop/Security/SessionService.cs`
- `work/production/src/Task.Desktop/ViewModels/AsyncCommand.cs`
- `work/production/src/Task.Desktop/ViewModels/AuthWorkflowViewModel.cs`
- `work/production/src/Task.Desktop/ViewModels/AuthenticationViewModels.cs`

Инкремент B, tests:

- `work/production/tests/Task.Desktop.Tests/AuthWorkflowViewModelTests.cs`

## Реализованные сценарии

Foundation A:

- абсолютный HTTPS URL, запрет user-info/query/fragment и нормализация;
- атомарное хранение server URL и безопасная изоляция повреждённого файла;
- последовательный `/health/live` и `/health/ready` probe с типизированными отказами;
- DPAPI vault для refresh token/device key и access token только в памяти;
- типизированные login, refresh, session, change-password и logout вызовы;
- подтверждение `/auth/session` после login/refresh/restore/change-password;
- single-flight refresh, terminal/retryable классификация и best-effort logout;
- защита от late refresh после logout или смены сервера.

Workflow/ViewModels B:

- startup state machine: server setup → login/restore → password change/recovery → ready;
- main shell разрешён только при `AuthWorkflowState.Ready` после server-confirmed metadata;
- первый запуск и безопасная смена сервера с очисткой старого vault;
- login без хранения пароля во ViewModel, локальная проверка пустых полей и сигнал
  очистки PasswordBox после попытки;
- безопасные русские сообщения для invalid credentials, blocked/temporary lock,
  rate limit, network, malformed и неизвестных security failures;
- обязательная смена пароля с локальной политикой, совпадением подтверждения и
  повторным `/auth/session` confirmation;
- offline/session-metadata recovery без автоматического уничтожения vault;
- terminal refresh/sign-out закрывает ready-state и возвращает login с причиной;
- logout всегда приводит к локальному выходу; recovery допускает retry или logout;
- cancellable `AsyncCommand` блокирует double-submit и содержит исключения на
  WPF `async void` boundary;
- отмена in-flight login при закрытии не приводит к гонке dispose/semaphore.

## Фактические проверки

1. Baseline перед Инкрементом B:
   `dotnet test tests/Task.Desktop.Tests/Task.Desktop.Tests.csproj -c Release --no-restore`
   — PASS, 119/119.
2. Финальный Gate B:
   `dotnet test tests/Task.Desktop.Tests/Task.Desktop.Tests.csproj -c Release --no-restore`
   — PASS, 148/148; добавлено 29 workflow/component tests.
3. Server auth regression:
   `dotnet test tests/Task.ServiceHosts.Tests/Task.ServiceHosts.Tests.csproj -c Release --filter "FullyQualifiedName~Auth"`
   — PASS, 69/69.
4. Полный solution test:
   `dotnet test Task.sln -c Release --no-restore`
   — PASS, 1001/1001: Desktop 148/148, ServiceHosts 135/135, core 718/718.
5. Release build:
   `dotnet build Task.sln -c Release --no-restore`
   — PASS, 0 errors, 0 warnings.
6. Secret/prototype scan:
   `rg -n -i "task2026|password.*=|accessToken.*=|refreshToken.*=|deviceKey.*=" work/production/src/Task.Desktop`
   — совпадения вручную проверены: только имена типов, свойств, параметров и
   присваивания; literal credentials и prototype password отсутствуют.
7. `git diff --check` — PASS.
8. Изменённые C# файлы отформатированы `dotnet format ... whitespace --include`.

Известное предупреждение отдельного desktop test запуска: существующий
`xUnit1031` в `DesktopCredentialVaultTests.cs`; Инкремент B этот файл не изменяет.
Финальный Release build проходит с 0 warnings.

## Manual smoke и accessibility

- Реальный API/PostgreSQL E2E: **не выполнялся**.
- Новый профиль, неверный TLS endpoint, offline, неверный пароль, rate/temporary
  lock, login, mustChangePassword, restart/restore, logout и server-side revoke:
  покрыты component/unit tests, но **не выполнялись вручную через WPF**.
- Keyboard-only, `AutomationProperties`, live regions, visible focus, Tab order,
  125/150/200% DPI, минимальный размер и системное увеличение текста:
  **не проверены**, потому что WPF composition относится к Инкременту C.

## Известные ограничения и следующий шаг

- `App` и WPF auth window ещё не подключены к workflow; текущий `MainWindow`
  остаётся прежним и не является доказательством E2E.
- `HttpClient` lifetime и показ/закрытие окон должны быть собраны в composition root C.
- Полный acceptance и E2E PASS заявлять нельзя до реального запуска с API/PostgreSQL.
- Следующий production-шаг: Инкремент C — WPF composition, UI Automation,
  keyboard/DPI smoke и фактический end-to-end запуск.
