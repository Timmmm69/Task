# Повторения и выбор даты — 1.0.0

## Scope

Shared WPF Calendar/DatePicker appearance; RecurrenceView section cards, existing form/list styles, all-day checkbox and accessible field names; TaskDayPicker sizing. No view models, API contracts or recurrence rules changed. Existing XAML bindings, commands and conditional fields are retained.

The shared DatePicker style intentionally also reaches CalendarView, TodayView and PersonalProjectsView. Native DatePickerTextBox is retained for parsing/watermarks and TaskDayPicker's read-only text contract. CalendarItem retains the 7×7 month grid, 3×4 year grid, named navigation parts and Year/Decade visibility triggers. Calendar cells distinguish selection, today, inactive dates, blackout dates, disabled and keyboard focus states. System brushes are used for high contrast.

## Verification on Linux, .NET SDK 10.0.402

- PASS: `dotnet build work/production/src/Task.Desktop/Task.Desktop.csproj -c Release -p:EnableWindowsTargeting=true -m:1 -nr:false` (0 warnings/errors).
- PASS: final `dotnet build work/production/tests/Task.Desktop.Tests/Task.Desktop.Tests.csproj -c Release -p:EnableWindowsTargeting=true -m:1 -nr:false --no-restore` (0 errors; 2 existing analyzer warnings in DesktopCredentialVaultTests and PersonalPlanningTests).
- PASS: XML parsing and resource-key resolution of modified XAML; comparison against baseline confirms all original RecurrenceView and TaskDayPicker binding expressions remain present.
- PASS: `git diff --check`.
- PASS: `dotnet format whitespace --folder --include work/production/tests/Task.Desktop.Tests/Calendar/CalendarAppearanceTests.cs --verify-no-changes`.
- Added STA runtime tests for native date picker parts, date clearing, populated days, month navigation and year/decade view switching. They compile.
- NOT RUN: runtime assertions. Attempted `dotnet test ... --filter FullyQualifiedName~CalendarAppearanceTests --no-build --no-restore`; testhost aborts because Microsoft.WindowsDesktop.App is unavailable on Linux.
- Initial multi-process build exited unsuccessfully without compiler diagnostics; serial build above passed. Project-mode formatter was blocked by sandbox named-pipe permissions; folder-mode formatting used instead.

## Windows acceptance still required

Run the production CI and CalendarAppearanceTests. Open recurrence forms in personal/corporate hosts at normal and compact window sizes and 100/150/200% DPI. Inspect all repeat frequencies and all three termination choices; check scroll access to bottom actions, series selection and read-only permissions. Check popup opening, day selection, previous/next month, year/decade selection, today/selected/blackout states, Escape, keyboard arrows/Tab, empty/invalid manual dates and high contrast. Check Today, CalendarView and personal project dates because the style is shared. Verify Today/Tomorrow shortcuts in TaskDayPicker. No runtime screenshots were generated; visual acceptance is pending.

No readiness increase or roadmap changes; no merge to main in this task.
