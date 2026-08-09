# Task — CalendarEvent editor Design QA

Date: 2026-08-01  
Scope: `SCR-044`, `FLOW-031`, canonical CalendarEvent create/edit/attendee/respond states  
Direction: accepted Direction 2  
Result: PASS for the implemented prototype scope

## Verified scenarios

| Scenario | Result | Evidence |
|---|---|---|
| Open existing CalendarEvent | PASS | Editor opens from the calendar event with version/If-Match context and existing draft fields. |
| Canonical fields | PASS | Title, event date, time zone, all-day, start, duration/end, project, status, description, user attendees and contact attendees are present. |
| Create from time slot | PASS | Slot composer preserves title/time and continues into the full editor; saved event appears once in the calendar. |
| Idempotency messaging | PASS | Create success explicitly states that repeating the same idempotency key does not create a duplicate. |
| Attendee replacement | PASS | User/contact attendee scopes remain separate; model deduplicates each scope. |
| RSVP | PASS | Accepted/tentative/declined response controls are functional and expose `aria-pressed`. |
| Validation | PASS | `VALIDATION_FAILED` preserves the draft and identifies canonical field-path behavior. |
| Version conflict | PASS | `VERSION_CONFLICT` blocks save and explains that If-Match prevents overwrite. |
| Authorization | PASS | `FORBIDDEN` blocks the command without exposing hidden data. |
| Deleted target | PASS | `OBJECT_DELETED` blocks update and does not create a replacement object. |
| Revoked session/device | PASS | `SESSION_REVOKED` blocks the command and preserves only the local draft. |
| Offline/read-only | PASS | Editor does not open, create controls are disabled and no offline mutation queue is claimed. |
| All-day behavior | PASS | Start and duration controls disable when all-day is selected. |
| Responsive 700 × 800 | PASS | No document-level horizontal overflow; dialog bounds remained inside viewport (`left=10`, `right=690`, `width=680`). |
| Browser console | PASS | Final warning/error ledger: 0. |

## Visual review

- Direction 2 typography, spacing, borders, colors and existing Fluent icons are preserved.
- The 1280 × 720 dialog uses internal vertical scrolling so the desktop shell does not overflow.
- The 700 × 800 layout collapses to one column and remains fully contained.
- No gradients, fabricated assets, emoji or ad-hoc icon drawings were introduced.

## Evidence

- `qa-wave-c-calendar-event-editor.png`
- `qa-wave-c-calendar-event-editor-responsive.png`
- `qa-wave-c-calendar-event-editor-validation.png`
- `qa-wave-c-calendar-event-editor-offline.png`

## Boundary

The browser run covered the final user-facing `App.jsx` and `styles.css` state. A later type-inference cleanup in `calendarEventModel.js` preserved runtime behavior and was verified by the final model test suite and production build. Native Windows UI Automation, Narrator and actual OS DPI scaling remain Stage 5.4 work.
