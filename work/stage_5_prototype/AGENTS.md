# Prototype Instructions

Run the local server yourself and open the preview in the browser available to this environment. Do not give the user server-start instructions when you can run it.

Before making substantial visual changes, use the Product Design plugin's `get-context` skill when the visual source is unclear or no longer matches the current goal. When the user gives durable prototype-specific design feedback, preferences, or decisions, record them in `AGENTS.md`.

When implementing from a selected generated mock, treat that image as the source of truth for layout, component anatomy, density, spacing, color, typography, visible content, and hierarchy.

Build app UI in `src/`. Keep `.openai/hosting.json`, `worker/index.js`, `scripts/prepare-sites-build.mjs`, and `tests/sites-worker.test.mjs` intact so the same local prototype can be handed to Sites. Before a Sites handoff, run `npm run build` and `npm run test:sites`; the build must leave `dist/client/index.html`, `dist/server/index.js`, and `dist/.openai/hosting.json`.

## Notification urgency (decision, 2026-08-13)
Deadline notifications carry a computed urgency tier (`src/notificationUrgencyModel.js`, pure module with unit tests in `tests/notification-urgency-model.test.mjs`). Tiers: overdue < critical < soon < hours < far, derived at render from `deadlineMinutesFromNow` using thresholds `criticalMinutes/soonMinutes/hoursMinutes` (defaults 60/360/1440). Tiers are editable in Settings > Уведомления; the center shows a left color border + chip, and toasts get an accent bar. Reuse `urgencyForMinutes` rather than re-deriving tiers inline.

## Day progress (decision, 2026-08-13)
The Today planner has a single source of truth `todayTaskItems` (each task has `completed: boolean`). Sections (scheduled/untimed/completed) and the day progress ring are derived via pure functions `deriveAgendaSections` and `computeDayProgress` in `src/todayAgendaModel.js` (tests in `tests/today-agenda-model.test.mjs`). Completion is toggled from the row checkbox (valid HTML: row is a `div` with separate action + select buttons), moves the task to «Завершённые», sets status Готово/Запланировано, and pushes an undo step. Offline disables toggling. Keep the single-state model; do not reintroduce separate task arrays.
