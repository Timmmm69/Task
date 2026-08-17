# Validation Report — Task.Reminders Domain Hardening (0.1.1)

## Package
- Packet ID: `20260817_task_reminders_domain_hardening_0.1.1`
- Version: 0.1.1 (fix/hardening packet over `20260816_task_reminders_domain_0.1.0`, base commit `e4bbfa4`)
- Date: 2026-08-17
- Base commit (origin/main before this packet): `e4bbfa4`
- Scope: `work/production/src/Task.Domain/Reminders/Reminder.cs`, `work/production/src/Task.Domain/Reminders/ReminderOccurrence.cs`, `work/production/tests/Task.Tests/Reminders/ReminderDomainTests.cs`, `work/production/tests/Task.Tests/Reminders/ReminderOccurrenceTests.cs`

## What this packet fixes (audit findings vs 0.1.0)
1. **Premature firing was possible.** `Reminder.MarkDue` accepted any `occurredAtUtc`, so a reminder could fire before its scheduled instant (`NextTriggerAt`, equal to `snoozedUntil` while snoozed). Now rejected with `ArgumentOutOfRangeException`. Applies to both `Scheduled` and `Snoozed` sources; the instant equal to `NextTriggerAt` remains allowed.
2. **Premature occurrence claiming was possible.** `ReminderOccurrence.Claim` accepted any timestamp, so an occurrence could be claimed before its first attempt instant (`NextAttemptAt`). Now rejected with `ArgumentOutOfRangeException`; claiming exactly at the attempt instant remains allowed.
3. **Stale line counts in the 0.1.0 validation report** (written from draft line counts, not the shipped files). Corrected counts below.

## Verification commands and results

| # | Command | Result |
|---|---------|--------|
| 1 | `dotnet test work/production/tests/Task.Tests/Task.Tests.csproj` | PASS: 287/287 (0 failed), incl. 30 Reminders tests (2 new guard tests) |
| 2 | `dotnet build work/production/Task.sln` | 0 errors, 0 warnings |
| 3 | `git diff --check` | clean |
| 4 | `git status --short` | only the 4 scope files modified |
| 5 | Manifest self-check (SHA-256 vs MANIFEST.sha256) | PASS |

## Correction (checksum integrity, 2026-08-17)

The canonical-LF SHA-256 recorded for `outputs/20260817_task_reminders_domain_hardening_0.1.1/VERSION.txt` was incorrect. Content is `0.1.1\n` (UTF-8 without BOM, CRLF normalized to LF).

- Recorded: `5a8fffdaf58b4a8e10d4f9f58380ec5c3e0a35821f9af44c48264ac71f4d79b7`
- Correct canonical-LF SHA-256: `11EE23B8FC2FC619D6EAB6277ADB5A527261067AC01CCFC3E11857F55BF18BCD`

`MANIFEST.sha256` and `manifest.json.artifactHashes` were updated to the correct value. All seven `MANIFEST.sha256` entries were then independently recalculated (UTF-8 without BOM, CRLF -> LF) and confirmed to match their files; `manifest.json` parses via `ConvertFrom-Json`. No production code, tests, `VERSION.txt` or any file outside `MANIFEST.sha256`, `manifest.json`, `VALIDATION_REPORT.md` was touched.

## Changed files (4) — all within the 0.1.0 scope

### Production
| File | Lines (after fix) | Delta vs 0.1.0 |
|------|-------------------|----------------|
| Reminder.cs | 391 | +9 (MarkDue guard + docs) |
| ReminderOccurrence.cs | 252 | +10 (Claim guard + docs) |

### Tests
| File | Lines | Change |
|------|-------|--------|
| ReminderDomainTests.cs | 370 | timings aligned with the firing-instant invariant; new `MarkDue_IsRejectedBeforeTheScheduledInstant` |
| ReminderOccurrenceTests.cs | 254 | timings aligned with the attempt-instant invariant; new `Claim_IsRejectedBeforeTheFirstAttemptInstant` |

## Module totals (9 files, as in 0.1.0)
| File | Lines |
|------|-------|
| Reminder.cs | 391 |
| ReminderOccurrence.cs | 252 |
| ReminderOccurrenceKey.cs | 99 |
| ReminderTrigger.cs | 132 |
| ReminderStatus.cs | 16 |
| ReminderTriggerType.cs | 15 |
| ReminderOccurrenceStatus.cs | 15 |
| ReminderDomainTests.cs | 370 |
| ReminderOccurrenceTests.cs | 254 |

Coverage: creation, trigger validation, lifecycle transitions, snooze/expire windows, reschedule windows with configured trigger retention, firing/claiming instant guards, occurrence key semantics, dedup and transition rejections. Recurring series, calendar sync, notifications, persistence and REST API remain out of scope (MOD-008 split).

## Evidence boundaries (explicit out-of-scope)
- NOT delivered: `RecurrencePattern`/series, calendar sync, notifications, persistence, REST API (OpenAPI `MOD-008`), timezone inputs (UTC-only).
- No files outside the two scope directories changed; enum/key/trigger files untouched in this packet.
- The guards implement the semantic that a firing/claim happens at or after the scheduled instant (BR-045/AC-045 snooze target reached before firing; BR-046 server-side dedup precondition: no firing of the same instant twice).