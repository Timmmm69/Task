# Validation report — SEC-04 container CVE hardening 1.1.0

Date: 2026-09-11

Base revision: `40b48ad9d344052232e25c55e9b46ad0ec0e6cd8`.

Validated implementation revision: `5be20916b343a2570cfbe332a0a3b63698a26b41`.

## Result

PASS. SEC-04 is complete at 100%. Production CI and the independent weekly/manual security workflow build the existing production package and scan every deployable image from its immutable local `sha256:` image ID with Trivy 0.74.0.

The gate blocks all CRITICAL vulnerabilities, fixable HIGH vulnerabilities, EOL operating systems, incomplete or mutable image maps, missing local images, and Trivy/database failures. Unfixed HIGH vulnerabilities are retained as warnings and machine-readable JSON evidence. The validation-only image is intentionally excluded because it is not deployable. No `.trivyignore`, `--ignore-unfixed`, suppression, or remote-registry fallback was added.

## Hosted validation

| Workflow / check | Result |
|---|---|
| Security hardening — Windows independent review and TLS contract | PASS |
| Security hardening — production packaging and Trivy scan | PASS |
| Full CI — cheap security gates | PASS |
| Full CI — real PostgreSQL 16 integration tests | PASS |
| Full CI — production container package and Trivy scan | PASS |

Hosted evidence:

- Security hardening run 34588448020: https://github.com/Timmmm69/Task/actions/runs/34588448020
- Full CI run 34588430981: https://github.com/Timmmm69/Task/actions/runs/34588430981

## Scanned deployable images

| Target | Immutable local image ID | CRITICAL | Fixable HIGH | Unfixed HIGH | EOL |
|---|---|---:|---:|---:|---|
| task-api | `sha256:160886d617e2157c21ada331b4f43f203239109b92ff8dbac789cb9ec59860b6` | 0 | 0 | 0 | false |
| task-worker | `sha256:9b9218a0df4127b116d786194006338613857de181cd0ae79b14267a7f8ab1cd` | 0 | 0 | 0 | false |
| task-backup-agent | `sha256:96c1173d1da5417c2001cd9267edff718cbdb3ada4f13570f73c3947bb2b521f` | 0 | 0 | 0 | false |
| task-database-migrator | `sha256:fb1bb109477e4ad5d5bad6b875d8f5686987572fbcd32eb8f6b6b43b2d146298` | 0 | 0 | 0 | false |

## Local validation

| Check | Result |
|---|---|
| PowerShell AST parse for all three changed verification scripts | PASS |
| Workflow YAML parse | PASS |
| Full independent security review under Windows PowerShell 5.1 | PASS |
| CI review mode (`-SkipRestore -NoBuild -SkipTests`) and evidence semantics | PASS |
| Scanner fail-closed cases: absent/mutable/incomplete map, absent image, scanner/DB failure | PASS |
| Scanner policy cases: EOL, CRITICAL fixed/unfixed, fixable HIGH | PASS — rejected as required |
| Unfixed HIGH policy | PASS — warning and JSON evidence, non-blocking |
| Validation-only image exclusion | PASS |
| Dashboard order and schema validation | PASS — 40 items, overall 89.55%, handoff_ready false |
| Scoped diff whitespace check | PASS |

Docker and Trivy were unavailable on the local workstation. Actual image builds and vulnerability scans were therefore validated on GitHub-hosted Ubuntu runners in both workflows above. The scanner policy branches were exercised locally with isolated command shims.

## Remediation during hosted validation

The first hosted run exposed a CRLF-sensitive regular expression in the independent compose-file review on Windows. Revision `5be20916b343a2570cfbe332a0a3b63698a26b41` normalizes the inspected content before matching. The security policy was not weakened. Both final hosted workflows passed after the correction.

## Reproduction

```powershell
Set-Location work/production
pwsh -NoProfile -File ./verification/Test-IndependentSecurityReview.ps1 -Configuration Release
pwsh -NoProfile -File ./verification/Test-ProductionSecretsTls.Contract.ps1
Set-Location ../..
npm run dashboard:order
npm run dashboard:validate
git diff --check
```

The container gate itself requires an Ubuntu runner with Docker and Trivy 0.74.0 or newer. CI first runs `Test-ContainerPackaging.ps1 -ImageScanMapPath <path>` and then passes that map to `Test-ContainerVulnerabilities.ps1`.
