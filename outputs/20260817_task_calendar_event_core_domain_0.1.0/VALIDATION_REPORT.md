# Validation Report — Task.Domain.Calendar CalendarEvent Core Aggregate (0.1.0)

## Package

- Packet ID: `TASK-PROD-MOD-009-CALENDAR-EVENT-CORE-DOMAIN-0.1.0`
- Version: 0.1.0
- Date: 2026-08-17
- Base (origin/main before this packet): `7de0ea65ea06be1fe4c99879a55e09f456e36e9e`
- Scope: new `work/production/src/Task.Domain/Calendar/CalendarEvent.cs`, `CalendarEventStatus.cs`, new `work/production/tests/Task.Tests/Calendar/CalendarEventTests.cs`, plus the output package.
- No existing file, project/solution file, NuGet dependency, API, persistence, worker, Desktop or authorization code was touched.

## What this packet delivers

Isolated `CalendarEvent` core aggregate for MOD-009 (continuation of the calendar timing foundation):

1. `CalendarEventStatus` — enum mirroring the OpenAPI `CalendarEvent.status` enum (`scheduled`, `cancelled`); unknown values are rejected.
2. `CalendarEvent` — immutable aggregate with scalar core fields: `SyncableEntityMetadata Metadata`, `Guid? ProjectId`, `string Title`, `string? Description`, `CalendarEventTiming Timing`, `CalendarEventStatus Status`.

Core scalar fields map 1:1 to OpenAPI `CalendarEvent` / `CalendarEventCreate` / `CalendarEventPatch`:

| OpenAPI field | Aggregate field | Limits / semantics |
| --- | --- | --- |
| `projectId` | `ProjectId` | nullable UUID; `Guid.Empty` rejected, `null` allowed |
| `title` | `Title` | required, trimmed, 1..500 |
| `description` | `Description` | nullable, max 20000 |
| `eventDate`, `isAllDay`, `startAtUtc`, `endAtUtc`, `timeZone` | `Timing` (`CalendarEventTiming`) | encapsulated; timezone/UTC/all-day validation stays in `CalendarEventTiming`, not duplicated |
| `status` | `Status` | `Scheduled` / `Cancelled`; undefined values rejected |

## Invariants implemented

- `Create` produces an active aggregate with `Status == Scheduled`.
- `Reconstitute` accepts only fully valid state: defined `CalendarEventStatus`, non-empty identifiers, valid `projectId` (null or non-empty), trimmed non-empty title (<=500), description (null or <=20000), non-null `Timing`, and an **Active** lifecycle metadata.
- Lifecycle: this packet allows only `EntityLifecycleState.Active`; archive/trash transitions are a separate future packet. Non-active (Archived/Trashed) metadata is rejected at the `Reconstitute` boundary; `UpdateDetails`/`Cancel`/`Reschedule` additionally carry `EnsureActive` guards (defense-in-depth, same pattern as `TaskAggregate`).
- `UpdateDetails`: applies valid scalar changes to an active event; when all values equal the current ones the same instance is returned without a version bump; otherwise `Metadata.RecordVisibleChange` is applied exactly once.
- `Cancel` is allowed only from `Scheduled`; `Reschedule` only from `Cancelled`; repeated or disallowed transitions throw `InvalidOperationException`.
- `Cancel` is a status transition, not a DELETE/trash synonym; no domain events/outbox, no permissions checks.
- `Timing` is preserved as an immutable value object and is never mutated by aggregate operations.

## Acceptance tests (38 new, all PASS)

`CalendarEventTests` (38 `Fact`):

- create with timed timing and with all-day timing (Scheduled, Active, version 1);
- create trims title, allows nullable projectId/description;
- create rejection: empty id / organizationId / creatorId / projectId, empty / whitespace / 501-char title, 20001-char description, null timing;
- reconstitute restores persisted fields (status, version, updatedBy) without advancing version;
- reconstitute rejection: null metadata, null timing, empty projectId, empty / 501-char title, 20001-char description, undefined status, archived lifecycle metadata, trashed lifecycle metadata;
- update single and multiple scalar fields (projectId, title, description, timing) with version +1 and updatedBy/updatedAtUtc recorded;
- update no-op returns the same instance without a version bump;
- update increments version exactly by one;
- update rejection: null timing, empty projectId, empty title, 20001-char description;
- cancel: Scheduled -> Cancelled with version bump; cancel from Cancelled rejected;
- reschedule: Cancelled -> Scheduled with version bump (also from a reconstituted Cancelled event);
- reschedule from Scheduled rejected;
- non-active (Archived/Trashed) events cannot be obtained: rejected at the reconstitution boundary;
- timing preserved as the same immutable instance across cancel/reschedule/update;
- update applies to an active event regardless of status.

## Verification commands and results

| # | Command | Result |
| --- | --- | --- |
| 1 | `dotnet test work/production/tests/Task.Tests/Task.Tests.csproj` | PASS: 366/366, 0 failed (328 before + 38 new CalendarEvent tests) |
| 2 | `dotnet build work/production/Task.sln` | 0 errors, 0 warnings |
| 3 | `git diff --check` | clean |
| 4 | Scope check (`git status --short`) | only the three new Calendar files and the new output package are present |
| 5 | `manifest.json | ConvertFrom-Json` | parses successfully |
| 6 | Canonical-LF SHA-256 verification of every `MANIFEST.sha256` entry and every `artifactHashes` entry | PASS |

## Files created

| File | Lines | Role |
| --- | --- | --- |
| `work/production/src/Task.Domain/Calendar/CalendarEventStatus.cs` | 12 | `Scheduled` / `Cancelled` status enum |
| `work/production/src/Task.Domain/Calendar/CalendarEvent.cs` | 230 | immutable core aggregate: Create / Reconstitute / UpdateDetails / Cancel / Reschedule |
| `work/production/tests/Task.Tests/Calendar/CalendarEventTests.cs` | 428 | 38 acceptance tests |
| `outputs/20260817_task_calendar_event_core_domain_0.1.0/VERSION.txt` | 1 | version |
| `outputs/20260817_task_calendar_event_core_domain_0.1.0/manifest.json` | 53 | packet manifest |
| `outputs/20260817_task_calendar_event_core_domain_0.1.0/VALIDATION_REPORT.md` | 98 | this report |
| `outputs/20260817_task_calendar_event_core_domain_0.1.0/MANIFEST.sha256` | 6 | canonical-LF hashes |

## Explicit confirmations

1. **Cancel is not a deletion.** `Cancel`/`Reschedule` change only `CalendarEventStatus`; DELETE/trash, archive, restore and unarchive are separate lifecycle packets. No domain events/outbox and no permission checks are introduced in this packet.
2. **Timing validation is not duplicated.** `CalendarEventTiming` remains the single place for timezone resolution, UTC-offset, all-day-without-instants and `eventDate == local start date` validation; `CalendarEvent` only requires a non-null timing.
3. **Lifecycle is Active-only in this packet.** Reconstitution of Archived/Trashed metadata is rejected; mutation methods additionally guard on Active lifecycle.

## Hash rule (portable, explicitly recorded)

- Hash basis: **UTF-8 text without BOM**, newline canonicalization **CRLF -> LF**.
- The hashes in `MANIFEST.sha256` and in `manifest.json.artifactHashes` are computed on this canonical form; raw `Get-FileHash` output on a worktree with `core.autocrlf=true` is **not** claimed to match.
- Validation step 6 recalculates the canonical-LF hash for every listed file and confirms it equals both `MANIFEST.sha256` and `artifactHashes`.
- `MANIFEST.sha256` itself is not recursively hashed.

## Evidence boundaries (explicit out-of-scope)

- NOT delivered: `EventAttendee`/`ContactAttendee` and replace/respond; DELETE/trash, archive, restore, unarchive; API endpoints FR-073..FR-081, permissions, If-Match/idempotency; audit/outbox, sync, persistence/repository, worker, calendar query/read model; Desktop editor, drag/resize, overlap UI.
- No file outside the three new Calendar files and the new output package was changed, added, deleted or renamed. `CalendarEventTiming`, `CalendarTimelinePlacement`, `TaskSchedule` and `TaskAggregate` remain untouched; all existing CalendarTiming/placement tests still pass (366/366).
