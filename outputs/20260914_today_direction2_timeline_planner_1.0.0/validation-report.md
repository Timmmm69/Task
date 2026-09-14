# Today Direction 2 Timeline Planner validation report — 1.0.0

Result: **PASS**

## Implemented

- Production WPF `Сегодня` now uses the canonical Direction 2 three-zone layout.
- The header exposes localized date, refresh, shell-level `Новая задача` (`Alt+N`), connectivity and logout without replacing existing commands.
- The left planner renders a 24-hour, 69 px/hour timeline, hourly rules, current time, all-day/untimed rail and scheduled cards from the real calendar/task DTOs.
- Intersecting scheduled intervals receive equal-width side-by-side lanes, preventing real cards from obscuring one another.
- The upper-right queue contains overdue and unscheduled active tasks. A scheduled task is deduplicated from the queue by its real identifier.
- Timeline and queue selection share one inspector; opening the selected item delegates to the pre-existing task/calendar navigation contract.
- Compact presentation preserves title, primary action, refresh, connectivity, planner, queue, inspector and open-card action. Secondary date/shortcut metadata collapses first at strict high-DPI width.
- Offline activation retains the last confirmed data and selection, disables mutation and presents an explicit read-only banner. Empty, loading, forbidden/session-ended and error states use the same visual language.
- No product fixture was added and no server API, DTO, database migration or business rule was changed.

## Verification

1. Release solution build — PASS, 0 errors. One pre-existing `xUnit1031` analyzer warning remains in `DesktopCredentialVaultTests.cs`.
2. Desktop regression suite — PASS, 319 passed, 0 failed, 0 skipped.
3. Production QA-03 gate — PASS on an isolated PostgreSQL 16 database, production HTTPS API, production worker and Release WPF executable.
4. Product API verification — PASS, 23 checks.
5. Critical persisted product verification — PASS, 12 checks.
6. Notification verification — PASS, 8 checks.
7. Normal screenshot — PASS, 1487×1058.
8. Compact screenshot — PASS, 1200×900; all four critical UIA anchors visible.
9. Offline/read-only screenshot — PASS, cached selection remains inspectable and mutation remains disabled.
10. Side-by-side normal, compact and focused-header comparisons — PASS after final re-render.
11. Dashboard ordering and validation — PASS; 40 items, 8 categories, 6 gates, overall 100, handoff ready.

Primary machine-readable evidence is in `evidence/ui-assertions.json`, `evidence/api-assertions.json`, `evidence/product-db-assertions.json`, `evidence/notifications-assertions.json`, `evidence/worker-delivery.json` and `evidence/qa03-gate.log`.

## Acceptance boundaries

- Project display names are not present in the current Today task/calendar read DTOs. The UI reports `Без проекта` or `Проект назначен` truthfully instead of inventing a label.
- Screenshot counts differ from the prototype because the production gate uses a smaller deterministic real-data seed. Layout and state behavior, not fixture volume, were the comparison criteria.
- The final capture host ran at native DPI 144 (150%), exercising the compact composition under stricter logical space than the 100% baseline image.
