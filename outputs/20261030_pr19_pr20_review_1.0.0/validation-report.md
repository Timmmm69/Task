# PR 19 / PR 20 — Windows review 1.0.0

Date: 2026-10-30 (Europe/Minsk). Result: PASS for merge, subject to the explicit verification limits below.

## Reviewed changes

PR 19: 043830f07c665d468ae9394455cb72e9f7147f7e, interactions layout and native regression. Integrated with main ccf495d5f49e8b6047b7fd4aa90803ef5ccb114f in 5afff6d40e5e6bfaf2d24b8a9455e5f6e2371c33.
PR 20: a0046eb5b749b7e03fa9d666413b3c501cf41896, ProjectsView layout. Combined tested source: 7f3feee (full commit recorded in manifest).

One conflict in ProjectsView member rows was resolved by preserving the new wrapping layout and current main's `Исключить` action label. The existing removal command and confirmation behavior remain in place. No API, ViewModel, database, theme, dependencies or business rules changed by this review.

## Windows validation

Windows 10.0.26200, SDK 10.0.400, Microsoft.WindowsDesktop.App 10.0.11, Release.

- PASS: dotnet build work/production/Task.sln -c Release. Zero errors; 11 existing deprecation/xUnit analyzer warnings outside changed code.
- PASS: dotnet test work/production/tests/Task.Desktop.Tests/Task.Desktop.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~Interactions|FullyQualifiedName~Task.Desktop.Tests.Projects". 308 passed, zero failed or skipped.
- PASS: dotnet test Task.sln -c Release --no-build --no-restore (work/production). Desktop 1922 passed; domain 851 passed, 31 skipped; service hosts 671 passed. No failed tests. The 31 PostgreSQL tests require a configured real database; this run did not execute them.
- PASS: Test-DependencyAudit.ps1, Test-ProjectBoundaries.ps1, dotnet format whitespace Task.sln --verify-no-changes --no-restore.
- PASS: Test-IndependentSecurityReview.ps1 -Configuration Release -SkipRestore -NoBuild -SkipTests; Test-ProductionSecretsTls.Contract.ps1.
- PASS: git diff --check against reviewed main for production code and dashboard (raw evidence logs preserve original whitespace). No changes under sources/.

Native WPF tests actually create windows, load the production XAML and exercise interaction create/edit/type dropdown/conflict, wide/compact arrangement, project employee and manager pickers, UUID fallback, capability changes, confirmation dialogs, files/contacts/history/related tasks. Interaction test also checks binding errors. Generated screenshots were visually inspected for wide/compact interaction layout, project detail and narrow project editor with fixed save footer.

## CI interpretation and limits

The old PR19 production job failed installing the .NET SDK (aka.ms download), before source validation. The old PR20 production job failed whitespace validation on its old base, including unrelated files. The current integrated source passes that same full formatting command locally. Old red runs are not represented as successful; fresh remote runs can have independent infrastructure results.

No live company server walkthrough, real PostgreSQL gate, container image rebuild/scan, clean-machine deployment, physical mixed-DPI/150/200% monitor matrix, screen reader or OS High Contrast acceptance was performed here. Changes are local WPF presentation only. Native UI tests and inspected raster evidence do not establish those broader acceptance gates. No product progress/status is raised.

## Evidence integrity

manifest.json lists SHA-256 for every payload file. SHA256SUMS also covers the manifest. The standalone ZIP SHA-256 is stored next to the ZIP. Source overlay records exactly the four changed production/test files. Historical author manifests are retained unchanged as historical evidence.
