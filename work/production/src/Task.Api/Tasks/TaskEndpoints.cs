using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Task.Api.Security;
using Task.Application;
using Task.Application.Security;
using Task.Domain;

namespace Task.Api.Tasks;

/// <summary>
/// Read-only Task API adapter. The backing policy deliberately bridges the public
/// Task.Read capability to the existing task.manage permission until granular task
/// permissions are introduced. Organization and lifecycle visibility remain enforced
/// by <see cref="ITaskReadStore"/>.
/// </summary>
internal static class TaskEndpoints
{
    private const string TasksRoute = "/api/v1/tasks";
    private const string TaskByIdRoute = "/api/v1/tasks/{id}";

    public static IEndpointRouteBuilder MapTaskEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapGet(TasksRoute, GetTasksAsync)
            .RequireAuthorization(TaskPermissionAuthorization.TaskReadPolicyName);

        app.MapPost(TasksRoute, CreateTaskAsync)
            .RequireAuthorization(TaskPermissionAuthorization.TaskCreatePolicyName);

        app.MapGet(TaskByIdRoute, GetTaskByIdAsync)
            .RequireAuthorization(TaskPermissionAuthorization.TaskReadPolicyName);

        app.MapPatch(TaskByIdRoute, PatchTaskAsync)
            .RequireAuthorization(TaskPermissionAuthorization.TaskUpdatePolicyName);

        return app;
    }

    private static readonly HashSet<string> AllowedCreateProperties = new(StringComparer.Ordinal)
    {
        "title", "priority", "startAtUtc", "deadlineAt", "authorUserId",
    };

    private static async Task<IResult> GetTasksAsync(
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var requestContext = ReadRequestContext(context);
        if (requestContext is null)
        {
            return await WriteProblemAsync(
                context,
                StatusCodes.Status500InternalServerError,
                "INTERNAL_ERROR",
                "The authenticated request context is unavailable.",
                retryable: true);
        }

        var readStore = context.RequestServices.GetService<ITaskReadStore>();
        if (readStore is null)
        {
            return await WriteProblemAsync(
                context,
                StatusCodes.Status503ServiceUnavailable,
                "INTERNAL_ERROR",
                "Task read access is not configured.",
                retryable: true);
        }

        if (!TryReadListQuery(context.Request.Query, out var cursor))
        {
            return await WriteProblemAsync(
                context,
                StatusCodes.Status400BadRequest,
                "VALIDATION_FAILED",
                "This Task API increment supports only cursor pagination without filter or sort.",
                retryable: false);
        }

        try
        {
            var page = await readStore.GetPageAsync(
                new TaskReadPageRequest(
                    requestContext.OrganizationId,
                    requestContext.UserAccountId,
                    requestContext.AuthorizationScopeVersion,
                    cursor),
                cancellationToken);

            return Results.Json(new TaskPageResponse(
                page.Items.Select(ToResponse).ToArray(),
                page.NextCursor,
                page.Total));
        }
        catch (TaskReadCursorException)
        {
            return await WriteProblemAsync(
                context,
                StatusCodes.Status400BadRequest,
                "SEARCH_CURSOR_INVALID",
                "The search cursor is invalid.",
                retryable: false);
        }
    }

    private static async Task<IResult> CreateTaskAsync(
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var requestContext = ReadRequestContext(context);
        if (requestContext is null)
        {
            return await WriteProblemAsync(
                context,
                StatusCodes.Status500InternalServerError,
                "INTERNAL_ERROR",
                "The authenticated request context is unavailable.",
                retryable: true);
        }

        var service = context.RequestServices.GetService<TaskCreateCommandService>();
        if (service is null)
        {
            return await WriteProblemAsync(
                context,
                StatusCodes.Status503ServiceUnavailable,
                "INTERNAL_ERROR",
                "Task write access is not configured.",
                retryable: true);
        }

        if (!TryReadIdempotencyKey(context.Request.Headers["Idempotency-Key"].ToString(), out var idempotencyKey))
        {
            return await WriteProblemAsync(
                context,
                StatusCodes.Status400BadRequest,
                "VALIDATION_FAILED",
                "Idempotency-Key is required and must contain 8-200 printable ASCII characters.",
                retryable: false);
        }

        string body;
        try
        {
            using var reader = new StreamReader(context.Request.Body);
            body = await reader.ReadToEndAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return await WriteProblemAsync(
                context,
                StatusCodes.Status400BadRequest,
                "MALFORMED_JSON",
                "The request body is not valid JSON.",
                retryable: false);
        }

        if (!TryParseCreateRequest(body, requestContext.UserAccountId, out var model, out var parseError))
        {
            return await WriteProblemAsync(
                context,
                parseError.Status,
                parseError.Code,
                parseError.Title,
                retryable: false);
        }

        try
        {
            var command = service.CreateCommand(
                requestContext,
                idempotencyKey,
                body,
                model,
                aggregate => new TaskWriteHttpResult(
                    StatusCodes.Status201Created,
                    new Dictionary<string, string>(StringComparer.Ordinal) { ["ETag"] = "\"v1\"" },
                    JsonSerializer.Serialize(ToResponse(aggregate)),
                    aggregate.Metadata.Id));
            return await WriteCommandResultAsync(context, await service.ExecuteAsync(command, cancellationToken));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return await WriteProblemAsync(
                context,
                StatusCodes.Status503ServiceUnavailable,
                "INTERNAL_ERROR",
                "Task write access is temporarily unavailable.",
                retryable: true);
        }
    }

    private static async Task<IResult> GetTaskByIdAsync(
        HttpContext context,
        string id,
        CancellationToken cancellationToken)
    {
        var requestContext = ReadRequestContext(context);
        if (requestContext is null)
        {
            return await WriteProblemAsync(
                context,
                StatusCodes.Status500InternalServerError,
                "INTERNAL_ERROR",
                "The authenticated request context is unavailable.",
                retryable: true);
        }

        var readStore = context.RequestServices.GetService<ITaskReadStore>();
        if (readStore is null)
        {
            return await WriteProblemAsync(
                context,
                StatusCodes.Status503ServiceUnavailable,
                "INTERNAL_ERROR",
                "Task read access is not configured.",
                retryable: true);
        }

        if (!Guid.TryParseExact(id, "D", out var taskId) || taskId == Guid.Empty)
        {
            return await WriteObjectNotVisibleAsync(context);
        }

        var task = await readStore.GetByIdAsync(
            requestContext.OrganizationId,
            taskId,
            cancellationToken);
        if (task is null)
        {
            return await WriteObjectNotVisibleAsync(context);
        }

        context.Response.Headers.ETag = $"\"v{task.Version.ToString(CultureInfo.InvariantCulture)}\"";
        return Results.Json(ToResponse(task));
    }

    private static bool TryReadListQuery(IQueryCollection query, out string? cursor)
    {
        cursor = Normalize(query["cursor"].ToString());
        var filter = Normalize(query["filter"].ToString());
        var sort = Normalize(query["sort"].ToString());
        var pageValue = Normalize(query["page"].ToString());

        if (filter is not null || sort is not null)
        {
            return false;
        }

        return pageValue is null
            || int.TryParse(pageValue, NumberStyles.None, CultureInfo.InvariantCulture, out var page)
                && page == 1;
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static AuthenticatedRequestContext? ReadRequestContext(HttpContext context) =>
        context.Items.TryGetValue(
            TaskJwtAuthenticationHandler.AuthenticatedRequestContextItemName,
            out var value)
            && value is AuthenticatedRequestContext requestContext
                ? requestContext
                : null;

    private static bool TryReadIdempotencyKey(string? value, out string key)
    {
        key = value ?? string.Empty;
        return key.Length is >= 8 and <= 200 && key.All(character => character is >= '!' and <= '~');
    }

    private static bool TryParseCreateRequest(
        string body,
        Guid actorUserId,
        out TaskCreateModel model,
        out CreateRequestError error)
    {
        model = null!;
        error = null!;
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(body);
        }
        catch (JsonException)
        {
            error = new(StatusCodes.Status400BadRequest, "MALFORMED_JSON", "The request body is not valid JSON.");
            return false;
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                error = ValidationFailed("The request body must be a JSON object.");
                return false;
            }

            string? title = null;
            var priority = TaskPriority.Normal;
            var prioritySpecified = false;
            DateTimeOffset? startAtUtc = null;
            DateTimeOffset? deadlineAt = null;
            Guid? authorUserId = null;
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (!AllowedCreateProperties.Contains(property.Name))
                {
                    error = ValidationFailed("The request contains an unsupported property.");
                    return false;
                }

                switch (property.Name)
                {
                    case "title":
                        if (property.Value.ValueKind != JsonValueKind.String)
                        {
                            error = ValidationFailed("title must be a string.");
                            return false;
                        }

                        title = property.Value.GetString();
                        break;
                    case "priority":
                        if (property.Value.ValueKind == JsonValueKind.Null)
                        {
                            break;
                        }

                        if (property.Value.ValueKind != JsonValueKind.String ||
                            !TryParsePriority(property.Value.GetString(), out priority))
                        {
                            error = ValidationFailed("priority is invalid.");
                            return false;
                        }

                        prioritySpecified = true;
                        break;
                    case "startAtUtc":
                        if (property.Value.ValueKind == JsonValueKind.Null)
                        {
                            break;
                        }

                        if (!TryReadUtcInstant(property.Value, out startAtUtc))
                        {
                            error = ValidationFailed("startAtUtc must be an RFC 3339 UTC instant with an explicit Z.");
                            return false;
                        }

                        break;
                    case "deadlineAt":
                        if (property.Value.ValueKind == JsonValueKind.Null)
                        {
                            break;
                        }

                        if (!TryReadUtcInstant(property.Value, out deadlineAt))
                        {
                            error = ValidationFailed("deadlineAt must be an RFC 3339 UTC instant with an explicit Z.");
                            return false;
                        }

                        break;
                    case "authorUserId":
                        if (property.Value.ValueKind == JsonValueKind.Null)
                        {
                            break;
                        }

                        if (!TryReadGuid(property.Value, out authorUserId))
                        {
                            error = ValidationFailed("authorUserId is invalid.");
                            return false;
                        }

                        break;
                }
            }

            var normalizedTitle = title?.Trim();
            if (string.IsNullOrEmpty(normalizedTitle) || normalizedTitle.Length > 500)
            {
                error = ValidationFailed("title must contain 1-500 characters.");
                return false;
            }

            if (authorUserId is not null && authorUserId.Value != actorUserId)
            {
                error = new(StatusCodes.Status403Forbidden, "FORBIDDEN", "The requested author does not match the authenticated user.");
                return false;
            }

            if (startAtUtc is not null && deadlineAt is not null && deadlineAt.Value < startAtUtc.Value)
            {
                error = new(
                    StatusCodes.Status422UnprocessableEntity,
                    "VALIDATION_FAILED",
                    "Deadline must not be earlier than the scheduled start.");
                return false;
            }

            model = new TaskCreateModel(normalizedTitle, priority, prioritySpecified, startAtUtc, deadlineAt);
            return true;
        }
    }

    private static bool TryParsePriority(string? value, out TaskPriority priority)
    {
        priority = value switch
        {
            "low" => TaskPriority.Low,
            "normal" => TaskPriority.Normal,
            "high" => TaskPriority.High,
            "critical" => TaskPriority.Critical,
            _ => (TaskPriority)(-1),
        };
        return Enum.IsDefined(priority);
    }

    private static bool TryReadUtcInstant(JsonElement element, out DateTimeOffset? instant)
    {
        instant = null;
        if (element.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        var value = element.GetString();
        if (string.IsNullOrWhiteSpace(value) ||
            !value.EndsWith('Z') ||
            !DateTimeOffset.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var parsed) ||
            parsed.Offset != TimeSpan.Zero)
        {
            return false;
        }

        instant = parsed;
        return true;
    }

    private static bool TryReadGuid(JsonElement element, out Guid? value)
    {
        value = null;
        if (element.ValueKind == JsonValueKind.String &&
            Guid.TryParseExact(element.GetString(), "D", out var parsed) &&
            parsed != Guid.Empty)
        {
            value = parsed;
            return true;
        }

        return false;
    }

    private static async Task<IResult> WriteCommandResultAsync(
        HttpContext context,
        TaskWriteCommandExecutionResult executed)
    {
        switch (executed.Disposition)
        {
            case TaskWriteCommandDisposition.Executed:
            case TaskWriteCommandDisposition.Replayed:
                if (executed.HttpResult is null)
                {
                    return await WriteProblemAsync(
                        context,
                        StatusCodes.Status500InternalServerError,
                        "INTERNAL_ERROR",
                        "The write command returned no HTTP result.",
                        retryable: true);
                }

                context.Response.StatusCode = executed.HttpResult.StatusCode;
                foreach (var header in executed.HttpResult.Headers)
                {
                    context.Response.Headers[header.Key] = header.Value;
                }

                context.Response.Headers["Idempotency-Replayed"] = executed.IsReplay ? "true" : "false";
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync(executed.HttpResult.BodyJson);
                return Results.Empty;
            case TaskWriteCommandDisposition.IdempotencyKeyReused:
                return await WriteProblemAsync(
                    context,
                    StatusCodes.Status409Conflict,
                    "IDEMPOTENCY_KEY_REUSED",
                    "The idempotency key was reused with a different request.",
                    retryable: false);
            case TaskWriteCommandDisposition.RequestInProgress:
                var retryAfterSeconds = Math.Max(
                    1,
                    (int)Math.Ceiling((executed.RetryAfter ?? TimeSpan.FromSeconds(1)).TotalSeconds));
                context.Response.Headers.RetryAfter = retryAfterSeconds.ToString(CultureInfo.InvariantCulture);
                return await WriteProblemAsync(
                    context,
                    StatusCodes.Status409Conflict,
                    "IDEMPOTENCY_REQUEST_IN_PROGRESS",
                    "A request with this idempotency key is already in progress.",
                    retryable: true,
                    retryAfterSeconds);
            default:
                return await WriteProblemAsync(
                    context,
                    StatusCodes.Status500InternalServerError,
                    "INTERNAL_ERROR",
                    "The write command returned an unknown disposition.",
                    retryable: true);
        }
    }

    private static async Task<IResult> PatchTaskAsync(
        HttpContext context,
        string id,
        CancellationToken cancellationToken)
    {
        var requestContext = ReadRequestContext(context);
        if (requestContext is null)
        {
            return await WriteProblemAsync(
                context,
                StatusCodes.Status500InternalServerError,
                "INTERNAL_ERROR",
                "The authenticated request context is unavailable.",
                retryable: true);
        }

        var service = context.RequestServices.GetService<TaskUpdateCommandService>();
        if (service is null)
        {
            return await WriteProblemAsync(
                context,
                StatusCodes.Status503ServiceUnavailable,
                "INTERNAL_ERROR",
                "Task write access is not configured.",
                retryable: true);
        }

        if (!TryReadIdempotencyKey(context.Request.Headers["Idempotency-Key"].ToString(), out var idempotencyKey))
        {
            return await WriteProblemAsync(
                context,
                StatusCodes.Status400BadRequest,
                "VALIDATION_FAILED",
                "Idempotency-Key is required and must contain 8-200 printable ASCII characters.",
                retryable: false);
        }

        if (!TryReadIfMatch(context.Request.Headers.IfMatch, out var expectedVersion, out var ifMatchError))
        {
            return await WriteProblemAsync(
                context,
                ifMatchError.Status,
                ifMatchError.Code,
                ifMatchError.Title,
                retryable: false);
        }

        if (!Guid.TryParseExact(id, "D", out var taskId) || taskId == Guid.Empty)
        {
            return await WriteObjectNotVisibleAsync(context);
        }

        string body;
        try
        {
            using var reader = new StreamReader(context.Request.Body);
            body = await reader.ReadToEndAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return await WriteProblemAsync(
                context,
                StatusCodes.Status400BadRequest,
                "MALFORMED_JSON",
                "The request body is not valid JSON.",
                retryable: false);
        }

        if (!TryParsePatchRequest(body, out var model, out var parseError))
        {
            return await WriteProblemAsync(
                context,
                parseError.Status,
                parseError.Code,
                parseError.Title,
                retryable: false);
        }

        try
        {
            var preparation = service.CreateCommand(
                requestContext,
                idempotencyKey,
                body,
                taskId,
                expectedVersion,
                model,
                aggregate => new TaskWriteHttpResult(
                    StatusCodes.Status200OK,
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["ETag"] = $"\"v{aggregate.Metadata.Version.ToString(CultureInfo.InvariantCulture)}\"",
                    },
                    JsonSerializer.Serialize(ToResponse(aggregate)),
                    aggregate.Metadata.Id));

            if (preparation.Command.ChangedFields.Count == 0)
            {
                context.Response.Headers.ETag =
                    $"\"v{preparation.Current.Metadata.Version.ToString(CultureInfo.InvariantCulture)}\"";
                context.Response.Headers["Idempotency-Replayed"] = "false";
                return Results.Json(ToResponse(preparation.Current));
            }

            return await WriteCommandResultAsync(
                context,
                await service.ExecuteAsync(preparation.Command, cancellationToken));
        }
        catch (TaskUpdateConflictException conflict)
        {
            return await WriteProblemAsync(
                context,
                StatusCodes.Status409Conflict,
                conflict.ProblemCode,
                conflict.Message,
                retryable: false);
        }
        catch (TaskLifecycleConcurrencyException)
        {
            return await WriteProblemAsync(
                context,
                StatusCodes.Status412PreconditionFailed,
                "VERSION_CONFLICT",
                "The task version does not match the provided If-Match value.",
                retryable: false);
        }
        catch (KeyNotFoundException)
        {
            return await WriteObjectNotVisibleAsync(context);
        }
        catch (InvalidOperationException)
        {
            return await WriteProblemAsync(
                context,
                StatusCodes.Status409Conflict,
                "INVALID_STATE_TRANSITION",
                "The task is not in a state that allows this update.",
                retryable: false);
        }
        catch (ArgumentException)
        {
            return await WriteProblemAsync(
                context,
                StatusCodes.Status422UnprocessableEntity,
                "VALIDATION_FAILED",
                "The requested values are not valid for this task.",
                retryable: false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return await WriteProblemAsync(
                context,
                StatusCodes.Status503ServiceUnavailable,
                "INTERNAL_ERROR",
                "Task write access is temporarily unavailable.",
                retryable: true);
        }
    }

    private static bool TryReadIfMatch(
        Microsoft.Extensions.Primitives.StringValues ifMatch,
        out int expectedVersion,
        out PatchRequestError error)
    {
        expectedVersion = 0;
        if (ifMatch.Count == 0)
        {
            error = new(
                StatusCodes.Status428PreconditionRequired,
                "PRECONDITION_REQUIRED",
                "The If-Match header is required.");
            return false;
        }

        var values = ifMatch.ToString().Split(',', StringSplitOptions.TrimEntries);
        var digits = values.Length == 1 && values[0].Length >= 4
            ? values[0].AsSpan(2, values[0].Length - 3)
            : default;
        if (values.Length != 1 ||
            values[0].Length < 4 ||
            values[0][0] != '"' ||
            values[0][^1] != '"' ||
            values[0][1] != 'v' ||
            values[0][2] == '0' ||
            digits.IndexOfAnyExceptInRange('0', '9') >= 0 ||
            !long.TryParse(values[0].Substring(2, values[0].Length - 3), out var version) ||
            version > int.MaxValue)
        {
            error = new(
                StatusCodes.Status400BadRequest,
                "VALIDATION_FAILED",
                "If-Match must be a single strong entity tag of the form \"v<positive-integer>\".");
            return false;
        }

        expectedVersion = (int)version;
        error = null!;
        return true;
    }

    private static bool TryParsePatchRequest(
        string body,
        out TaskUpdateModel model,
        out PatchRequestError error)
    {
        model = null!;
        error = null!;
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(body);
        }
        catch (JsonException)
        {
            error = new(StatusCodes.Status400BadRequest, "MALFORMED_JSON", "The request body is not valid JSON.");
            return false;
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                error = PatchValidationFailed(StatusCodes.Status400BadRequest, "The request body must be a JSON object.");
                return false;
            }

            string? title = null;
            TaskPriority? priority = null;
            OptionalInstant startsAtUtc = OptionalInstant.Unspecified;
            OptionalInstant deadlineAt = OptionalInstant.Unspecified;
            var hasProperty = false;
            foreach (var property in document.RootElement.EnumerateObject())
            {
                hasProperty = true;
                if (!AllowedPatchProperties.Contains(property.Name))
                {
                    error = PatchValidationFailed(
                        StatusCodes.Status400BadRequest,
                        "The request contains an unsupported property.");
                    return false;
                }

                switch (property.Name)
                {
                    case "title":
                        if (property.Value.ValueKind != JsonValueKind.String)
                        {
                            error = PatchValidationFailed(StatusCodes.Status400BadRequest, "title must be a string.");
                            return false;
                        }

                        title = property.Value.GetString();
                        break;
                    case "priority":
                        if (property.Value.ValueKind != JsonValueKind.String ||
                            !TryParsePriority(property.Value.GetString(), out var parsedPriority))
                        {
                            error = PatchValidationFailed(StatusCodes.Status400BadRequest, "priority is invalid.");
                            return false;
                        }

                        priority = parsedPriority;
                        break;
                    case "startAtUtc":
                        if (!TryReadOptionalInstant(property.Value, out startsAtUtc))
                        {
                            error = PatchValidationFailed(
                                StatusCodes.Status400BadRequest,
                                "startAtUtc must be null or an RFC 3339 UTC instant with an explicit Z.");
                            return false;
                        }

                        break;
                    case "deadlineAt":
                        if (!TryReadOptionalInstant(property.Value, out deadlineAt))
                        {
                            error = PatchValidationFailed(
                                StatusCodes.Status400BadRequest,
                                "deadlineAt must be null or an RFC 3339 UTC instant with an explicit Z.");
                            return false;
                        }

                        break;
                }
            }

            if (!hasProperty)
            {
                error = PatchValidationFailed(StatusCodes.Status400BadRequest, "The request body must not be empty.");
                return false;
            }

            var normalizedTitle = title?.Trim();
            if (normalizedTitle is not null &&
                (string.IsNullOrEmpty(normalizedTitle) || normalizedTitle.Length > 500))
            {
                error = PatchValidationFailed(
                    StatusCodes.Status422UnprocessableEntity,
                    "title must contain 1-500 characters.");
                return false;
            }

            model = new TaskUpdateModel(normalizedTitle, priority, startsAtUtc, deadlineAt);
            return true;
        }
    }

    private static readonly HashSet<string> AllowedPatchProperties = new(StringComparer.Ordinal)
    {
        "title", "priority", "startAtUtc", "deadlineAt",
    };

    private static bool TryReadOptionalInstant(JsonElement element, out OptionalInstant instant)
    {
        if (element.ValueKind == JsonValueKind.Null)
        {
            instant = OptionalInstant.Clear();
            return true;
        }

        if (TryReadUtcInstant(element, out var value))
        {
            instant = OptionalInstant.Set(value!.Value);
            return true;
        }

        instant = default;
        return false;
    }

    private static PatchRequestError PatchValidationFailed(int status, string title) =>
        new(status, "VALIDATION_FAILED", title);

    private static CreateRequestError ValidationFailed(string title) =>
        new(StatusCodes.Status400BadRequest, "VALIDATION_FAILED", title);

    private static TaskResponse ToResponse(TaskAggregate task) =>
        ToResponse(new TaskReadProjection(
            task.Metadata.Id,
            task.Metadata.OrganizationId,
            task.Metadata.Version,
            task.Metadata.CreatedAtUtc,
            task.Metadata.UpdatedAtUtc,
            task.Title,
            task.Metadata.CreatedBy,
            task.WorkStatus,
            task.Priority,
            task.Schedule.StartsAtUtc,
            task.Schedule.DeadlineUtc));

    private static TaskResponse ToResponse(TaskReadProjection task) =>
        new(
            task.Id,
            task.OrganizationId,
            task.Version,
            task.CreatedAtUtc.UtcDateTime,
            task.UpdatedAtUtc.UtcDateTime,
            ProjectId: null,
            ParentTaskId: null,
            task.Title,
            Description: null,
            task.AuthorUserId,
            RequesterUserId: null,
            PrimaryCounterpartyObjectId: null,
            ToContractValue(task.Status),
            ToContractValue(task.Priority),
            ScheduledDate: null,
            StartTimeLocal: null,
            ScheduleTimeZone: null,
            task.StartAtUtc?.UtcDateTime,
            PlannedDurationMinutes: null,
            task.DeadlineAtUtc?.UtcDateTime,
            AssigneeIds: [],
            WatcherIds: [],
            RecurrenceSeriesId: null);

    private static string ToContractValue(TaskWorkStatus status) => status switch
    {
        TaskWorkStatus.New => "new",
        TaskWorkStatus.InProgress => "in_progress",
        TaskWorkStatus.Review => "review",
        TaskWorkStatus.Completed => "completed",
        TaskWorkStatus.Cancelled => "cancelled",
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };

    private static string ToContractValue(TaskPriority priority) => priority switch
    {
        TaskPriority.Low => "low",
        TaskPriority.Normal => "normal",
        TaskPriority.High => "high",
        TaskPriority.Critical => "critical",
        _ => throw new ArgumentOutOfRangeException(nameof(priority)),
    };

    private static Task<IResult> WriteObjectNotVisibleAsync(HttpContext context) =>
        WriteProblemAsync(
            context,
            StatusCodes.Status404NotFound,
            "OBJECT_NOT_VISIBLE",
            "The requested object is absent or not visible.",
            retryable: false);

    private static async Task<IResult> WriteProblemAsync(
        HttpContext context,
        int statusCode,
        string code,
        string title,
        bool retryable,
        int? retryAfterSeconds = null)
    {
        await TaskApiProblemResponse.WriteAsync(context, statusCode, code, title, retryable, retryAfterSeconds);
        return Results.Empty;
    }

    private sealed record CreateRequestError(int Status, string Code, string Title);

    private sealed record PatchRequestError(int Status, string Code, string Title);

    internal sealed record TaskResponse(
        [property: JsonPropertyName("id")] Guid Id,
        [property: JsonPropertyName("organizationId")] Guid OrganizationId,
        [property: JsonPropertyName("version")] long Version,
        [property: JsonPropertyName("createdAt")] DateTime CreatedAt,
        [property: JsonPropertyName("updatedAt")] DateTime UpdatedAt,
        [property: JsonPropertyName("projectId")] Guid? ProjectId,
        [property: JsonPropertyName("parentTaskId")] Guid? ParentTaskId,
        [property: JsonPropertyName("title")] string Title,
        [property: JsonPropertyName("description")] string? Description,
        [property: JsonPropertyName("authorUserId")] Guid AuthorUserId,
        [property: JsonPropertyName("requesterUserId")] Guid? RequesterUserId,
        [property: JsonPropertyName("primaryCounterpartyObjectId")] Guid? PrimaryCounterpartyObjectId,
        [property: JsonPropertyName("status")] string Status,
        [property: JsonPropertyName("priority")] string Priority,
        [property: JsonPropertyName("scheduledDate")] DateOnly? ScheduledDate,
        [property: JsonPropertyName("startTimeLocal")] TimeOnly? StartTimeLocal,
        [property: JsonPropertyName("scheduleTimeZone")] string? ScheduleTimeZone,
        [property: JsonPropertyName("startAtUtc")] DateTime? StartAtUtc,
        [property: JsonPropertyName("plannedDurationMinutes")] int? PlannedDurationMinutes,
        [property: JsonPropertyName("deadlineAt")] DateTime? DeadlineAt,
        [property: JsonPropertyName("assigneeIds")] IReadOnlyList<Guid> AssigneeIds,
        [property: JsonPropertyName("watcherIds")] IReadOnlyList<Guid> WatcherIds,
        [property: JsonPropertyName("recurrenceSeriesId")] Guid? RecurrenceSeriesId);

    internal sealed record TaskPageResponse(
        [property: JsonPropertyName("items")] IReadOnlyList<TaskResponse> Items,
        [property: JsonPropertyName("nextCursor")] string? NextCursor,
        [property: JsonPropertyName("total")] long? Total);
}
