# Corporate object-link target discovery — 10.3.0

`GET /api/v1/object-link-targets?q=...&types=...&limit=50[&cursor=...]`
uses `ObjectLink.Create` and the current read capabilities of each requested type.
It does not require `Search.Use`. Global `/search` retains its existing permission
and type contract. Deploy the API and Desktop changes together; an older API
returns an ordinary picker failure, without falling back to directory scans.

`q` is trimmed, 2–200 characters. `types` is mandatory and contains core object
types accepted as link targets: task, project, contact, company, catalog_item,
calendar_event, interaction, employee_profile, network_resource, notification,
inbox_item. Locations and comments are excluded. Only `q`, `types`, `cursor`,
and `limit` are accepted; the existing search limit bounds apply (1–500).

The response contains `items` with objectId, objectType, title, version, updatedAt,
lifecycleState, plus nextCursor and tookMs. Paths, notification bodies and inbox
content are not returned. Only active, tenant-scoped, currently readable objects
are returned. Notifications and inbox items retain own-recipient/owner visibility
even for administrators. Search snapshots expire after 15 minutes, bind to the
route, user, permissions and filters, and recheck current visibility per page.
The API forwards Notification.ReadOwn and Inbox.ReadOwn to discovery and all
object-link operations so that discovered targets can be read back and linked.

The picker retains the typed-link matrix and excludes the source itself.
POST remains authoritative for target/source state, object permissions and type
compatibility. No new capability, schema migration or business rule is introduced.
If-Match, idempotency and confirmed server read-back remain mandatory.

Desktop existing-links reads follow every cursor at 200 rows per request. A
changed source ETag, failed page, expired cursor, duplicate row or repeated cursor
fails the whole read; partial lists never confirm a mutation as successful.

UI Automation names include the target title and type, with the UUID in HelpText;
status binding updates raise LiveRegionChanged for a listening screen reader.
Native WPF/UIA verification does not establish audible Narrator behavior or
physical movement between monitors of different DPI. Installed-client/live API,
Narrator listening and physical mixed-DPI require a separate acceptance receipt.
