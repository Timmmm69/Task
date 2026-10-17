# Object Link Target Picker — task 10.1 / package 10.1.0

Baseline: `57fa0159fe63e734649d6f4473c5240a5815f70e` (`origin/main`, 2026-10-17).
Branch: `feature/object-link-target-picker`. Implementation is Corporate-only and is not mounted in ObjectLinksView.

## Component contract

`ObjectLinkTargetPickerViewModel` owns the query, context, candidate, confirmed selection, access state and bounded cursor traversal. It reuses ViewModelBase, AsyncCommand, DesktopSearchResult and DesktopWorkResult. `ObjectLinkTargetPickerView` is a standalone UserControl, following the Employee Picker keyboard and lifecycle patterns without using the employee directory or User.Read.

- Construct with `IDesktopObjectLinkTargetSearchClient`, current capabilities and optional debounce (default 300 ms).
- Call `SetContext(sourceId, sourceType, linkType)` before use. Any actual context change clears results and selection, including compatible changes, to prevent carrying stale visibility between source objects.
- Bind SearchText, Results, Candidate and commands; read `SelectedObjectId` / `SelectedTarget` after confirmation. Neither query text nor an externally constructed DTO is a selection. Confirm requires the actual current result instance.
- Search text: trimmed, 2–200 characters. Page size 50; total at most 500 results / 20 pages (including empty or duplicate-only pages); user explicitly requests each subsequent page. Refine query at the limit.
- Generation and cancellation guard every response, including non-cooperative transports. Query/context/access changes and Cancel invalidate pending work. Dispose with the host form.
- Duplicate objects are removed by ObjectId. Repeated/cyclic cursors, unexpected types (including file_location), invalid IDs/title/version/date, oversized pages and malformed responses fail closed and clear all results. Self is excluded.
- Retry starts a fresh snapshot; an expired cursor never reuses partial results. All errors clear candidates and confirmed selection. Session expiry disables retry until the host supplies a valid session.
- Host MUST forward every authorization-scope change via UpdateAccess, even if capability names are unchanged; also forward sign-out and connectivity changes. This is the same explicit host lifecycle contract as EmployeePickerViewModel. A search response 403/401 clears data even without a prior host event.
- UI states: Initial, Loading, Ready, Empty, Selected, Forbidden, Offline, SessionExpired, ServerUnavailable, RequestFailed. Retry is an accessible command/button.
- Keyboard: Enter in query searches; Down focuses results; arrows navigate; Enter in results confirms; Escape cancels and returns focus; Tab traverses controls. Unload / DataContext replacement cancels the previous model. Titles, type, update time and returned ID distinguish equal names without further reads; UUID is never typed to select.

## Actual API and permissions

`DesktopWorkApiClient.SearchTargetsAsync` implements the narrow new interface in a partial class. It uses the existing authenticated executor and GET `/api/v1/search?q=...&types=...&limit=50[&cursor=...]`. It never uses local company-wide lists, an alternate identity or a new endpoint. Existing SearchAsync overloads and their global-search behavior remain unchanged. Response lifecycle must be active, as emitted by PostgresProductApiSearch.

Search route requires Search.Use. Results are filtered by resource read permission and `iam.object_allowed`; server rechecks visibility on cursor pages. Picker requires ObjectLink.Create, Search.Use, source read capability, applicable source update capability (Project.Update / Contact.Update) and FileCatalog.Read or Contact.Read for the permitted target types. ObjectLink.Read alone cannot enable this picker. The picker does not request or grant permissions.

The picker does NOT certify that a returned target can be linked. POST creation still requires all server checks: ObjectLink.Create route permission; readable source and target; source active (task not completed/cancelled); source object action; Project.Update and project-write rules for project sources; Contact.Update for contact/company sources; target object-level Contact.Update for contact/company or FileCatalog.Update for catalog_item. These last checks use `CheckObjectAction` / `iam.object_allowed`, not a WPF permission approximation. Host must handle creation 403/404/409/412 and refresh scope/source as appropriate.

Existing links API (not called by the picker): GET `/api/v1/objects/{id}/links` requires ObjectLink.Read; POST same route requires ObjectLink.Create, If-Match and Idempotency-Key; DELETE `/api/v1/objects/{id}/links/{childId}` requires ObjectLink.Delete and If-Match. `childId` is a link ID, not the target ID.

| Link | Source | Target |
| --- | --- | --- |
| task_file | task | catalog_item |
| project_file | project | catalog_item |
| contact_file | contact or company | catalog_item |
| task_contact | task | contact or company |

This exactly matches the current Links implementation. Other backend link types are intentionally outside this component. file_location is a location identity, not catalog_item; no parent-ID substitution occurs.

## 10.2 integration and limitations

1. Create one form-local picker for Corporate links using DesktopWorkApiClient and current capabilities; mount its view in the future UI change.
2. Forward source/type changes through SetContext. Subscribe to PropertyChanged(SelectedObjectId) to set `ObjectLinksViewModel.TargetId` to the confirmed ID, or empty on clear. Keep AddCommand disabled until confirmation; do not populate it from SearchText. Consider replacing the public free-form TargetId flow in 10.2, together with tests.
3. Forward access/session/connectivity/scope events to UpdateAccess, cancel on teardown, dispose when the host is disposed. Do not reuse it in Personal mode.
4. Submit through the existing AddLinkAsync flow with source version and idempotency; refresh and report server denials. No create request is made by the picker.

Without Search.Use this implementation is unavailable, even if ObjectLink.Create exists. There ARE alternative authorized server-filtered sources: GET `/contacts`, `/companies` (Contact.Read), `/catalog-items` (FileCatalog.Read) with q, lifecycle, limit and cursor (`ProductApiRoutes` and `PostgresProductApiQueries.List`). Existing DesktopWorkApiClient directory methods do not expose q for this use case and GetCatalogAsync uses the tree contract. A deliberate fallback adapter with separate mapping/pagination tests can be considered in 10.2; absence of Search.Use must not trigger an unfiltered full-directory load or a permission grant.

Search only establishes read visibility, so target object-write authorization remains a possible create-time denial. There is no endpoint here to preflight effective per-object create eligibility, and extra detail requests cannot replace that check. This is an integration constraint, not a change to permissions or server behavior.

10.1 supplies the reusable, tested component. 10.2 can integrate it under Search.Use; full coverage of users without Search.Use remains an explicit follow-up. ObjectLinksViewModel, ObjectLinksView, Personal, DB, OpenAPI and backend contracts are unchanged. Native automated WPF tests do not replace a manual installed-client walkthrough or assistive-technology acceptance.
