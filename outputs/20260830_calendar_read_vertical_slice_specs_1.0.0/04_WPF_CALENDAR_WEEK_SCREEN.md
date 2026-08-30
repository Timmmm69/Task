# ПРОМТ ДЛЯ НОВОГО ЧАТА — 04. Production WPF экран календарной недели

Ты работаешь в `C:\Users\novik\Таск`. Реализуй production read-only экран раздела «Календарь» на существующем WPF shell. Это четвёртое из пяти последовательных заданий; начинай только после появления typed calendar client из задания 03 в актуальном `origin/main`.

## Обязательное начало и design audit

Прочитай `AGENTS.md` и `work/delegation/README.md`; fetch, проверь чистый synchronized `main`, fast-forward. По коду/tests убедись, что три server endpoints и `IDesktopCalendarApiClient` доступны.

До изменений проведи узкий audit `MainWindow.xaml`, `MainWindowViewModel`, `TasksViewModel`, navigation/lifecycle wiring, resource dictionaries и Desktop presentation tests. Полностью изучи визуальные references:

- `sources/stage_3_4/Organizer_Stage3_Final_Baseline_3.4.zip` и `work/stage_4_1_2/input_stage3/Stage_3_Contract_Delta_3.5.md` в части Calendar;
- `work/stage_5_prototype/implementation-direction2-calendar-week.png` — основная visual truth;
- `work/stage_5_prototype/implementation-direction2-calendar-readonly.png`;
- `work/stage_5_prototype/implementation-direction2-calendar-overlap.png`;
- `work/stage_5_prototype/design-qa-stage5-surfaces.md` и `design-qa-stage5-edge-states.md`;
- `outputs/20260825_task_wpf_visual_foundation_0.1.0/**` — принятые WPF tokens/styles/QA.

Не создавай второй design system, stock WPF prototype или browser UI. Переиспользуй Theme, spacing, typography, colors, buttons, states, navigation, icons и established shell composition.

## Пользовательский сценарий и layout

При выборе navigation route `calendar` активируется `CalendarViewModel` и загружается текущая локальная неделя понедельник–воскресенье. Запрос использует UTC boundaries, вычисленные из выбранной недели и системной timezone, и передаёт timezone id серверу. При уходе со страницы запрос отменяется; при возврате выполняется актуальная загрузка. Повторное быстрое переключение недели не позволяет stale response перезаписать новую.

Экран содержит:

- заголовок «Календарь», видимый диапазон недели и кнопки «Предыдущая», «Сегодня», «Следующая», «Обновить»;
- семь day columns с локальной датой и понятным состоянием today;
- all-day lane отдельно от временной сетки;
- временную шкалу и размещение timed events/tasks по start/end; point task отображается как marker, а не fake-duration block;
- различимые типы Task и CalendarEvent, status и task priority без зависимости только от цвета;
- визуально честные overlap indicators на основе server conflicts, с текстовым accessible explanation;
- selection одного item и inspector. Для CalendarEvent inspector загружает `GetEventAsync` и показывает title, date/time/timezone, status, description, project id при наличии и attendees в read-only виде. Для Task показывает доступные schedule DTO fields и ясную подпись «Задача»; не вызывает event details endpoint;
- empty state, initial loading skeleton/progress, refresh overlay без исчезновения подтверждённых данных, error state с retry, forbidden/read-only explanation.

Этот этап не добавляет create/edit, drag/resize, attendee response, generic context actions или локальную mutation queue. Controls записи отсутствуют, а не выглядят активными. `nextCursor != null` считается unsupported protocol state: не показывай неполный календарь как полный.

## Layout и адаптация

Основной вид — недельный, принят Direction 2. Не нужно реализовывать month/day modes. На широком окне сетка + inspector; при 1000×640 и 800×480 inspector переносится/сворачивается так, чтобы navigation, week controls, day headers и выбранное содержимое оставались доступны без document-level horizontal clipping. Допустим внутренний горизонтальный scroll только самой time grid при минимальной ширине, с сохранёнными day headers; он не должен ломать keyboard focus.

Используй WPF layout primitives и data templates; не рисуй текст/иконки вручную на Canvas. Canvas допустим только для вычисляемого размещения timeline blocks, если он остаётся automation-accessible через параллельное item tree. Геометрия должна учитывать minimum visible height, clipping внутри day column и overlap lanes; не выдумывать время для all-day/point items.

## ViewModel и состояние

Создай `CalendarViewModel`, зависящий только от `IDesktopCalendarApiClient`, clock/timezone abstraction только если проект уже имеет pattern либо она нужна для deterministic tests. Нужны Activate/Deactivate/Dispose, cancellation generation, single-flight refresh, navigation commands, selected item/details state, busy/error/empty properties. UI thread updates выполняются корректно. Confirmed data сохраняется при transient refresh error с честным stale/error banner. Session end или loss `Calendar.Read` очищает защищённые Calendar data до показа auth flow.

`MainWindowViewModel` должен владеть Tasks и Calendar lifecycles без двойной активности: выбран `tasks` — активны Tasks, выбран `calendar` — Calendar, остальные — оба deactivated. Composition root создаёт единственный client/ViewModel instance на session.

## Accessibility

- logical Tab order, keyboard week navigation и item selection;
- видимый focus на всех interactive controls;
- `AutomationProperties.Name`, HelpText/ItemStatus для navigation, day columns, schedule items, overlap, inspector, loading/error;
- screen reader получает тип, title, локальный интервал/all-day, status и conflict severity;
- loading/error/selection change объявляются через существующий live-region pattern;
- target size/contrast/state semantics из visual foundation; цвет не единственный носитель информации.

## Разрешённые изменения

- новый `work/production/src/Task.Desktop/ViewModels/CalendarViewModel.cs`;
- максимум два Calendar-specific WPF view/control files и один узкий converter/layout helper;
- `MainWindow.xaml`, минимальный `MainWindow.xaml.cs`, `MainWindowViewModel.cs`, `App.xaml.cs` для composition/lifecycle;
- существующие resource dictionaries только для действительно reusable missing styles;
- новые `Task.Desktop.Tests/Calendar/CalendarViewModelTests.cs` и calendar presentation tests;
- узкие lifecycle/MainWindow/visual tests.

Не менять server/domain/migrations, typed client contract без доказанного blocker, Task behavior, sources, outputs, dependencies или unrelated screens. Ориентир — до 12 production/test files и 700 changed lines. Если полноценный week layout не помещается, раздели внутреннюю реализацию на классы в том же задании, но не вырезай acceptance states.

## Обязательные tests и visual verification

Покрой: initial activate; week UTC boundary at timezone/DST; prev/today/next; cancellation/deactivation; stale suppression; refresh preserving confirmed data; empty/error/retry/forbidden; mixed task/event/all-day/point/timed grouping; conflict mapping; event detail selection; task selection without event request; malformed cursor state; session/capability revocation clearing; dispose; exactly one active section; keyboard/automation metadata; required resource usage; compact widths.

Собери production WPF и сделай screenshots минимум: 1487×1058, 1200×900, 1000×640, 800×480; states loaded mixed week, all-day, overlap, empty, loading, retryable error, forbidden, event inspector. Сравни с Direction 2 references и исправь P0/P1 visual issues.

## Gate

```powershell
cd work/production
dotnet format Task.sln --no-restore
dotnet test tests/Task.Desktop.Tests/Task.Desktop.Tests.csproj -c Release --filter "FullyQualifiedName~Calendar|FullyQualifiedName~MainWindow"
dotnet test tests/Task.Desktop.Tests/Task.Desktop.Tests.csproj -c Release
dotnet test Task.sln -c Release
dotnet build Task.sln -c Release --no-restore
powershell -ExecutionPolicy Bypass -File verification/Test-DesktopShell.ps1
git diff --check
```

## Stop и публикация

Не push при server/client workaround, отсутствующей visual truth, неработающей keyboard navigation, P0/P1 visual issue, stale/session leakage, failing tests, dirty tree, конфликте или scope overflow. Не объявляй реальный E2E — это задание 05.

Commit: `feat(desktop): add calendar week screen`. Fetch, rebase `origin/main`, повтори gate и push `HEAD:main`. В финале перечисли UX, screenshots/checks, files, SHA и push.
