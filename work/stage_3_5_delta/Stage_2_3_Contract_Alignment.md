# Stage 2.3 — Contract Alignment

## OQ-001: organization notification urgency scale

The scale is organization-owned; no per-user override is introduced because the concept requires a common configurable scale, not personal urgency semantics. Four semantic levels (`low`, `normal`, `high`, `critical`) remain explicit and color/display tokens are secondary presentation metadata. Scores are inclusive 0–100 and the four intervals must be ordered, contiguous, complete, and non-overlapping. Defaults are 0–24, 25–49, 50–74, 75–100. `PUT` and reset require `System.Configure`, `If-Match`, and `Idempotency-Key`, emit audit action `notification_urgency_scale.changed`, and return ETag. Existing notifications keep their semantic urgency; both existing and future notifications resolve presentation from the current scale. A 2.2 client remains compatible because the old notification DTO is unchanged and it uses its existing display mapping.

## OQ-003: employees in global search

`employee` is a new value of the existing `types` filter and is returned as `SearchSuggestion.resultType=employee` with concrete `EmployeeSearchResult`. It supplies display name, department, optional job title (only where modeled), account status, deep link, and redaction marker. The server authorizes, redacts, ranks, groups as “Employees”, and filters before cursor pagination; cursor binding adds employee visibility policy version. `userIds` remains a related-object filter and is not an employee-search substitute. Blocked users are omitted unless `User.ReadBlocked`; unauthorized callers cannot infer their existence.

## Errors and permissions

No new stable error code is needed: `VALIDATION_FAILED`, `FORBIDDEN`, and `VERSION_CONFLICT` cover the additions. Existing `Settings.Read`, `System.Configure`, `Search.Use`, and `User.ReadBlocked` are reused.
