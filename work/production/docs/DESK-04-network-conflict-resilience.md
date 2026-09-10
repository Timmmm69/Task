# DESK-04 — network and conflict resilience

Status: implemented and regression-tested on 2026-09-10.

## Runtime contract

The desktop process owns one `DesktopConnectivityService` shared by every authenticated API
client. A transport failure moves it to `ServerUnavailable`; the next explicit read/retry moves it
to `Reconnecting`; any complete HTTP response (including a typed authorization or validation
response) confirms `Online`. Caller cancellation does not claim that the server is unavailable.

The shell renders the actual state instead of inferring connectivity from the configured URL.
While the state is not `Online`:

- all server mutation commands in Tasks, Calendar, Projects, Catalog, Contacts, Notifications,
  Archive, Trash and Settings are disabled;
- confirmed in-memory lists remain visible with their last-successful-refresh timestamp;
- local file opening remains available and is still governed by Windows/SMB checks;
- open editors and settings fields remain in memory and are explicitly marked as unsent;
- no offline command queue, implicit resend or last-write-wins merge is created.

The selected section's existing Refresh command is the explicit reconnect action. A successful
response restores write commands according to the current session and capability set. Drafts are
never submitted by that refresh.

## Session expiry

`SessionService` still owns refresh single-flight and terminal sign-out. The application now
propagates session/capability changes to Projects as well as Tasks, Calendar, Today and WorkHub.
Terminal session loss disables every mutation. Confirmed calendar data is cleared, but an open
calendar editor is retained and labelled as unsent; task, project and settings drafts are likewise
retained by their view models.

## Conflict recovery

- Tasks retain the local draft, expose the established conflict/reload flow and reuse an
  idempotency key only for protocol-safe retries.
- Projects and Calendar now fetch the authoritative current object after a version conflict,
  replace only the editor's server base/version, retain every local input field, and require the
  user to review and press Save again. Nothing is auto-merged or auto-committed.
- Settings and other WorkHub forms retain entered values on transport or conflict failure; their
  server result types remain explicit and no optimistic local success is applied.

## Verified scenarios

Focused desktop tests cover transport failure and recovery (`ServerUnavailable → Reconnecting →
Online`), shell state, write blocking and draft retention in Tasks, Calendar, Projects and
Settings, session-expiry draft retention, and explicit version-conflict retry with the refreshed
ETag version for Calendar and Projects. The full solution regression is recorded in the packaged
validation report.

## Deliberate boundary

This is the architecture-approved MVP online-write model. Confirmed data and unsaved drafts survive
a network interruption and section navigation within the running client. The client does not
persist or replay an offline write queue; adding offline editing would require a separate command
log, security revocation policy and aggregate-specific merge design.
