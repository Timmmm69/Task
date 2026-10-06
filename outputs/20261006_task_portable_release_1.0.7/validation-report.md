# Task 1.0.7 — portable delivery validation

Date: 6 October 2026, Europe/Minsk. **Overall release acceptance: BLOCKED.**
This is a verified unsigned self-contained win-x64 **candidate**, not a final
production-ready delivery. Code signing is optional for this explicitly selected
portable path; the existing signed pipeline and Corporate security remain intact.

Source commit: `4e679712e4cd4d8205fcb3923f72906ea17d4d81`. Build inputs were committed, clean before and after
publish. Full Release regression ran on the source inputs subsequently committed as this SHA; Git confirms identical
application/test files before and after publish. The container Dockerfile
was fixed before the final regression. Container contract checks are 8/8 PASS and
the complete actual container build/runtime/scan was repeated in source CI.

The artifact publication commit is separate from the source commit. Resolve it via
`git log -1 --format=%H -- outputs/20261006_task_portable_release_1.0.7/manifest.json`.
The Git commit containing this manifest binds its exact contents and archive.
Source CI: [37514135326](https://github.com/Timmmm69/Task/actions/runs/37514135326), exact source SHA, **SUCCESS**.
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
