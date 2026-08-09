# Stage 5.3 Wave C — coverage/backlog package 0.1

**Status:** working coverage package; no Gate 5.3 or 5.4 closure.  
**Scope:** Search; Archive; Trash; Settings; Admin users/departments/roles/sessions/devices; health/jobs/backups/audit.  
**Authority:** Stage 5 Visual Design Plan 1.0; Stage 5.2 Component Inventory, Flow Inventory, Role/Capability Matrix and State/Component Matrix. This package does not add API operations, DTO fields, permissions, errors, or business rules.

## Traceability and coverage

The row-level backlog is `Wave_C_Coverage_Backlog_0.1.csv`. It covers 38 published SCR: Search (SCR-133–136, 4); Lifecycle (SCR-140–143, 4); Settings (SCR-150–159, SCR-161, 10); Admin (SCR-170–188, 20).

| Canonical area | Required evidence | Linked contract |
|---|---|---|
| Search | command overlay, full-results page, filters, permission-safe partial/offline result | FLOW-019; SCR-133–136; ROLE-031, ROLE-035; STC-021, STC-022, STC-055 |
| Archive / Trash | list, archived/trashed read-only state, restore conflict, irreversible purge | FLOW-035; SCR-140–143; ROLE-019, ROLE-024; STC-011, STC-012, STC-043–045 |
| Settings | shell plus each published section and destructive/recovery variants | SCR-150–159, SCR-161; global role/capability recheck; STC-007, STC-009, STC-013, STC-054–055 |
| Admin users / departments | directory, user card/create/block, hierarchy/editor | FLOW-029; SCR-170–176; STC-006–009, STC-055 |
| Admin roles / sessions / devices | roles editor, effective-permission diagnostics, device/session lists | FLOW-030; SCR-177–181; ROLE-017; STC-007–009, STC-055 |
| Operations | network resources, system health, jobs, backup, maintenance and audit | SCR-182–188; STC-048–050, STC-054–055 |

All listed surfaces require the supplied component behavior: visible focus, keyboard path, UIA name/role/state/value, non-colour semantics, long Russian text and 200% scaling. The backlog adds relevant NFR references from the component specs, notably NFR-002–005 globally, NFR-006 for lists/pagination, NFR-008 for filters/search, NFR-023 for search/audit pagination, NFR-025 for recovery and destructive outcomes, and NFR-018–020 for connection/diagnostic/file-adjacent Settings evidence.

## Reusable component handoff

No new component is proposed. Use the frozen Stage 5.2 library references from the CSV: SearchBox, FilterBar, DataList, Pagination, EmptyState, LoadingState, ErrorMessage, PermissionState, RedactionMarker, ConnectivityBanner, ReadOnlyBanner, RetryAction, LifecycleBanner, DialogShell, FocusTrap, ConflictNotice, SemanticStatus, ProgressIndicator, TimelineHistory, TreeView, InspectorPanel, FormLayout and ValidationMessage.

In every capability variant, server authorization stays authoritative. Hidden objects/actions are not counted, named, or exposed in the UI Automation tree. A disabled action must state its allowed safe reason; a forbidden result removes optimistic preview and refreshes capability. Recovery preserves safe draft/focus context unless the published state says otherwise.

## Prototype storyboards to create

| ID | Storyboard (minimum annotated frames) | Acceptance evidence |
|---|---|---|
| SB-01 | Search: invoke → query/loading → allowed result → partial/redacted result → no result | keyboard invoke, arrows/Enter/Escape, no hidden-result count |
| SB-02 | Filters and cursor: apply → grouped page → cursor expired → restart → offline cache-only → reconnect | query/filter retained or cleared exactly as published; completeness message |
| SB-03 | Archive: list → archived detail → read-only explanation → restore available/forbidden | lifecycle actions and focus return |
| SB-04 | Trash: tombstone → restore conflict (name/parent) → legal-hold blocked purge → typed irreversible confirmation | no silent relocation/purge; consequence announced |
| SB-05 | Settings: shell → scoped section → server-managed lock → offline read-only → save conflict | scope labels, validation and recovery |
| SB-06 | Security/preferences: change password failure → logout-all/revoke confirmation → notification OS denial | destructive consequences, Windows hand-off label |
| SB-07 | Connection/cache/device: sync state → endpoint/TLS failure → limited diagnostics → device/session revoked | safe copied diagnostic report and forced sign-in route |
| SB-08 | Users: filtered list → partial user card → block/deactivate confirmation → self/last-admin guard → audit consequence | capability recheck and conflict recovery |
| SB-09 | Departments: hierarchy → editor → cycle/hidden-parent validation → archive/restore | tree keyboard semantics and no hidden parent disclosure |
| SB-10 | Roles: immutable role → permission edit/compare → dangerous-permission confirmation → effective access unavailable | no raw policy-engine invention |
| SB-11 | Devices/sessions: large filtered list → stale/revoked/compromised row → revoke → target transition | relationship links and partial data redaction |
| SB-11A | Network resources: list → edit/probe → unsafe-root validation → unavailable network → retry | metadata permission remains distinct from Windows/SMB availability |
| SB-12 | Operations: unhealthy system → global read-only → queued/running/failed job → backup failure → maintenance approval/active-job guard | non-colour critical state and safe retry boundaries |
| SB-13 | Audit/org settings: wide filter → large-range constraint/background export → redacted audit entry → flag compatibility/conflict | no unlisted export schema or feature policy |

## Stage 5.4 execution checklist

### Automatable now (design-package and prototype checks)

- [ ] Assert 37 Wave C SCR IDs occur exactly once in a traceability table and each names a frame, reusable component/state reference or documented N/A.
- [ ] Assert FLOW-019, FLOW-029, FLOW-030 and FLOW-035 name a storyboard/prototype and retain their published roles/capabilities.
- [ ] Assert every Wave C backlog row lists default, loading, empty where list-like, partial, error, forbidden, offline/read-only where applicable, destructive where applicable, and large-list/long-content behavior.
- [ ] Detect prohibited speculative contract tokens: new endpoint paths, DTO-field names, permission names or error names not copied from the inventory.
- [ ] Inspect prototype semantics: unique accessible names; programmatic disabled/read-only/expanded/selected/current states; status/live regions; modal focus trap and focus return; keyboard alternatives for all pointer controls.
- [ ] Check hidden navigation/action targets are absent from the accessibility tree and redacted states contain no protected names/counts.
- [ ] Run contrast/non-colour token checks and static layout snapshots at 100%, 125%, 150%, 175% and 200% with long Russian fixtures.
- [ ] Flag clipping, overlap, invisible focus, horizontal loss of critical action, non-deterministic tab order, missing error association and missing text equivalent for progress/status.

### Requires Windows / OS-level evidence

- [ ] Validate compiled desktop client with Narrator and Inspect/UIA at 100–200% and across monitors with different scaling; capture focus, name, role, state and value evidence.
- [ ] Keyboard-only walkthroughs in actual Windows controls: Ctrl+F Search, filter/list/tree navigation, dialogs, destructive confirmations, session/device revoke and admin maintenance flow.
- [ ] Verify Windows notification permission denial, opening Windows Settings, autostart/tray behavior, screen-reader announcement timing, DPI changes while running and reconnect/read-only transition.
- [ ] Validate TLS/endpoint, cache clear/bootstrap, device revoke and server-loss/recovery against the integrated client. These require real server/OS behavior, not a visual mock.

### Requires stakeholder / owner evidence

- [ ] Product/design owner approves each annotated Wave C frame and all intentionally documented N/A decisions.
- [ ] Security/admin owner validates wording and consequence frames for blocking users, roles, sessions/devices, audit, backups and restore/maintenance; no inferred approval policy.
- [ ] Technical contract owner resolves any discrepancy found between the frames and published API/DTO/capability/error contracts.
- [ ] QA and desktop tech lead triage Critical/High findings, approve realistic fixtures and attach rerun evidence. Medium findings need explicit owner acceptance under the Stage 5.4 gate rule.

## Remaining work / gate status

This is coverage planning, not final visual evidence. Gate 5.3 remains open until every listed SCR has an approved frame/reuse/N/A record and every normative flow (all 37, not only these four) has prototype or storyboard evidence. Gate 5.4 remains open until role comparison frames, Windows/UIA keyboard and screen-reader evidence, high-DPI multi-monitor evidence and finding closure/acceptance are complete.
