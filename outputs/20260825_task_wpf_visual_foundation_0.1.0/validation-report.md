# Validation report

## Статус

- Версия: `0.1.0`.
- Scope: `PRODUCTION-WPF-VISUAL-FOUNDATION-01`.
- Implementation commit: `0e5b5b7c98b9cc1487a2d62c41bc78ab1ee7a506`.
- Automated desktop/targeted/full gate: **PASS**.
- Release build: **PASS**, 0 errors, 0 warnings.
- Release desktop resource/auth smoke: **PASS**.
- Реальный PostgreSQL 16 + HTTPS API + production WPF E2E: **PASS**.
- UI Automation и keyboard smoke: **PASS**.
- Manifest/SHA-256: **PASS** после запуска `Verify-Manifest.ps1`.

## Реализация

- Добавлены раздельные resource dictionaries для canonical colors, typography,
  spacing/geometry, Fluent icon geometries, buttons, navigation, data rows,
  semantic states и общей theme composition.
- Shell сохраняет нативный Windows title bar и реализует Direction 2 navigation,
  header, command area, factual connection state, content и status footer.
- Navigation использует 212 px expanded / 178 px compact layout, строку 52 px,
  leading brand rail, soft selected fill, hover и видимый 2 px keyboard focus.
- Экран «Задачи» показывает только реальные поля DTO: title, status, priority,
  deadline и updated time. Project/assignee/watchers не выдумываются.
- Status и priority представлены текстом, official Fluent geometry и
  presentation-only semantic tone, поэтому смысл не зависит только от цвета.
- Inspector адаптируется: side-by-side на широком окне и под списком на узком;
  Enter раскрывает его и переводит focus в `TaskDetailsArea`.
- Loading, empty, network error, read-only, selected и unavailable states имеют
  разные безопасные presentation patterns без exception details.
- Disabled «Новая задача» остаётся видимой secondary-командой с tooltip и UIA
  HelpText о следующем write-инкременте.
- `TasksViewModel` получил только presentation projection: icon keys, semantic
  tones и фактическое время успешного refresh. API/pagination/cancellation/
  session semantics не менялись.
- Ограниченный набор Microsoft Fluent UI System Icons зафиксирован как WPF
  Geometry; source, version `@fluentui/svg-icons` 1.1.334 и MIT attribution
  приведены в `THIRD_PARTY_NOTICES.md`.

## Изменённые production/test файлы

- `work/production/src/Task.Desktop/App.xaml`
- `work/production/src/Task.Desktop/MainWindow.xaml`
- `work/production/src/Task.Desktop/MainWindow.xaml.cs`
- `work/production/src/Task.Desktop/Converters/VisualFoundationConverters.cs`
- `work/production/src/Task.Desktop/Resources/Tokens.Colors.xaml`
- `work/production/src/Task.Desktop/Resources/Tokens.Typography.xaml`
- `work/production/src/Task.Desktop/Resources/Tokens.Spacing.xaml`
- `work/production/src/Task.Desktop/Resources/Icons.xaml`
- `work/production/src/Task.Desktop/Resources/Controls.Buttons.xaml`
- `work/production/src/Task.Desktop/Resources/Controls.Navigation.xaml`
- `work/production/src/Task.Desktop/Resources/Controls.Data.xaml`
- `work/production/src/Task.Desktop/Resources/Controls.States.xaml`
- `work/production/src/Task.Desktop/Resources/Theme.xaml`
- `work/production/src/Task.Desktop/THIRD_PARTY_NOTICES.md`
- `work/production/src/Task.Desktop/ViewModels/MainWindowViewModel.cs`
- `work/production/src/Task.Desktop/ViewModels/NavigationSection.cs`
- `work/production/src/Task.Desktop/ViewModels/TasksViewModel.cs`
- `work/production/tests/Task.Desktop.Tests/VisualFoundationTests.cs`
- `work/production/tests/Task.Desktop.Tests/MainWindowViewModelTests.cs`
- `work/production/tests/Task.Desktop.Tests/Tasks/TasksViewModelTests.cs`

`sources/**`, API, Application, Domain, Infrastructure, database/migrations,
OpenAPI, auth/session implementation и React baseline не изменялись.

## Автоматические проверки

1. `dotnet format Task.sln --no-restore --include <изменённые C# файлы>` — PASS.
2. `dotnet test tests/Task.Desktop.Tests/Task.Desktop.Tests.csproj -c Release` —
   PASS, 194/194.
3. Targeted task/shell/visual filter из задания — PASS, 194/194.
4. `dotnet test Task.sln -c Release` — PASS, 1074/1074:
   Task.Tests 724, ServiceHosts 156, Desktop 194.
5. `dotnet build Task.sln -c Release --no-restore` — PASS, 0 errors,
   0 warnings.
6. `git diff --check` — PASS.
7. Secret scan staged production/test diff — PASS, совпадений нет.
8. Presentation tests проверяют resource graph/missing keys, canonical colors,
   dimensions, Fluent icon mappings, semantic status/priority projection,
   non-color selected/focus indicators, disabled action reason, navigation UIA,
   responsive converters, High Contrast system brushes и старые AutomationId.

## Release и реальный E2E

Release desktop отдельно запускался без сохранённой сессии: окно
`Task — вход` открылось без XAML/resource exception, UIA tree сохранил стабильные
auth AutomationId. Auth automation не выполнялась.

Полный synthetic E2E использовал изолированный PostgreSQL 16.14 на loopback,
новую БД, schema version 4, доверенный localhost HTTPS development certificate,
новую организацию/администратора и существующие desktop session/vault clients.
Секреты и token values в пакет не включены.

- `/health/live` вернул `Alive`.
- Создано 55 active tasks целевой организации.
- Production desktop восстановил DPAPI session и открыл main shell.
- Раздел «Задачи» показал первую страницу: 50 строк и secondary load-more.
- «Загрузить ещё» довела count до 55 и исчезла после последней страницы.
- Выбор второй строки и Enter раскрыли inspector; UIA focus перешёл в
  `TaskDetailsArea`.
- Refresh вернул первую страницу, continuation и реальное время обновления.
- После server-side revoke API отклонил tasks/refresh как revoked; общий workflow
  закрыл main shell и открыл login с сообщением об истёкшей сессии.
- Тестовые WPF/API/PostgreSQL процессы остановлены; credential vault был очищен
  самим terminal-session workflow. В `%LOCALAPPDATA%\Task` остался только
  несекретный адрес loopback-сервера, поскольку политика среды заблокировала его
  удаление вне workspace.

## Viewport, DPI и accessibility

| Проверка | Результат |
|---|---|
| 1487×1058 | PASS, wide list + side inspector |
| 1280×720 | PASS |
| 1200×900 | PASS, включая real E2E |
| 1000×640 | PASS, adaptive stacked inspector |
| 800×480 | PASS, scroll containers сохраняют navigation/content/footer |
| Фактический системный DPI 144 / 150% | PASS для всей screenshot matrix |
| High Contrast resource/static smoke | PASS |
| Реальный OS High Contrast | Не переключался |
| Отдельный 200% DPI session | Не выполнен: безопасной изолированной display session нет |

Keyboard navigation, arrows/ListBox semantics, Enter-to-details, 2 px focus,
stable AutomationId, Automation Name/HelpText, disabled/read-only semantics и
LiveSetting проверены presentation tests и UIA tree. Подробности находятся в
`evidence/accessibility/ui-automation-summary.md`.

## Визуальные отклонения и findings

- Нативные WPF font rasterization, scrollbar и Windows title bar отличаются от
  web reference намеренно; geometry и hierarchy следуют WPF baseline.
- Header не содержит фиктивные search/notifications/profile controls.
- Inspector на компактной ширине складывается под список вместо обрезания.
- Open Critical findings: 0. Open High findings: 0.
- Открытые verification gaps: фактический OS High Contrast и отдельный 200% DPI
  прогон. Оба отмечены как непроверенные, а не как PASS.

## Следующий increment

Foundation готов для отдельного Task write vertical slice: create, PATCH и
status transitions с серверными контрактами, concurrency, audit и outbox.
Эта работа их не начинает и весь desktop UI завершённым не объявляет.
