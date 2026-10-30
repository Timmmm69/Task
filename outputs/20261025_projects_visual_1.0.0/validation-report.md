# Projects — Today design alignment — 1.1.0

Scope: corporate ProjectsView only. Reference: work/production/src/Task.Desktop/Views/TodayView.xaml at PR base 243cc5a650a5c3714a7a7cf146c98ebaf728bc08.

## Design correspondence

| Today pattern | Projects implementation |
| --- | --- |
| Adjoining flat regions | List and inspector share a white canvas with a 1px vertical divider and proportional 32/68 columns |
| Queue header: 49 DIP, section title: 16 DIP | Same list header height and heading typography; project count beside heading |
| Queue rows: icon, semibold title, muted metadata, bottom rule | Project list, member rows and related tasks use that hierarchy |
| Inspector inset: 20/14/20/18, detail title: 21 DIP | Same section inset and title typography |
| Status badge: min-height 38 DIP, padding 10 DIP | Matching status badge dimensions and control radius |
| Calendar glyph beside deadline | Project dates use shared calendar glyph with 7 DIP gap |
| Facts: 116 DIP label column, wrapping description | Owner, manager and description use matching alignment and 20 DIP description line height |
| Grouped actions beneath facts | Project actions follow the overview; member buttons wrap beneath roles |
| Centered icon and empty-inspector prompt | Empty project selection follows the same treatment |
| Planning section hierarchy | Editor groups main details, management and dates; separated fixed header and action/error footer |

The work changes layout and hierarchy, beyond the earlier style unification. No new palette, global theme, API, ViewModel, storage or business rule changes. Employee picker, file and contact child implementations are reused and outside scope. Personal projects are outside scope.

## Fresh static verification
- PASS: Python ElementTree parses updated XAML.
- PASS: counted comparison against PR baseline preserves commands, command parameters, ItemsSource, SelectedItem, section keys, form-save binding, AutomationIds, events and permission bindings.
- PASS: all 27 named StaticResource/DynamicResource keys have local/shared definitions.
- PASS: no conflicting attribute/property-element assignments; single-content controls have one child; explicit grid indices are within declared definitions.
- PASS: git diff --check.
- NOT RUN: build, WPF runtime tests, screenshots. Linux host has no dotnet SDK. Source alignment does not prove runtime appearance; Windows verification remains required.

## Windows checks before merge

From work/production:

```powershell
dotnet build src/Task.Desktop/Task.Desktop.csproj --configuration Release
dotnet test tests/Task.Desktop.Tests/Task.Desktop.Tests.csproj --configuration Release --filter FullyQualifiedName~Task.Desktop.Tests.Projects
```

Compare Today and Projects side by side at supported window sizes, 100/150/200% scale and high contrast. Inspect long project/employee names, action wrapping, empty selection, create/edit, manager catalog and UUID fallback, member permissions/add/save/remove, fixed footer, Ctrl+Enter, keyboard navigation, persisted sections, collapse/unload cancellation and lazy loading of files/contacts/history/tasks.

Roadmap progress is unchanged because Windows verification remains outstanding.
