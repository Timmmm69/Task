# Повторения — компоновка Direction 2 / Today, 1.1.0

Reference: production Views/TodayView.xaml and Resources/Controls.Shell.xaml.

Replaced the single long form with series list, editor and preview/action panes. Uses Today page-title typography, ruled surfaces and a fixed editor action footer. Below 1050 logical pixels the inspector moves under the editor with a bounded scroll area. Added truthful busy, empty-list, preview-placeholder and read-only states. Existing recurrence/date styles from 1.0.0 remain in this PR.

Verified: XML parsing, original binding multiset preservation, named resource resolution, absence of duplicate XAML property assignments, git diff --check. Business logic and permission commands are unchanged.

Windows visual acceptance remains pending: normal/compact sizes, 100/150/200% DPI, keyboard and popup navigation, read-only state and long series names. Linux cannot run WPF or produce native screenshots. No visual approval is claimed.

Previous PR CI stopped in solution-wide whitespace checking, including pre-existing Work/ObjectLinkTargetPickerTests.cs and other Work tests, before compilation. These unrelated files were not reformatted. Readiness/roadmap was not changed.

Fresh Release build: PASS (0 warnings, 0 errors). Command: dotnet build work/production/src/Task.Desktop/Task.Desktop.csproj -c Release -p:EnableWindowsTargeting=true -m:1 -nr:false.
