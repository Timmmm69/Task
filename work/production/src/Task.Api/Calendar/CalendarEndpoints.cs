using System.Globalization;
using System.Text.Json.Serialization;
using Task.Api.Security;
using Task.Application.Calendar;
using Task.Application.Security;
using Task.Domain.Calendar;

namespace Task.Api.Calendar;

internal static class CalendarEndpoints
{
    private const string CalendarRoute = "/api/v1/calendar";
    private const string CalendarEventRoute = "/api/v1/calendar-events/{id}";
    private const string ConflictsRoute = "/api/v1/calendar/conflicts";
    private const int MaximumItems = 500;
    private const int MaximumIdentifiers = 100;

    public static IEndpointRouteBuilder MapCalendarEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapGet(CalendarRoute, GetScheduleAsync)
            .RequireAuthorization(TaskPermissionAuthorization.CalendarReadPolicyName);
        app.MapGet(CalendarEventRoute, GetEventAsync)
            .RequireAuthorization(TaskPermissionAuthorization.CalendarReadPolicyName);
        app.MapGet(ConflictsRoute, GetConflictsAsync)
            .RequireAuthorization(TaskPermissionAuthorization.CalendarReadPolicyName);
        return app;
    }

    private static async Task<IResult> GetScheduleAsync(HttpContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var requestContext = ReadRequestContext(context);
        var service = context.RequestServices.GetService<ScheduleQueryService>();
        if (requestContext is null || service is null)
        {
            return await InternalErrorAsync(context, "Calendar read access is not configured.");
        }

        if (!TryReadScheduleQuery(context.Request.Query, out var query, out var error))
        {
            return await ValidationProblemAsync(context, error);
        }

        try
        {
            var page = service.GetSchedule(
                requestContext.OrganizationId,
                query.From,
                query.To,
                query.TimeZone,
                query.Users,
                query.Projects,
                query.Status);
            return Results.Json(new SchedulePageResponse(
                page.Items.Take(MaximumItems).Select(ToResponse).ToArray(),
                NextCursor: null,
                page.RangeStart.UtcDateTime,
                page.RangeEnd.UtcDateTime));
        }
        catch (ArgumentException exception)
        {
            return await DomainValidationProblemAsync(context, exception.Message);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return await InternalErrorAsync(context, "Calendar read access is temporarily unavailable.");
        }
    }

    private static async Task<IResult> GetEventAsync(HttpContext context, string id)
    {
        var requestContext = ReadRequestContext(context);
        var service = context.RequestServices.GetService<CalendarEventQueryService>();
        if (requestContext is null || service is null)
        {
            return await InternalErrorAsync(context, "Calendar event read access is not configured.");
        }

        if (!Guid.TryParseExact(id, "D", out var eventId) || eventId == Guid.Empty)
        {
            return await ObjectNotVisibleAsync(context);
        }

        try
        {
            var details = service.GetById(requestContext.OrganizationId, eventId);
            if (details is null)
            {
                return await ObjectNotVisibleAsync(context);
            }

            context.Response.Headers.ETag = $"\"v{details.Version.ToString(CultureInfo.InvariantCulture)}\"";
            return Results.Json(ToResponse(details));
        }
        catch (Exception)
        {
            return await InternalErrorAsync(context, "Calendar event read access is temporarily unavailable.");
        }
    }

    private static async Task<IResult> GetConflictsAsync(HttpContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var requestContext = ReadRequestContext(context);
        var service = context.RequestServices.GetService<ScheduleQueryService>();
        if (requestContext is null || service is null)
        {
            return await InternalErrorAsync(context, "Calendar conflict read access is not configured.");
        }

        if (!TryReadConflictsQuery(context.Request.Query, out var query, out var error))
        {
            return await ValidationProblemAsync(context, error);
        }

        try
        {
            var conflicts = service.GetConflicts(
                requestContext.OrganizationId,
                query.From,
                query.To,
                timezoneId: "UTC",
                query.UserIds,
                query.ExcludeObjectId);
            return Results.Json(conflicts.Take(MaximumItems).Select(ToResponse).ToArray());
        }
        catch (ArgumentException exception)
        {
            return await DomainValidationProblemAsync(context, exception.Message);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return await InternalErrorAsync(context, "Calendar conflict read access is temporarily unavailable.");
        }
    }

    private static bool TryReadScheduleQuery(
        IQueryCollection values,
        out ScheduleQuery query,
        out string error)
    {
        query = null!;
        if (!HasOnly(values, "from", "to", "users", "departments", "projects", "status", "timezone", "cursor") ||
            !TryReadUtcInstant(values, "from", out var from) ||
            !TryReadUtcInstant(values, "to", out var to) ||
            !TryReadIdentifiers(values, "users", out var users) ||
            !TryReadIdentifiers(values, "projects", out var projects))
        {
            error = "The calendar query is malformed.";
            return false;
        }

        if (HasNonEmpty(values, "departments") || HasNonEmpty(values, "cursor"))
        {
            error = "departments and cursor are not supported by this calendar read slice.";
            return false;
        }

        if (!TryReadOptionalScalar(values, "status", 40, out var status) ||
            !TryReadOptionalScalar(values, "timezone", 64, out var timezone))
        {
            error = "status or timezone is malformed.";
            return false;
        }

        query = new(from, to, timezone ?? "UTC", users, projects, status);
        error = string.Empty;
        return true;
    }

    private static bool TryReadConflictsQuery(
        IQueryCollection values,
        out ConflictsQuery query,
        out string error)
    {
        query = null!;
        if (!HasOnly(values, "from", "to", "userIds", "excludeObjectId") ||
            !TryReadUtcInstant(values, "from", out var from) ||
            !TryReadUtcInstant(values, "to", out var to) ||
            !TryReadIdentifiers(values, "userIds", out var userIds) ||
            !TryReadOptionalIdentifier(values, "excludeObjectId", out var excludeObjectId))
        {
            error = "The conflict query is malformed.";
            return false;
        }

        query = new(from, to, userIds, excludeObjectId);
        error = string.Empty;
        return true;
    }

    private static bool HasOnly(IQueryCollection values, params string[] allowed)
    {
        var names = new HashSet<string>(allowed, StringComparer.Ordinal);
        return values.Keys.All(names.Contains);
    }

    private static bool HasNonEmpty(IQueryCollection values, string name) =>
        values.TryGetValue(name, out var entries) && entries.Any(entry => !string.IsNullOrWhiteSpace(entry));

    private static bool TryReadUtcInstant(IQueryCollection values, string name, out DateTimeOffset instant)
    {
        instant = default;
        if (!values.TryGetValue(name, out var entries) || entries.Count != 1)
        {
            return false;
        }

        var value = entries[0];
        return value is not null && value.EndsWith('Z') &&
            DateTimeOffset.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out instant) && instant.Offset == TimeSpan.Zero;
    }

    private static bool TryReadIdentifiers(
        IQueryCollection values,
        string name,
        out IReadOnlyList<Guid>? identifiers)
    {
        identifiers = null;
        if (!values.TryGetValue(name, out var entries))
        {
            return true;
        }

        if (entries.Count == 0 || entries.Count > MaximumIdentifiers)
        {
            return false;
        }

        var parsed = new List<Guid>(entries.Count);
        var unique = new HashSet<Guid>();
        foreach (var entry in entries)
        {
            if (string.IsNullOrWhiteSpace(entry) ||
                !Guid.TryParseExact(entry, "D", out var identifier) ||
                identifier == Guid.Empty ||
                !unique.Add(identifier))
            {
                return false;
            }

            parsed.Add(identifier);
        }

        identifiers = parsed;
        return true;
    }

    private static bool TryReadOptionalIdentifier(
        IQueryCollection values,
        string name,
        out Guid? identifier)
    {
        identifier = null;
        if (!values.TryGetValue(name, out var entries))
        {
            return true;
        }

        if (entries.Count != 1 ||
            !Guid.TryParseExact(entries[0], "D", out var parsed) ||
            parsed == Guid.Empty)
        {
            return false;
        }

        identifier = parsed;
        return true;
    }

    private static bool TryReadOptionalScalar(
        IQueryCollection values,
        string name,
        int maximumLength,
        out string? result)
    {
        result = null;
        if (!values.TryGetValue(name, out var entries))
        {
            return true;
        }

        if (entries.Count != 1)
        {
            return false;
        }

        result = entries[0]?.Trim();
        return result is { Length: > 0 } && result.Length <= maximumLength;
    }

    private static AuthenticatedRequestContext? ReadRequestContext(HttpContext context) =>
        context.Items.TryGetValue(TaskJwtAuthenticationHandler.AuthenticatedRequestContextItemName, out var value)
            ? value as AuthenticatedRequestContext
            : null;

    private static ScheduleItemResponse ToResponse(ScheduleItem item) => new(
        item.ObjectId,
        item.ItemType == ScheduleItemType.Task ? "task" : "calendar_event",
        item.Title,
        item.LocalDate,
        item.StartAtUtc?.UtcDateTime,
        item.EndAtUtc?.UtcDateTime,
        item.IsAllDay,
        item.ProjectId,
        item.Status,
        item.Priority is null ? null : item.Priority.Value.ToString().ToLowerInvariant());

    private static ScheduleConflictResponse ToResponse(ScheduleConflict conflict) => new(
        conflict.LeftObjectId,
        conflict.RightObjectId,
        conflict.OverlapStart.UtcDateTime,
        conflict.OverlapEnd.UtcDateTime,
        conflict.Severity.ToString().ToLowerInvariant());

    private static CalendarEventResponse ToResponse(CalendarEventDetails details) => new(
        details.Id,
        details.OrganizationId,
        details.Version,
        details.CreatedAtUtc.UtcDateTime,
        details.UpdatedAtUtc.UtcDateTime,
        details.ProjectId,
        details.Title,
        details.Description,
        details.EventDate,
        details.IsAllDay,
        details.StartAtUtc?.UtcDateTime,
        details.EndAtUtc?.UtcDateTime,
        details.TimeZoneId,
        details.Status == CalendarEventStatus.Scheduled ? "scheduled" : "cancelled",
        details.UserAttendees.Select(attendee => new EventAttendeeResponse(
            attendee.UserAccountId,
            ToContractValue(attendee.Role),
            ToContractValue(attendee.ResponseStatus),
            attendee.RespondedAtUtc?.UtcDateTime)).ToArray(),
        details.ContactAttendees.Select(attendee => new ContactAttendeeResponse(
            attendee.ContactId,
            ToContractValue(attendee.Role),
            ToContractValue(attendee.ResponseStatus),
            attendee.RespondedAtUtc?.UtcDateTime)).ToArray());

    private static string ToContractValue(CalendarAttendeeRole role) => role.ToString().ToLowerInvariant();

    private static string ToContractValue(CalendarAttendeeResponseStatus status) =>
        status.ToString().ToLowerInvariant();

    private static Task<IResult> ObjectNotVisibleAsync(HttpContext context) =>
        WriteProblemAsync(context, StatusCodes.Status404NotFound, "OBJECT_NOT_VISIBLE",
            "The requested object is absent or not visible.", retryable: false);

    private static Task<IResult> ValidationProblemAsync(HttpContext context, string title) =>
        WriteProblemAsync(context, StatusCodes.Status400BadRequest, "VALIDATION_FAILED", title, retryable: false);

    private static Task<IResult> DomainValidationProblemAsync(HttpContext context, string title) =>
        WriteProblemAsync(context, StatusCodes.Status422UnprocessableEntity, "VALIDATION_FAILED", title, retryable: false);

    private static Task<IResult> InternalErrorAsync(HttpContext context, string title) =>
        WriteProblemAsync(context, StatusCodes.Status503ServiceUnavailable, "INTERNAL_ERROR", title, retryable: true);

    private static async Task<IResult> WriteProblemAsync(
        HttpContext context,
        int status,
        string code,
        string title,
        bool retryable)
    {
        await TaskApiProblemResponse.WriteAsync(context, status, code, title, retryable);
        return Results.Empty;
    }

    private sealed record ScheduleQuery(
        DateTimeOffset From,
        DateTimeOffset To,
        string TimeZone,
        IReadOnlyList<Guid>? Users,
        IReadOnlyList<Guid>? Projects,
        string? Status);

    private sealed record ConflictsQuery(
        DateTimeOffset From,
        DateTimeOffset To,
        IReadOnlyList<Guid>? UserIds,
        Guid? ExcludeObjectId);

    internal sealed record ScheduleItemResponse(
        [property: JsonPropertyName("objectId")] Guid ObjectId,
        [property: JsonPropertyName("itemType")] string ItemType,
        [property: JsonPropertyName("title")] string Title,
        [property: JsonPropertyName("localDate")] DateOnly LocalDate,
        [property: JsonPropertyName("startAtUtc")] DateTime? StartAtUtc,
        [property: JsonPropertyName("endAtUtc")] DateTime? EndAtUtc,
        [property: JsonPropertyName("isAllDay")] bool IsAllDay,
        [property: JsonPropertyName("projectId")] Guid? ProjectId,
        [property: JsonPropertyName("status")] string Status,
        [property: JsonPropertyName("priority")] string? Priority);

    internal sealed record SchedulePageResponse(
        [property: JsonPropertyName("items")] IReadOnlyList<ScheduleItemResponse> Items,
        [property: JsonPropertyName("nextCursor")] string? NextCursor,
        [property: JsonPropertyName("rangeStart")] DateTime RangeStart,
        [property: JsonPropertyName("rangeEnd")] DateTime RangeEnd);

    internal sealed record ScheduleConflictResponse(
        [property: JsonPropertyName("leftObjectId")] Guid LeftObjectId,
        [property: JsonPropertyName("rightObjectId")] Guid RightObjectId,
        [property: JsonPropertyName("overlapStart")] DateTime OverlapStart,
        [property: JsonPropertyName("overlapEnd")] DateTime OverlapEnd,
        [property: JsonPropertyName("severity")] string Severity);

    internal sealed record EventAttendeeResponse(
        [property: JsonPropertyName("userAccountId")] Guid UserAccountId,
        [property: JsonPropertyName("role")] string Role,
        [property: JsonPropertyName("responseStatus")] string ResponseStatus,
        [property: JsonPropertyName("respondedAt")] DateTime? RespondedAt);

    internal sealed record ContactAttendeeResponse(
        [property: JsonPropertyName("contactId")] Guid ContactId,
        [property: JsonPropertyName("role")] string Role,
        [property: JsonPropertyName("responseStatus")] string ResponseStatus,
        [property: JsonPropertyName("respondedAt")] DateTime? RespondedAt);

    internal sealed record CalendarEventResponse(
        [property: JsonPropertyName("id")] Guid Id,
        [property: JsonPropertyName("organizationId")] Guid OrganizationId,
        [property: JsonPropertyName("version")] long Version,
        [property: JsonPropertyName("createdAt")] DateTime CreatedAt,
        [property: JsonPropertyName("updatedAt")] DateTime UpdatedAt,
        [property: JsonPropertyName("projectId")] Guid? ProjectId,
        [property: JsonPropertyName("title")] string Title,
        [property: JsonPropertyName("description")] string? Description,
        [property: JsonPropertyName("eventDate")] DateOnly EventDate,
        [property: JsonPropertyName("isAllDay")] bool IsAllDay,
        [property: JsonPropertyName("startAtUtc")] DateTime? StartAtUtc,
        [property: JsonPropertyName("endAtUtc")] DateTime? EndAtUtc,
        [property: JsonPropertyName("timeZone")] string TimeZone,
        [property: JsonPropertyName("status")] string Status,
        [property: JsonPropertyName("userAttendees")] IReadOnlyList<EventAttendeeResponse> UserAttendees,
        [property: JsonPropertyName("contactAttendees")] IReadOnlyList<ContactAttendeeResponse> ContactAttendees);
}
