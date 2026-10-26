# Calendar event forms 1.0.0

Base: `3fe76562b3c51fee3e36b933b169b101376afb47`.

Changes: scoped calendar form resource dictionary; styled native DatePicker and checkbox; fluid two-column field groups; sections for event details, timing and attendees; calendar-local employee list style; empty and permission states. All-day timing fields are disabled without clearing values. Existing commands, bindings, automation IDs, UUID fallback and personal/corporate mode rules are retained. No API or persistence changes.

## Evidence

- PASS: Release build of Task.Desktop.Tests and its application dependencies, including XAML/BAML compilation. SDK 10.0.402 on Linux. Final incremental build: 0 errors, 0 warnings. Initial full successful build had two existing xUnit analyzer warnings in DesktopCredentialVaultTests.cs and PersonalPlanningTests.cs.
- PASS: XML parsing, duplicate property assignment check, no newly missing named resources, binding/command and named control preservation, native DatePicker part names. Run `python outputs/20261024_calendar_event_forms_1.0.0/verify_xaml.py` from repository root (Python + lxml).
- PASS: `git diff --check`.
- BLOCKED: calendar tests could not start on Linux because Microsoft.WindowsDesktop.App is unavailable; see tests.log. Native WPF rendering, keyboard use, popup behavior, screen size/DPI and high contrast remain unverified at runtime.
- Extended the existing NativeCalendarUsesSharedPickerAndContactFallback test with native date-popup selection and all-day timing-state assertions; it compiled successfully.

Build command (substitute installed dotnet path):
`dotnet build tests/Task.Desktop.Tests/Task.Desktop.Tests.csproj --configuration Release -p:EnableWindowsTargeting=true -p:UseSharedCompilation=false --no-restore --nologo -m:1`

Before merge on Windows: build Release, run `dotnet test tests/Task.Desktop.Tests/Task.Desktop.Tests.csproj --configuration Release --filter FullyQualifiedName~Calendar`, inspect new/edit forms at 900x700 and 1120x900 with scaling, native date entry/popup and keyboard focus, personal/corporate modes, participant permissions and saving state. Existing Windows CI covers these tests when earlier gates pass.

No roadmap progress changed: this is a UI consistency update without new product capabilities. Existing unresolved Task.Radius.Window outside the event editor was left unchanged.
