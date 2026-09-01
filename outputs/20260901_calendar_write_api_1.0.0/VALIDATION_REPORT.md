# Validation report — Calendar write API 1.0.0

Дата проверки: 2026-09-01

## Итог

PASS. CalendarEvent create/update/lifecycle endpoints реализованы, защищены отдельными capabilities и проверены на optimistic concurrency, tenant isolation, строгую JSON-валидацию и регрессию полного solution.

## Проверки

| Проверка | Результат |
|---|---|
| Targeted Calendar HTTP contract tests | PASS — 33/33 |
| Full `dotnet test Task.sln -c Release --no-build --no-restore` | PASS — 1276 passed, 2 existing Task command PostgreSQL tests skipped |
| `dotnet build Task.sln -c Release --no-restore` | PASS — 0 errors; 8 pre-existing analyzer/deprecation warnings |
| `dotnet format Task.sln whitespace --no-restore --verify-no-changes` | PASS |
| `git diff --check` | PASS |
| Permission/capability regression | PASS — session projection includes CalendarEvent.Create/Update/Delete |
| Migration history contract | PASS — expected migration version 7 |
| Optimistic concurrency | PASS — strong If-Match, 428 missing, 412 stale, current ETag on success |
| Multi-field PATCH atomicity | PASS — all writable fields applied with one version increment |
| Lifecycle flow | PASS — archive v2, unarchive v3, trash v4, restore v5 |
| Tenant boundary | PASS — cross-organization mutation returns OBJECT_NOT_VISIBLE |

## Примечания

Два skip полного solution gate существовали до данного изменения: `TaskUpdateTests.RealPostgres_PatchIsAtomicDurableAndConcurrencySafe` и `TaskCreateCommandTests.RealPostgres_CreateIsAtomicDurableAndReadable`. Они не относятся к CalendarEvent write scope. CalendarEvent persistence/concurrency tests в `Task.Tests` прошли.

Полный analyzer gate `dotnet format Task.sln --no-restore --verify-no-changes` остаётся красным только из-за существующего `xUnit1031` в `DesktopCredentialVaultTests.cs:347`; whitespace gate и весь изменённый scope чисты.
