# Object links design — 1.0.0

Base: 3fe76562b3c51fee3e36b933b169b101376afb47 (main).

## Changes
Theme-based relation table (44px minimum rows, title tooltips, full-row single selection, larger bounded viewport), separate action bar and add-link card, wrapping actions and labelled stretching inputs. Link count, busy indicator and neutral empty-list state. Theme-based picker result rows, metadata and confirmed-selection card.
Commands, access checks, cancellation, keyboard handlers, automation IDs and persisted column IDs retained. No model/API/database changes. No readiness progress raised.

## Fresh validation
PASS: both XAML files parse as XML; Task resource keys resolve in theme/App/local dictionaries; unique style assignments; automation IDs, named controls, selection/command bindings, keyboard hooks and persisted column IDs retained; git diff --check.

NOT RUN: .NET build and native WPF tests (dotnet is unavailable on this Linux host).
NOT VERIFIED: native rendering, narrow-window/DPI/high-contrast layout. No screenshots or visual-pass claim.

## Required Windows verification
Run existing production CI build/test gate, especially ObjectLinkTargetPickerWindowsTests, ObjectLinksIntegrationWindowsTests and ViewStateUiTests. Inspect WorkHub and Project file/contact hosts at narrow width and 100%/150% DPI/high contrast. Check empty, busy, selected, offline, forbidden and Personal UUID entry states. Native screenshots remain required for visual acceptance.

Main has not been modified by this package.
