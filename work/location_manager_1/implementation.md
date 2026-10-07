# Location Manager 1.0.0 — implementation record

Workspace: `C:/Users/novik/Таск`; baseline main/origin/main:
`06443765750ccea544c136f8d22551324afed667`.

Scope is the existing Corporate LAN API flow. Personal uses a separate SQLite
schema with a unique catalog_id/path relation; changing that schema would be a
different persistence task. Its existing adapter and UI behavior are preserved.

Plan followed: inspect current contracts/security/storage; add only missing
desktop client operations; implement the manager as a separate MVVM use case;
connect WPF selection/form/pickers/confirmation; add desktop and PostgreSQL
regressions; extend the existing QA-03 UIA gate; inspect diff and screenshots;
package source, test evidence, manifest and SHA-256.

Contract constraints found before implementation:

- Locations GET returns a JSON array, unlike generic paged lists.
- All three location mutations use the parent CatalogItem If-Match. Response
  ETag is that aggregate's version; body version is the location row version.
- All mutations bump the aggregate. Primary changes also alter sibling row
  versions, so the list must be read again.
- Read requires FileCatalog.Read and FileReference.Open; mutation requires
  FileLocation.Update plus readable object scope.
- Backend owns ownerUserId/deviceId. Sensitive reads may omit rawPath entirely.
  Ownership can permit a path read without the sensitive-path permission; the
  client follows the server response and never reconstructs omitted fields.
- UNC requires an active visible NetworkResource and a path under its root.
  Resource names may be read while roots remain redacted.
- Current stable errors include FORBIDDEN, OBJECT_NOT_VISIBLE,
  VALIDATION_FAILED and VERSION_CONFLICT. Several detailed path/resource
  validations share VALIDATION_FAILED; the client does not invent new codes.
- Server omits rawPath from mutation bodies. These bodies are never treated as
  an authoritative list; the parent and list are read after every success.

No production backend, database schema, permissions or OpenAPI changes were
needed. Serena was activated and its manual read; the configured TypeScript
language service could not extract C# symbols, so scoped file reads were used.
Context7 verified the bundled .NET WPF OpenFolderDialog API.

Independent read-only review found same-item version refresh and access-revoke
resource-loading races. Both have regression tests. Real UIA inspection also
found an ItemsControl peer refresh issue and technical DTO accessibility names.
The manager uses the project's ListBox pattern with readable row names; DTO
ToString overrides exclude internal identifiers from accessibility output.
