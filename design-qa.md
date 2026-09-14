# Design QA — Today / Direction 2 Timeline Planner

Final result: **passed**

## Source and implementation evidence

- Source type: canonical Task visual baseline (Direction 2).
- Source, normal: `work/stage_5_prototype/implementation-direction2-final.png` (1487×1058).
- Source, compact: `work/stage_5_prototype/implementation-direction2-compact-1200x900.png` (1200×900).
- Implementation, normal: `outputs/20260914_today_direction2_timeline_planner_1.0.0/evidence/today-normal.png` (1487×1058).
- Implementation, compact: `outputs/20260914_today_direction2_timeline_planner_1.0.0/evidence/today-compact-1200x900.png` (1200×900).
- Implementation, offline/read-only: `outputs/20260914_today_direction2_timeline_planner_1.0.0/evidence/today-offline-readonly.png` (1200×900).
- Host density: native Windows DPI 144 (150%); the captured 1200×900 state therefore exercises a stricter logical viewport than the 100% reference.

## Combined comparison evidence

- Full normal comparison: `outputs/20260914_today_direction2_timeline_planner_1.0.0/evidence/comparison-normal.png`.
- Full compact comparison: `outputs/20260914_today_direction2_timeline_planner_1.0.0/evidence/comparison-compact.png`.
- Focused compact header comparison: `outputs/20260914_today_direction2_timeline_planner_1.0.0/evidence/comparison-compact-header.png`.

All three combined images were opened after generation and reviewed as a single side-by-side input rather than as independent screenshots.

## Findings

- The production screen preserves the canonical three-zone composition: planner on the left, queue in the upper-right, and selected-item inspector in the lower-right.
- The principal split is 47.6% / 52.4%; the queue/inspector split and 49 px section headers match the baseline hierarchy.
- The timeline uses a 100 px label rail, 69 px per hour, hourly rules, an 08:00 initial offset, and a live current-time rule and label.
- Concurrent intervals are assigned deterministic side-by-side lanes, so neither real task hides another while their true start and duration remain unchanged.
- Scheduled cards expose time, title, project state, priority and workflow state. Queue cards expose the same identity and urgency metadata. The inspector follows selection from either source.
- Normal header shows `Сегодня`, the localized date, refresh, `Новая задача`, connection state and logout. At the strict 150%-scaled compact capture the date and shortcut hint collapse before critical commands; the title and commands remain visible.
- Offline/read-only keeps the last confirmed planner, queue and selection visible, adds a warning banner, disables mutation and keeps the inspector readable.
- Empty, initial-loading, forbidden/session-ended and error treatments reuse the same typography, semantic colors and inline-state language.

The prototype contains a denser fixture set than the deterministic production QA seed. That changes the number of visible cards, not the implemented structure. Production evidence is sourced from the real HTTPS API and PostgreSQL stand; no static screen fixtures were added.

The current Today API provides a project identifier but not a project display name. The UI therefore renders the truthful states `Без проекта` or `Проект назначен` and does not invent names.

## Iteration history

1. Initial native render exposed a WPF `Run.Text` two-way binding failure; the count binding was made explicitly one-way and covered by a static XAML regression assertion.
2. Direct target-window capture replaced foreground-screen capture so evidence remains deterministic in a locked/remote desktop session.
3. Compact UI Automation anchors were moved onto peers WPF actually exposes.
4. Offline activation was corrected to preserve confirmed data and selection rather than clearing them before a failing request.
5. The compact header was rebalanced: connection chrome and the primary button contract first, while section title and all critical commands remain visible.
6. Real overlapping 09:00 intervals were moved into equal-width lanes; both cards remain visible and selectable.
7. The current-time overlay was attached to a true Canvas so `CurrentTimeTop` controls its exact vertical position instead of layout centering it.
8. Final normal, compact and offline states were re-rendered, combined with their references, opened and reviewed. No substantial structural mismatch remains.
