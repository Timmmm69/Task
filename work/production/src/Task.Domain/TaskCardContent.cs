using System.Text.Json;
using System.Text.Json.Nodes;

namespace Task.Domain;

/// <summary>Immutable, versioned task-card values. References are checked in the write transaction.</summary>
public sealed record TaskCardContent
{
    public string? Description { get; init; }
    public Guid? ProjectId { get; init; }
    public Guid? ParentTaskId { get; init; }
    public Guid? RequesterUserId { get; init; }
    public Guid? PrimaryCounterpartyObjectId { get; init; }
    public DateOnly? ScheduledDate { get; init; }
    public TimeOnly? StartTimeLocal { get; init; }
    public string? ScheduleTimeZone { get; init; }
    public int? PlannedDurationMinutes { get; init; }
    public IReadOnlyList<Guid> AssigneeIds { get; init; } = [];
    public IReadOnlyList<Guid> WatcherIds { get; init; } = [];

    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    public static readonly IReadOnlySet<string> Fields = new HashSet<string>(StringComparer.Ordinal)
    {
        "description", "projectId", "parentTaskId", "requesterUserId", "primaryCounterpartyObjectId",
        "scheduledDate", "startTimeLocal", "scheduleTimeZone", "plannedDurationMinutes", "assigneeIds", "watcherIds"
    };

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);
    public static TaskCardContent FromJson(string json) =>
        JsonSerializer.Deserialize<TaskCardContent>(json, JsonOptions) ?? throw new ArgumentException("Task card is required.");

    public TaskCardContent Apply(string? patch)
    {
        if (patch is null) return this;
        var values = JsonNode.Parse(ToJson())!.AsObject();
        foreach (var (key, value) in JsonNode.Parse(patch)!.AsObject())
        {
            if (!Fields.Contains(key)) throw new ArgumentException("Unsupported task card field.");
            values[key] = value?.DeepClone();
        }
        try { return FromJson(values.ToJsonString()); }
        catch (JsonException exception) { throw new ArgumentException("Invalid task card field.", exception); }
    }

    public void Validate(DateTimeOffset? startAtUtc)
    {
        if (Description?.Length > 50000) throw new ArgumentException("Description is too long.");
        if (new[] { ProjectId, ParentTaskId, RequesterUserId, PrimaryCounterpartyObjectId }.Any(id => id == Guid.Empty))
            throw new ArgumentException("A related identifier is empty.");
        foreach (var ids in new[] { AssigneeIds, WatcherIds })
            if (ids is null || ids.Count > 100 || ids.Any(id => id == Guid.Empty) || ids.Distinct().Count() != ids.Count)
                throw new ArgumentException("Participants must be unique valid identifiers (maximum 100).");
        if (PlannedDurationMinutes is <= 0 or > 10080) throw new ArgumentException("Duration is outside the accepted range.");
        if (StartTimeLocal is not null)
        {
            if (ScheduledDate is null || string.IsNullOrWhiteSpace(ScheduleTimeZone))
                throw new ArgumentException("A local start requires a date and time zone.");
            TimeZoneInfo zone;
            try { zone = TimeZoneInfo.FindSystemTimeZoneById(ScheduleTimeZone); }
            catch (Exception e) when (e is TimeZoneNotFoundException or InvalidTimeZoneException)
            { throw new ArgumentException("Unknown schedule time zone.", e); }
            var local = ScheduledDate.Value.ToDateTime(StartTimeLocal.Value, DateTimeKind.Unspecified);
            if (zone.IsInvalidTime(local) || zone.IsAmbiguousTime(local))
                throw new ArgumentException("Local start is invalid or ambiguous in this time zone.");
            if (startAtUtc != new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, zone)))
                throw new ArgumentException("Local schedule and UTC start must agree.");
        }
        else if (ScheduledDate is not null && startAtUtc is not null)
            throw new ArgumentException("A date-only task cannot have a UTC start.");
    }
}
