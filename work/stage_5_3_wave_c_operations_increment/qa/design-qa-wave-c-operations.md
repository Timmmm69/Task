# Design QA — Wave C Operations 0.2.0

- Date: 2026-08-01
- Browser: Codex in-app Browser
- Primary viewport: 1280 × 720
- Result: PASS for the implemented desktop prototype scope

## Verified flow

1. Health: degraded readiness, dependency failures and safe retry controls render correctly.
2. Background jobs: queued/running/failed states, safe retry and server-authoritative progress are interactive.
3. Backups: approval, exact `RESTORE` confirmation and active-job guard prevent unsafe maintenance entry.
4. Audit: authorized events, redaction, 90-day `REQUEST_TOO_LARGE` handling and background export are visible and interactive.
5. Organization: client-version incompatibility and optimistic `VERSION_CONFLICT` block publication.
6. Limited role: navigation exposes only Health and Audit without disclosing hidden section count.
7. Offline: Operations remains readable while refresh and all mutations are disabled.

## Remediation completed during QA

- Job and backup inspectors now remount on selected-object changes, resetting stale scroll position.
- The Operations workspace now remounts on section changes, preventing scroll position from leaking across tabs.
- Disabled danger actions use an explicit readable foreground/background pair instead of opacity-only styling.

## Evidence

The seven accepted screenshots in this folder were captured from the current code state and inspected after saving. They cover Health, Jobs, Restore guard, Audit export, limited role, organization conflict and offline read-only states.

## Boundary

This is a frontend prototype audit. It does not claim a real operations backend, native Windows UI Automation, Narrator certification, OS-level 200% scaling or production authorization enforcement. Responsive behavior remains supported by the existing ≤900 px rules and build inspection; the approved browser run primarily verified the 1280 × 720 desktop surface.
