param([Parameter(Mandatory)][string]$PackageDirectory, [Parameter(Mandatory)][long]$SourceCiRun)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path "$PSScriptRoot/../..").Path
$package = (Resolve-Path -LiteralPath $PackageDirectory).Path
$manifest = Get-Content -LiteralPath (Join-Path $package 'manifest.json') -Raw | ConvertFrom-Json
$version = $manifest.version
$source = $manifest.sourceRevision
$ciText = & gh run view $SourceCiRun --json headSha,status,conclusion,url,jobs
if ($LASTEXITCODE -ne 0) { throw 'Cannot read source CI.' }
$ci = $ciText | ConvertFrom-Json
if ($ci.headSha -ne $source -or $ci.status -ne 'completed' -or $ci.conclusion -ne 'success') { throw 'Source CI is not successful for this exact source commit.' }
$checks = Get-Content -LiteralPath "$PSScriptRoot/evidence/regression-release/checks.json" -Raw | ConvertFrom-Json
$coverage = Get-Content -LiteralPath "$PSScriptRoot/evidence/db-coverage-release.json" -Raw | ConvertFrom-Json
if (@($checks | Where-Object exitCode -NE 0).Count -or $coverage.runnerPassed -ne 1906 -or $coverage.runnerSkipped -ne 0 -or $coverage.runnerFailed -ne 0 -or $coverage.newlyExecutedPassed -ne 54) { throw 'Release regression incomplete.' }
& git -C $root diff --quiet $source -- work/production/src work/production/tests
if ($LASTEXITCODE -ne 0) { throw 'Application/test inputs differ from full Release regression.' }
$receipt = Join-Path $package 'receipts'
New-Item -ItemType Directory -Path $receipt -Force | Out-Null
$copies = @{
    'release-checks.json' = "$PSScriptRoot/evidence/regression-release/checks.json"
    'db-coverage.json' = "$PSScriptRoot/evidence/db-coverage-release.json"
    'security-review.json' = "$PSScriptRoot/evidence/regression-release/security-review/checks.json"
    'dependency-audit.json' = "$PSScriptRoot/evidence/regression-release/security-review/dependency-vulnerabilities.json"
    'windows-smoke.json' = "$PSScriptRoot/evidence/windows-$version/delivery-smoke.json"
    'personal-replacement.json' = "$PSScriptRoot/evidence/windows-$version/persistence/native-portable.json"
    'corporate-zip.json' = "$PSScriptRoot/evidence/corporate-$version/corporate-portable.json"
    'windows-environment.json' = "$PSScriptRoot/evidence/windows-environment.json"
    'personal-ux-provenance.json' = "$PSScriptRoot/evidence/personal-ux-provenance.json"
    'postgres16-fixture.json' = "$PSScriptRoot/evidence/fixture16/fixture.json"
    'postgres15-fixture.json' = "$PSScriptRoot/evidence/fixture15/fixture.json"
    'fixture-stop.json' = "$PSScriptRoot/evidence/fixture-stop.json"
    'personal-ux-native-baseline.json' = "$root/outputs/20261006_personal_ux_1.0.0/native/native-gate.json"
}
foreach ($name in $copies.Keys) { Copy-Item -LiteralPath $copies[$name] -Destination (Join-Path $receipt $name) }
$ciText | Set-Content -LiteralPath (Join-Path $receipt 'source-ci.json') -Encoding utf8
$ciLog = Join-Path $PSScriptRoot 'evidence/source-ci.log'
& gh run view $SourceCiRun --log > $ciLog
if ($LASTEXITCODE -ne 0) { throw 'Cannot retrieve source CI receipts.' }
Get-Content -LiteralPath $ciLog | Where-Object { $_ -match 'Production container package' -and $_ -match '\[ OK \]|Scanning task-|critical=|Container vulnerability gate passed|exporting manifest|writing image sha256|naming to docker.io/library/task-' } | Set-Content -LiteralPath (Join-Path $receipt 'container-ci.txt') -Encoding utf8
$closure = @('Task.Domain','Task.Application','Task.Desktop') | ForEach-Object {
    $path = Join-Path $root "work/production/src/$_/obj/portable.packages.lock.json"
    @{project=$_;sha256=(Get-FileHash -LiteralPath $path).Hash.ToLowerInvariant();closure=(Get-Content -LiteralPath $path -Raw | ConvertFrom-Json)}
}
$closure | ConvertTo-Json -Depth 30 | Set-Content -LiteralPath (Join-Path $receipt 'portable-dependencies.json') -Encoding utf8
$inputs = @(& git -C $root ls-files --stage -- work/production/src | ForEach-Object {
    if ($_ -notmatch '^\d+ ([0-9a-f]+) 0\t(.+)$') { throw 'Unexpected source index entry.' }
    $path = $Matches[2]
    @{path=$path;gitBlob=$Matches[1];sha256=(Get-FileHash -LiteralPath (Join-Path $root $path)).Hash.ToLowerInvariant()}
})
@{sourceCommit=$source;regressionCommit=$source;applicationAndTestsUnchangedSinceRegression=$true;sdk=(& dotnet --version).Trim();pipeline='work/production/deployment/desktop/Build-UnsignedPortableClient.ps1';command="-Version $version -RequireCleanSourceTree -OutputDirectory outputs/20261006_task_portable_release_$version";canonicalIconSha256='1500bf60892702387af3a458e2718069c2ab38a47cc9b4a8008f63df0083d93c';sourceFiles=$inputs} | ConvertTo-Json -Depth 7 | Set-Content -LiteralPath (Join-Path $receipt 'build-provenance.json') -Encoding utf8
$report = @"
# Task $version — portable delivery validation

Date: 6 October 2026, Europe/Minsk. **Overall release acceptance: BLOCKED.**
This is a verified unsigned self-contained win-x64 **candidate**, not a final
production-ready delivery. Code signing is optional for this explicitly selected
portable path; the existing signed pipeline and Corporate security remain intact.

Source commit: ``$source``. Build inputs were committed, clean before and after
publish. Full Release regression ran on the source inputs subsequently committed as this SHA; Git confirms identical
application/test files before and after publish. The container Dockerfile
was fixed before the final regression. Container contract checks are 8/8 PASS and
the complete actual container build/runtime/scan was repeated in source CI.

The artifact publication commit is separate from the source commit. Resolve it via
``git log -1 --format=%H -- outputs/20261006_task_portable_release_$version/manifest.json``.
The Git commit containing this manifest binds its exact contents and archive.
Source CI: [$SourceCiRun]($($ci.url)), exact source SHA, **SUCCESS**.
Publication CI will run on the artifact commit; its actual result is supplied in
the final delivery response and can be read from GitHub Actions for that commit.

| Gate | Actual result | Receipt / limits |
|---|---|---|
| Release restore/audit/format/boundaries/build/tests/locked restore/security review | PASS | release-checks.json; desktop 497, domain/application/infrastructure 819, service hosts 590 |
| Entire solution with real PostgreSQL 16 and 15.19 | **1906 PASS / 0 SKIP / 0 FAIL / 0 NOT RUN** | db-coverage.json: all 54 baseline DB scenarios truly passed, including PostgreSQL 15 rejection before bootstrap DDL |
| TLS/auth/credentials/secrets contract | PASS | security-review.json; existing fail-closed production TLS contract and wrong-DNS rejection; no validation bypass |
| Personal design acceptance | SCOPED PASS | 24 prior real native routes/viewports at 1200×820 / 800×480; relevant source hashes match accepted UX; current 497 desktop PASS. Full current DPI/physical monitor matrix is not newly accepted. |
| Five actual Linux container targets and runtime integration | PASS in source CI | container-ci.txt; pinned base images, apt signature verification, non-root/read-only/least privilege, real PostgreSQL/grants/startup/readiness/task-store/SIGTERM/backup-restore checks |
| Four deployable images, immutable image IDs, Trivy 0.74.0 | PASS in source CI | container-ci.txt with actual image IDs and severity counts; no suppression or weakened policy. Initial OpenSSL HIGH blocked publication; fixed packages libssl3t64/openssl are pinned to 3.0.13-0ubuntu3.16, then genuinely rescanned. |
| Unsigned self-contained win-x64 documented build | PASS | manifest.json, build-provenance.json, portable-dependencies.json; canonical Task icon; NotSigned |
| Exact handed-over ZIP: hash, inventory, extraction, EXE startup | PASS | windows-smoke.json: first-run selector, Personal, Corporate authentication window, Personal restart; extracted executable hash equals delivered inventory |
| Previous client replacement without Personal loss | PASS on current Windows host | personal-replacement.json: differing old/new EXE hashes, exact objects/search/dedupe retained, next recurrence/new backup/restart, metadata lifecycle and physical-file survival |
| Personal/Corporate separation on exact delivered EXE | PASS | isolated roots; no pre-Corporate probes, second Personal refusal, Corporate login while Personal owns DB; corporate-zip.json: real HTTPS/session/capabilities/logout, cross-mode marker isolation, native DPAPI session restore and unchanged Personal DB hash |
| Backup/restore, corruption and interrupted-restore safety | PASS in regression | production-service tests with actual SQLite/process scenarios; complete backup/restore system-dialog walkthrough of this ZIP is NOT RUN |
| Delivery composition and secrets | PASS | seven exact runtime files in ZIP; no test DB, runtime cache, password/token, key, certificate, video, source fixture or developer tool |
| Clean Windows with no installed modern .NET | **BLOCKED / NOT RUN** | developer Windows 11 host has SDK/runtime; no Get-VM, VBoxManage, vmrun or WindowsSandbox guest; self-contained build is not a guest acceptance result |
| Physical offline / firewall-isolated Personal and Corporate reconnect | **BLOCKED / NOT RUN** | no isolated Windows guest/elevated network control; host network settings unchanged; zero-probe tests are separate evidence |
| Visible Windows popup, background display and activation | **NOT RUN** | no independent visual popup observation. Durable notification/dedupe and Shell submission evidence cannot establish visibility. |

Runtime Corporate checks use real isolated PostgreSQL and loopback HTTPS with the
existing trusted developer TLS certificate. That certificate, private key and
all synthetic credentials/data stay outside this delivery. This is not acceptance
on the company's actual server. Both native replacement packages use Personal
schema 4; older-schema migration/rollback is tested by regression fixtures, not
claimed for this specific native replacement.

Temporary API/PostgreSQL processes were stopped. Automatic approval review rejected
an attempted external fixture-directory deletion; it was not performed. Remaining
local fixture files are outside the delivery and are not committed. Real Task
AppData and unrelated project/video/cache changes were preserved. Old outputs
were not overwritten. Only Task source and this candidate's evidence are published.

Remaining requirements blocking final production-ready acceptance are clean-Windows,
physical offline and visible popup acceptance; complete native system-dialog backup/
restore and current full Windows/DPI walkthrough are also unverified. No signing
certificate, secret-sharing, TLS downgrade or synthetic success is required to
close them. The package integrity PASS does not override these explicit limits.
"@
$report | Set-Content -LiteralPath (Join-Path $package 'validation-report.md') -Encoding utf8
$support = @(Get-ChildItem -LiteralPath $package -File -Recurse | Where-Object {
    $_.Name -notin @('manifest.json','PACKAGE-SHA256SUMS') -and -not $_.FullName.StartsWith((Join-Path $package 'Task') + '\')
} | Sort-Object FullName | ForEach-Object { @{path=[IO.Path]::GetRelativePath($package,$_.FullName).Replace('\','/');bytes=$_.Length;sha256=(Get-FileHash -LiteralPath $_.FullName).Hash.ToLowerInvariant()} })
$manifest | Add-Member -NotePropertyName releaseAcceptance -NotePropertyValue 'BLOCKED: clean Windows, physical offline and visible popup not accepted' -Force
$manifest | Add-Member -NotePropertyName sourceCi -NotePropertyValue @{run=$SourceCiRun;headSha=$source;conclusion='success';url=$ci.url} -Force
$manifest | Add-Member -NotePropertyName artifactPublication -NotePropertyValue @{distinctFromSource=$true;commitResolution="git log -1 --format=%H -- outputs/20261006_task_portable_release_$version/manifest.json";reason='A commit cannot embed its own SHA; Git history binds this manifest and ZIP'} -Force
$manifest | Add-Member -NotePropertyName supportingFiles -NotePropertyValue $support -Force
$manifest | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $package 'manifest.json') -Encoding utf8
@($support | ForEach-Object { "$($_.sha256)  $($_.path)" }) + "$((Get-FileHash -LiteralPath (Join-Path $package 'manifest.json')).Hash.ToLowerInvariant())  manifest.json" | Set-Content -LiteralPath (Join-Path $package 'PACKAGE-SHA256SUMS') -Encoding ascii
& "$PSScriptRoot/../production/deployment/desktop/Test-UnsignedPortableClient.ps1" -PackageDirectory $package | Out-Host
Write-Output 'PASS: finalized candidate manifest, documentation and actual acceptance receipts.'
