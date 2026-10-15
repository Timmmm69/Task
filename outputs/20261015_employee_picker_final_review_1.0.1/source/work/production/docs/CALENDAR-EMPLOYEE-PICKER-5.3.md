# Calendar Employee Picker — 5.3

The calendar editor reuses `EmployeePickerView` / `EmployeePickerViewModel` and
`IDesktopEmployeeDirectory` from stage 5.1. It does not implement another search or cache.
Corporate composition supplies the existing authenticated administration client. Personal
events continue to have no attendees or employee-directory reads.

## Editing and data

- For employees, search by name, choose the account (login distinguishes namesakes), confirm,
  then add. The existing add command re-reads that exact account and requires active status.
- Newly added picker employees are revalidated before event submission. Existing attendees
  remain unchanged, including inaccessible or deactivated accounts, unless explicitly edited.
- Employee and contact identity is `(IsUser, Id)`. The same UUID may occur once per type;
  duplicates of the same type are rejected. Each collection retains its existing 500 limit.
- Roles remain `required`, `optional`, `observer`; defaults remain `required` / `pending` /
  null response time. Directory reads never mutate role, response or response timestamp.
- Contacts retain UUID entry. Changing type clears both entry and employee confirmation,
  invalidates pending search/add requests and never changes any existing attendee row.
- Names are optional, form-local display data, fetched only with User.Read. Failed name reads
  retain stored IDs; forbidden/authentication results clear all loaded names and search data.
- Form close/disposal, deactivate, capability/session/connectivity changes cancel reads and
  invalidate late completions. Closing and reopening an editor restores directory access after
  an explicit 403; there is no automatic fallback bypass after a server denial.
- Save is disabled while an add is awaiting account validation. All fields are disabled while
  saving, and the command is built again after validation immediately before API submission.
  No optimistic participant or success announcement is created after a failed event write.

## Effective permissions

| Editor access | Directory behavior | Allowed existing behavior |
|---|---|---|
| Calendar.Read + CalendarEvent.Create + User.Read | Picker in a new event | Add/remove employees and contacts, create |
| Calendar.Read + CalendarEvent.Update + User.Read | Picker in an existing event | Add/remove employees and contacts, update |
| Corresponding calendar rights without User.Read | No directory requests or employee names | Existing UUID entry, removal/role/response edits, event writes |
| User.Read without corresponding calendar write right | No editable attendee form | Calendar read remains governed by Calendar.Read |
| No Calendar.Read, session ended, offline or deactivated surface | Picker queries and attendee mutations disabled | Existing calendar/draft rules retained |
| Server User API 403 despite advertised User.Read | Catalog closed and names cleared | Existing attendees and contacts remain editable; unvalidated new picker employees must be removed or access restored |

No roles or grants changed. Calendar write scope is still enforced server-side. User.Read is
independent of Calendar.Read, CalendarEvent.Create/Update and project permissions.

## APIs and unchanged contracts

Existing `GET /api/v1/users?sort=id&filter=...&cursor=...` and `GET /api/v1/users/{id}`
provide employee search/confirmation/names. Calendar uses its existing schedule/conflicts reads,
event detail read and POST/PATCH `/api/v1/calendar-events[/{id}]`.
`DesktopCalendarAttendee`, calendar DTOs, separate `userAttendees` / `contactAttendees`,
If-Match/version handling, idempotency, timezone/time rules and notification rules are unchanged.
No backend, migrations, permissions, OpenAPI or shared-picker implementation changed in 5.3.

Known contract limit: calendar persistence does not perform a transactional active-account
check for attendees. Client revalidation cannot prevent deactivation between the final read
and server commit. Closing this race needs a separately authorized backend change; it must
not be claimed as solved by the picker. The UUID compatibility path likewise retains the
pre-existing server validation behavior.

## Verification

`CalendarEmployeePickerTests`, `CalendarPickerWindowsTests` and the mixed-collection HTTP tests
cover permissions, duplicates/limit/removal, names and typed ID round-trip, inactive/missing
accounts, offline, auth/403, cancellation, stale responses, contact switching, version conflict,
event disappearance and save/add races. A real Windows WPF window checks bindings, shared
control composition, scrolling, button focus, field disablement and UUID visibility.
This automated fixture does not constitute a manual production-network walkthrough.

The final versioned validation report, TRX receipts, screenshots, hashes and source snapshot
are in `outputs/20261015_calendar_employee_picker_5_3_1.0.0`.
