# VALIDATION_REPORT — Calendar attendee domain values 0.1.0

Packet: TASK-PROD-MOD-009-CALENDAR-ATTENDEE-DOMAIN-0.1.0
Base: 39f7dd134ab5b751653138eb2c8f4ec0bfd1e9d7 (origin/main)
Date: 2026-08-17

## Delivered scope
- `work/production/src/Task.Domain/Calendar/CalendarAttendeeTypes.cs` —
  `CalendarAttendeeRole` (Required=0, Optional=1, Observer=2) and
  `CalendarAttendeeResponseStatus` (Pending=0, Accepted=1, Declined=2,
  Tentative=3), per OpenAPI `role`/`responseStatus` enums.
- `work/production/src/Task.Domain/Calendar/CalendarAttendees.cs` — immutable
  `EventAttendee` (UserAccountId, Role, ResponseStatus, RespondedAtUtc) and
  `ContactAttendee` (ContactId, Role, ResponseStatus, RespondedAtUtc) value
  objects with the `Create(...)` factories from the packet contract.
- `work/production/tests/Task.Tests/Calendar/CalendarAttendeeTests.cs` — 30
  focused tests.

## Verification results (actual)
| Check | Command / method | Result |
|---|---|---|
| Unit tests | `dotnet test work/production/tests/Task.Tests/Task.Tests.csproj --no-restore` | PASS 396/396 (0 failed, 0 skipped); 30 new attendee tests, previously 366 |
| Solution build | `dotnet build work/production/Task.sln --no-restore` | 0 errors, 0 warnings |
| Diff hygiene | `git diff --check` | clean |
| Scope | `git status --short` after implementation | only the 3 new attendee files and the output package |
| Manifest parse | `ConvertFrom-Json` | valid JSON |
| Hashes | canonical-LF SHA-256, UTF-8 without BOM, CRLF -> LF | every MANIFEST.sha256 entry and manifest.json artifactHash matches a fresh recomputation |
| Rebase | `git fetch origin --prune` + `git rebase origin/main` before push | no conflicts; pushed HEAD == origin/main (b49a280), base 39f7dd1 is the parent commit |

## Invariant coverage
- Non-empty identifier: `EventAttendee.Create`/`ContactAttendee.Create` throw
  `ArgumentException` on `Guid.Empty` (tested for both).
- Defined enum values: undefined role or response status throws
  `ArgumentOutOfRangeException` (tested for both value objects).
- UTC rule: a non-null `RespondedAtUtc` must have offset exactly zero, null is
  allowed (tested: null accepted, UTC accepted and preserved, non-UTC rejected,
  for both value objects).
- No invented status/timestamp relationship: `respondedAt` stays nullable
  independently of `responseStatus`; factories never infer or set it.
- Immutability: properties are get-only (`CanWrite == false` asserted for every
  public property of both types); private constructors, static factories only.
- Every declared role (3) and response status (4) is accepted, asserted via
  `Enum.GetValues`-driven theories for both value objects.

## Out-of-scope (explicitly deferred)
- Attaching `userAttendees`/`contactAttendees` to `CalendarEvent`, max-500
  aggregate enforcement, duplicate detection across attendee kinds.
- Replace-attendees (`AttendeesReplace`), attendee response workflow
  (`AttendeeResponse`, If-Match), notifications, audit/outbox.
- API endpoints (FR-073..FR-081), permissions, persistence, sync, worker,
  Desktop UI, and any change to existing domain types.

## Hash rule
UTF-8 without BOM; CRLF normalized to LF before SHA-256. MANIFEST.sha256 is
not recursively hashed. All hashes verified after the final rebase.

## Revision history
- 2026-08-17, correction commit (after initial push of b49a280): validation
  helpers of `EventAttendee`/`ContactAttendee` consolidated into one internal
  `AttendeeValidation` class inside `CalendarAttendees.cs` (public API
  unchanged; production + tests now 342 lines, within the 360-line guideline);
  the rebase description was corrected — pushed HEAD == origin/main (b49a280),
  base 39f7dd1 is the parent commit, not an equal state. All hashes and test
  results were recomputed for the corrected files.
