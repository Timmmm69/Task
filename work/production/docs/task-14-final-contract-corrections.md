# Task 14 final contract corrections

Version: 1.0.0. Decision: DEC-14-CONTRACT-002, 2026-10-22 (Europe/Minsk).

The user requested correction of all errors reported by the pre-main review,
followed by publication of 14.1–14.4 to main. This authorizes the technical
corrections below. Business transitions, permissions and user workflows do not
change. Immutable `sources/` and historical audit packages remain unchanged.

## HistoryEntry: API-14.4-07

Task/project history now includes all seven required normative fields: id,
objectId, objectVersion, changedAt (UTC Z), changeType, changedFields and
correlationId. They come from the committed, tenant/object-scoped domain event.
changeType uses the canonical object_history categories (created, updated,
state_changed, archived, restored, trashed, purged). TaskStatusChanged maps to
state_changed; its original event name remains available as action.

Existing WPF clients keep action/version/occurredAt/actorUserId. Removing those
fields would break project history and lose its actor display. They are explicit
properties in the amended schema, with additionalProperties=false; version and
occurredAt are compatibility aliases. This is a documented backward-compatible
contract amendment, not a claim that the unamended historical schema accepts
extra fields. No event payload, private values or reason text is exposed.

The two implemented history routes use page 1..10000 and limit 1..200 (default
50). The runtime contract declares those parameters instead of advertising an
unsupported cursor or ETag. Other API routes are outside this amendment.

## Transition errors: DOC-14.4-08

The operation's If-Match failure is VERSION_CONFLICT=412, consistent with its
existing OpenAPI response and runtime. The derived operation-specific catalog
supersedes the historical CSV's 409 for this operation only. It also declares
ACCOUNT_BLOCKED=423, INTERNAL_ERROR=503 for storage unavailability (500 remains
possible for an invalid internal result), and
IDEMPOTENCY_REQUEST_IN_PROGRESS=409 with Retry-After. No HTTP behavior or stable
error code was changed to match an obsolete catalog.

The existing retryable boolean is declared in ProblemDetails. The shared problem
writer now emits the required fieldErrors array (empty for non-field errors),
instead of omitting it. Session invalidation,
idempotency and unavailable-storage responses are schema-validated against real
HTTP test responses, including their status/code pairs.

## Reproduction and scope

`verification/Build-Task14Contract.py` derives openapi.json and an operation-specific
error CSV from pinned Stage 2.3 OpenAPI SHA-256
36c15dff5adba0041fcfd79f5a0d203835dac5cdd4ad24122bcd92177c13220d.
It never writes to sources. Generated files belong in the final outputs package.
The script requires PyYAML 6.0.3; validation uses jsonschema 4.25.1 and
rfc3339-validator 0.1.4. Set TASK_RETURN_CONTRACT_EVIDENCE to an empty work directory
when running the return/history HTTP and PostgreSQL tests, then invoke:

```powershell
python work/production/verification/Build-Task14Contract.py --out <output-contract-directory> --samples <test-evidence-directory>
```

This closes the two reported contract defects for the named routes. It does not
certify every endpoint in the inherited full OpenAPI, live LAN acceptance,
physical mixed-DPI setups or screen-reader behavior. Those manual acceptance
activities are not source-code defects and remain separately disclosed.
