using Task.Domain;
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
    Guid? RecurrenceSeriesId, TaskCardContent? Card = null, DateTimeOffset? CompletedAtUtc = null);

public sealed record DesktopTaskPage(
    IReadOnlyList<DesktopTaskDto> Items,
    string? NextCursor,
    long? Total);

public readonly record struct DesktopTaskField<T>(bool IsSpecified, T? Value)
{
    public static DesktopTaskField<T> From(T? value) => new(true, value);
}

public sealed record DesktopCreateTaskCommand
{
    public DesktopCreateTaskCommand(
        string title,
        DesktopTaskPriority priority,
        DateTimeOffset? startAtUtc = null,
        DateTimeOffset? deadlineAtUtc = null, TaskCardContent? card = null)
    {
        DesktopTasksApiClient.ValidateCreateCommand(title, priority, startAtUtc, deadlineAtUtc);
        Title = title;
        Priority = priority;
        StartAtUtc = startAtUtc;
        DeadlineAtUtc = deadlineAtUtc;
        Card = card;
    }

    public string Title { get; }
    public DesktopTaskPriority Priority { get; }
    public DateTimeOffset? StartAtUtc { get; }
    public DateTimeOffset? DeadlineAtUtc { get; }
    public TaskCardContent? Card { get; }
    public string IdempotencyKey { get; } = Guid.NewGuid().ToString("D");
}

public sealed record DesktopPatchTaskCommand
{
    public DesktopPatchTaskCommand(
        Guid id,
        long expectedVersion,
        DesktopTaskField<string> title = default,
        DesktopTaskField<DesktopTaskPriority> priority = default,
        DesktopTaskField<DateTimeOffset?> startAtUtc = default,
        DesktopTaskField<DateTimeOffset?> deadlineAtUtc = default, string? cardPatch = null)
    {
        DesktopTasksApiClient.ValidatePatchCommand(
            id, expectedVersion, title, priority, startAtUtc, deadlineAtUtc, cardPatch is not null);
        Id = id;
        ExpectedVersion = expectedVersion;
        Title = title;
        Priority = priority;
        StartAtUtc = startAtUtc;
        DeadlineAtUtc = deadlineAtUtc;
        CardPatch = cardPatch;
    }

    public Guid Id { get; }
    public long ExpectedVersion { get; }
    public DesktopTaskField<string> Title { get; }
    public DesktopTaskField<DesktopTaskPriority> Priority { get; }
    public DesktopTaskField<DateTimeOffset?> StartAtUtc { get; }
    public DesktopTaskField<DateTimeOffset?> DeadlineAtUtc { get; }
    public string? CardPatch { get; }
    public string IdempotencyKey { get; } = Guid.NewGuid().ToString("D");
}

public sealed record DesktopTransitionTaskCommand
{
    public DesktopTransitionTaskCommand(
        Guid id,
        long expectedVersion,
        DesktopTaskStatus targetStatus,
        string? reason = null)
    {
        DesktopTasksApiClient.ValidateTransitionCommand(id, expectedVersion, targetStatus, reason);
        Id = id;
        ExpectedVersion = expectedVersion;
        TargetStatus = targetStatus;
        Reason = reason;
    }

    public Guid Id { get; }
    public long ExpectedVersion { get; }
    public DesktopTaskStatus TargetStatus { get; }
    public string? Reason { get; }
    public string IdempotencyKey { get; } = Guid.NewGuid().ToString("D");
}

/// <summary>Typed Task API boundary used by the desktop presentation layer.</summary>
public interface IDesktopTasksApiClient : IDesktopTaskWriteApiClient
{
    global::System.Threading.Tasks.Task<DesktopTasksApiResult<DesktopTaskPage>> GetTasksAsync(
        string? cursor = null,
        CancellationToken cancellationToken = default);

    global::System.Threading.Tasks.Task<DesktopTasksApiResult<DesktopTaskDto>> GetTaskByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default);
}

/// <summary>Typed Task write boundary; UI code never handles HTTP status or raw JSON.</summary>
public interface IDesktopTaskWriteApiClient
{
    global::System.Threading.Tasks.Task<DesktopTaskWriteResult<DesktopTaskDto>> CreateTaskAsync(
        DesktopCreateTaskCommand command,
        CancellationToken cancellationToken = default);

    global::System.Threading.Tasks.Task<DesktopTaskWriteResult<DesktopTaskDto>> PatchTaskAsync(
        DesktopPatchTaskCommand command,
        CancellationToken cancellationToken = default);

    global::System.Threading.Tasks.Task<DesktopTaskWriteResult<DesktopTaskDto>> TransitionTaskAsync(
        DesktopTransitionTaskCommand command,
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

public abstract record DesktopTaskWriteResult<T>
    where T : class
{
    private DesktopTaskWriteResult()
    {
    }

    public sealed record Succeeded(T Value, long Version, bool WasReplayed) : DesktopTaskWriteResult<T>;
    public sealed record AuthenticationFailure : DesktopTaskWriteResult<T>;
    public sealed record Forbidden : DesktopTaskWriteResult<T>;
    public sealed record NotFound : DesktopTaskWriteResult<T>;
    public sealed record ValidationFailure(
        string Message,
        IReadOnlyDictionary<string, IReadOnlyList<string>> FieldErrors) : DesktopTaskWriteResult<T>;
    public sealed record VersionConflict : DesktopTaskWriteResult<T>;
    public sealed record PreconditionRequired : DesktopTaskWriteResult<T>;
    public sealed record IdempotencyConflict : DesktopTaskWriteResult<T>;
    public sealed record RequestInProgress : DesktopTaskWriteResult<T>;
    public sealed record InvalidTransition : DesktopTaskWriteResult<T>;
    public sealed record ServerUnavailable : DesktopTaskWriteResult<T>;
    public sealed record MalformedResponse : DesktopTaskWriteResult<T>;
}

/// <summary>Desktop client for the typed Task read/write API boundary.</summary>
public sealed partial class DesktopTasksApiClient : IDesktopTasksApiClient
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

    public async global::System.Threading.Tasks.Task<DesktopTaskWriteResult<DesktopTaskDto>> CreateTaskAsync(
        DesktopCreateTaskCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var title = ValidateTitle(command.Title);
        var createBody = JsonSerializer.SerializeToNode(new
        {
            title,
            priority = ToContractValue(command.Priority),
            startAtUtc = command.StartAtUtc?.UtcDateTime,
            deadlineAt = command.DeadlineAtUtc?.UtcDateTime,
        }, JsonOptions);
        if (command.Card is not null)
            foreach (var field in System.Text.Json.Nodes.JsonNode.Parse(command.Card.ToJson())!.AsObject()) createBody![field.Key] = field.Value?.DeepClone();
        var body = JsonSerializer.SerializeToUtf8Bytes(createBody, JsonOptions);
        var response = await SendWriteAsync(
            HttpMethod.Post, _tasksUri, body, null, command.IdempotencyKey, cancellationToken);
        return MapWriteResult(response, HttpStatusCode.Created);
    }

    public async global::System.Threading.Tasks.Task<DesktopTaskWriteResult<DesktopTaskDto>> PatchTaskAsync(
        DesktopPatchTaskCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var body = new Dictionary<string, object?>(4);
        if (command.CardPatch is not null)
        {
            using var patch = JsonDocument.Parse(command.CardPatch);
            foreach (var field in patch.RootElement.EnumerateObject()) body[field.Name] = field.Value.Clone();
        }
        if (command.Title.IsSpecified)
        {
            body["title"] = ValidateTitle(command.Title.Value);
        }

        if (command.Priority.IsSpecified)
        {
            body["priority"] = ToContractValue(command.Priority.Value);
        }

        if (command.StartAtUtc.IsSpecified)
        {
            body["startAtUtc"] = command.StartAtUtc.Value?.UtcDateTime;
        }

        if (command.DeadlineAtUtc.IsSpecified)
        {
            body["deadlineAt"] = command.DeadlineAtUtc.Value?.UtcDateTime;
        }

        var response = await SendWriteAsync(
            HttpMethod.Patch,
            new Uri($"{_tasksUri.AbsoluteUri}/{command.Id:D}"),
            JsonSerializer.SerializeToUtf8Bytes(body, JsonOptions),
            FormatEntityTag(command.ExpectedVersion),
            command.IdempotencyKey,
            cancellationToken);
        return MapWriteResult(response, HttpStatusCode.OK);
    }

    public async global::System.Threading.Tasks.Task<DesktopTaskWriteResult<DesktopTaskDto>> TransitionTaskAsync(
        DesktopTransitionTaskCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var reason = command.Reason?.Trim();

        var body = JsonSerializer.SerializeToUtf8Bytes(new
        {
            targetStatus = ToContractValue(command.TargetStatus),
            reason,
        }, JsonOptions);
        var response = await SendWriteAsync(
            HttpMethod.Post,
            new Uri($"{_tasksUri.AbsoluteUri}/{command.Id:D}/transition"),
            body,
            FormatEntityTag(command.ExpectedVersion),
            command.IdempotencyKey,
            cancellationToken);
        return MapWriteResult(response, HttpStatusCode.OK);
    }

    private async global::System.Threading.Tasks.Task<AuthenticatedGetResult> SendWriteAsync(
        HttpMethod method,
        Uri uri,
        byte[] body,
        string? ifMatch,
        string idempotencyKey,
        CancellationToken cancellationToken) =>
        await _executor.SendAsync(
            method,
            uri,
            body,
            Guid.NewGuid().ToString("D"),
            ifMatch,
            idempotencyKey,
            cancellationToken).ConfigureAwait(false);

    private static DesktopTaskWriteResult<DesktopTaskDto> MapWriteResult(
        AuthenticatedGetResult result,
        HttpStatusCode successStatus) => result switch
        {
            AuthenticatedGetResult.Response response when response.StatusCode == successStatus =>
                TryReadTask(response.Body, out var task)
                && TryReadEntityTag(response.EntityTag, out var version)
                && version == task.Version
                && TryReadReplay(response.IdempotencyReplayed, out var wasReplayed)
                    ? new DesktopTaskWriteResult<DesktopTaskDto>.Succeeded(task, version, wasReplayed)
                    : new DesktopTaskWriteResult<DesktopTaskDto>.MalformedResponse(),
            AuthenticatedGetResult.AuthenticationFailure =>
                new DesktopTaskWriteResult<DesktopTaskDto>.AuthenticationFailure(),
            AuthenticatedGetResult.ServerUnavailable =>
                new DesktopTaskWriteResult<DesktopTaskDto>.ServerUnavailable(),
            AuthenticatedGetResult.Response { StatusCode: HttpStatusCode.Forbidden } =>
                new DesktopTaskWriteResult<DesktopTaskDto>.Forbidden(),
            AuthenticatedGetResult.Response { StatusCode: HttpStatusCode.NotFound } =>
                new DesktopTaskWriteResult<DesktopTaskDto>.NotFound(),
            AuthenticatedGetResult.Response response when IsServerFailure(response.StatusCode) =>
                new DesktopTaskWriteResult<DesktopTaskDto>.ServerUnavailable(),
            AuthenticatedGetResult.Response response => MapWriteProblem(response),
            _ => new DesktopTaskWriteResult<DesktopTaskDto>.MalformedResponse(),
        };

    private static DesktopTaskWriteResult<DesktopTaskDto> MapWriteProblem(
        AuthenticatedGetResult.Response response)
    {
        var problem = TryReadProblem(response.Body);
        return (response.StatusCode, problem?.Code) switch
        {
            (HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity,
                "VALIDATION_FAILED" or "MALFORMED_JSON") =>
                new DesktopTaskWriteResult<DesktopTaskDto>.ValidationFailure(
                    "Проверьте введённые данные.", CopyFieldErrors(problem?.Errors)),
            (HttpStatusCode.PreconditionFailed, "VERSION_CONFLICT") =>
                new DesktopTaskWriteResult<DesktopTaskDto>.VersionConflict(),
            ((HttpStatusCode)428, _) =>
                new DesktopTaskWriteResult<DesktopTaskDto>.PreconditionRequired(),
            (HttpStatusCode.Conflict, "IDEMPOTENCY_KEY_REUSED") =>
                new DesktopTaskWriteResult<DesktopTaskDto>.IdempotencyConflict(),
            (HttpStatusCode.Conflict, "IDEMPOTENCY_REQUEST_IN_PROGRESS") =>
                new DesktopTaskWriteResult<DesktopTaskDto>.RequestInProgress(),
            (HttpStatusCode.Conflict,
                "INVALID_STATE_TRANSITION" or "OBJECT_ARCHIVED" or "OBJECT_DELETED") =>
                new DesktopTaskWriteResult<DesktopTaskDto>.InvalidTransition(),
            _ => new DesktopTaskWriteResult<DesktopTaskDto>.MalformedResponse(),
        };
    }

    private static ProblemPayload? TryReadProblem(string body)
    {
        try
        {
            return JsonSerializer.Deserialize<ProblemPayload>(body, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<string>> CopyFieldErrors(
        Dictionary<string, string[]>? errors) => (errors ?? [])
            .Where(pair => pair.Key.Length <= 100
                && pair.Value is { Length: <= 20 }
                && pair.Value.All(message => message is { Length: <= 500 }))
            .ToDictionary(
                pair => pair.Key,
                pair => (IReadOnlyList<string>)Array.AsReadOnly(pair.Value.ToArray()),
                StringComparer.Ordinal);

    private static bool TryReadEntityTag(string? value, out long version)
    {
        version = 0;
        return value is { Length: >= 4 }
            && value[0] == '"' && value[1] == 'v' && value[^1] == '"'
            && value[2] != '0'
            && value.AsSpan(2, value.Length - 3).IndexOfAnyExceptInRange('0', '9') < 0
            && long.TryParse(value.AsSpan(2, value.Length - 3), out version)
            && version > 0;
    }

    private static bool TryReadReplay(string? value, out bool wasReplayed)
    {
        wasReplayed = value == "true";
        return wasReplayed || value == "false";
    }

    private static string FormatEntityTag(long version) =>
        $"\"v{version.ToString(System.Globalization.CultureInfo.InvariantCulture)}\"";

    internal static void ValidateCreateCommand(
        string title,
        DesktopTaskPriority priority,
        DateTimeOffset? startAtUtc,
        DateTimeOffset? deadlineAtUtc)
    {
        _ = ValidateTitle(title);
        ValidatePriority(priority);
        ValidateSchedule(startAtUtc, deadlineAtUtc);
    }

    internal static void ValidatePatchCommand(
        Guid id,
        long expectedVersion,
        DesktopTaskField<string> title,
        DesktopTaskField<DesktopTaskPriority> priority,
        DesktopTaskField<DateTimeOffset?> startAtUtc,
        DesktopTaskField<DateTimeOffset?> deadlineAtUtc, bool hasCardPatch = false)
    {
        ValidateIdentity(id, expectedVersion);
        Require(title.IsSpecified || priority.IsSpecified
            || startAtUtc.IsSpecified || deadlineAtUtc.IsSpecified || hasCardPatch,
            "At least one patch field must be specified.");
        if (title.IsSpecified)
        {
            _ = ValidateTitle(title.Value);
        }

        if (priority.IsSpecified)
        {
            ValidatePriority(priority.Value);
        }

        if (startAtUtc.IsSpecified)
        {
            ValidateUtc(startAtUtc.Value);
        }

        if (deadlineAtUtc.IsSpecified)
        {
            ValidateUtc(deadlineAtUtc.Value);
        }

        if (startAtUtc.IsSpecified && deadlineAtUtc.IsSpecified)
        {
            ValidateSchedule(startAtUtc.Value, deadlineAtUtc.Value);
        }
    }

    internal static void ValidateTransitionCommand(
        Guid id,
        long expectedVersion,
        DesktopTaskStatus targetStatus,
        string? reason)
    {
        ValidateIdentity(id, expectedVersion);
        Require(Enum.IsDefined(targetStatus), "The target status is invalid.");
        Require(reason?.Trim().Length is not > 2000, "The transition reason must not exceed 2000 characters.");
    }

    private static void ValidateIdentity(Guid id, long version)
    {
        Require(id != Guid.Empty, "The task id must not be empty.");
        Require(version > 0, "The task version must be positive.");
    }

    private static string ValidateTitle(string? value)
    {
        var title = value?.Trim();
        Require(title is { Length: >= 1 and <= 500 }, "The task title must contain 1-500 characters.");

        return title!;
    }

    private static void ValidatePriority(DesktopTaskPriority? priority)
    {
        Require(priority.HasValue && Enum.IsDefined(priority.Value), "The task priority is invalid.");
    }

    private static void ValidateSchedule(DateTimeOffset? start, DateTimeOffset? deadline)
    {
        ValidateUtc(start);
        ValidateUtc(deadline);
        Require(!start.HasValue || deadline >= start, "The deadline must not be earlier than the start.");
    }

    private static void ValidateUtc(DateTimeOffset? value) =>
        Require(!value.HasValue || value.Value.Offset == TimeSpan.Zero, "Task instants must use UTC.");

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new ArgumentException(message);
        }
    }

    private static string ToContractValue(DesktopTaskPriority? priority) => priority switch
    {
        DesktopTaskPriority.Low => "low",
        DesktopTaskPriority.Normal => "normal",
        DesktopTaskPriority.High => "high",
        DesktopTaskPriority.Critical => "critical",
        _ => throw new ArgumentOutOfRangeException(nameof(priority)),
    };

    private static string ToContractValue(DesktopTaskStatus status) => status switch
    {
        DesktopTaskStatus.New => "new",
        DesktopTaskStatus.InProgress => "in_progress",
        DesktopTaskStatus.Review => "review",
        DesktopTaskStatus.Completed => "completed",
        DesktopTaskStatus.Cancelled => "cancelled",
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };

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
            payload.RecurrenceSeriesId, payload.Card, payload.CompletedAt);
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
        public DateTimeOffset? CompletedAt { get; init; }
        public string? Description { get; init; }
        public Guid? ProjectId { get; init; }
        public Guid? ParentTaskId { get; init; }
        public Guid? RequesterUserId { get; init; }
        public Guid? PrimaryCounterpartyObjectId { get; init; }
        public DateOnly? ScheduledDate { get; init; }
        public TimeOnly? StartTimeLocal { get; init; }
        public string? ScheduleTimeZone { get; init; }
        public int? PlannedDurationMinutes { get; init; }
        public TaskCardContent Card => new()
        {
            Description = Description,
            ProjectId = ProjectId,
            ParentTaskId = ParentTaskId,
            RequesterUserId = RequesterUserId,
            PrimaryCounterpartyObjectId = PrimaryCounterpartyObjectId,
            ScheduledDate = ScheduledDate,
            StartTimeLocal = StartTimeLocal,
            ScheduleTimeZone = ScheduleTimeZone,
            PlannedDurationMinutes = PlannedDurationMinutes,
            AssigneeIds = AssigneeIds ?? [],
            WatcherIds = WatcherIds ?? []
        };
    }

    private sealed class ProblemPayload
    {
        [JsonPropertyName("code")]
        public string? Code { get; init; }

        [JsonPropertyName("errors")]
        public Dictionary<string, string[]>? Errors { get; init; }
    }
}
