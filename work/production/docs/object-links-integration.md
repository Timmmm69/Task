# Corporate object links — task 10.2 / version 10.2.0

Historical stage 10.2 receipt. The [10.3 review](object-link-picker-review.md) supersedes the four-type matrix and records additional session/cache/cancellation fixes and current acceptance limits.

Baseline: `736a306e2ca16820f40c1f9fb82fb5ef4cc69ad7` (stage 10.1).
Branch: `feature/object-link-target-picker`. No API, OpenAPI, database, permission vocabulary or business-status changes.

## User flow

WorkHub mounts the stage 10.1 ObjectLinkTargetPickerView in the existing ObjectLinksView.
Corporate UUID entry is hidden and cannot supply the creation target. Enter 2–200 characters,
search, choose a result and confirm it; the selected title and type are displayed. AddLinkAsync
receives that confirmed record's ObjectId. Search text, an arbitrary UUID and an unconfirmed
candidate never enable creation. Existing link listing/removal remains in the same panel.

Contact and catalog selections provide explicit source type and version. Search result rows
have an accessible “Связи” button to select the source without navigating away. The expander
header identifies the selected source. Its confirmed version is propagated back to the host
selection after a successful read or write. Selection in an inactive area cannot replace the
current source. IDs are never used to infer types.

| Source | Available creation types | Target |
| --- | --- | --- |
| task | task_file, task_contact | catalog_item; contact/company |
| project | project_file | catalog_item |
| contact/company | contact_file | catalog_item |
| catalog_item, calendar_event, interaction | none | Existing links can still be read/deleted with permissions |

No additional backend link types are offered. Source changes clear the picker, cancel pending
search and link requests and increment a generation guard. Late success, error and read-back
responses are ignored, including A → B → A selection changes. A version change on the same
source also invalidates selection. WPF publishes choices before their selected value so a
source change retains a visible compatible link type.

## Authorization and concurrency

- Read requires ObjectLink.Read and the source resource's read capability. Calendar.Read is
  an alias of Task.Read; interactions use Contact.Read. The server filters individual objects.
- Create requires ObjectLink.Create independently of ObjectLink.Delete. Source Project.Update
  and Contact.Update are required for project and CRM sources; task writes retain the existing
  ObjectLink.Create contract. Source/target read rights and Search.Use are enforced by the picker.
- Delete requires ObjectLink.Delete and the existing source write rules. Incoming relations
  cannot be deleted through a target's version. Creation types do not restrict permitted deletion.
- Effective target Contact.Update/FileCatalog.Update object scope, project membership/write
  rules and terminal task status are decided by the existing POST checks. They cannot be
  preflighted by this picker. No global target Update capability is invented as a substitute for
  iam.object_allowed: it may authorize an owned or related object without that global capability.
- Capabilities/scope, session, connectivity, activation and disposal are forwarded to the picker.
  Revocation clears the confirmed target; loss of read access or session also clears link rows.
- If-Match, source version, fresh per-command idempotency key and the existing authenticated
  transport are unchanged. AsyncCommand prevents repeated execution; all sibling mutation and
  refresh commands are disabled while a request is running. WorkHub mode-switch protection
  includes pending link mutations.
- Rows change only after a successful confirmed server read-back. A successful POST followed by
  failed GET is not success in the UI. Known duplicate triples/self-links are blocked locally;
  concurrent duplicates remain server-authoritative.
- Failed/uncertain writes disable further writes until “Обновить связи” succeeds. This includes
  412 VERSION_CONFLICT; the previous confirmed rows remain visible, with no success message.
  CONFLICT duplicates, INVALID_STATE_TRANSITION, VALIDATION_FAILED, invisible/deleted objects,
  server/network failures and session expiry use safe client messages. Raw server titles and
  exceptions are not displayed. Successful reconciliation restores commands at the new version.

## Personal mode and boundaries

PersonalWorkspaceViewModel keeps its local Target selection and PersonalWorkClient. It does
not construct the corporate picker or make server requests. The original two-argument
SetSource and constructor remain compatible; local TargetId selection is retained after saves.

Without Search.Use the stage 10.1 picker cannot search; no unfiltered directory fallback or
new permissions are introduced. A separate authorized q-list adapter remains outside this
integration. An archived/deleted/changed target between selection and POST is rejected by the
server. There is no effective-object-write preflight endpoint. Existing links GET fails closed
when more than 200 rows would require further pagination; this pre-existing adapter limit is
unchanged. TaskWorkspace's separate file command is outside this ObjectLinks integration.

Automated native WPF checks cover embedding, keyboard/focus, automation names, confirmed
creation, type switches and narrow layout. They do not certify a manual installed-client/live
company server, Narrator, physical mixed-DPI or deployment acceptance. See the 10.2.0 output
package validation report for exact executed checks and any regression-suite limitations.
