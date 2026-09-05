using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Task.Api.Security;
using Task.Application.Calendar;
using Task.Application.Security;
using Task.Domain.Calendar;

namespace Task.Api.Calendar;

internal static class CalendarWriteEndpoints
{
    private const string CollectionRoute = "/api/v1/calendar-events";
    private const string EventRoute = "/api/v1/calendar-events/{id}";
    private const string ArchiveRoute = "/api/v1/calendar-events/{id}/archive";
    private const string UnarchiveRoute = "/api/v1/calendar-events/{id}/unarchive";
    private const string RestoreRoute = "/api/v1/calendar-events/{id}/restore";
    private static readonly HashSet<string> CreateProperties = new(StringComparer.Ordinal)
    {
        "projectId", "title", "description", "eventDate", "isAllDay", "startAtUtc", "endAtUtc",
        "timeZone", "status", "userAttendees", "contactAttendees",
    };
    private static readonly HashSet<string> PatchProperties = new(CreateProperties, StringComparer.Ordinal);

    public static IEndpointRouteBuilder MapCalendarWriteEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost(CollectionRoute, CreateAsync)
            .RequireAuthorization(TaskPermissionAuthorization.CalendarEventCreatePolicyName);
        app.MapPatch(EventRoute, PatchAsync)
            .RequireAuthorization(TaskPermissionAuthorization.CalendarEventUpdatePolicyName);
        app.MapPost(ArchiveRoute, ArchiveAsync)
            .RequireAuthorization(TaskPermissionAuthorization.CalendarEventUpdatePolicyName);
        app.MapPost(UnarchiveRoute, UnarchiveAsync)
            .RequireAuthorization(TaskPermissionAuthorization.CalendarEventUpdatePolicyName);
        app.MapDelete(EventRoute, DeleteAsync)
            .RequireAuthorization(TaskPermissionAuthorization.CalendarEventDeletePolicyName);
        app.MapPost(RestoreRoute, RestoreAsync)
            .RequireAuthorization(TaskPermissionAuthorization.CalendarEventDeletePolicyName);
        return app;
    }

    private static async Task<IResult> CreateAsync(HttpContext context, CancellationToken cancellationToken)
    {
        var requestContext = ReadRequestContext(context);
        var service = context.RequestServices.GetService<CalendarEventLifecycleService>();
        if (requestContext is null || service is null)
        {
            return await ProblemAsync(context, 503, "INTERNAL_ERROR", "Calendar event write access is not configured.", true);
        }

        if (!TryReadIdempotencyKey(context, out _))
        {
            return await ProblemAsync(context, 400, "VALIDATION_FAILED",
                "Idempotency-Key is required and must contain 8-200 printable ASCII characters.", false);
        }

        var body = await ReadBodyAsync(context, cancellationToken);
        if (body is null || !TryParseDocument(body, out var document))
        {
            return await ProblemAsync(context, 400, "MALFORMED_JSON", "The request body is not valid JSON.", false);
        }

        using (document)
        {
            if (!TryParseCreate(document.RootElement, out var model, out var error))
            {
                return await ProblemAsync(context, error.Status, error.Code, error.Title, false);
            }

            if (model.Status != CalendarEventStatus.Scheduled)
            {
                return await ProblemAsync(context, 422, "INVALID_STATE_TRANSITION",
                    "A new calendar event must start in the scheduled status.", false);
            }

            try
            {
                var calendarEvent = service.Create(
                    Guid.NewGuid(), requestContext.OrganizationId, requestContext.UserAccountId,
                    model.ProjectId, model.Title, model.Description, model.Timing,
                    DateTimeOffset.UtcNow, model.UserAttendees, model.ContactAttendees);
                SetEtag(context, calendarEvent.Metadata.Version);
                return Results.Json(CalendarEndpoints.ToResponse(calendarEvent), statusCode: StatusCodes.Status201Created);
            }
            catch (KeyNotFoundException)
            {
                return await NotVisibleAsync(context);
            }
            catch (ArgumentException exception)
            {
                return await ProblemAsync(context, 422, "VALIDATION_FAILED", exception.Message, false);
            }
            catch (Exception)
            {
                return await ProblemAsync(context, 503, "INTERNAL_ERROR", "Calendar event write access is temporarily unavailable.", true);
            }
        }
    }

    private static async Task<IResult> PatchAsync(HttpContext context, string id, CancellationToken cancellationToken)
    {
        var requestContext = ReadRequestContext(context);
        var service = context.RequestServices.GetService<CalendarEventLifecycleService>();
        var store = context.RequestServices.GetService<ICalendarEventStore>();
        if (requestContext is null || service is null || store is null)
        {
            return await ProblemAsync(context, 503, "INTERNAL_ERROR", "Calendar event write access is not configured.", true);
        }

        if (!TryReadEventId(id, out var eventId))
        {
            return await NotVisibleAsync(context);
        }

        if (!TryReadIfMatch(context, out var expectedVersion, out var preconditionError))
        {
            return await ProblemAsync(context, preconditionError.Status, preconditionError.Code, preconditionError.Title, false);
        }

        CalendarEvent? current;
        try
        {
            current = store.GetForUser(eventId, requestContext.OrganizationId, requestContext.UserAccountId);
        }
        catch (Exception)
        {
            return await ProblemAsync(context, 503, "INTERNAL_ERROR", "Calendar event write access is temporarily unavailable.", true);
        }
        if (current is null)
        {
            return await NotVisibleAsync(context);
        }

        var body = await ReadBodyAsync(context, cancellationToken);
        if (body is null || !TryParseDocument(body, out var document))
        {
            return await ProblemAsync(context, 400, "MALFORMED_JSON", "The request body is not valid JSON.", false);
        }

        using (document)
        {
            if (!TryParsePatch(document.RootElement, current, out var model, out var error))
            {
                return await ProblemAsync(context, error.Status, error.Code, error.Title, false);
            }

            try
            {
                var updated = service.ApplyPatch(
                    requestContext.OrganizationId, eventId, expectedVersion, requestContext.UserAccountId,
                    DateTimeOffset.UtcNow, model.ProjectId, model.Title, model.Description, model.Timing,
                    model.Status, model.UserAttendees, model.ContactAttendees);
                SetEtag(context, updated.Metadata.Version);
                return Results.Json(CalendarEndpoints.ToResponse(updated));
            }
            catch (CalendarEventConcurrencyException)
            {
                return await VersionConflictAsync(context);
            }
            catch (KeyNotFoundException)
            {
                return await NotVisibleAsync(context);
            }
            catch (InvalidOperationException exception)
            {
                return await ProblemAsync(context, 409, "INVALID_STATE_TRANSITION", exception.Message, false);
            }
            catch (ArgumentException exception)
            {
                return await ProblemAsync(context, 422, "VALIDATION_FAILED", exception.Message, false);
            }
            catch (Exception)
            {
                return await ProblemAsync(context, 503, "INTERNAL_ERROR", "Calendar event write access is temporarily unavailable.", true);
            }
        }
    }

    private static Task<IResult> ArchiveAsync(HttpContext context, string id, CancellationToken cancellationToken) =>
        TransitionAsync(context, id, cancellationToken, requireIdempotencyKey: true, BodyKind.OptionalReason,
            static (service, request, eventId, version, now) =>
                service.Archive(request.OrganizationId, eventId, version, request.UserAccountId, now));

    private static Task<IResult> UnarchiveAsync(HttpContext context, string id, CancellationToken cancellationToken) =>
        TransitionAsync(context, id, cancellationToken, requireIdempotencyKey: false, BodyKind.None,
            static (service, request, eventId, version, now) =>
                service.RestoreFromArchive(request.OrganizationId, eventId, version, request.UserAccountId, now));

    private static Task<IResult> RestoreAsync(HttpContext context, string id, CancellationToken cancellationToken) =>
        TransitionAsync(context, id, cancellationToken, requireIdempotencyKey: true, BodyKind.Restore,
            static (service, request, eventId, version, now) =>
                service.RestoreFromTrash(request.OrganizationId, eventId, version, request.UserAccountId, now));

    private static async Task<IResult> DeleteAsync(HttpContext context, string id, CancellationToken cancellationToken)
    {
        var result = await TransitionAsync(context, id, cancellationToken, requireIdempotencyKey: false, BodyKind.None,
            static (service, request, eventId, version, now) =>
                service.MoveToTrash(request.OrganizationId, eventId, version, request.UserAccountId, now),
            deletionReceipt: true);
        return result;
    }

    private static async Task<IResult> TransitionAsync(
        HttpContext context,
        string id,
        CancellationToken cancellationToken,
        bool requireIdempotencyKey,
        BodyKind bodyKind,
        Func<CalendarEventLifecycleService, AuthenticatedRequestContext, Guid, int, DateTimeOffset, CalendarEvent> transition,
        bool deletionReceipt = false)
    {
        var requestContext = ReadRequestContext(context);
        var service = context.RequestServices.GetService<CalendarEventLifecycleService>();
        if (requestContext is null || service is null)
        {
            return await ProblemAsync(context, 503, "INTERNAL_ERROR", "Calendar event write access is not configured.", true);
        }

        if (!TryReadEventId(id, out var eventId))
        {
            return await NotVisibleAsync(context);
        }

        if (requireIdempotencyKey && !TryReadIdempotencyKey(context, out _))
        {
            return await ProblemAsync(context, 400, "VALIDATION_FAILED",
                "Idempotency-Key is required and must contain 8-200 printable ASCII characters.", false);
        }

        if (!TryReadIfMatch(context, out var expectedVersion, out var preconditionError))
        {
            return await ProblemAsync(context, preconditionError.Status, preconditionError.Code, preconditionError.Title, false);
        }

        if (bodyKind != BodyKind.None)
        {
            var body = await ReadBodyAsync(context, cancellationToken);
            if (body is null)
            {
                return await ProblemAsync(context, 400, "MALFORMED_JSON", "The request body is not valid JSON.", false);
            }
            if (!TryParseTransitionBody(body, bodyKind, expectedVersion, out var bodyError))
            {
                return await ProblemAsync(context, bodyError.Status, bodyError.Code, bodyError.Title, false);
            }
        }

        try
        {
            var now = DateTimeOffset.UtcNow;
            var updated = transition(service, requestContext, eventId, expectedVersion, now);
            SetEtag(context, updated.Metadata.Version);
            if (deletionReceipt)
            {
                var deletedAt = updated.Metadata.DeletedAtUtc ?? now;
                return Results.Json(new DeletionReceiptResponse(
                    updated.Metadata.Id, "calendar_event", deletedAt.UtcDateTime,
                    deletedAt.AddDays(30).UtcDateTime, updated.Metadata.Version),
                    statusCode: StatusCodes.Status202Accepted);
            }

            return Results.Json(CalendarEndpoints.ToResponse(updated));
        }
        catch (CalendarEventConcurrencyException)
        {
            return await VersionConflictAsync(context);
        }
        catch (KeyNotFoundException)
        {
            return await NotVisibleAsync(context);
        }
        catch (InvalidOperationException exception)
        {
            return await ProblemAsync(context, 409, "INVALID_STATE_TRANSITION", exception.Message, false);
        }
        catch (ArgumentException exception)
        {
            return await ProblemAsync(context, 422, "VALIDATION_FAILED", exception.Message, false);
        }
        catch (Exception)
        {
            return await ProblemAsync(context, 503, "INTERNAL_ERROR", "Calendar event write access is temporarily unavailable.", true);
        }
    }

    private static bool TryParseCreate(JsonElement root, out CalendarWriteModel model, out RequestError error)
    {
        model = null!;
        if (!TryValidateObject(root, CreateProperties, requireProperties: true, out error) ||
            !TryReadRequiredString(root, "title", out var title) ||
            !TryReadRequiredDate(root, "eventDate", out var eventDate) ||
            !TryReadRequiredBoolean(root, "isAllDay", out var isAllDay) ||
            !TryReadRequiredString(root, "timeZone", out var timeZone) ||
            !TryReadOptionalGuid(root, "projectId", null, out var projectId) ||
            !TryReadOptionalNullableString(root, "description", null, out var description) ||
            !TryReadOptionalInstant(root, "startAtUtc", null, out var startAtUtc) ||
            !TryReadOptionalInstant(root, "endAtUtc", null, out var endAtUtc) ||
            !TryReadStatus(root, CalendarEventStatus.Scheduled, out var status) ||
            !TryReadUserAttendees(root, [], out var userAttendees) ||
            !TryReadContactAttendees(root, [], out var contactAttendees))
        {
            error = ValidationError();
            return false;
        }

        return TryBuildModel(projectId, title, description, eventDate, isAllDay, startAtUtc, endAtUtc,
            timeZone, status, userAttendees, contactAttendees, out model, out error);
    }

    private static bool TryParsePatch(
        JsonElement root,
        CalendarEvent current,
        out CalendarWriteModel model,
        out RequestError error)
    {
        model = null!;
        if (!TryValidateObject(root, PatchProperties, requireProperties: true, out error) ||
            !TryReadOptionalGuid(root, "projectId", current.ProjectId, out var projectId) ||
            !TryReadOptionalNullableString(root, "description", current.Description, out var description) ||
            !TryReadOptionalString(root, "title", current.Title, out var title) ||
            !TryReadOptionalDate(root, "eventDate", current.Timing.EventDate, out var eventDate) ||
            !TryReadOptionalBoolean(root, "isAllDay", current.Timing.IsAllDay, out var isAllDay) ||
            !TryReadOptionalInstant(root, "startAtUtc", current.Timing.StartAtUtc, out var startAtUtc) ||
            !TryReadOptionalInstant(root, "endAtUtc", current.Timing.EndAtUtc, out var endAtUtc) ||
            !TryReadOptionalString(root, "timeZone", current.Timing.TimeZoneId, out var timeZone) ||
            !TryReadStatus(root, current.Status, out var status) ||
            !TryReadUserAttendees(root, current.UserAttendees, out var userAttendees) ||
            !TryReadContactAttendees(root, current.ContactAttendees, out var contactAttendees))
        {
            error = ValidationError();
            return false;
        }

        return TryBuildModel(projectId, title, description, eventDate, isAllDay, startAtUtc, endAtUtc,
            timeZone, status, userAttendees, contactAttendees, out model, out error);
    }

    private static bool TryBuildModel(
        Guid? projectId, string title, string? description, DateOnly eventDate, bool isAllDay,
        DateTimeOffset? startAtUtc, DateTimeOffset? endAtUtc, string timeZone,
        CalendarEventStatus status, IReadOnlyList<EventAttendee> userAttendees,
        IReadOnlyList<ContactAttendee> contactAttendees,
        out CalendarWriteModel model, out RequestError error)
    {
        try
        {
            var timing = CalendarEventTiming.Create(eventDate, isAllDay, startAtUtc, endAtUtc, timeZone);
            model = new(projectId, title, description, timing, status, userAttendees, contactAttendees);
            error = null!;
            return true;
        }
        catch (ArgumentException exception)
        {
            model = null!;
            error = new(422, "VALIDATION_FAILED", exception.Message);
            return false;
        }
    }

    private static bool TryValidateObject(JsonElement root, HashSet<string> allowed, bool requireProperties, out RequestError error)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            error = ValidationError();
            return false;
        }

        var properties = root.EnumerateObject().ToArray();
        if ((requireProperties && properties.Length == 0) || properties.Any(property => !allowed.Contains(property.Name)))
        {
            error = ValidationError();
            return false;
        }

        error = null!;
        return true;
    }

    private static bool TryReadUserAttendees(JsonElement root, IReadOnlyList<EventAttendee> fallback, out IReadOnlyList<EventAttendee> attendees)
    {
        attendees = fallback;
        if (!root.TryGetProperty("userAttendees", out var value)) return true;
        if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() > 500) return false;
        var parsed = new List<EventAttendee>();
        foreach (var item in value.EnumerateArray())
        {
            if (!TryReadAttendee(item, "userAccountId", out var id, out var role, out var status, out var respondedAt)) return false;
            parsed.Add(EventAttendee.Create(id, role, status, respondedAt));
        }
        attendees = parsed;
        return true;
    }

    private static bool TryReadContactAttendees(JsonElement root, IReadOnlyList<ContactAttendee> fallback, out IReadOnlyList<ContactAttendee> attendees)
    {
        attendees = fallback;
        if (!root.TryGetProperty("contactAttendees", out var value)) return true;
        if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() > 500) return false;
        var parsed = new List<ContactAttendee>();
        foreach (var item in value.EnumerateArray())
        {
            if (!TryReadAttendee(item, "contactId", out var id, out var role, out var status, out var respondedAt)) return false;
            parsed.Add(ContactAttendee.Create(id, role, status, respondedAt));
        }
        attendees = parsed;
        return true;
    }

    private static bool TryReadAttendee(
        JsonElement item, string idProperty, out Guid id, out CalendarAttendeeRole role,
        out CalendarAttendeeResponseStatus status, out DateTimeOffset? respondedAt)
    {
        id = default;
        role = default;
        status = default;
        respondedAt = null;
        if (item.ValueKind != JsonValueKind.Object) return false;
        var allowed = new HashSet<string>(StringComparer.Ordinal) { idProperty, "role", "responseStatus", "respondedAt" };
        if (item.EnumerateObject().Any(property => !allowed.Contains(property.Name)) ||
            !item.TryGetProperty(idProperty, out var idValue) || idValue.ValueKind != JsonValueKind.String ||
            !Guid.TryParseExact(idValue.GetString(), "D", out id) || id == Guid.Empty ||
            !item.TryGetProperty("role", out var roleValue) || roleValue.ValueKind != JsonValueKind.String ||
            !TryParseRole(roleValue.GetString(), out role) ||
            !item.TryGetProperty("responseStatus", out var statusValue) || statusValue.ValueKind != JsonValueKind.String ||
            !TryParseResponseStatus(statusValue.GetString(), out status)) return false;

        if (item.TryGetProperty("respondedAt", out var respondedValue))
        {
            if (respondedValue.ValueKind == JsonValueKind.Null) return true;
            if (!TryParseInstant(respondedValue, out var parsed)) return false;
            respondedAt = parsed;
        }
        return true;
    }

    private static bool TryParseRole(string? value, out CalendarAttendeeRole role)
    {
        role = value switch
        {
            "required" => CalendarAttendeeRole.Required,
            "optional" => CalendarAttendeeRole.Optional,
            "observer" => CalendarAttendeeRole.Observer,
            _ => default,
        };
        return value is "required" or "optional" or "observer";
    }

    private static bool TryParseResponseStatus(string? value, out CalendarAttendeeResponseStatus status)
    {
        status = value switch
        {
            "pending" => CalendarAttendeeResponseStatus.Pending,
            "accepted" => CalendarAttendeeResponseStatus.Accepted,
            "declined" => CalendarAttendeeResponseStatus.Declined,
            "tentative" => CalendarAttendeeResponseStatus.Tentative,
            _ => default,
        };
        return value is "pending" or "accepted" or "declined" or "tentative";
    }

    private static bool TryReadStatus(JsonElement root, CalendarEventStatus fallback, out CalendarEventStatus status)
    {
        status = fallback;
        if (!root.TryGetProperty("status", out var value)) return true;
        if (value.ValueKind != JsonValueKind.String) return false;
        status = value.GetString() switch
        {
            "scheduled" => CalendarEventStatus.Scheduled,
            "cancelled" => CalendarEventStatus.Cancelled,
            _ => (CalendarEventStatus)(-1),
        };
        return Enum.IsDefined(status);
    }

    private static bool TryReadRequiredString(JsonElement root, string name, out string value)
    {
        value = string.Empty;
        return root.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String &&
            (value = property.GetString() ?? string.Empty).Length > 0;
    }

    private static bool TryReadOptionalString(JsonElement root, string name, string fallback, out string value)
    {
        value = fallback;
        if (!root.TryGetProperty(name, out var property)) return true;
        if (property.ValueKind != JsonValueKind.String) return false;
        value = property.GetString() ?? string.Empty;
        return value.Length > 0;
    }

    private static bool TryReadOptionalNullableString(JsonElement root, string name, string? fallback, out string? value)
    {
        value = fallback;
        if (!root.TryGetProperty(name, out var property)) return true;
        if (property.ValueKind == JsonValueKind.Null) { value = null; return true; }
        if (property.ValueKind != JsonValueKind.String) return false;
        value = property.GetString();
        return true;
    }

    private static bool TryReadOptionalGuid(JsonElement root, string name, Guid? fallback, out Guid? value)
    {
        value = fallback;
        if (!root.TryGetProperty(name, out var property)) return true;
        if (property.ValueKind == JsonValueKind.Null) { value = null; return true; }
        if (property.ValueKind != JsonValueKind.String || !Guid.TryParseExact(property.GetString(), "D", out var parsed) || parsed == Guid.Empty) return false;
        value = parsed;
        return true;
    }

    private static bool TryReadRequiredDate(JsonElement root, string name, out DateOnly value)
    {
        value = default;
        return root.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String &&
            DateOnly.TryParseExact(property.GetString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out value);
    }

    private static bool TryReadOptionalDate(JsonElement root, string name, DateOnly fallback, out DateOnly value)
    {
        value = fallback;
        return !root.TryGetProperty(name, out var property) ||
            (property.ValueKind == JsonValueKind.String && DateOnly.TryParseExact(property.GetString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out value));
    }

    private static bool TryReadRequiredBoolean(JsonElement root, string name, out bool value)
    {
        value = default;
        if (!root.TryGetProperty(name, out var property) ||
            property.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) return false;
        value = property.GetBoolean();
        return true;
    }

    private static bool TryReadOptionalBoolean(JsonElement root, string name, bool fallback, out bool value)
    {
        value = fallback;
        if (!root.TryGetProperty(name, out var property)) return true;
        if (property.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) return false;
        value = property.GetBoolean();
        return true;
    }

    private static bool TryReadOptionalInstant(JsonElement root, string name, DateTimeOffset? fallback, out DateTimeOffset? value)
    {
        value = fallback;
        if (!root.TryGetProperty(name, out var property)) return true;
        if (property.ValueKind == JsonValueKind.Null) { value = null; return true; }
        if (!TryParseInstant(property, out var parsed)) return false;
        value = parsed;
        return true;
    }

    private static bool TryParseInstant(JsonElement property, out DateTimeOffset value)
    {
        value = default;
        var text = property.ValueKind == JsonValueKind.String ? property.GetString() : null;
        return text is not null && text.EndsWith('Z') &&
            DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out value) &&
            value.Offset == TimeSpan.Zero;
    }

    private static bool TryParseTransitionBody(string body, BodyKind kind, int expectedVersion, out RequestError error)
    {
        error = new(400, "MALFORMED_JSON", "The request body is not valid JSON.");
        if (!TryParseDocument(body, out var document)) return false;
        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object) { error = ValidationError(); return false; }
            var allowed = kind == BodyKind.Restore
                ? new HashSet<string>(StringComparer.Ordinal) { "reason", "expectedVersion" }
                : new HashSet<string>(StringComparer.Ordinal) { "reason" };
            if (document.RootElement.EnumerateObject().Any(property => !allowed.Contains(property.Name)) ||
                !TryReadOptionalNullableString(document.RootElement, "reason", null, out var reason) ||
                (reason?.Length ?? 0) > 2000)
            {
                error = ValidationError();
                return false;
            }
            if (kind == BodyKind.Restore &&
                (!document.RootElement.TryGetProperty("expectedVersion", out var version) ||
                 version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out var parsed) || parsed != expectedVersion))
            {
                error = ValidationError("expectedVersion must match If-Match.");
                return false;
            }
            error = null!;
            return true;
        }
    }

    private static async Task<string?> ReadBodyAsync(HttpContext context, CancellationToken cancellationToken)
    {
        try
        {
            using var reader = new StreamReader(context.Request.Body);
            return await reader.ReadToEndAsync(cancellationToken);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { return null; }
    }

    private static bool TryParseDocument(string body, out JsonDocument document)
    {
        try { document = JsonDocument.Parse(body); return true; }
        catch (JsonException) { document = null!; return false; }
    }

    private static bool TryReadEventId(string id, out Guid eventId) =>
        Guid.TryParseExact(id, "D", out eventId) && eventId != Guid.Empty;

    private static bool TryReadIdempotencyKey(HttpContext context, out string key)
    {
        key = context.Request.Headers["Idempotency-Key"].ToString();
        return key.Length is >= 8 and <= 200 && key.All(character => character is >= ' ' and <= '~');
    }

    private static bool TryReadIfMatch(HttpContext context, out int expectedVersion, out RequestError error)
    {
        expectedVersion = 0;
        var values = context.Request.Headers.IfMatch;
        if (values.Count == 0)
        {
            error = new(428, "PRECONDITION_REQUIRED", "The If-Match header is required.");
            return false;
        }
        var entries = values.ToString().Split(',', StringSplitOptions.TrimEntries);
        var tag = entries.Length == 1 ? entries[0] : string.Empty;
        if (tag.Length < 4 || tag[0] != '"' || tag[^1] != '"' || tag[1] != 'v' ||
            tag[2] == '0' ||
            !int.TryParse(tag.AsSpan(2, tag.Length - 3), NumberStyles.None, CultureInfo.InvariantCulture, out expectedVersion) ||
            expectedVersion < 1)
        {
            error = ValidationError("If-Match must be a single strong entity tag of the form \"v<positive-integer>\".");
            return false;
        }
        error = null!;
        return true;
    }

    private static void SetEtag(HttpContext context, int version) =>
        context.Response.Headers.ETag = $"\"v{version.ToString(CultureInfo.InvariantCulture)}\"";

    private static AuthenticatedRequestContext? ReadRequestContext(HttpContext context) =>
        context.Items.TryGetValue(TaskJwtAuthenticationHandler.AuthenticatedRequestContextItemName, out var value)
            ? value as AuthenticatedRequestContext
            : null;

    private static Task<IResult> NotVisibleAsync(HttpContext context) =>
        ProblemAsync(context, 404, "OBJECT_NOT_VISIBLE", "The requested object is absent or not visible.", false);

    private static Task<IResult> VersionConflictAsync(HttpContext context) =>
        ProblemAsync(context, 412, "VERSION_CONFLICT", "The calendar event version does not match If-Match.", false);

    private static RequestError ValidationError(string title = "The calendar event request is invalid.") =>
        new(400, "VALIDATION_FAILED", title);

    private static async Task<IResult> ProblemAsync(HttpContext context, int status, string code, string title, bool retryable)
    {
        await TaskApiProblemResponse.WriteAsync(context, status, code, title, retryable);
        return Results.Empty;
    }

    private enum BodyKind { None, OptionalReason, Restore }

    private sealed record CalendarWriteModel(
        Guid? ProjectId,
        string Title,
        string? Description,
        CalendarEventTiming Timing,
        CalendarEventStatus Status,
        IReadOnlyList<EventAttendee> UserAttendees,
        IReadOnlyList<ContactAttendee> ContactAttendees);

    private sealed record RequestError(int Status, string Code, string Title);

    private sealed record DeletionReceiptResponse(
        [property: JsonPropertyName("objectId")] Guid ObjectId,
        [property: JsonPropertyName("objectType")] string ObjectType,
        [property: JsonPropertyName("deletedAt")] DateTime DeletedAt,
        [property: JsonPropertyName("purgeAfter")] DateTime PurgeAfter,
        [property: JsonPropertyName("version")] int Version);
}
