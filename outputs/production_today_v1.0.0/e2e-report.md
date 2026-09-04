# Today HTTPS/PostgreSQL E2E

Production API, trusted HTTPS, isolated PostgreSQL 16, schema 11.

- PASS: Login task-e2e-admin through trusted HTTPS
- PASS: Fresh current local day is empty
- PASS: Persist task with start time
- PASS: Persist task without start time
- PASS: Persist timed calendar event
- PASS: Persist all-day event
- PASS: Persist next-day exclusion probe
- PASS: Read exactly current local midnights through Calendar API
- PASS: Current day contains exactly four persisted records; tomorrow excluded
- PASS: Two timed records
- PASS: Two untimed/all-day records
- PASS: Login task-e2e-reader through trusted HTTPS
- PASS: Read-only account can read calendar (Calendar.Read maps to task.read)
