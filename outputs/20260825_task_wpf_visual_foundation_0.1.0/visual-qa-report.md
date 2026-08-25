# Visual QA report

## Итог

Direction 2 structural parity достигнут в пределах фактически доступных данных и
нативного WPF. Critical/High visual findings после итераций отсутствуют.

## Before gap list

До increment production shell использовал преимущественно стандартные WPF
контролы, локальные hardcoded colors/sizes и простую DockPanel-композицию.
Основные gaps относительно Stage 5:

- отсутствовал переиспользуемый token/style foundation;
- navigation не имела Fluent icons, soft selection и leading rail;
- online/read-only выглядел как технический notice, а не connection component;
- page commands, data header, status/priority и detail были визуально плоскими;
- loading/empty/error не образовывали общей state system;
- shell не повторял Direction 2 header/footer/density hierarchy.

Evidence: `evidence/before/1200x900-loaded.jpg` и
`evidence/before/uia-tree.txt`.

## Reference и comparison

- Reference: `evidence/reference/direction2-tasks-reference.png`.
- Compact reference: `evidence/reference/direction2-compact-reference.png`.
- WPF reference viewport: `evidence/after/1487x1058-loaded-selected.jpg`.
- Side-by-side: `evidence/comparison/reference-vs-wpf.jpg`.
- Before/after: `evidence/comparison/before-vs-after.jpg`.

Raw pixel diff не применялся как единственный gate: reference создан web
prototype, а production использует native WPF text, scrollbars и title bar.

## Token-controlled checks

| Параметр | Canonical | WPF | Результат |
|---|---:|---:|---|
| Expanded navigation | 212 px | token 212 | PASS |
| Compact navigation | 178 px | token 178 | PASS |
| Header | 70 px | token 70 | PASS |
| Footer | 46 px | token 46 | PASS |
| Navigation row | 52 px | token 52 | PASS |
| Default control | 38–40 px | token 40 | PASS |
| Radius | 4–5 px | tokens 4/5 | PASS |
| Default stroke | 1 px | token 1 | PASS |
| Focus stroke | 2 px | token 2 | PASS |
| Spacing scale | 4/8/12/16/20/24 | tokens 4/8/12/16/20/24 | PASS |
| Brand primary | `#0F6CBD` | `#0F6CBD` | PASS |
| Brand strong | `#005A9E` | `#005A9E` | PASS |
| Brand soft | `#EAF3FF` | `#EAF3FF` | PASS |
| Primary text | `#1B1A19` | `#1B1A19` | PASS |
| Secondary text | `#605E5C` | `#605E5C` | PASS |
| Base/subtle surface | `#FFFFFF` / `#FAFAFA` | matching tokens | PASS |
| Default border | `#E1DFDD` | `#E1DFDD` | PASS |
| Critical/success/warning | canonical | matching tokens | PASS |

Typography uses `Segoe UI Variable Text` with `Segoe UI` fallback and canonical
12/13/14/16/21/29 px roles. Reference geometry differences caused by native
title bar and DPI rounding remain within the intended token-controlled layout;
no structural overlap or clipping was found.

## Viewport matrix

| Viewport | Layout result | Evidence |
|---|---|---|
| 1487×1058 | 212 px nav, table + side inspector | `after/1487x1058-loaded-selected.jpg` |
| 1280×720 | full command/header hierarchy, usable table | `after/1280x720-loaded.jpg` |
| 1200×900 | compact 178 px nav, stable list/detail/footer | `after/1200x900-loaded.jpg` |
| 1000×640 | stacked inspector, vertical scrolling | `after/1000x640-loaded.jpg` |
| 800×480 | minimum window remains operable; no overlap | `after/800x480-loaded.jpg` |

All captures were produced at the current 144 DPI / 150% system scale. A
separate 200% display session was unavailable and was not simulated by changing
the user's OS setting.

## State matrix

| State | Result | Evidence |
|---|---|---|
| Loaded | PASS | `after/1200x900-loaded.jpg` |
| Real API loaded | PASS | `after/real-e2e-1200x900-loaded.jpg` |
| Selected + keyboard focus | PASS | `after/1200x900-selected-keyboard-focus.jpg` |
| Empty | PASS | `after/1200x900-empty.jpg` |
| Initial loading | PASS | `after/1200x900-loading.jpg` |
| Network error + retry | PASS | `after/1200x900-network-error.jpg` |
| Read-only/disabled create | PASS | visible in loaded screenshots and UIA |

Loading reserves the content region; refresh uses an inline fixed-width
indicator. Error avoids exception details. Empty keeps only safe refresh.
Selected row uses fill, border/leading indicator and 2 px focus, not color alone.

## Annotated findings

Resolved during QA:

- normal online state separated from warning/error semantics;
- long header strings receive ellipsis plus tooltip/UIA value;
- adaptive inspector now expands and receives focus on Enter;
- compact viewport uses internal scroll containers instead of clipped content;
- progress indicator uses shared brand/High Contrast resources;
- disabled New Task has non-opacity styling and an accessible reason.

Accepted native differences:

- Windows title bar, WPF scrollbar and font rasterization are platform-native;
- no fake search, filters, profile, assignee or project data were added to chase
  the web reference;
- table column proportions adapt to available native window width.

Open Critical: 0. Open High: 0. Verification gaps: real OS High Contrast and an
isolated 200% DPI session.
