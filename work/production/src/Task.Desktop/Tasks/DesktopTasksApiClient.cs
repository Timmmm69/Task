using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using Task.Desktop.Security;

namespace Task.Desktop.TaskApi;

public enum DesktopTaskStatus
{
    New,
    InProgress,
    Review,
    Completed,
    Cancelled,
}

public enum DesktopTaskPriority
{
    Low,
    Normal,
    High,
    Critical,
}

/// <summary>Validated canonical Task data consumed by the desktop presentation layer.</summary>
public sealed record DesktopTaskDto(
    Guid Id,
    Guid OrganizationId,
    long Version,
    DateTimeOffset? CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc,
    string Title,
    Guid AuthorUserId,
    DesktopTaskStatus Status,
    DesktopTaskPriority Priority,
    DateTimeOffset? StartAtUtc,
    DateTimeOffset? DeadlineAtUtc,
    IReadOnlyList<Guid> AssigneeIds,
    IReadOnlyList<Guid> WatcherIds,
    Guid? RecurrenceSeriesId);

public sealed record DesktopTaskPage(
    IReadOnlyList<DesktopTaskDto> Items,
    string? NextCursor,
    long? Total);

/// <summary>Read-only Task API boundary used by the desktop presentation layer.</summary>
public interface IDesktopTasksApiClient
{
    global::System.Threading.Tasks.Task<DesktopTasksApiResult<DesktopTaskPage>> GetTasksAsync(
        string? cursor = null,
        CancellationToken cancellationToken = default);

    global::System.Threading.Tasks.Task<DesktopTasksApiResult<DesktopTaskDto>> GetTaskByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default);
}

/// <summary>Typed Task API outcome; cancellation is propagated as an exception.</summary>
public abstract record DesktopTasksApiResult<T>
    where T : class
{
    private DesktopTasksApiResult()
    {
    }

    public sealed record Succeeded(T Value) : DesktopTasksApiResult<T>;

    public sealed record AuthenticationFailure : DesktopTasksApiResult<T>;

    public sealed record Forbidden : DesktopTasksApiResult<T>;

    public sealed record NotFound : DesktopTasksApiResult<T>;

    public sealed record InvalidCursor : DesktopTasksApiResult<T>;

    public sealed record ServerUnavailable : DesktopTasksApiResult<T>;

    public sealed record MalformedResponse : DesktopTasksApiResult<T>;
}

/// <summary>Read-only desktop client for GET /api/v1/tasks and GET /api/v1/tasks/{id}.</summary>
public sealed class DesktopTasksApiClient : IDesktopTasksApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly DesktopAuthenticatedGetExecutor _executor;
    private readonly Uri _tasksUri;

    public DesktopTasksApiClient(
        HttpClient httpClient,
        Uri serverEndpoint,
        SessionService sessionService)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(serverEndpoint);
        ArgumentNullException.ThrowIfNull(sessionService);
        if (!serverEndpoint.IsAbsoluteUri)
        {
            throw new ArgumentException("The server endpoint must be absolute.", nameof(serverEndpoint));
        }

        _executor = new DesktopAuthenticatedGetExecutor(httpClient, sessionService);
        _tasksUri = new Uri($"{serverEndpoint.AbsoluteUri.TrimEnd('/')}/api/v1/tasks", UriKind.Absolute);
    }

    public async global::System.Threading.Tasks.Task<DesktopTasksApiResult<DesktopTaskPage>> GetTasksAsync(
        string? cursor = null,
        CancellationToken cancellationToken = default)
    {
        var requestUri = string.IsNullOrWhiteSpace(cursor)
            ? _tasksUri
            : new Uri($"{_tasksUri.AbsoluteUri}?cursor={Uri.EscapeDataString(cursor.Trim())}");
        var response = await _executor
            .GetAsync(requestUri, Guid.NewGuid().ToString("D"), cancellationToken)
            .ConfigureAwait(false);
        return MapPageResult(response);
    }

    public async global::System.Threading.Tasks.Task<DesktopTasksApiResult<DesktopTaskDto>> GetTaskByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("The task id must not be empty.", nameof(id));
        }

        var requestUri = new Uri($"{_tasksUri.AbsoluteUri}/{id:D}");
        var response = await _executor
            .GetAsync(requestUri, Guid.NewGuid().ToString("D"), cancellationToken)
            .ConfigureAwait(false);
        return MapTaskResult(response);
    }

    private static DesktopTasksApiResult<DesktopTaskPage> MapPageResult(
        AuthenticatedGetResult result) => result switch
    {
        AuthenticatedGetResult.Response response when response.StatusCode == HttpStatusCode.OK =>
            TryReadPage(response.Body, out var page)
                ? new DesktopTasksApiResult<DesktopTaskPage>.Succeeded(page)
                : new DesktopTasksApiResult<DesktopTaskPage>.MalformedResponse(),
        _ => MapFailure<DesktopTaskPage>(result),
    };

    private static DesktopTasksApiResult<DesktopTaskDto> MapTaskResult(
        AuthenticatedGetResult result) => result switch
    {
        AuthenticatedGetResult.Response response when response.StatusCode == HttpStatusCode.OK =>
            TryReadTask(response.Body, out var task)
                ? new DesktopTasksApiResult<DesktopTaskDto>.Succeeded(task)
                : new DesktopTasksApiResult<DesktopTaskDto>.MalformedResponse(),
        _ => MapFailure<DesktopTaskDto>(result),
    };

    private static DesktopTasksApiResult<T> MapFailure<T>(AuthenticatedGetResult result)
        where T : class => result switch
    {
        AuthenticatedGetResult.AuthenticationFailure =>
            new DesktopTasksApiResult<T>.AuthenticationFailure(),
        AuthenticatedGetResult.ServerUnavailable =>
            new DesktopTasksApiResult<T>.ServerUnavailable(),
        AuthenticatedGetResult.MalformedResponse =>
            new DesktopTasksApiResult<T>.MalformedResponse(),
        AuthenticatedGetResult.Response { StatusCode: HttpStatusCode.Forbidden } =>
            new DesktopTasksApiResult<T>.Forbidden(),
        AuthenticatedGetResult.Response { StatusCode: HttpStatusCode.NotFound } =>
            new DesktopTasksApiResult<T>.NotFound(),
        AuthenticatedGetResult.Response response when IsServerFailure(response.StatusCode) =>
            new DesktopTasksApiResult<T>.ServerUnavailable(),
        AuthenticatedGetResult.Response response
            when response.StatusCode == HttpStatusCode.BadRequest
                && HasProblemCode(response.Body, "SEARCH_CURSOR_INVALID") =>
            new DesktopTasksApiResult<T>.InvalidCursor(),
        _ => new DesktopTasksApiResult<T>.MalformedResponse(),
    };

    private static bool IsServerFailure(HttpStatusCode statusCode) => (int)statusCode >= 500;

    private static bool HasProblemCode(string body, string expectedCode)
    {
        try
        {
            var problem = JsonSerializer.Deserialize<ProblemPayload>(body, JsonOptions);
            return string.Equals(problem?.Code, expectedCode, StringComparison.Ordinal);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TryReadPage(string body, out DesktopTaskPage page)
    {
        page = null!;
        TaskPagePayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<TaskPagePayload>(body, JsonOptions);
        }
        catch (JsonException)
        {
            return false;
        }

        if (payload?.Items is null
            || payload.Items.Count > 500
            || payload.NextCursor?.Length > 512
            || payload.Total < 0)
        {
            return false;
        }

        var items = new List<DesktopTaskDto>(payload.Items.Count);
        foreach (var taskPayload in payload.Items)
        {
            if (taskPayload is null || !TryMapTask(taskPayload, out var task))
            {
                return false;
            }

            items.Add(task);
        }

        page = new DesktopTaskPage(items.ToArray(), payload.NextCursor, payload.Total);
        return true;
    }

    private static bool TryReadTask(string body, out DesktopTaskDto task)
    {
        task = null!;
        TaskPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<TaskPayload>(body, JsonOptions);
        }
        catch (JsonException)
        {
            return false;
        }

        return payload is not null && TryMapTask(payload, out task);
    }

    private static bool TryMapTask(TaskPayload payload, out DesktopTaskDto task)
    {
        task = null!;
        if (payload.Id == Guid.Empty
            || payload.OrganizationId == Guid.Empty
            || payload.Version < 1
            || string.IsNullOrWhiteSpace(payload.Title)
            || payload.Title.Length > 500
            || payload.AuthorUserId == Guid.Empty
            || !TryMapStatus(payload.Status, out var status)
            || !TryMapPriority(payload.Priority, out var priority)
            || !IsUtc(payload.CreatedAt)
            || !IsUtc(payload.UpdatedAt)
            || !IsUtc(payload.StartAtUtc)
            || !IsUtc(payload.DeadlineAt)
            || !IsValidIds(payload.AssigneeIds)
            || !IsValidIds(payload.WatcherIds)
            || payload.RecurrenceSeriesId == Guid.Empty)
        {
            return false;
        }

        task = new DesktopTaskDto(
            payload.Id,
            payload.OrganizationId,
            payload.Version,
            payload.CreatedAt,
            payload.UpdatedAt,
            payload.Title,
            payload.AuthorUserId,
            status,
            priority,
            payload.StartAtUtc,
            payload.DeadlineAt,
            payload.AssigneeIds!.ToArray(),
            payload.WatcherIds!.ToArray(),
            payload.RecurrenceSeriesId);
        return true;
    }

    private static bool IsUtc(DateTimeOffset? value) => !value.HasValue || value.Value.Offset == TimeSpan.Zero;

    private static bool IsValidIds(IReadOnlyList<Guid>? ids) => ids is not null
        && ids.Count <= 100
        && ids.All(id => id != Guid.Empty)
        && ids.Distinct().Count() == ids.Count;

    private static bool TryMapStatus(string? value, out DesktopTaskStatus status)
    {
        status = value switch
        {
            "new" => DesktopTaskStatus.New,
            "in_progress" => DesktopTaskStatus.InProgress,
            "review" => DesktopTaskStatus.Review,
            "completed" => DesktopTaskStatus.Completed,
            "cancelled" => DesktopTaskStatus.Cancelled,
            _ => default,
        };
        return value is "new" or "in_progress" or "review" or "completed" or "cancelled";
    }

    private static bool TryMapPriority(string? value, out DesktopTaskPriority priority)
    {
        priority = value switch
        {
            "low" => DesktopTaskPriority.Low,
            "normal" => DesktopTaskPriority.Normal,
            "high" => DesktopTaskPriority.High,
            "critical" => DesktopTaskPriority.Critical,
            _ => default,
        };
        return value is "low" or "normal" or "high" or "critical";
    }

    private sealed class TaskPagePayload
    {
        [JsonPropertyName("items")]
        public List<TaskPayload?>? Items { get; init; }

        [JsonPropertyName("nextCursor")]
        public string? NextCursor { get; init; }

        [JsonPropertyName("total")]
        public long? Total { get; init; }
    }

    private sealed class TaskPayload
    {
        public Guid Id { get; init; }

        public Guid OrganizationId { get; init; }

        public long Version { get; init; }

        public DateTimeOffset? CreatedAt { get; init; }

        public DateTimeOffset? UpdatedAt { get; init; }

        public string? Title { get; init; }

        public Guid AuthorUserId { get; init; }

        public string? Status { get; init; }

        public string? Priority { get; init; }

        public DateTimeOffset? StartAtUtc { get; init; }

        public DateTimeOffset? DeadlineAt { get; init; }

        public List<Guid>? AssigneeIds { get; init; }

        public List<Guid>? WatcherIds { get; init; }

        public Guid? RecurrenceSeriesId { get; init; }
    }

    private sealed class ProblemPayload
    {
        [JsonPropertyName("code")]
        public string? Code { get; init; }
    }
}
