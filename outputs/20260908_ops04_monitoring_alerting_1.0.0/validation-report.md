# OPS-04 validation report — 1.0.0

Result: SOURCE/CONFIGURATION/SYNTHETIC DELIVERY PASS. Base commit: b4b77634c9a1bb2a100b085054d4564edaca2b31.

## Verified

| Assembly | Passed / total | Skipped |
|---|---:|---:|
| Task.Desktop.Tests | 269/269 | 0 |
| Task.ServiceHosts.Tests | 566/566 | 0 |
| Task.Tests | 792/796 | 4 |

Full Release solution total: 1627 passed, 4 environment-dependent PostgreSQL tests
skipped, zero failures. The focused monitoring suite passed 4/4. The acceptance gate parsed seven
YAML documents, required 19 owned alert rules and ten scrape jobs, started the real alert router in
production mode against a local receiver, injected a failure, verified bearer delivery and proved
that non-allowlisted secret fields were absent from the forwarded body and durable hash receipt.

Docker Compose configuration parsing passed for the monitoring stack and for both merged
production/security and backup overlays. Python bytecode compilation and POSIX shell syntax checks
passed. The implementation covers API/readiness, PostgreSQL and connection capacity, host disk,
four background loops, backup/check/restore freshness, mounted backup capacity, Prometheus,
Alertmanager, blackbox and delivery-path failures. Metrics and alert payloads use bounded labels;
customer URLs, credentials and personal contacts are excluded from the repository and package.

## Production boundary

This package proves the source contract and a real local HTTP delivery path. It does not claim that
customer infrastructure, immutable image digests, the company HTTPS receiver, on-call ownership or
retention have been configured. Deployment must replace every image placeholder with an approved
linux/amd64 digest, set the real `TASK_ALERT_OWNER`, place the HTTPS URL/token in protected external
files, and retain customer-side firing and resolved receipts. Native promtool and live target
scrapes remain deployment checks because the repository intentionally contains no vendor image
digests or customer environment.
