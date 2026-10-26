# PR #11 review — 1.0.0

Reviewed 2026-10-26 on Windows, .NET SDK 10.0.400.
PR: https://github.com/Timmmm69/Task/pull/11
Base: 3fe76562b3c51fee3e36b933b169b101376afb47
Head: 48ee9a0f1fb8d05b102ac4895ef658658ea156b2
Merged: 285f764ae51736d30defa9171aae531d0bbbb657

## Result

No concrete source defects found. Confirmation defaults to cancellation without a handler. MainWindow supplies the existing modal dialog. Archive and member removal capture their target, then recheck selection, DTO identity/version, permissions, section state, connectivity, session and generation before the API call. Cancel/Esc/window close cannot mutate server data. AsyncCommand prevents duplicate invocation. The owner cannot be removed; account UUID remains available without User.Read. No business, API, database or permission contract changes.

The PR was merged after the following local checks against current main plus PR:

- Release solution build: PASS, 0 errors, 11 existing analyzer/deprecation warnings.
- Projects: 301/301 PASS, including native WPF confirmation, cancellation, Esc, close, keyboard focus and UI Automation.
- Final sequential full desktop regression: 1917/1917 PASS, 0 skipped.
- Service hosts: 671/671 PASS.
- Dependency audit, project boundaries, independent static security review and secrets/TLS contract: PASS.
- git diff --check and original PR archive SHA-256: PASS.
- Fetched merged production source/test files exactly match the locally tested files.

## Baseline failures and limits

Full CI is not green. Both clean origin/main and main plus PR report exactly the same 672 normalized WHITESPACE diagnostics. The scoped check reports two inherited lines in ProjectDetailSectionsTests.cs, outside the edited line. GitHub CI for the PR stopped at formatting before build/tests.

Without TASK_POSTGRES_TEST_ADMIN_CONNECTION, Task.Tests has 851 PASS / 33 FAIL / 22 SKIP on both clean main and the merged tree. The failing test-name sets are identical. No real PostgreSQL or container runtime gate was executed in this desktop-only review.

Initial full desktop run: 1916 PASS / 1 FAIL in TaskContextActionsUiTests.NativeWindows_MouseKeyboardFocusRelationsAndSmallWindow (focus assertion). An isolated repeat failed at a different mouse/editor timeout; a later isolated repeat passed and the final full sequential run passed 1917/1917. Clean-main isolated checks passed four times. Failed receipts are retained; this does not establish a deterministic baseline failure or a source regression. The former CommandPalette debounce failure did not recur.

## Reproduction

From work/production:

    dotnet build Task.sln --configuration Release
    dotnet test tests/Task.Desktop.Tests/Task.Desktop.Tests.csproj --configuration Release --no-build --no-restore --filter FullyQualifiedName~Projects
    dotnet test Task.sln --configuration Release --no-build --no-restore
    dotnet test tests/Task.Desktop.Tests/Task.Desktop.Tests.csproj --configuration Release --no-build --no-restore
    dotnet format whitespace Task.sln --verify-no-changes --no-restore
    powershell -ExecutionPolicy Bypass -File verification/Test-DependencyAudit.ps1
    powershell -ExecutionPolicy Bypass -File verification/Test-ProjectBoundaries.ps1
    powershell -ExecutionPolicy Bypass -File verification/Test-IndependentSecurityReview.ps1 -Configuration Release -SkipRestore -NoBuild -SkipTests -EvidenceDirectory <evidence>/security
    pwsh -ExecutionPolicy Bypass -File verification/Test-ProductionSecretsTls.Contract.ps1 -EvidenceDirectory <evidence>/tls

TRX and logs in the local evidence package retain exact counters and failed receipts. source-manifest.json records SHA-256 for the merged production changes. manifest.json records SHA-256 for every evidence file except itself; manifest.sha256 verifies the manifest.
