# Validation report — TASK-PROD-MOD-008-REMINDERS-DOMAIN-0.1.0

Executed locally on 2026-08-17 (Windows, .NET SDK 10.0.400), worktree `main`
pinned to evidence base `50d3d567acd981fdf21bd82d139df0f539da814f`
(`origin/main` and `HEAD` identical).

## Package

- Packet ID: `TASK-PROD-MOD-008-REMINDERS-DOMAIN-0.1.0`
- Version: `0.1.0`
- Directory: `outputs/20260816_task_reminders_domain_0.1.0/`
- Stage: production code, domain layer only (`Task.Domain`)
- Implementation base (pre-MOD-008): `2056218388cf362b07cdaf0cc613ebc5379a92d5`
- Evidence base (verified HEAD): `50d3d567acd981fdf21bd82d139df0f539da814f`

## Implementation commits covered by this package

| SHA | Purpose |
|-----|---------|
| `e4bbfa434bdcfa9511c7009cf297b2c3e3e6b3d9` | Add Task.Reminders domain: reminder aggregates with occurrence lifecycle |
| `50d3d567acd981fdf21bd82d139df0f539da814f` | Harden reminders: reject firing/claiming before the scheduled instant |

## Verification commands and results (re-run at packaging time)

| # | Command | Result |
|---|---------|--------|
| 1 | `dotnet test work/production/tests/Task.Tests/Task.Tests.csproj` | PASS: 287 passed, 0 failed, 0 skipped |
| 2 | `dotnet build work/production/Task.sln` | PASS: 0 warnings, 0 errors |
| 3 | `git diff --check 2056218..50d3d56` | clean |
| 4 | `git diff --check` (working tree) | clean |
| 5 | `git status --short` | only the four new files of this package (untracked) |
| 6 | `manifest.json` parses via `ConvertFrom-Json` | PASS |
| 7 | Every hash in `MANIFEST.sha256` recomputed against the files (`Get-FileHash`) | PASS, full match |
| 8 | `manifest.json` `artifactHashes` vs `MANIFEST.sha256` | PASS, full match |

## Files covered by this package (nine existing source/test files, unchanged)

### Production — `work/production/src/Task.Domain/Reminders/`
| File | Role |
|------|------|
| `Reminder.cs` | Reminder aggregate root: Create/Reconstitute, MarkDue/MarkDelivered/Snooze/Cancel/Reschedule/Expire; firing before the scheduled instant rejected |
| `ReminderTrigger.cs` | Single-trigger value object: absolute/relative modes, UTC invariant, contradictory configs rejected at construction |
| `ReminderTriggerType.cs` | Trigger modes (absolute, before_start, before_deadline, at_start, at_deadline) |
| `ReminderStatus.cs` | Reminder lifecycle statuses (scheduled, delivered, snoozed, cancelled, expired) |
| `ReminderOccurrence.cs` | Occurrence aggregate: Create/Reconstitute, Claim/MarkDelivered/Fail/DeadLetter/Dismiss, deterministic `Deduplicate` |
| `ReminderOccurrenceStatus.cs` | Occurrence lifecycle statuses |
| `ReminderOccurrenceKey.cs` | Deterministic occurrence key value object (`<reminderId>|<dueAt>`), UTC-only |

### Tests — `work/production/tests/Task.Tests/Reminders/`
| File | Role |
|------|------|
| `ReminderDomainTests.cs` | Aggregate state machine, snooze/reschedule/cancel/expire windows, blocked transitions, firing-before-instant rejection |
| `ReminderOccurrenceTests.cs` | Occurrence lifecycle, deduplication, claiming-before-instant rejection |

## Contract mapping (MOD-008 «Напоминания»)

- BR-044 / AC-044 — a reminder has **exactly one trigger mode**: the single
  `ReminderTrigger` value object enforces presence/absence of the mode-specific
  fields and rejects contradictory combinations at construction
  (`ReminderTrigger.Create`); `Reminder.Create` accepts exactly one trigger.
- BR-045 / AC-045 — **snooze changes only the reminder time**: `Reminder.Snooze`
  moves status to `Snoozed` and updates only `SnoozedUntil`/`NextTriggerAt`;
  it never touches Task/Event schedule or any other aggregate.
- BR-046 / AC-046 — **deterministic domain-level occurrence deduplication**:
  `ReminderOccurrenceKey` derives a canonical key from `reminderId` + UTC `dueAt`,
  and `ReminderOccurrence.Deduplicate` collapses duplicates by that key,
  making occurrence materialization idempotent within the domain.
- Explicit non-claim: a worker/persistence-level guarantee of delivery
  deduplication (DB unique index, outbox/at-least-once enforcement) is **not
  implemented and not claimed** by this packet; it belongs to later
  Infrastructure/Worker work.

## Scope and evidence boundaries

This packet is a documentation-only deliverable. It **does not modify**
production code, tests, project files, solutions, dependencies, migrations,
configuration, `sources/**`, or any existing file: exactly four new files were
created in `outputs/20260816_task_reminders_domain_0.1.0/` (`VERSION.txt`,
`manifest.json`, `MANIFEST.sha256`, `VALIDATION_REPORT.md`).

Explicitly out of scope of MOD-008 implementation evidence here:
- API/DTO classes and OpenAPI endpoints (incl. `GET/POST /api/v1/reminders*`);
- persistence / repositories / migrations;
- worker delivery and claiming jobs;
- authorization (`Reminder.ManageOwn` capability checks);
- audit/outbox publication and sync semantics;
- Desktop UI (snooze/dismiss/reschedule surfaces).

## Package integrity verification

`MANIFEST.sha256` lists SHA-256 for the nine covered source/test files and the
three package files (`VERSION.txt`, `manifest.json`, `VALIDATION_REPORT.md`);
`manifest.json` itself is anchored there non-recursively (its own hash cannot
be self-referenced inside the JSON). Verified after packaging: all twelve
recorded hashes match the files on disk, and every `artifactHashes` entry in
`manifest.json` agrees with `MANIFEST.sha256`.