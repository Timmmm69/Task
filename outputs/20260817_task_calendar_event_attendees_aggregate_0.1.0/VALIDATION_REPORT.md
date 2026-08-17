# VALIDATION_REPORT — Calendar event attendees aggregate 0.1.0

Packet: TASK-PROD-MOD-009-CALENDAR-EVENT-ATTENDEES-AGGREGATE-0.1.0
Base: 422e6d6bbcefc36ba7bdca867dedef287ea74926 (origin/main)
Date: 2026-08-17

## Delivered scope
- `work/production/src/Task.Domain/Calendar/CalendarEvent.cs` — attendee
  collections attached to the `CalendarEvent` aggregate:
  - public read-only `IReadOnlyList<EventAttendee> UserAttendees` and
    `IReadOnlyList<ContactAttendee> ContactAttendees`;
  - new `Create(...)` and `Reconstitute(...)` overloads accepting both
    collections; the existing 8/6-parameter overloads are preserved exactly
    and continue to create/reconstitute events with empty collections;
  - `ReplaceAttendees(Guid actorId, DateTimeOffset occurredAtUtc,
    IEnumerable<EventAttendee>, IEnumerable<ContactAttendee>)`;
  - `UpdateDetails`, `Cancel` and `Reschedule` preserve both collections.
- `work/production/tests/Task.Tests/Calendar/CalendarEventTests.cs` — 26
  focused attendee-aggregate tests (previously 396 tests, now 422).
- Output package `outputs/20260817_task_calendar_event_attendees_aggregate_0.1.0/`.

## Verification results (actual)
| Check | Command / method | Result |
|---|---|---|
| Unit tests | `dotnet test work/production/tests/Task.Tests/Task.Tests.csproj --no-restore` | PASS 422/422 (0 failed, 0 skipped); 26 new tests, previously 396 |
| Solution build | `dotnet build work/production/Task.sln --no-restore` | 0 errors, 0 warnings |
| Diff hygiene | `git diff --check` | clean |
| Scope | `git status --short` after implementation | only the two allowed production/test files and the new output package |
| Manifest parse | `ConvertFrom-Json` | valid JSON |
| Hashes | canonical-LF SHA-256, UTF-8 without BOM, CRLF -> LF | every MANIFEST.sha256 entry and manifest.json artifactHash matches a fresh recomputation after the final rebase |
| Rebase | `git fetch origin --prune` + `git rebase origin/main` before push | no conflicts; pushed HEAD == origin/main |

## Contract coverage (actual)
- Both attendee collections are required, copied on entry and exposed
  read-only (`Array.AsReadOnly`; caller mutation tests prove no leakage after
  construction and after replacement; direct `IList<T>` mutation of the
  exposed collections throws `NotSupportedException`).
- Null collection throws `ArgumentNullException`; null element throws
  `ArgumentException`; 501 entries throw `ArgumentException`; exactly 500 are
  accepted — enforced in `Create`, `Reconstitute` and `ReplaceAttendees` for
  both kinds.
- Supplied order and values are preserved exactly; no deduplication and no
  cross-kind validation (the contract defines none).
- `ReplaceAttendees` requires an Active lifecycle (same gate as the scalar
  update; enforced via the existing `EnsureActive`, mirroring the OpenAPI
  `OBJECT_ARCHIVED` / `OBJECT_DELETED` conflict codes) and operates on both
  Scheduled and Cancelled events (tested on a cancelled event).
- Sequence-equal replacement returns the same instance and does not advance
  the version; a genuine replacement calls `Metadata.RecordVisibleChange`
  exactly once and bumps the version by exactly one, recording actor and
  occurrence timestamp.
- `UpdateDetails`, `Cancel` and `Reschedule` preserve both attendee
  collections (tested).
- The existing public `Create` and `Reconstitute` overloads are unchanged in
  signature and behavior and expose two empty collections (tested).

## Not delivered (explicitly out of scope)
- API endpoints (`PUT /api/v1/calendar-events/{id}/attendees` and the rest of
  FR-073..FR-081), permissions, If-Match/idempotency, attendee response
  workflow (`/respond`), notifications, audit/outbox.
- Cross-kind duplicate rules (not defined by the contract).
- Recurrence, query/read model, persistence/repository, worker, sync,
  Desktop UI, authorization and deployment.
- Any change to `CalendarAttendeeTypes.cs`, `CalendarAttendees.cs`,
  `CalendarEventTiming.cs`, `CalendarEventStatus.cs`,
  `CalendarTimelinePlacement.cs`, `SyncableEntityMetadata.cs`,
  `TaskAggregate.cs`, solution/project files, dependencies or `sources/**`.

## Hash rule
UTF-8 without BOM; CRLF normalized to LF before SHA-256. MANIFEST.sha256 is
not recursively hashed. All hashes verified after the final rebase.

## Revision history
- 2026-08-17: initial delivery, base 422e6d6bbcefc36ba7bdca867dedef287ea74926.
