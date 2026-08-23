# Validation report

## Статус

- Версия: `0.1.0`.
- Scope: Инкременты A, B и C — client/session foundation, workflow/ViewModels и WPF composition.
- Implementation commit A: `4a5f72355f45b84f4df70496e3db092ba98bc9eb`.
- Implementation commit B: `0502c79f231446a96e2424455435ed2ee2ca60ba`.
- Implementation commit C: `bed8d1298cc20501ab383a41e484bfcac52a52c0`.
- Gate A: **PASS**.
- Gate B: **PASS**.
- Gate C (code/build/limited UI smoke): **PASS**.
- Полный vertical slice / реальный API+PostgreSQL E2E: **NOT VERIFIED**.

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

Инкремент C, production:

- `work/production/src/Task.Desktop/App.xaml`
- `work/production/src/Task.Desktop/App.xaml.cs`
- `work/production/src/Task.Desktop/AuthWindow.xaml`
- `work/production/src/Task.Desktop/AuthWindow.xaml.cs`
- `work/production/src/Task.Desktop/MainWindow.xaml`
- `work/production/src/Task.Desktop/MainWindow.xaml.cs`
- `work/production/src/Task.Desktop/ViewModels/MainWindowViewModel.cs`

Инкремент C, tests:

- `work/production/tests/Task.Desktop.Tests/MainWindowViewModelTests.cs`

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

WPF composition C:

- отдельное auth/startup-окно показывается до подтверждённого `Ready`;
- server setup, login, mandatory change-password и recovery подключены к ViewModels B;
- пароли читаются только из `PasswordBox`, передаются параметром одной команды и
  очищаются по сигналу ViewModel после каждой завершившейся попытки;
- `App` владеет workflow, `HttpClient` и окнами; ресурсы освобождаются при выходе;
- main shell создаётся только после `Ready`, показывает подтверждённый server/session status;
- logout защищён от double-submit, всегда возвращает auth window;
- terminal sign-out закрывает main shell и показывает login с безопасной причиной;
- интерактивные элементы имеют стабильные AutomationId/Name, live regions,
  видимый keyboard focus и логичный Tab order; длинный контент помещён в ScrollViewer.

## Фактические проверки

1. Baseline перед Инкрементом B:
   `dotnet test tests/Task.Desktop.Tests/Task.Desktop.Tests.csproj -c Release --no-restore`
   — PASS, 119/119.
2. Финальный desktop Gate C:
   `dotnet test tests/Task.Desktop.Tests/Task.Desktop.Tests.csproj -c Release --no-restore`
   — PASS, 150/150; добавлены 2 shell composition tests поверх Gate B.
3. Server auth regression:
   `dotnet test tests/Task.ServiceHosts.Tests/Task.ServiceHosts.Tests.csproj -c Release --filter "FullyQualifiedName~Auth"`
   — PASS, 69/69.
4. Полный solution test:
   `dotnet test Task.sln -c Release --no-restore`
   — PASS, 1003/1003: Desktop 150/150, ServiceHosts 135/135, core 718/718.
5. Release build:
   `dotnet build Task.sln -c Release --no-restore`
   — PASS, 0 errors, 0 warnings.
6. Secret/prototype scan:
   `rg -n -i "task2026|password.*=|accessToken.*=|refreshToken.*=|deviceKey.*=" work/production/src/Task.Desktop`
   — совпадения вручную проверены: только имена типов, свойств, параметров и
   присваивания; literal credentials и prototype password отсутствуют.
7. `git diff --check` — PASS.
8. Изменённые C# файлы отформатированы `dotnet format Task.sln --no-restore --include ...`.

Известное предупреждение отдельного desktop test запуска: существующий
`xUnit1031` в `DesktopCredentialVaultTests.cs`; Инкремент C этот файл не изменяет.
Финальный Release build проходит с 0 warnings.

## Manual smoke и accessibility

- Реальный API/PostgreSQL E2E: **не выполнялся**; окружение и production credentials
  не предоставлены.
- Ограниченный WPF smoke: **PASS**. Release executable запущен на профиле без
  `%LocalAppData%\Task`; показан экран «Первое подключение», initial focus установлен
  на server address, UI Automation tree содержит ожидаемые Name/AutomationId,
  keyboard Enter запускает probe, `http://example.test` даёт безопасное сообщение
  «Используйте защищённый адрес HTTPS», Continue остаётся disabled.
- Новый профиль и invalid HTTP проверены вручную. Неверный TLS endpoint, offline,
  неверный пароль, rate/temporary lock, успешный login, mustChangePassword,
  restart/restore, logout и server-side revoke покрыты automated tests, но вручную
  через реальный API не выполнялись.
- 125/150/200% DPI и системное увеличение текста: **не проверены**, поскольку
  изменение системных параметров в текущем окружении не выполнялось.
- Минимальный поддержанный размер задаётся `900x620`; вертикальный overflow auth
  panes закрыт ScrollViewer, но ручная матрица размера/увеличения текста не выполнена.

## Известные ограничения и следующий шаг

- WPF composition и resource lifetime реализованы; предметная синхронизация и
  предметные модули остаются вне scope, main shell честно показывает read-only notice.
- Полный acceptance и E2E PASS заявлять нельзя до реального запуска с API/PostgreSQL.
- Следующий production-шаг: провести E2E на тестовом API/PostgreSQL со сценариями
  login/change-password/restore/logout/revoke и отдельную DPI/accessibility-матрицу.
