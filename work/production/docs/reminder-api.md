# Corporate reminder API

The Stage 2.3.1 reminder routes are implemented with Reminder.ManageOwn.
Ownership is always the authenticated recipient, including for administrators.
Source visibility and current permissions are rechecked within each transaction
and before an idempotency replay. Mutations commit together with audit, domain
event and sync outbox records. Migration 15 adds occurrence.version, incremented
on every update including worker claims/retries. Existing migration checksums
and reminder/notification records are preserved.

Rule mutations use the rule's strong "vN" ETag. Snooze and dismiss use the
current occurrence's version in both If-Match and expectedVersion. The current
occurrence is the row with the rule's current nextTriggerAt; it is locked before
the version check and is never replaced by a different historical occurrence.
A stale or missing occurrence returns 412.

## Compatible opt-in response extension

The baseline Reminder and ReminderPage retain their canonical field sets.
Desktop requests includeOccurrence=true on GET/list/create/patch to opt into:

| Field | Type | Meaning |
|---|---|---|
| currentOccurrence | canonical ReminderOccurrence or null | Current occurrence and independent version |
| targetTitle | string or null | Title of the currently authorized source |

This resolves the baseline ambiguity: the snooze/dismiss URI carries a rule ID
while its precondition targets an occurrence. It does not add a body occurrence
ID or change existing routes. Strict baseline clients do not receive extra
fields. The canonical /reminders/upcoming array also exposes occurrence
versions. Private lease tokens, worker identifiers, error details and snooze
counters are excluded from public DTOs.

Absolute instants are normalized to UTC PostgreSQL microsecond precision.
Relative rules use the task/event start or deadline. An omitted relative offset
uses the recipient's saved default. Inactive/completed/cancelled targets cannot
create, restore or snooze reminders.

Snooze requires a future RFC3339 instant, supports up to 100 postponements and
creates one successor. Previous delivered occurrences remain delivered; the
current unread notification is dismissed without deleting history. Cancel and
dismiss stop the current pending occurrence. Restore can reactivate a cancelled,
never-delivered occurrence at its future due time. An already delivered due time
cannot be reused. Changing a rule's due time preserves historical delivery.

Desktop provides source search, all five triggers, create/edit/cancel/restore,
occurrence dismiss and default/preset/custom snooze. Reminder CRUD honors its
own management capability; popup actions additionally require notification read
access. Settings defaults are editable in Corporate. Physical Windows banner
acceptance is outside this increment.
