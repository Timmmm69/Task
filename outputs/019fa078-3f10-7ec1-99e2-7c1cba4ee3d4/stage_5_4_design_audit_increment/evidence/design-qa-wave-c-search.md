# Stage 5.3 Wave C Search — Design QA

## Comparison target

- Source visual truth: `C:\Users\novik\Таск\work\stage_5_1\directions\Stage_5_1_Direction_2.png`.
- Combined comparison: `C:\Users\novik\Таск\work\stage_5_prototype\design-qa-wave-c-search-comparison.png`.
- Implementation evidence:
  - `qa-wave-c-search.png`;
  - `qa-wave-c-search-offline.png`;
  - `qa-wave-c-search-overlay.png`.
- Source pixels: 1536 × 1024.
- Implementation captures: 1280 × 720 CSS px at device density 1.
- State: authenticated desktop client, Direction 2, full Search, Ctrl+K overlay, online permission-safe partial state and offline cache-only state.

## Full-view comparison evidence

The combined comparison confirms that the Search increment preserves the selected Direction 2:

- Windows title bar, persistent left navigation, workspace header, primary blue action and bottom status bar remain stable;
- Segoe UI hierarchy, Fluent icons, blue selection, neutral surfaces, subtle borders and restrained semantic colors match the approved desktop direction;
- the Search workspace uses the same compact enterprise density and internal scrolling as the existing Today, Projects, Files and CRM surfaces;
- no target assets are replaced with CSS art, custom SVG drawings, emoji or placeholder imagery;
- the main query, filter and notice hierarchy remains legible at 1280 × 720 without clipping persistent shell controls.

## Focused state evidence

- `qa-wave-c-search.png`: online full Search, query form, category filters, permission-safe notice, allowed-result count and representative results.
- `qa-wave-c-search-offline.png`: server-unavailable banner, disabled refresh, cache-only notice, allowed cached result count and read-only status bar.
- `qa-wave-c-search-overlay.png`: Ctrl+K modal, category filters, scrollable result list, keyboard helper and “Все результаты” action.

## Primary interactions verified

- Sidebar Search opens the full Search workspace.
- Ctrl+F opens the full Search workspace from another view.
- Ctrl+K opens the global-search overlay.
- Query and category filters narrow the visible allowed result set.
- “Все результаты” transfers both the overlay query and selected category to the full Search workspace.
- Loading and empty-result states render and recover through “Сбросить фильтры”.
- Online mode shows a permission-safe partial notice and counts only allowed results.
- The unavailable result exposes no hidden object title, department, matched fields or hidden-object count.
- Offline mode shows only the allowed cache, disables refresh and write-oriented shell actions, and explains possible incompleteness.
- Browser console verification returned zero warnings and zero errors.

## Required fidelity surfaces

- Fonts and typography: passed. Segoe UI hierarchy, weights, compact metadata and helper copy remain consistent with Direction 2.
- Spacing and layout rhythm: passed. The query form, filter rail, notice, summary and result rows use the established desktop spacing and border language.
- Colors and tokens: passed. Primary blue, neutral grays, permission-safe blue and offline warning yellow are used semantically without gradients.
- Image quality and asset fidelity: passed. The selected direction contains no Search-specific content imagery; Fluent icons are used consistently.
- Copy and content: passed. Permission and offline boundaries are explicit without revealing hidden-object information.
- Accessibility and behavior: passed for the prototype scope. Controls have semantic roles and labels, pressed/disabled states are exposed, keyboard entry points work and status notices use live semantics.

## Findings and fixes

### Pass 1

- [P1] When the full Search page was already mounted, “Все результаты” closed the overlay but retained the previous full-page query and filter.
- Fix: synchronize the full Search local query/filter state whenever the overlay transfer request changes.

### Final pass

The complete interaction scenario was repeated after the fix. Query and filter transfer now succeeds. No actionable P0, P1 or P2 findings remain.

- [P3] The prototype retains the established technical terms `permission-safe partial` and `offline cache-only` in helper copy. A later production copy pass may localize these phrases without changing the security semantics.

## Build and diagnostic evidence

- Serena language diagnostics: 0 errors, 0 warnings; one pre-existing non-blocking Calendar Wave A unused-variable hint.
- Production Vite build: passed, 222 modules.
- Production assets:
  - `index-BbIhisk4.css` — 59.26 kB;
  - `index-DHEMcDAt.js` — 375.47 kB.
- Sites runtime tests: 4/4 passed.

## Residual verification boundary

- Native Windows UI Automation, Narrator, actual 200% OS scaling and external infrastructure behavior remain later Stage 5 gate checks and are not claimed here.
- This report verifies the local browser prototype at 1280 × 720 and the current code state only.

## Implementation checklist

- [x] Preserve Direction 2 shell and component language.
- [x] Implement full Search and Ctrl+K overlay states.
- [x] Implement query/filter transfer, loading and empty states.
- [x] Implement permission-safe partial and offline cache-only behavior.
- [x] Compare source and implementation in one combined visual input.
- [x] Fix the interaction defect found during browser QA.
- [x] Pass language diagnostics, production build and Sites runtime tests.
- [x] Pass Design QA with P0/P1/P2 = 0.

final result: passed
