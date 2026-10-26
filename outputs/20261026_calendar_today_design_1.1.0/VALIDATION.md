# Calendar event forms — Today design, 1.1.0

Parent: `3ef60359be732898f520da65942730982a55d947` (PR #16).
Reference: `work/production/src/Task.Desktop/Views/TodayView.xaml`, blob `48d66567474df39e9c2c5454d3302245cc1d2f57`.

The initial PR unified widgets. This follow-up changes the visual composition using Today as the concrete source reference.

| Today reference | Calendar implementation |
| --- | --- |
| TodayPlannerRegion header, calendar icon and compact typography | Dedicated event header with calendar badge, title and secondary metadata |
| TodayThreeZoneGrid, separated working regions | Event details/timing and attendees in two flat white regions; attendees move below details when dialog width is below 900 DIP |
| TodayQueueTemplate, bottom-only hairline row dividers | Flat attendee rows with name hierarchy and labelled role/response selectors |
| Surface.Base, Border.Default, Brand.Soft, 20-DIP panel padding | Same tokens and padding rhythm in the event editor; replaced stacked tinted cards |
| Dedicated task action area | Footer outside the body ScrollViewer, separated by a hairline; save/cancel and saving feedback stay in place |

Rounded text fields and native ComboBox popups are scoped to this editor, including the embedded employee search. Native DatePicker and checkbox behavior from 1.0.0 is retained. Participant count appears in a compact badge. Personal events use the full-width details region and hide attendees. Binding targets, commands, automation IDs, validation, role/response values, personal project choices and the corporate UUID fallback are unchanged. No view model, API, database or permission changes.

## Fresh verification

- PASS: Release build of Task.Desktop.Tests and application dependencies, including WPF markup compilation, exit 0. SDK 10.0.402; build.log. Two existing xUnit warnings in DesktopCredentialVaultTests.cs and PersonalPlanningTests.cs.
- PASS: XML parsing, unique property assignment, existing binding/command/automation preservation, resource-key checks, native template part checks, responsive structure and footer placement declarations; static-checks.log.
- PASS: git diff --check.
- Extended the existing native WPF test with wide/narrow/wide layout assertions, footer bounds at 900x700, and the native status popup. Existing date, all-day, employee, contact and permission checks remain. Test code compiled successfully.
- BLOCKED: testhost cannot start on Linux without Microsoft.WindowsDesktop.App; tests.log. Native runtime tests and visual validation are NOT claimed to have passed. No generated illustration is used as runtime evidence.

Build from work/production:
`dotnet build tests/Task.Desktop.Tests/Task.Desktop.Tests.csproj --configuration Release -p:EnableWindowsTargeting=true -p:UseSharedCompilation=false --no-restore --nologo -m:1`

Required before merge on Windows:
`dotnet test tests/Task.Desktop.Tests/Task.Desktop.Tests.csproj --configuration Release --filter FullyQualifiedName~Calendar`

Visually compare with Today: new/edit events at 1120x900 and 900x700, personal/corporate modes, 100/150/200% DPI, keyboard tab/arrow/escape behavior, native popup scrolling and selection, read-only/saving states. Set TASK_CALENDAR_PICKER_EVIDENCE to capture wide/narrow screenshots via the existing test hook.

No roadmap readiness percentage changed; this increment changes presentation rather than feature coverage. PR remains a draft pending Windows runtime and visual verification.
