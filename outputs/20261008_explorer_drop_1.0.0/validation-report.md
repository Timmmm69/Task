# Explorer Drag & Drop 1.0.0 — 2026-10-08

Prepared for user-authorized publication to main. Existing unrelated working changes are preserved and excluded from this commit.

## Flow and drop zones

Catalog: drop on a virtual folder targets that folder; drop on the catalog surface targets the current creation folder (selected virtual folder or selected reference's parent); explicit root targets root. File/folder reference nodes are invalid targets. Internal catalog move drag remains supported.

The repository had no combined picker-to-new-reference use case: it created catalog metadata and added locations as separate actions. CatalogPathAddition now coordinates the existing CreateCatalogItemAsync and CreateLocationAsync/AddLocationAsync commands for both Explorer drop and the new native multi-select file/folder picker buttons. Existing location picker/editor is retained. No backend DTO, API, DB, import subsystem or transaction contract changed.

Overlay uses non-hit-testable adorners, with accepted/rejected colors, target outline and native OLE Link (Copy fallback) effects. These effects register a reference and never copy physical files. Deferred DragLeave cleanup is invalidated by the next DragOver to avoid sibling flicker; true leave, canceled drag, drop, context changes and unload remove feedback. Keyboard Tab/focus and access keys remain available.

## Paths, folders and permissions

Paths use WindowsFileAccessAdapter.IsAllowedPath and existing FileLocationPolicy.ValidateUnc. Unsupported URI/shell/device paths, credentials, admin shares, traversal, streams and executable types blocked by the existing adapter are rejected. UNC must match a visible active network resource before SMB metadata access; server checks that resource again. Local locations omit deviceId in the client because the existing server binds it to SessionDevice and ownerUserId, and enforces allowLocalPaths. No SMB/Windows ACL bypass or credentials are introduced.

Only File.GetAttributes runs on a worker thread; no file contents are read or transmitted by this feature. Folder is one folder_reference. No enumeration/indexing/recursive import, physical copy/move/delete/open, command execution or compensating API operation occurs. Server remains authoritative for permissions and parent scope. Drop requires active session/network, fresh catalog, FileCatalog.Read/Create and FileLocation.Update; offline/read-only creates no local pending writes or drafts.

## Batch and failure

At most 1000 paths per drop, one operation at a time. Existing loading presentation is used during async operations. Continuations retain the caller dispatcher context; filesystem metadata runs off it. Each input has a numbered result with success/failure and created ID where known. Network/authentication loss stops remaining writes. Existing visible usable locations are checked for duplicates. A bounded in-memory journal (10000 entries; hashed path keys) suppresses repeated successful creates and uncertain outcomes within the current workspace lifetime.

If CatalogItem succeeded and FileLocation failed, the reference stays in the catalog and its ID is reported. Complete it through the existing location editor; drop retry never blindly creates it again. Unknown create responses are never automatically replayed with a fresh idempotency key. No transaction/rollback is fabricated. After an application restart, uncertain results require inspecting the server catalog; no durable retry journal is promised or written to disk. Hidden/inaccessible locations are not inspected for deduplication.

## Inbox blocker

Canonical InboxItemCreate contains itemType=file_link and rawPath. However, the running InboxViewModel.CaptureAsync constructs DesktopCreateTaskCommand(title, priority) and sends CreateTaskAsync. ProductApiContracts currently has no inbox-items endpoint, and the current client has no typed file/path capture command. Reusing task title/description as a file DTO would invent semantics. Inbox displays rejected drop feedback and points to Catalog; normal task capture remains unchanged. Implementing that missing backend/client use case is outside this change.

## Validation

- Release solution build: PASS, zero errors (existing analyzer warning).
- Final solution test runner: 2026 passed, 4 skipped, zero failures. Desktop: 615 passed, zero skipped; 28 new tests. Publication gate ran against the current origin/main in an isolated worktree, excluding unrelated working changes.
- Coverage: real single file and folder metadata with file byte equality; multi-file and 300-item sequential flow; UNC normalization/root boundary; nonrecursive folders; current/explicit/invalid target; denied permissions/Windows access; offline/canceled input; unsupported paths; duplicate; partial and uncertain outcomes; connection-loss stop; background metadata probe; real WPF overlay reuse, target outline, canceled drag/DragLeave cleanup and keyboard focus. Existing picker/location/catalog suites passed.
- Diff and self-review: PASS. No source/ changes, unrelated modifications preserved; no API or physical-file mutation added.
- Live PostgreSQL/SMB/ACL and physical mouse drag from Explorer: NOT VERIFIED. PostgreSQL connection was absent; DB tests that return early are not evidence of database execution. Native tests use real WPF windows/dispatcher and synthesized Explorer-format routed events, not an OS mouse gesture. Four existing test skips are retained in TRX.

See evidence/accepted/*.trx, build.log, tests.log and validation.json for inspectable results. Failed intermediate native-test attempts are quarantined and excluded from the accepted package.

## Publication review

Verified base: b03ef62ba73c9a332bd9bba392f0a9d68ad0ea22. Review corrected the successful native drop effect (Link/Copy rather than None) and rechecks FileCatalog.Read during a batch. Regression checks cover a real-file WPF drop with accepted effect and revoked catalog-read capability during metadata probing. Release solution gate in evidence/publish-final: 2026 PASS / 4 SKIP / 0 FAIL. Earlier evidence/accepted is historical; final source snapshot matches the publication worktree. No live PostgreSQL/SMB/ACL or physical Explorer gesture is newly claimed.
