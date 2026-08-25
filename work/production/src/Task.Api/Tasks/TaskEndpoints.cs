using System.Globalization;
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

        app.MapGet(TaskByIdRoute, GetTaskByIdAsync)
            .RequireAuthorization(TaskPermissionAuthorization.TaskReadPolicyName);

        return app;
    }

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
        bool retryable)
    {
        await TaskApiProblemResponse.WriteAsync(context, statusCode, code, title, retryable);
        return Results.Empty;
    }

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
