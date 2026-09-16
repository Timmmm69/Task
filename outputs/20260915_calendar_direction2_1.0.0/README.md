# Task — production WPF Calendar Direction 2, 1.0.0

Validated production increment for the calendar and event editor. The calendar domain model, HTTPS API contract and DST conversion remain unchanged; `sources/` is untouched.

- `validation-report.md`: implementation scope, checks and known limits.
- `design-qa.md`: visual and interaction comparison against Direction 2 references.
- `evidence/calendar-week.png`, `calendar-overlap.png`, `calendar-editor.png`, `calendar-offline.png`: required Release WPF captures.
- `evidence/comparison-calendar-*.png`: side-by-side references and production captures.
- `evidence/scaling/calendar-{100,125,150,200}.png`: native WPF viewport-equivalent scaling captures.
- `evidence/ui-assertions.json`, `evidence/scaling/windows-ux.json`: machine-readable E2E and UIA/DPI results.
- `source/`: exact snapshots of changed production, tests, verification scripts and roadmap.
- `manifest.json` and `MANIFEST.sha256`: byte counts and SHA-256 checksums.
