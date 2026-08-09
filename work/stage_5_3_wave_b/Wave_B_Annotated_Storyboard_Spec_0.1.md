# Task — Wave B Annotated Storyboard / Prototype Specification 0.1

**Working status — not a Gate 5.3 closure.** Frames below are buildable prototype requirements. Data labels are only those named in the Component Inventory; no endpoint schema, response field, or invented API is specified.

## P1 — Project creation and overview

1. `SCR-060` active Projects list: filter retains context; empty state offers Create only with `Project.Create`.
2. `SCR-070` modal editor: Save / Cancel; inline `VALIDATION_FAILED`, duplicate warning, and `VERSION_CONFLICT` retain the draft and focus the first actionable notice.
3. `SCR-061` overview after save: Summary plus Tasks, Calendar, Members, Files, Contacts, Comments, History, Settings. A capability-filtered tab is absent; a currently visible tab that becomes unavailable moves to neutral unavailable state (`STC-020`).
4. `SCR-069` Settings: Pause and Complete are visible only for `Project.Update`; active-task warning is a validation/transition result, never a silent change.

## P2 — Member management and ownership

1. `SCR-064` Members shows member, project role, and allowed narrow actions. It communicates the owner invariant without revealing raw policy machinery.
2. `SCR-071` role/overrides dialog: PeoplePicker only returns allowed people; Apply gets server recheck. `DUPLICATE_RESOURCE`, `FORBIDDEN`, `SYNC_SCOPE_CHANGED`, and conflict preserve the table selection safely.
3. Transfer ownership is a distinct consequential confirmation (`SCR-208`); remove and role controls carry an accessible disabled reason through `SCR-204` where appropriate.

## P3 — Project completion, archive, trash, restore

1. `SCR-072`: Complete and Archive are separate commands. Complete returns to active project with lifecycle status; Archive goes through confirmation.
2. `SCR-140`: archived project is read-only (`STC-011`); history is available, and Unarchive appears only when permitted.
3. `SCR-141`/`SCR-142`: trash gives a tombstone; restore can require a permitted parent/name choice. Do not expose a hidden parent.
4. `SCR-143`: Purge requires typed confirmation, explains irreversible **metadata** deletion, and gives retention/legal-hold/conflict recovery instead of pretending success.

## F1 — File catalog, metadata, and location list

1. `SCR-080` tree/list plus `SCR-081` inspector use virtual catalog metadata. A visible CatalogItem is not an assurance that the OS can open it.
2. `SCR-083` lists locations with scope, owner/device, priority, availability and last-check evidence as supplied by the inventory. A foreign local path is redacted; the UI must not infer a path.
3. `SCR-088` typed links omit hidden targets and use a neutral unavailable result on recheck.

## F2 — Resolve/open and SMB diagnosis

1. `SCR-084` resolve/open frame shows an allowed resolved path and alternatives, then hands off to Windows.
2. `SCR-085` recovery frame distinguishes: `FILE_NO_LOCATION`, `FILE_NOT_FOUND`, `FILE_ACCESS_DENIED`, `NETWORK_RESOURCE_UNAVAILABLE`, `UNSAFE_FILE_TYPE`, and other-device availability. `FILE_ACCESS_DENIED` is an OS/SMB result after an allowed open action, not a metadata-permission verdict.
3. `SCR-090` diagnostics permits Recheck and Copy safe report; detail is redacted by path/error visibility. Only `NetworkResource.Manage` exposes the admin-resource route.
4. Global offline preserves known metadata read-only but permits a local Windows open where available; no stale availability claim is made.

## F3 — Relink or add an alternative location

1. `SCR-086` replaces metadata location only after explicit confirmation; `SCR-087` adds an alternative and preserves the existing location.
2. Native selection (`SCR-210`) is an OS dialog handoff. Cancel returns focus to the invoking control.
3. `UNSAFE_PATH`, wrong scope, duplicate, access denied, network unavailable, and conflict are explicit variants. Neither path action deletes or moves physical files.

## C1 — Contacts and companies

1. `SCR-110` list supports allowed-scope filters and makes offline results visibly partial/read-only.
2. `SCR-111` contact and `SCR-113` company cards disclose only allowed fields; unavailable linked objects have no count or identifying detail.
3. `SCR-112`/`SCR-114` editors retain drafts on validation/duplicate/conflict. Channel data lives in `SCR-115`; external handler opening is an explicit user action, not an automatic message or email.

## C2 — Interaction timeline

1. `SCR-116` timeline is manual chronology. `SCR-117` collects only inventory-named concepts: occurred_at, type, summary, participants, next step.
2. Hidden participant is neutral/unavailable; create/update requires `Interaction.Create/Update`. Delete/restore are capability/lifecycle contingent, with no implied outbound side effect.
3. `SCR-118` uses typed Task/Project/File links and redacts unavailable target metadata.

## S1 — Comments, watchers, history

1. `SCR-035`, `SCR-067`, and reusable `SCR-202` use CommentThread. Editable own comment and moderator action are distinguishable by permission; deleted comment is a tombstone, not silently removed.
2. Offline uses ReadOnlyBanner and RetryAction; a server `FORBIDDEN` reverses any optimistic affordance and retains no hidden content.
3. `SCR-036`, `SCR-068`, and reusable `SCR-201` use TimelineHistory with structured event/time/actor presentation, copy-safe data, and redaction under current `History.Read` rights.
4. `WB-WATCH-01`: Task card only. A `Task.Watch`-gated watcher action/PeoplePicker has no dedicated screen. It hides when irrelevant and uses the shared unavailable explanation after server recheck.

## L1 — Cross-cutting error, permission, and destructive annotations

* `STC-002/003`: skeleton for no usable data; subtle refresh for usable data.
* `STC-007/008/020`: permission/visibility is server-authoritative; protected values are removed before render, not merely disabled.
* `STC-009/010`: version conflict/precondition keeps draft and supplies Refresh / Compare / Reapply / Discard, never silent overwrite.
* `STC-011/012`: archive and trash are distinct read-only lifecycle states.
* `STC-013/014`: server unavailable/reconnecting disables writes and marks cache freshness; file opening is separately assessed by Windows.
* `SCR-207`: right-click and Shift+F10 offer the same capability-filtered commands. `SCR-208` confirmations restore focus to the source action on cancel/failure.
