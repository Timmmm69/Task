# Task product continuation

## Mandatory startup in a new Codex chat

1. Activate this repository in Serena and read the Serena manual.
2. Read `AGENTS.md`, this file, and `work/delegation/README.md`.
3. Run `git fetch origin --prune`, inspect the current branch/status, and compare `HEAD` with `origin/main` before any change.
4. Do not modify `sources/`. Work only under `work/`; final packaged artifacts belong under `outputs/`.

## Verified Git state at this handoff

- Active branch: `codex/stage1-gap-consolidation`.
- `origin/main`: `17f5926b00c890c6ec9da0999cd0bce1370061b5`.
- The branch is clean and contains these unpublished commits above `origin/main`:
  - `eb801a3` — integrate all Wave A/B/C gap overrides into the executable Stage 1 matrix;
  - `404eb8f` — add syncable entity identity/version/audit/lifecycle metadata and domain tests;
  - `b3fcbe9` — integrate delegated gap verification, unresolved-gap analysis, Desktop VM tests, and service-host lifecycle tests.
- Do not restart this work from `main` or recreate these changes. Continue from the active branch and rebase only after checking for new upstream changes and conflicts.

## Production solution

- Solution: `work/production/Task.sln`, .NET 10.
- Projects: Task.Domain, Task.Application, Task.Infrastructure, Task.Api, Task.Worker, Task.BackupAgent, Task.Desktop, Task.Tests, Task.ServiceHosts.Tests, Task.Desktop.Tests.
- API foundation: correlation middleware, `/health/live`, honest unready `/health/ready`, Windows Service hosting, black-box smoke test.
- Worker and BackupAgent: Windows-service-capable hosts with bounded lifecycle tests and logging assertions.
- Desktop: Russian WPF MVVM shell with 11 canonical routes and accessibility automation labels.
- Domain: `SyncableEntityMetadata` implements immutable identity, organization boundary, UTC audit fields, monotonic versioning, archive/trash/restore transitions.

## Stage 1 executable baseline

- Directory: `work/production_stage_1_baseline/traceability`.
- `build_implementation_matrix.py` consumes all three Wave matrices plus all three gap override files.
- Generated matrix: 3,968 rows; 3,409 API rows; 314 `no_api`; 245 unresolved `gap`; 0 unknown OpenAPI operations.
- Gap overrides: 1,246 rows total; 1,001 resolved; 245 unresolved.
- Independent verification: `work/production_stage_1_baseline/verification/Test-GapOverrides.ps1`.
- Deterministic analysis: `work/production_stage_1_baseline/analysis/analyze_unresolved_gaps.py` and `UNRESOLVED_GAPS_REPORT.md`.
- Unresolved distribution: Wave A=110, Wave B=64, Wave C=71.
- Stage 1 is NOT complete. The report classifies remaining gaps but resolves none of them.

## Last verified checks

- `dotnet restore work/production/Task.sln` — PASS.
- `dotnet build work/production/Task.sln --no-restore` — PASS, 0 warnings, 0 errors.
- `dotnet test work/production/Task.sln --no-build --no-restore` — PASS, 15/15 tests:
  - Task.Tests: 5;
  - Task.ServiceHosts.Tests: 4;
  - Task.Desktop.Tests: 6.
- `work/production/verification/Test-ProjectBoundaries.ps1` — PASS.
- `work/production/verification/Test-TaskApi.ps1` — PASS.
- `work/production_stage_1_baseline/verification/Test-GapOverrides.ps1` — PASS; OpenAPI 244 operations; resolved=1001; unresolved=245.
- Unresolved report generation is deterministic; current SHA-256: `13413EFD08E51D4047BB0B0470D61F644ACAB2BBFD7B76E5A06E81888907759E`.

## Original main worktree caution

The registered worktree `C:\Users\novik\Таск` still contains untracked helper files and untracked copies produced by manual DeepSeek chats. They were not deleted or modified during integration. Do not remove them without an explicit scope check. The reviewed delegated source files are now committed on `codex/stage1-gap-consolidation`; `bin/obj` copies in the original worktree are build outputs only.

## Next objective

Continue the production foundation with one deliberate, independently testable increment derived from the canonical architecture. The recommended next Codex-owned increment is the Task aggregate lifecycle and optimistic-concurrency boundary, grounded in `sources/stage_1/architecture_organizer.md` sections 3.11, 6.4, 11.1, 12.1, 12.3, 12.11, and 12.12. Keep API/DTO, persistence/migrations, authorization, sync, deployment, and backup decisions out of that increment unless separately reviewed.

Before implementing it:

1. review `SyncableEntityMetadata` and its tests;
2. inspect the exact Task lifecycle/status rules in the canonical sources and implementation matrix;
3. define the smallest domain-only behavior with explicit acceptance tests;
4. independently identify any non-overlapping L/M verification or UI-test work suitable for DeepSeek and provide complete Russian `DELEGATION_PACKET` blocks.
