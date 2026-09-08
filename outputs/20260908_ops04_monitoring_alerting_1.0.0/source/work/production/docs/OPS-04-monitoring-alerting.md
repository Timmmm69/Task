# OPS-04 — monitoring and alerting runbook

## Outcome and ownership

The Task operations owner is accountable for API, PostgreSQL, host capacity, background workers,
backup protection and the monitoring path itself. `TASK_ALERT_OWNER` must name the real company
team or on-call rotation before production acceptance. The repository never contains a personal
address, webhook credential or customer infrastructure identifier.

Prometheus evaluates bounded rules, Alertmanager groups/retries notifications, and the Task alert
router delivers a redacted allowlisted payload to the customer HTTPS webhook. The router stores a
hash-only receipt without the target URL, token, original payload or secret annotations. In `test`
mode it is a local receipt sink; `TaskAlertRouteNotProduction` remains active until the customer
route is configured.

## Signals and thresholds

| Area | Signal | Alert threshold |
|---|---|---|
| API | readiness probe, bounded HTTP status counters/duration | readiness fails 2m; sustained 5xx rate >0.1/s for 5m |
| PostgreSQL | TLS exporter with dedicated `pg_monitor` login | scrape fails 2m; connections >80% for 10m |
| Host | node exporter filesystem capacity | <15% warning for 15m; <5% critical for 5m |
| Workers | protected textfile heartbeat, per-loop last success and failure count | snapshot >60s; pass older than loop budget; any failure in 10m |
| Backup | enabled/failure, last base/check/restore drill, three filesystem capacities | base >26h; check >10m; drill >8d; any failure/disabled |
| Alerting | Prometheus, Alertmanager, blackbox and router health; router mode and delivery counters | any target down; any delivery failure; test mode after 15m |

API metrics require a 32-byte bearer token read from a mounted file. PostgreSQL monitoring uses an
independent password file and `pg_monitor`; it receives no application or migration credential.
Prometheus binds to loopback by default. No database, exporter, Alertmanager, worker or router port
is published to the LAN. Container logs use the local JSON driver with 10 MiB × 5 rotation and are
queried with `docker compose logs`; the alert signals mirror every operational failure class needed
for detection, while detailed protected backup journals remain under the backup policy.

## Deployment

1. Copy `ops04.parameters.example.env`, replace every image with an approved immutable
   `linux/amd64` digest, set the actual owner and keep the file outside the repository.
2. With PostgreSQL already running, execute `Bootstrap-Ops04Monitoring.sh` with
   `OPS04_POSTGRES_CONTAINER` and `TASK_SECRET_ROOT`. It creates independent random metrics and
   database credentials, assigns their files to `TASK_MONITORING_GID` with mode 0640 and
   idempotently grants only `CONNECT` plus `pg_monitor`.
3. Start `deployment/monitoring/compose.yaml` first so the internal `task-monitoring` network exists.
4. Recreate the production stack with both `deployment/security/compose.production.yaml` and
   `deployment/monitoring/compose.production.yaml`; API and PostgreSQL join the internal monitoring
   network and the worker writes to `task-worker-metrics`. The base OPS-02 file remains independently
   deployable for recovery and clean-room validation.
5. Recreate the backup stack with both `deployment/backup/compose.yaml` and
   `deployment/monitoring/compose.backup.yaml`; the agent writes the bounded backup snapshot to
   `task-backup-metrics` without gaining network access.
6. Confirm all Prometheus targets are up and no alert other than `TaskAlertRouteNotProduction` is
   firing during the initial test-mode observation window.

## Configure customer contact

Write the approved HTTPS endpoint to
`$TASK_SECRET_ROOT/monitoring/alert-webhook-url` and, only if required, its bearer credential to
`alert-webhook-token`; both files must be root-owned, assigned to `TASK_MONITORING_GID`, mode 0640,
and readable only by deployment administrators and the explicitly enrolled container processes.
Set `TASK_ALERT_DELIVERY_MODE=production`, set the named owner/rotation, recreate `alert-router`, and
send a synthetic `TaskApiUnavailable` alert. Retain the receiver-side timestamp/ticket ID and the
matching hash receipt. Do not copy URLs, tokens or full payloads into evidence packages.

## Response procedures

### API or database unavailable

Check `/health/ready`, the TLS proxy and PostgreSQL exporter. Correlate the API correlation ID with
rotated container logs. If schema readiness, TLS or database availability is not current, stop
writes and follow OPS-02/SEC-03 recovery; do not bypass TLS or use the migration credential.

### API server errors

Use the alert window and correlation IDs to isolate the failing operation. Preserve the bounded log
slice, classify the dependency, and rollback the last deployment if errors began after release.

### Worker failure

The `worker` label identifies `sessions.expired-purge`, `recurrence.horizon`, `outbox.publish` or
`reminders.dispatch`. Inspect the matching error log and PostgreSQL readiness. Durable leases and
backoff permit a safe restart; never edit queues directly.

### Backup protection

Treat disabled, failed, missing or overdue backup signals as RPO risk. Follow
`backup-restore-runbook.md`, inspect the protected journal, restore free capacity if needed and run
the required check/verification. Never delete WAL to silence a capacity alert.

### Capacity

Identify the filesystem/location label, stop nonessential growth, preserve WAL and backup data, and
extend or replace the correct approved storage. Do not confuse container writable-layer space with
the separate local/off-host backup media.

### Monitoring self-failure

Use local container state and logs because the normal path is impaired. Restore Prometheus,
exporters, Alertmanager and router in that order, then send a synthetic firing and resolved pair.

### Alert delivery

Confirm the customer receiver is reachable over HTTPS and the secret files are present and mode
0600. Rotate a rejected credential through the customer process. Alertmanager retries non-2xx
responses; never switch to test mode merely to clear the alert.

## Acceptance and evidence

Run `python work/production/verification/ops04-acceptance.py`. The gate parses every YAML file,
requires all 19 alerts and ten scrape jobs, starts the real router in production mode against a
local receiver, injects a failure, verifies bearer delivery and proves unallowlisted secret fields
are absent from both the forwarded body and receipt. Run the targeted .NET monitoring tests and the
full Release solution gate. Customer acceptance additionally records one firing and one resolved
receipt at the approved receiver after deployment.
