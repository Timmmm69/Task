# Direction 2 visual foundation — design QA

- Date: 2026-09-14
- Reference: `work/stage_5_prototype/implementation-direction2-final.png`
- Reference dimensions: 1487×1058 px; frozen Direction 2 `Сегодня` state.
- Implementation evidence: `work/production/evidence/direction2-foundation-1.0.0/main-100.png` (2560×1528 physical px, 1920×1040 logical viewport, host DPI 144) and `main-150.png` (1920×1040 physical px, 1280×693 logical viewport, host DPI 144).
- Comparison views: `comparison-full.png` and the shell-normalized `comparison-shell.png`.
- State note: the production capture intentionally exercises the deterministic `Задачи` QA state. The baseline captures `Сегодня`; content-layout differences are outside this visual-foundation scope.

## Full-view assessment

The production shell is recognizably aligned with Direction 2: compact left navigation, neutral navigation gradient, selected-item accent rail, contextual single-line header, blue primary task action, compact connection status, white working surface, and thin bottom status bar. Density, 4/8-based spacing rhythm, restrained borders, and hierarchy match the frozen handoff closely enough for the shared application shell.

## Focused shell assessment

The normalized shell crop confirms the intended proportions and visual sequence. The production client preserves its existing native title bar, command set, and task content while matching the baseline's shell geometry. No P0, P1, or P2 visual defects remain.

Accepted P3 differences:

- Windows-native title-bar chrome and product icon differ from the prototype browser rendering.
- Production connection and session copy is bound to live ViewModel state and is therefore not text-identical to the frozen reference.
- The production `Задачи` content view is not redesigned by this shell-focused change.

## Iteration history

1. Pre-change review found an oversized blue product block, card-like navigation selection, stacked title/context, a non-primary new-task action, and a clipped connection treatment.
2. The shell and shared resources were brought back to the frozen token, geometry, and interaction-state contract.
3. Native UI verification exposed a `Double`/`GridLength` resource mismatch; a load-and-measure regression test was added and the token was corrected.
4. Final connection width was adjusted after screenshot review so the online label and context remain readable without clipping.
5. Post-fix native captures passed the 100/125/150/200% matrix, UIA patterns, keyboard traversal, and focused visual review.

final result: passed

---

# Direction 2 search, notifications and lifecycle — design QA

- Date: 2026-09-19
- Source visual truth: `work/stage_5_prototype/qa-wave-c-search.png`, `qa-wave-c-search-overlay.png`, `qa-wave-c-lifecycle-archive.png`, `qa-wave-c-lifecycle-trash-retention.png`, and `edge-notification-target-changed.png`.
- Implementation evidence: `work/production/evidence/direction2-search-lifecycle-1.0.1/normal.png`, `normal-overlay.png`, `notification-center.png`, `offline.png`, and `limited-role.png`.
- Combined comparison evidence: `comparison-search.png`, `comparison-overlay.png`, and `comparison-notification-center.png` in the same evidence directory.
- Source pixels: 1280×720. Implementation pixels and native viewport: 1280×820 at the host's active Windows density. The comparisons preserve each capture's native pixel size and align the 1280 px content width; the extra 100 px of implementation height is retained rather than cropped.
- States: online search, global-search overlay, unread warning/critical notifications, offline retained results, and limited-role redaction.

## Full-view and focused comparison

The search workspace and global overlay preserve the Direction 2 shell, Segoe UI hierarchy, compact navigation, neutral surfaces, blue selection, grouped results, match highlighting, and permission-safe notice. The notification comparison confirms the same right-aligned center anatomy as the frozen prototype: unread badge, filter, bulk-read action, source actions, and a scrollable event list. Warning and critical entries now have readable urgency labels, soft semantic surfaces, and distinct left accents; urgency does not depend on color alone.

Focused comparison was required for the notification list because the urgency labels, unread markers, border accents, and action hierarchy are too small to judge reliably in the full search view. The lifecycle implementation reuses the already-frozen split list/inspector anatomy and was verified functionally by the release tests; no lifecycle-specific production screenshot was regenerated in this incremental urgency pass.

## Required fidelity surfaces

- Fonts and typography: passed. Native Segoe UI sizes, weights, wrapping, truncation, and hierarchy remain consistent with the frozen desktop direction.
- Spacing and layout rhythm: passed. Search and overlay regions keep the established 4/8-based rhythm, native scaling behavior, and scroll access at the verified viewport.
- Colors and visual tokens: passed. Brand blue, neutral surfaces, warning yellow, and critical red use shared WPF resources and retain readable labels in addition to color.
- Image quality and asset fidelity: passed. The target surfaces contain no custom raster imagery; existing theme icon geometries remain crisp at native scale and no placeholder or generated asset was introduced.
- Copy and content: passed for production UI. English notification titles in the evidence are deterministic ASCII-only E2E fixtures; all application chrome, urgency labels, feedback, and redaction copy remain localized.

## Comparison history

1. The first post-merge comparison found a P2 mismatch: the full notification page styled warning and critical entries, but the bell overlay rendered every urgency with the same neutral card.
2. The overlay item template was aligned with the full center using shared warning/critical resources, a text chip, and a left accent; a UIA contract test now requires the urgency labels and semantic triggers.
3. A native WPF/UIA rerun with isolated warning and critical fixtures produced `notification-center.png`; the combined post-fix comparison shows no remaining actionable P0, P1, or P2 mismatch.

## Primary interactions and accessibility

- Ctrl+K, Escape, arrow selection, Enter activation, overlay focus return, notification opening, bulk read, archive/trash restore, and offline write blocking remain covered by the implementation and tests.
- UIA exposes stable roots and the visible urgency label; normal, offline, and limited-role captures were regenerated after the change.
- Release build passed with 0 errors and 10 pre-existing test-code warnings (`xUnit1031`, `ASPDEPR004`); all 1742 tests passed, with 4 intentionally skipped database integration cases.

## Follow-up polish

- P3: the native notification cards are intentionally roomier than the browser prototype at this viewport; the list remains scrollable and no persistent controls are clipped.

final result: passed
