# Validation report — SEC-04 dependency and security hardening 1.0.0

Date: 2026-09-11

Base revision: `8652264b187efcdece91f8ba6e184e44516a16a2`.

Validated implementation revision: `c4b88dfd904cf46a28595710f303b748dddae3be`.

Dashboard evidence revision: `b814f07`.

## Result

PASS. Production CI now performs a fail-closed NuGet vulnerability audit for direct and transitive dependencies. NU1901/NU1902 remain visible warnings; NU1900, NU1903, NU1904, and NU1905 block the pipeline. A separate scheduled workflow runs every Monday at 04:23 UTC and can also be dispatched manually. Its Windows dependency/security/TLS job and Ubuntu production-container job are independent.

No vulnerability suppressions were added. Existing non-root, read-only filesystem, dropped-capability, secret-metadata, runtime-only-image, and related container checks remain in the existing production container gate.

## Executed checks

| Check | Result |
|---|---|
| Local direct/transitive dependency audit with live NuGet vulnerability data | PASS |
| Production whitespace verification | PASS |
| Production project-boundary verification | PASS |
| Release build | PASS — 0 errors; 8 pre-existing analyzer warnings |
| Production secrets/TLS contract and negative DNS scenario | PASS |
| Linux locked-mode restore for all five container targets | PASS |
| GitHub Actions Security hardening workflow_dispatch | PASS — run 34577240447 |
| GitHub Actions full CI | PASS — run 34578360556 |
| Real PostgreSQL 16 integration job in full CI | PASS |
| Production container package job in full CI | PASS |
| Dashboard order and validation | PASS — 40 items, overall 89.55%, handoff_ready false |
| Diff whitespace check | PASS |

Hosted evidence:

- Security hardening: https://github.com/Timmmm69/Task/actions/runs/34577240447
- Full CI: https://github.com/Timmmm69/Task/actions/runs/34578360556

## Remedial compatibility work

Hosted execution exposed stale Application/Domain lockfiles, accumulated whitespace-gate failures, CRLF-sensitive deployment-contract reads, and one Windows PowerShell 5.1 invocation of a contract requiring modern PowerShell. These prerequisites were corrected without weakening any security policy. All corrections are included in the revision chain recorded in `manifest.json`.

Docker was unavailable locally. Container verification is therefore supported by the successful Ubuntu GitHub-hosted jobs above; the equivalent Linux locked-mode restores were also executed locally.

## Reproduction

```powershell
Set-Location work/production
pwsh -NoProfile -File ./verification/Test-DependencyAudit.ps1
dotnet format whitespace Task.sln --verify-no-changes --no-restore
powershell -NoProfile -ExecutionPolicy Bypass -File ./verification/Test-ProjectBoundaries.ps1
dotnet build Task.sln -c Release --no-restore
pwsh -NoProfile -File ./verification/Test-ProductionSecretsTls.Contract.ps1
Set-Location ../..
npm run dashboard:order
npm run dashboard:validate
git diff --check
```
