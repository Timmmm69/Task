# Calendar production E2E

Environment: isolated PostgreSQL 16, schema 8, production HTTPS API, real administrator and read-only sessions.

- PASS: Login task-e2e-admin
- PASS: Administrator has recurrence capability
- PASS: Create series and occurrences transaction (HTTP 201)
- PASS: Same-key creation replays the same series
- PASS: Changed payload with reused key is rejected
- PASS: Exactly four persisted tasks generated for count limit
- PASS: Occurrence retains full template snapshot
- PASS: Replay adds no domain events
- PASS: Month-sized calendar range reads production data
- PASS: Calendar marks every generated task as recurring
- PASS: Single-occurrence edit succeeds with task and series preconditions
- PASS: Scoped change replays before stale-version validation
- PASS: Stale series version is rejected
- PASS: Whole-series template edit succeeds
- PASS: Single-instance exception survives whole-series editing
- PASS: Remaining instances receive the template change
- PASS: Pause stops generation
- PASS: Resume restores generation
- PASS: Mutually exclusive termination modes are rejected
- PASS: Login task-e2e-reader
- PASS: Read-only account cannot write recurrence series
- PASS: Unknown series fails closed
- PASS: Event with attendee is created
- PASS: Omitted attendee array preserves membership
- PASS: Explicit empty array removes membership
