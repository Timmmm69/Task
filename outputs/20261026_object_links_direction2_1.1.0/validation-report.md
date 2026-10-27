# Связанные объекты — Direction 2, версия 1.1.0

Reference: work/production/src/Task.Desktop/Views/TodayView.xaml at main 3fe76562b3c51fee3e36b933b169b101376afb47.
Previous revision: 81914e4400fba90bfb7d2a2e06069e5390a389ea, PR #15.

## Observable changes
- Three related regions: relation table, selected-relation details/actions, search/add region.
- Reuses Today's flat surfaces, 49-DIP region headers, 12/16-DIP spacing, thin dividers, Fluent icons and blue selection.
- At control width >=760 DIPs, the list/inspector and add region are side by side. Below that they stack in the host scroll area. Existing picker is not recreated on resize.
- If creation types are absent, no empty second column is reserved.
- Search results retain title/type/update/ID, keyboard handlers and automation names; confirmed choice has a blue rail instead of a rounded card.
- Personal UUID entry, Corporate picker, command eligibility, permissions, cancellation and persistence identifiers are unchanged.

## Checks actually executed
PASS: in-memory structural checks for balanced XAML element nesting, duplicate attributes/styles, single Border content, resource-key resolution, trailing whitespace.
PASS: named picker controls, keyboard/lifecycle hooks, selection/command bindings, automation IDs and persisted column IDs retained.
PASS: SHA-256 implementation checked with the published abc vector; hashes cover the UTF-8 file contents.
These source checks do not substitute for WPF markup compilation or visual inspection.

## Environment and CI limits
The execution service for the previous workspace is unavailable (exec-server: No such file or directory); edits prepared through GitHub APIs.
Previous CI run 40115384680 failed in Verify production formatting on existing C# sources, including WorkHubInteractionsTests.cs, WorkHubNavigationTests.cs and WorkHubObjectLinksTests.cs. That revision changed only XAML and output documents; production build/test steps did not run.
No unrelated formatter cleanup and no CI gate changes are included.

## Still required before visual acceptance
Windows .NET 10 WPF build and existing ObjectLinkTargetPickerWindowsTests, ObjectLinksIntegrationWindowsTests, ProjectFilesWindowsTests, ProjectContactsWindowsTests, ViewStateUiTests.
Inspect wide/narrow hosts, 100%/150% DPI, keyboard focus and high contrast; capture empty/busy/selected/forbidden/offline/Personal states. Native screenshots and pixel-level comparison with Today are NOT available. No native visual-pass claim.
No dashboard readiness progress raised; main is not modified.
