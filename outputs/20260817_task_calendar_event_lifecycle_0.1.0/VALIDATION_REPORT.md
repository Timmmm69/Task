# VALIDATION_REPORT — Calendar event lifecycle transitions 0.1.0

Packet: TASK-PROD-MOD-009-CALENDAR-EVENT-LIFECYCLE-0.1.0
Base: c2d0a497c426e9221b80280357be5ed1741e223f (origin/main)
Date: 2026-08-17

## Delivered scope
- `work/production/src/Task.Domain/Calendar/CalendarEvent.cs` — full
  existing lifecycle-state transitions on the `CalendarEvent` aggregate:
  - `Archive(Guid actorId, DateTimeOffset occurredAtUtc)`;
  - `RestoreFromArchive(Guid actorId, DateTimeOffset occurredAtUtc)`;
  - `MoveToTrash(Guid actorId, DateTimeOffset occurredAtUtc)`;
  - `RestoreFromTrash(Guid actorId, DateTimeOffset occurredAtUtc)`.
  Each method delegates to the matching `SyncableEntityMetadata` transition
  (version advances exactly once there) and preserves every business field:
  `ProjectId`, `Title`, `Description`, `Timing`, `Status`, `UserAttendees`
  and `ContactAttendees`.
- The Active-only rejection was removed from both `Reconstitute` overloads:
  valid Active, Archived and Trashed metadata are all reconstitutable; the
  existing `EnsureActive` guards keep `UpdateDetails`, `Cancel`, `Reschedule`
  and `ReplaceAttendees` Active-only, now enforced against reconstituted
  Archived/Trashed events. No status prerequisites were added: Scheduled and
  Cancelled events use lifecycle transitions whenever the metadata permits.
- No hard delete was added: `DELETE /api/v1/calendar-events/{id}` is
  represented by `MoveToTrash`. All existing `Create`/`Reconstitute`
  signatures are preserved.
- `work/production/tests/Task.Tests/Calendar/CalendarEventTests.cs` — 15 new
  lifecycle facts plus a 3-case lifecycle reconstitution theory; the three
  obsolete Active-only reconstitution tests were replaced (previously 422
  tests, now 437).
- Output package `outputs/20260817_task_calendar_event_lifecycle_0.1.0/`.

## Verification results (actual)
| Check | Command / method | Result |
|---|---|---|
| Unit tests | `dotnet test work/production/tests/Task.Tests/Task.Tests.csproj --no-restore` | PASS 437/437 (0 failed, 0 skipped); previously 422 |
| Solution build | `dotnet build work/production/Task.sln --no-restore` | 0 errors, 0 warnings |
| Diff hygiene | `git diff --check` | clean |
| Scope | `git status --short` after implementation | only the two allowed production/test files and the new output package |
| Manifest parse | `ConvertFrom-Json` | valid JSON |
| Hashes | canonical-LF SHA-256, UTF-8 without BOM, CRLF -> LF | every MANIFEST.sha256 entry and manifest.json artifactHash matches a fresh recomputation after the final rebase |
| Rebase | `git fetch origin --prune` + `git rebase origin/main` before push | no conflicts; pushed HEAD == origin/main |

## Lifecycle invariant coverage (actual)
- Reconstitution accepts valid Active, Archived and Trashed metadata and
  alters nothing: version, metadata (record equality) and every business
  field, including both attendee lists, are unchanged.
- `Archive`: Active Scheduled and Active Cancelled events archive; the
  version advances exactly once with correct `UpdatedBy`/`UpdatedAtUtc` and
  `ArchivedAtUtc`; archiving an Archived or Trashed event is rejected.
- `RestoreFromArchive`: only Archived restores to Active with archive
  metadata cleared; scalar fields and both attendee lists are preserved;
  Active and Trashed source states are rejected.
- `MoveToTrash`: Active and Archived events move to trash; the trashed
  metadata records the correct prior lifecycle state (including the
  preserved original `ArchivedAtUtc` for a previously archived event);
  repeated trash is rejected.
- `RestoreFromTrash`: restores to Active or Archived exactly per the
  recorded prior state, clears all trash metadata and preserves every
  business field; Active and Archived source states are rejected.
- Reconstituted Archived and Trashed events reject `UpdateDetails`, `Cancel`,
  `Reschedule` and `ReplaceAttendees` with `InvalidOperationException`
  (mirroring `OBJECT_ARCHIVED` / `OBJECT_DELETED` conflict semantics).
- Existing Active-event behavior is unchanged: the full prior test suite
  still passes without modification of its assertions.

## Not delivered (explicitly out of scope)
- HTTP endpoint handlers, permissions, If-Match/idempotency, audit/outbox,
  domain events, sync and notifications.
- Persistence/repository, worker, recurrence and attendee response logic.
- Hard deletion of a `CalendarEvent` (the DELETE operation maps to
  `MoveToTrash` in this domain slice).
- Desktop UI, authorization and deployment.
- Any change to `SyncableEntityMetadata.cs`, `TaskAggregate.cs`,
  `CalendarAttendeeTypes.cs`, `CalendarAttendees.cs`,
  `CalendarEventTiming.cs`, `CalendarEventStatus.cs`, calendar
  timeline/overlap files, solution/project files, dependencies or
  `sources/**`.

## Hash rule
UTF-8 without BOM; CRLF normalized to LF before SHA-256. MANIFEST.sha256 is
not recursively hashed. All hashes verified after the final rebase.

## Revision history
- 2026-08-17: initial delivery, base c2d0a497c426e9221b80280357be5ed1741e223f.
