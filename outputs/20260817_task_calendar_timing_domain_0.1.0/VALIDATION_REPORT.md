# Validation Report — Task.Domain.Calendar Timing Foundation (0.1.0)

## Package

- Packet ID: `TASK-PROD-MOD-009-CALENDAR-TIMING-DOMAIN-0.1.0`
- Version: 0.1.0
- Date: 2026-08-17
- Base (origin/main before this packet): `fcb53db020c3ac54871f7d4d4ddf29d17dbec116`
- **Base note:** the packet declared base `d0e886afbcaa43ccee1ab73f3beb4ab6b841caab`, but `origin/main` first advanced to `fcb53db020c3ac54871f7d4d4ddf29d17dbec116` by an explicit user decision — the previously uncommitted reminders hardening output package (`outputs/20260817_task_reminders_domain_hardening_0.1.1`) was committed and pushed as a standalone docs commit before this packet started. This packet was then implemented and verified on the actual `origin/main` head.
- Scope: new `work/production/src/Task.Domain/Calendar/` (4 files) and new `work/production/tests/Task.Tests/Calendar/` (2 files), plus the output package.
- No existing file, project/solution file, NuGet dependency, API, persistence, worker, Desktop or authorization code was touched.

## What this packet delivers

Isolated domain foundation for MOD-009 temporal placement:

1. `CalendarEventTiming` — value object mirroring OpenAPI `CalendarEvent` temporal fields (`eventDate`, `isAllDay`, `startAtUtc`, `endAtUtc`, `timeZone`).
2. `CalendarTimelinePlacement` + `CalendarTimelinePlacementKind` — pure domain projection (`Timeline` / `DateOnly` / `None`) with factories for `CalendarEventTiming` and the existing `TaskSchedule` (read-only).
3. `CalendarOverlapPolicy` (+ `CalendarOverlapResult`, `CalendarOverlapPair`) — pure warm overlap policy for two placements and collections.

## Invariants implemented

- timezone is required, trimmed, 1..64 chars and must resolve via `TimeZoneInfo.TryFindSystemTimeZoneById`.
- all non-null instants have the UTC offset (`Offset == TimeSpan.Zero`).
- `isAllDay == true` requires both instants to be null; only the all-day shape is exposed, so instants cannot be supplied (rejection path is compile-time enforced by the factory shape).
- `isAllDay == false` requires both instants; `endAtUtc` strictly later than `startAtUtc`.
- a timed event's `eventDate` must equal the local start date of `startAtUtc` in the event time zone (BR-050/AC-050 local time is never interpreted without a time zone).
- timed interval is half-open `[startAtUtc, endAtUtc)`; touching boundaries (`end == next.start`) do not overlap.
- all-day events are date-based, not time-based, and never have a timeline interval.

## BR mapping

| BR | AC | Implemented by | Verified by |
| --- | --- | --- | --- |
| BR-047 Overlap разрешён и только предупреждается | AC-047 | `CalendarOverlapPolicy.Evaluate` / `FindOverlaps`: overlap is returned as a warning/result, never thrown, never a placement veto | `OverlapPolicy_DoesNotThrowForValidOverlaps`, `OverlapPolicy_DetectsRealOverlap`, `FindOverlaps_*` |
| BR-048 Date-only Task отображается вне временной шкалы | AC-048 | `CalendarTimelinePlacement.FromTaskSchedule`: no `StartsAtUtc` => `None`, even with `DeadlineUtc` present | `FromTaskSchedule_NoStartWithoutDeadline_YieldsNonePlacement`, `FromTaskSchedule_NoStartWithDeadline_YieldsNonePlacement` |
| BR-049 Deadline не используется как позиция на timeline | AC-049 | `FromTaskSchedule`: timeline placement derives from `StartsAtUtc` only; `DeadlineUtc` never becomes start/end | `FromTaskSchedule_WithStart_YieldsTimelinePlacementDrivenByStartOnly`, `FromTaskSchedule_NoStartWithDeadline_YieldsNonePlacement` |
| BR-050 Все локальные времена интерпретируются с timezone | AC-050 | `CalendarEventTiming`: required/resolvable time zone; `eventDate` equals local start date in that zone | `CreateTimed_ConvertsLocalDateAcrossUtcDateBoundary`, `CreateTimed_EventDateMustMatchLocalStartDate`, timezone rejection tests |

## Acceptance tests (36 new, all PASS)

`CalendarEventTimingTests` (20):
- all-day event with null instants;
- all-day trims the time zone;
- all-day rejects empty / unknown / too-long / null time zone;
- timed event with both UTC instants and matching local `eventDate`;
- only-shape all-day: no all-day-with-instants shape exists (compile-time);
- timed rejects end before start, end equal to start, non-UTC start, non-UTC end, empty / unknown / too-long / null time zone;
- timed time-zone conversion crossing the local/UTC date boundary;
- timed rejects `eventDate` that does not match the local start date.

`CalendarTimelinePolicyTests` (16):
- all-day => DateOnly placement;
- timed => Timeline placement;
- task without start (with and without deadline) => None placement;
- task with start => Timeline placement driven by `StartsAtUtc`; deadline is not a timeline position;
- real overlap detected with reported intersection;
- touching half-open boundaries do not overlap;
- nested interval overlaps and reports inner bounds;
- disjoint intervals do not overlap;
- all-day participant => no warning;
- none participant => no warning;
- policy does not throw for valid overlaps;
- open-ended task interval overlaps an interval starting after its start;
- `FindOverlaps` ignores date-only/none placements;
- `FindOverlaps` empty collection => no pairs;
- sets without overlap => no pairs;
- chain of touching intervals => no overlaps.

## Verification commands and results

| # | Command | Result |
| --- | --- | --- |
| 1 | `dotnet test work/production/tests/Task.Tests/Task.Tests.csproj` | PASS: 323/323, 0 failed (287 before + 36 new Calendar tests) |
| 2 | `dotnet build work/production/Task.sln` | 0 errors, 0 warnings |
| 3 | `git diff --check` | clean |
| 4 | Scope check (`git status` + `git diff --stat` before commit) | only the two new Calendar directories and the one output package are present |
| 5 | `manifest.json | ConvertFrom-Json` | parses successfully |
| 6 | Canonical-LF SHA-256 verification of every `MANIFEST.sha256` entry and every `artifactHashes` entry | PASS |

## Files created

| File | Lines | Role |
| --- | --- | --- |
| `work/production/src/Task.Domain/Calendar/CalendarTimelinePlacementKind.cs` | 20 | `None` / `Timeline` / `DateOnly` placement kinds |
| `work/production/src/Task.Domain/Calendar/CalendarEventTiming.cs` | 139 | event timing value object with all invariants |
| `work/production/src/Task.Domain/Calendar/CalendarTimelinePlacement.cs` | 101 | placement projection + factories (`FromTiming`, `FromTaskSchedule`) |
| `work/production/src/Task.Domain/Calendar/CalendarOverlapPolicy.cs` | 102 | warm overlap policy + result/pair records |
| `work/production/tests/Task.Tests/Calendar/CalendarEventTimingTests.cs` | 186 | 20 acceptance tests |
| `work/production/tests/Task.Tests/Calendar/CalendarTimelinePolicyTests.cs` | 197 | 16 acceptance tests |
| `outputs/20260817_task_calendar_timing_domain_0.1.0/VERSION.txt` | 1 | version |
| `outputs/20260817_task_calendar_timing_domain_0.1.0/manifest.json` | 1 | packet manifest |
| `outputs/20260817_task_calendar_timing_domain_0.1.0/VALIDATION_REPORT.md` | 1 | this report |
| `outputs/20260817_task_calendar_timing_domain_0.1.0/MANIFEST.sha256` | 1 | canonical-LF hashes |

## Explicit confirmations

1. **Overlap only warns.** `CalendarOverlapPolicy` returns `CalendarOverlapResult` with `HasOverlap` and the intersection interval; it never throws for valid placements and never blocks creation or placement (BR-047/AC-047).
2. **Deadline is not a timeline position.** `CalendarTimelinePlacement.FromTaskSchedule` uses only `TaskSchedule.StartsAtUtc`; `DeadlineUtc` is ignored for placement and never becomes a start/end position of the timeline interval (BR-049/AC-049). A task without `StartsAtUtc` is placed outside the timeline even when a deadline exists (BR-048/AC-048).
3. **Local time is always interpreted with a time zone.** `CalendarEventTiming` requires a resolvable `timeZone` (trimmed, 1..64 chars) and demands `eventDate == local start date of startAtUtc` in that time zone (BR-050/AC-050). All instants are UTC-offset only; all-day events carry no instants and are date-based.

## Hash rule (portable, explicitly recorded)

- Hash basis: **UTF-8 text without BOM**, newline canonicalization **CRLF -> LF**.
- The hashes in `MANIFEST.sha256` and in `manifest.json.artifactHashes` are computed on this canonical form; raw `Get-FileHash` output on a worktree with `core.autocrlf=true` is **not** claimed to match.
- Validation step 6 recalculates the canonical-LF hash for every listed file and confirms it equals both `MANIFEST.sha256` and `artifactHashes`.
- `MANIFEST.sha256` itself is not recursively hashed.

## Evidence boundaries (explicit out-of-scope)

- NOT delivered: `CalendarEvent` aggregate and attendee model; endpoints `GET /api/v1/calendar`, CRUD `/api/v1/calendar-events`, attendees, response status, archive/restore; permissions `Calendar.Read`, `CalendarEvent.Create`, `CalendarEvent.Update`; optimistic concurrency, idempotency, audit, sync, conflict UI; query/read-model, filters, storage, database or worker; Task/Event modification, drag/resize UI, real timeline rendering.
- No file outside the two new `Calendar` directories and the new output package was changed, added, deleted or renamed.
- `TaskSchedule`, `TaskAggregate` and all other existing files remain untouched; `TaskSchedule` is used read-only.