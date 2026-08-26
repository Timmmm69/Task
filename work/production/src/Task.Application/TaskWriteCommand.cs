using System.Security.Cryptography;
using System.Text.Json;
using Task.Domain;

namespace Task.Application;

/// <summary>
/// Executes one Task mutation together with its audit entry, domain event, outbox message,
/// and durable HTTP response. Implementations must commit all five effects atomically.
/// </summary>
public interface ITaskWriteCommandExecutor
{
    /// <summary>Executes or safely replays a tenant-scoped Task command.</summary>
    global::System.Threading.Tasks.Task<TaskWriteCommandExecutionResult> ExecuteAsync(
        TaskWriteCommand command,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Complete input for one Task write. A null <see cref="ExpectedVersion"/> denotes create;
/// otherwise the executor loads the current aggregate and enforces that exact version before
/// invoking <see cref="Mutation"/> inside the database transaction.
/// </summary>
public sealed class TaskWriteCommand
{
    private readonly byte[] _requestHash;

    /// <summary>Creates the complete, immutable input for one Task write unit of work.</summary>
    public TaskWriteCommand(
        Guid organizationId,
        Guid actorUserId,
        Guid? actorSessionId,
        string operationId,
        Guid correlationId,
        string idempotencyKey,
        byte[] requestHash,
        Guid taskId,
        long? expectedVersion,
        string auditAction,
        string eventType,
        IReadOnlyList<string> changedFields,
        string safePayloadJson,
        TaskWriteMutation mutation)
    {
        OrganizationId = organizationId;
        ActorUserId = actorUserId;
        ActorSessionId = actorSessionId;
        OperationId = operationId;
        CorrelationId = correlationId;
        IdempotencyKey = idempotencyKey;
        _requestHash = requestHash?.ToArray() ?? throw new ArgumentNullException(nameof(requestHash));
        TaskId = taskId;
        ExpectedVersion = expectedVersion;
        AuditAction = auditAction;
        EventType = eventType;
        ChangedFields = Array.AsReadOnly(
            changedFields?.ToArray() ?? throw new ArgumentNullException(nameof(changedFields)));
        SafePayloadJson = safePayloadJson;
        Mutation = mutation ?? throw new ArgumentNullException(nameof(mutation));
    }

    /// <summary>Tenant that owns every row written by the command.</summary>
    public Guid OrganizationId { get; }
    /// <summary>Authenticated user that owns the idempotency scope and audit action.</summary>
    public Guid ActorUserId { get; }
    /// <summary>Authenticated session when one is available for audit attribution.</summary>
    public Guid? ActorSessionId { get; }
    /// <summary>Stable OpenAPI operation identifier.</summary>
    public string OperationId { get; }
    /// <summary>Correlation identifier shared by audit, event and response.</summary>
    public Guid CorrelationId { get; }
    /// <summary>Validated opaque replay key.</summary>
    public string IdempotencyKey { get; }
    /// <summary>Defensive copy of the 32-byte normalized request SHA-256.</summary>
    public byte[] RequestHash => _requestHash.ToArray();
    /// <summary>Task aggregate identifier.</summary>
    public Guid TaskId { get; }
    /// <summary>Expected aggregate version, or null for create.</summary>
    public long? ExpectedVersion { get; }
    /// <summary>Stable audit action code.</summary>
    public string AuditAction { get; }
    /// <summary>Stable Task domain-event type.</summary>
    public string EventType { get; }
    /// <summary>Non-sensitive field names changed by the command.</summary>
    public IReadOnlyList<string> ChangedFields { get; }
    /// <summary>Validated non-sensitive JSON persisted in audit and event records.</summary>
    public string SafePayloadJson { get; }
    /// <summary>Aggregate mutation invoked only after idempotency acquisition.</summary>
    public TaskWriteMutation Mutation { get; }
}

/// <summary>Changes a loaded Task and produces the HTTP result to persist before commit.</summary>
public delegate TaskWriteMutationResult TaskWriteMutation(TaskAggregate? current);

/// <summary>
/// The aggregate state, serializable response and fields actually changed by a Task mutation.
/// A null <see cref="ChangedFields"/> preserves the command-level field list; an empty list
/// explicitly marks a durable no-op that must not create aggregate, audit, event or outbox effects.
/// </summary>
public sealed record TaskWriteMutationResult(
    TaskAggregate Aggregate,
    TaskWriteHttpResult HttpResult,
    IReadOnlyList<string>? ChangedFields = null);

/// <summary>A durable HTTP result. Headers and JSON body are stored and replayed as one unit.</summary>
public sealed record TaskWriteHttpResult(
    int StatusCode,
    IReadOnlyDictionary<string, string> Headers,
    string BodyJson,
    Guid? ResourceId);

/// <summary>Outcome of idempotency acquisition and optional durable HTTP result.</summary>
public sealed record TaskWriteCommandExecutionResult(
    TaskWriteCommandDisposition Disposition,
    TaskWriteHttpResult? HttpResult,
    TimeSpan? RetryAfter = null)
{
    /// <summary>True when <see cref="HttpResult"/> was loaded from durable storage.</summary>
    public bool IsReplay => Disposition == TaskWriteCommandDisposition.Replayed;
}

/// <summary>Stable outcomes understood by future Task write endpoints.</summary>
public enum TaskWriteCommandDisposition
{
    /// <summary>The mutation and all side effects committed.</summary>
    Executed,
    /// <summary>A previously committed response was loaded without executing the mutation.</summary>
    Replayed,
    /// <summary>The scoped key exists with another normalized request hash.</summary>
    IdempotencyKeyReused,
    /// <summary>Another owner holds an unexpired lease for the scoped key.</summary>
    RequestInProgress,
}

/// <summary>Canonical JSON hashing and sensitive-field validation for Task write payloads.</summary>
public static class TaskWriteRequestHasher
{
    private static readonly string[] SensitiveNameFragments =
    [
        "access_token", "accesstoken", "refresh_token", "refreshtoken", "password",
        "passwd", "cookie", "connectionstring", "connection_string", "secret", "authorization",
    ];

    /// <summary>
    /// Computes SHA-256 after recursively ordering object properties. Array order is preserved.
    /// Payloads containing credential- or secret-like property names are rejected.
    /// </summary>
    public static byte[] ComputeSha256(string json)
    {
        using var document = ParseAndValidateSafeJson(json, nameof(json));
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = false }))
        {
            WriteCanonical(document.RootElement, writer);
        }

        return SHA256.HashData(stream.ToArray());
    }

    /// <summary>Validates JSON intended for audit and event persistence without logging it.</summary>
    public static void ValidateSafePayload(string json, string parameterName = "json")
    {
        using var _ = ParseAndValidateSafeJson(json, parameterName);
    }

    private static JsonDocument ParseAndValidateSafeJson(string json, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new ArgumentException("JSON payload must not be empty.", parameterName);
        }

        var document = JsonDocument.Parse(json);
        ValidateElement(document.RootElement, parameterName);
        return document;
    }

    private static void ValidateElement(JsonElement element, string parameterName)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                var normalizedName = property.Name.Replace("-", "_", StringComparison.Ordinal).ToLowerInvariant();
                if (SensitiveNameFragments.Any(fragment => normalizedName.Contains(fragment, StringComparison.Ordinal)))
                {
                    throw new ArgumentException("JSON payload contains a sensitive field name.", parameterName);
                }

                ValidateElement(property.Value, parameterName);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                ValidateElement(item, parameterName);
            }
        }
    }

    private static void WriteCanonical(JsonElement element, Utf8JsonWriter writer)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            writer.WriteStartObject();
            foreach (var property in element.EnumerateObject().OrderBy(item => item.Name, StringComparer.Ordinal))
            {
                writer.WritePropertyName(property.Name);
                WriteCanonical(property.Value, writer);
            }

            writer.WriteEndObject();
            return;
        }

        if (element.ValueKind == JsonValueKind.Array)
        {
            writer.WriteStartArray();
            foreach (var item in element.EnumerateArray())
            {
                WriteCanonical(item, writer);
            }

            writer.WriteEndArray();
            return;
        }

        element.WriteTo(writer);
    }
}
