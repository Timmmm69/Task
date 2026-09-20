# Validation report — final Direction 2 production acceptance

Version: 1.0.0

Date: 2026-09-20

Result: **PASS**

## Acceptance decision

- Critical visual defects remaining: **0**.
- High visual defects remaining: **0**.
- Practically removable Medium defects remaining: **0**.
- The primary Release WPF screens remain recognizably consistent with frozen Direction 2 in composition, hierarchy, typography, colour roles, density, state language and interaction model.
- sources/ and business requirements were not changed.

## Corrected during this acceptance

- Removed the 200%/narrow-viewport shell clipping: navigation becomes the canonical 64 DIP icon rail below 900 DIP, the page heading steps down, and search/new-task/context labels collapse before they can crowd the header.
- Added exact native 1200×900 coverage for authentication, tasks and calendar instead of inferring it from the DPI matrix.
- Extended deterministic evidence to every primary section plus bootstrap failure/progress, offline, limited-role, notification urgency and retained search results.
- Strengthened regression contracts for responsive breakpoints, long Russian text wrapping/ellipsis, and all shared high-contrast resource dictionaries.

## Verification

- Release solution build: **PASS**, 0 errors and 10 existing test-code warnings: one xUnit1031 in DesktopCredentialVaultTests.cs and nine ASPDEPR004 uses of the legacy test-host WebHostBuilder. They are not product/runtime failures.
- Full solution tests: **PASS**, 1747 passed, 4 expected PostgreSQL-only skips, 0 failed.
- DESK-05 native Windows gate: **PASS** — UIA Value/Invoke/Selection/Scroll, Tab, complete F6 cycle, 100/125/150/200%, plus exact 1200×900 auth/tasks/calendar.
- Direction 2 section/edge capture: **PASS** — 20 named screenshots.
- Search/lifecycle capture: **PASS** — grouped search, overlay, semantic notification urgency, offline retained results and limited-role redaction.
- Offline/read-only: **PASS** through real API shutdown and capability-limited login; retained confirmed data remains visible and write actions remain unavailable.
- Long Russian strings: **PASS** through visible wrapped Russian explanatory/status copy in section and offline captures plus explicit TextWrapping/TextTrimming contract tests.
- Forced colours/high contrast: **PASS (resource/runtime contract)** through dynamic SystemParameters.HighContrast and SystemColors resources in Theme, shell, buttons, navigation, data and state controls, loaded by the WPF test host.
- Dashboard ordering/validation: **PASS**; only DESK-05 and QA-01 evidence were updated.

## Visual comparison method

The contact sheet compares nine frozen prototype states with current Release WPF captures: Today, Tasks, Calendar, Inbox, Projects, Search, Settings, Administration, and Offline/read-only. Each production panel names its scenario and scale. Individual unscaled PNGs remain under vidence/.

The systematic review used the frozen baseline document, prototype JSX/CSS, stage 5 evidence, design-system tokens and accessibility baseline. It checked shell geometry, three-pane composition, typography scale, Fluent colour roles, 4/8/12/16/20/24 spacing rhythm, control density, focus/state semantics, responsive behaviour and data-state truthfulness.

## Known limits (not release blockers)

- The host's physical DPI was 150%. The 100/125/200% cases are deterministic logical viewports exercised in a live PerMonitorV2 WPF process, not physical monitor switching.
- Windows High Contrast was not toggled globally during automation. The forced-colours result is based on loaded runtime resource triggers/SystemColors and regression tests; a physical OS-theme screenshot remains deployment smoke evidence.
- The four skipped solution tests require an externally enabled shared PostgreSQL test fixture; the isolated PostgreSQL 16 + HTTPS native gates used by this acceptance passed.

## Evidence map

- direction2-baseline-production-contact-sheet.png — nine baseline/production pairs.
- vidence/accessibility/windows-ux.json — named scenarios, platform, DPI/viewport bounds, keyboard and UIA results.
- vidence/accessibility/*-100.png through *-200.png — scale evidence.
- vidence/accessibility/*-1200x900.png — exact minimum viewport evidence.
- vidence/sections-and-edge-states/validation.json — primary sections and auth/admin/settings edges.
- vidence/search-lifecycle/validation.json — search, notification, offline and limited-role evidence.
- vidence/full-tests/*.trx — complete solution test results.

All isolated test services were stopped and Desktop AppData was restored by the verification scripts.