# Validation report — TASK-PROD-MOD-007-RECURRENCE-DOMAIN-0.1.0

Executed locally on 2026-08-16 (Windows, .NET SDK 10.0.400), worktree `main` at
base `7708ada2bee16b666a9b8cc42ad4413e296f8e04`.

## Checks

1. `dotnet test work/production/tests/Task.Tests/Task.Tests.csproj` — **passed**:
   257 tests, 0 failed, 0 skipped (83 of them are the new `Task.Tests.Recurrence` suite).
2. `dotnet build work/production/Task.sln` — **passed**: 0 warnings, 0 errors.
3. `git diff --check` — **clean**.

## Deliverable

Isolated domain model MOD-007 «Повторяющиеся задачи» in
`work/production/src/Task.Domain/Recurrence` (15 files) with tests in
`work/production/tests/Task.Tests/Recurrence` (7 files). No NuGet dependencies,
no migrations, no API endpoints; `sources/**` untouched; no existing file was
modified, renamed, or deleted (all new paths are untracked additions).

## Contract mapping

- BR-040 / AC-040 — series time zone validated via `TimeZoneInfo`; at most one
  termination mode (`untilDate` XOR `maxOccurrences`); interval 1..999,
  weekdays 1..7 distinct, month days -31..31 distinct (zero rejected),
  month of year 1..12; cross-frequency grammar per concept §11.6
  (workdays = daily + weekdays; weekly/monthly/yearly combinations).
- BR-041 / AC-041 — deterministic occurrence key (ISO-8601 local date,
  architecture §13.5 "unique occurrence key series+date"); regeneration is
  idempotent via `RecurrenceGenerator.GenerateMissing` (dedupe by key).
- BR-042 / AC-042 — explicit `RecurrenceChangeScope` (this_occurrence /
  this_and_future / entire_series per `RecurrenceScopedChange.scope`); the
  default enum value is rejected; `RecurrenceScopeFilter.Select` resolves the
  affected occurrences deterministically.
- BR-043 / AC-043 — `RecurrenceSeries.Cancel` sets status `cancelled` without
  trash/archive lifecycle metadata (verified in tests).
- OpenAPI DTO/enum names are mirrored 1:1 (`RecurrenceSeries`,
  `RecurrenceOccurrence`, `RecurrenceSeriesCreate/Patch` fields,
  `RecurrenceTaskTemplate` and nested checklist/reminder schemas,
  `RecurrenceSeries.status`, `RecurrenceOccurrence.status`,
  `GenerationSummary` semantics).

## Deliberate domain decisions (documented in XML docs)

- `RecurrenceSeries.Create` accepts only `active`/`paused`; `completed`/
  `cancelled` are reachable only through transitions. `completed` reconstitution
  requires a termination mode.
- Weekly rules require at least one weekday; monthly/yearly rules require at
  least one month day; yearly rules require a month of year. These combinations
  correspond to the concept §11.6 supported patterns; the OpenAPI field bounds
  remain the outer contract.
- Weekly interval counts in 7-day blocks anchored at the occurrence start date;
  month days that do not exist in a month (e.g., 31 in February) are skipped.
- `RecurrenceGenerator.MaxOccurrencesPerWindow = 500` aligns with the OpenAPI
  preview limit and `RecurrenceChangeResult.changedTaskIds` bound (architecture
  §13.5 / recurrence complexity limit).
- `Resume` restores a `paused` series to `active` (AC-412); a `cancelled` or
  `completed` series is terminal.

## Out of scope (explicitly not covered by this packet)

- API layer, DTO classes, infrastructure persistence, WPF desktop UI, worker
  horizon jobs (`Task.Api`, `Task.Infrastructure`, `Task.Desktop`,
  `Task.Worker`, `Task.Application` untouched).
- Occurrence-to-task materialization rules beyond `MarkGenerated(taskId)`;
  reminder scheduling; sync/audit events; error-code mapping
  (`RECURRENCE_RULE_INVALID` etc. belong to the API boundary).

## Package integrity verification

`MANIFEST.sha256` lists SHA-256 for all 22 new source files and the three
package files (`VERSION.txt`, `manifest.json`, `VALIDATION_REPORT.md`).
Re-verification was executed after packaging:

- All 25 recorded hashes match the files on disk.
- `manifest.json` parses as valid JSON and its `artifactHashes` agree with
  `MANIFEST.sha256`.
- `git status` shows only new untracked `Recurrence` directories under
  `work/production` and the new package directory under `outputs`.
