# Projects visual consistency — 1.0.0

Base: `3fe7656` (origin/main at implementation start). Scope: corporate ProjectsView only.

## Changes
- Shared page spacing, list selection styles, card surfaces and section headings.
- Wrapping project title and dates visible in overview; wrapping metadata cards.
- Members use labelled roles and individual cards; actions wrap below the role selector.
- Existing permission bindings and command guards retained; role selectors disabled without member management rights.
- Editor occupies detail column, with independently scrolling fields and fixed heading, errors and save/cancel footer.
- Detail content hidden during editing to exclude covered controls from keyboard navigation. List remains available.
- Empty selection prompt; accessible names for editor fields.
- Section wrappers follow child visibility, preventing empty cards for unavailable sections.

## Fresh verification
- PASS: Python ElementTree parses updated XAML.
- PASS: original commands, command parameters, item sources, selected items, section persistence keys, automation IDs and event handlers preserved by comparison with git baseline.
- PASS: all 24 named StaticResource/DynamicResource keys exist in local/shared dictionaries.
- PASS: no element combines Style attribute with a property-element Style.
- PASS: git diff --check.
- NOT RUN: dotnet build, WPF tests and screenshot verification. Host is Linux and dotnet is absent (exit 127). Static evidence does not establish WPF runtime correctness or visual acceptance.

## Required Windows verification
Run from work/production:

```powershell
dotnet build src/Task.Desktop/Task.Desktop.csproj --configuration Release
dotnet test tests/Task.Desktop.Tests/Task.Desktop.Tests.csproj --configuration Release --filter FullyQualifiedName~Task.Desktop.Tests.Projects
```

Manually inspect normal and high contrast at 100%, 150%, 200% scaling: long names, empty selection, create/edit, catalog and UUID fallback, read-only member permissions, role save/remove, fixed footer on a short window, Ctrl+Enter, keyboard navigation, section restore/cancellation, files/contacts/history/tasks. Minimum supported shell size and full-screen should both be checked.

No ViewModel, API, storage, shared theme or business rule changes. Personal projects are outside this change. Product readiness dashboard unchanged: Windows verification remains outstanding.
