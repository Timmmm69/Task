# Stage 5.1 — Foundations & Tokens, Direction 2

Version: 0.1  
Date: 2026-07-28  
Decision: VIS-001 — Direction 2, Timeline planner  
Visual truth: `work/stage_5_1/directions/Stage_5_1_Direction_2.png`

## Design intent

Schedule-first Windows desktop organizer with calm information density, persistent navigation, a time-based daily canvas, an unscheduled queue and a task-detail pane. The visual language follows native Windows/Fluent conventions without depending on color alone.

## Typography

| Token | Value | Use |
|---|---|---|
| `font.family.ui` | Segoe UI Variable Text, Segoe UI, system-ui | All product surfaces |
| `font.size.100` | 12 px | Supporting metadata, shortcuts, status |
| `font.size.200` | 13 px | Dense field labels and task metadata |
| `font.size.300` | 14 px | Default controls and task rows |
| `font.size.400` | 15–16 px | Navigation and section headings |
| `font.size.600` | 21 px | Detail title |
| `font.size.800` | 29 px | Page title |
| `font.weight.regular` | 400 | Body text |
| `font.weight.semibold` | 600–650 | Titles, selected navigation, primary action |

## Color roles

| Token | Value | Role |
|---|---|---|
| `color.brand.primary` | `#0F6CBD` | Primary action, links, selected states, focus |
| `color.brand.primaryStrong` | `#005A9E` | Hover/pressed primary |
| `color.brand.soft` | `#EAF3FF` | Selection background |
| `color.text.primary` | `#1B1A19` | Primary content |
| `color.text.secondary` | `#605E5C` | Metadata and supporting text |
| `color.surface.base` | `#FFFFFF` | Main surfaces |
| `color.surface.subtle` | `#FAFAFA` | Footer, hover and grouped regions |
| `color.border.default` | `#E1DFDD` | Dividers and group boundaries |
| `color.semantic.critical` | `#D13438` | Overdue/high priority/current time |
| `color.semantic.success` | `#107C10` | Completed/connected/low-priority direction |
| `color.semantic.warning` | `#F2A900` | Medium priority/deadline attention |

Color never carries meaning alone: every semantic color is paired with an icon, label, position, or border.

## Geometry and density

| Token | Value |
|---|---:|
| `space.1` | 4 px |
| `space.2` | 8 px |
| `space.3` | 12 px |
| `space.4` | 16 px |
| `space.5` | 20 px |
| `space.6` | 24 px |
| `control.height.compact` | 38–40 px |
| `nav.row` | 52 px |
| `timeline.hour` | 69 px |
| `radius.control` | 4–5 px |
| `radius.window` | 7 px |
| `stroke.default` | 1 px |
| `focus.stroke` | 2 px |

## Layout contract

- Window title bar: 48 px.
- Left navigation: 212 px at reference viewport; may compact to 178 px below 1220 px.
- App header: 70 px.
- Content: timeline 41.2%; right work area consumes the remainder.
- Status footer: 46 px.
- Timeline label rail: 100 px.
- Reference QA viewport: 1487 × 1058 CSS px at device scale factor 1.

## Iconography and assets

- Microsoft Fluent UI System Icons through `@fluentui/react-icons`.
- Default icon size: 20–24 px; compact metadata icons: 15–18 px.
- No emoji, text glyphs, handcrafted SVGs or CSS-drawn substitute icons.
- Direction 2 contains no photographic or illustrative assets; no generated raster assets are required.

## Accessibility baseline

- Visible two-pixel focus indicator with offset.
- Minimum interactive target is 38 px for dense desktop controls and 44–52 px for primary navigation.
- Native buttons, selects, checkboxes and semantic headings are retained.
- `Alt+N` opens the new-task dialog; `Escape` closes it.
- Selected states use border/background/icon plus `aria-pressed` or `aria-expanded`.
- High-DPI and long Russian text validation remains required at Gate 5.2/5.4.

## Implementation evidence

- Tokens are implemented in `work/stage_5_prototype/src/styles.css`.
- Interactive composition is implemented in `work/stage_5_prototype/src/App.jsx`.
- Browser and design-QA evidence will be linked after visual verification.
