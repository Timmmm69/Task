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
