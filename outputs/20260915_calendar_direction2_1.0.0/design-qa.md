# Direction 2 visual and interaction QA

Result: **PASS for the requested production calendar flow**, with the bounded differences below. Inspected together: `evidence/comparison-calendar-week.png`, `comparison-calendar-overlap.png`, `comparison-calendar-editor.png`; inspected standalone: `calendar-offline.png` and `evidence/scaling/calendar-{100,125,150,200}.png`. References are 1280×720 prototype captures; production captures use a native 1487×1058 WPF window on a 144-DPI host, so this is a layout/behavior comparison, not a pixel-diff score.

## Core fidelity

| Surface | Evidence and result |
| --- | --- |
| Layout and spacing | Direction 2 hierarchy is present: title/range, view switcher, week navigation, all-day rail, hour grid, cards, inspector and fixed status footer. Five workdays are visible at the captured 150% viewport; Saturday/Sunday remain available by horizontal scroll, preserving the existing seven-day calendar model. No card or toolbar collision in the required captures. |
| Typography and content | Segoe UI Variable tokens and primary/secondary hierarchy follow the desktop design system. Time labels and day headers are legible. Dense overlap titles are ellipsized inside separate lanes; full title/time/status/conflict are available in UIA, tooltip and selected inspector. Russian UI copy is coherent in normal/offline states. |
| Color, surfaces and icons | Primary blue, soft selected day, semantic warning/critical card tones, white calendar surface, borders and radii follow the existing Task tokens. Icon assets use the production vector resource family; there are no substitute images or decorative fake assets. |
| Editor | Modal overlay, title/version/timezone, date/all-day/start/end, project/status, description, attendees, validation, saving and conflict states are implemented. Save/cancel and focus cycle are native WPF controls. Project and attendee identities remain GUID-based because the preserved calendar API supplies IDs rather than a name directory; this is a data-source limitation, not a broken field. |
| States | Loading/empty/error, offline/read-only, save, conflict/rollback and selected inspector have explicit UI. Real API stop retains the last confirmed calendar, disables creation and displays the read-only banner (`calendar-offline.png`). |
| Responsiveness | Calendar uses a minimum readable week width and horizontal scrolling rather than compressing seven day-columns into illegible lanes. At the 200%-equivalent viewport, the secondary recurrence command wraps to a second line; week range, refresh, new event and the scrollable timeline remain usable and visible. |

## Accessibility and interactions

- Native UIA verified separate overlapping event buttons, selected inspector, editor title/save control and offline banner. The calendar grid exposes a scroll pattern at all four tested viewport equivalents.
- Keyboard F6/Escape handling, modal Tab cycle and visible input focus are retained; focused Windows UX tests (7/7) and desktop regression tests pass.
- Contrast and semantic status colors use existing design-system brushes. No motion is required; reduced-motion behavior is unchanged.
- Actual Windows display scaling was not changed: the DPI matrix resizes a 144-DPI native WPF window to logical viewport equivalents of 100/125/150/200% and verifies UIA bounds plus screenshots.

## Bounded differences / follow-up

- The production shell and seven-day data model differ from the five-day static prototype crop. This preserves the established product navigation and week semantics.
- Named project/contact pickers would require a cross-feature directory source beyond the existing calendar read/write contract. The editor currently validates GUIDs and preserves participant roles/responses; replacing identity entry with a directory-backed selector is a separate usability increment.
- The reference prototype demonstrates drag/resize scheduling gestures. This increment does not add them; the requested navigation, inspection and server-backed create/update flows are covered instead.

No unresolved clipping, overlap, inaccessible critical control, or failed requested state was found in the captured production flow.
