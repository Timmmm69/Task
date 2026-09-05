using System.Text.Json;
using System.Text.Json.Serialization;
using Task.Domain;
using Task.Domain.Recurrence;

namespace Task.Application.Calendar;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RecurrenceTemplateData
{
    public Guid? ProjectId { get; init; }
    public required string Title { get; init; }
    public string? Description { get; init; }
    public required Guid AuthorUserId { get; init; }
    public Guid? RequesterUserId { get; init; }
    public Guid? PrimaryCounterpartyObjectId { get; init; }
    public string Priority { get; init; } = "normal";
    public int? PlannedDurationMinutes { get; init; }
    public int? DeadlineOffsetMinutes { get; init; }
    public Guid[] AssigneeIds { get; init; } = [];
    public Guid[] WatcherIds { get; init; } = [];
    public JsonElement[] Checklists { get; init; } = [];
    public JsonElement[] ReminderRules { get; init; } = [];
    public long TemplateVersion { get; init; } = 1;

    public RecurrenceTaskTemplate ToDomain()
    {
        // The production Task aggregate does not yet support these collections.
        // Reject them explicitly instead of silently losing template content.
        if (Checklists is null || ReminderRules is null || Checklists.Length != 0 || ReminderRules.Length != 0)
            throw new ArgumentException("Checklist and reminder templates are not supported by the task store yet.");
        return RecurrenceTaskTemplate.Create(ProjectId, Title, Description, AuthorUserId, RequesterUserId,
            PrimaryCounterpartyObjectId, ParsePriority(Priority), PlannedDurationMinutes, DeadlineOffsetMinutes,
            AssigneeIds, WatcherIds, [], [], TemplateVersion);
    }

    public static TaskPriority ParsePriority(string value) => value switch
    {
        "low" => TaskPriority.Low,
        "normal" => TaskPriority.Normal,
        "high" => TaskPriority.High,
        "critical" => TaskPriority.Critical,
        _ => throw new ArgumentException("Unknown task priority."),
    };
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RecurrenceDefinition
{
    public string Status { get; init; } = "active";
    public required string Frequency { get; init; }
    public required int Interval { get; init; }
    public int[] Weekdays { get; init; } = [];
    public int[] MonthDays { get; init; } = [];
    public int? MonthOfYear { get; init; }
    public required DateOnly OccurrenceStartDate { get; init; }
    public TimeOnly? LocalStartTime { get; init; }
    public required string TimeZone { get; init; }
    public DateOnly? UntilDate { get; init; }
    public int? MaxOccurrences { get; init; }
    public DateOnly? NextGenerationDate { get; init; }
    public required RecurrenceTemplateData Template { get; init; }

    public RecurrenceRule ToRule() => RecurrenceRule.Create(Frequency switch
    {
        "daily" => RecurrenceFrequency.Daily,
        "weekly" => RecurrenceFrequency.Weekly,
        "monthly" => RecurrenceFrequency.Monthly,
        "yearly" => RecurrenceFrequency.Yearly,
        _ => throw new ArgumentException("Unknown recurrence frequency."),
    }, Interval, Weekdays, MonthDays, MonthOfYear, OccurrenceStartDate, LocalStartTime, UntilDate, MaxOccurrences);

    public void Validate()
    {
        if (Status is not ("active" or "paused" or "completed" or "cancelled")) throw new ArgumentException("Unknown series status.");
        if (OccurrenceStartDate == default || OccurrenceStartDate == DateOnly.MaxValue) throw new ArgumentException("Invalid series start date.");
        if (string.IsNullOrWhiteSpace(TimeZone) || TimeZone.Length > 64 || !TimeZoneInfo.TryFindSystemTimeZoneById(TimeZone, out _))
            throw new ArgumentException("Unknown time zone.");
        ArgumentNullException.ThrowIfNull(Template);
        _ = ToRule(); _ = Template.ToDomain();
    }
}

public sealed record RecurrenceRecord(Guid Id, Guid OrganizationId, long Version, DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt, Guid CreatedBy, RecurrenceDefinition Definition);
public sealed record RecurrenceOccurrenceRecord(DateOnly LocalDate, Guid TaskId, bool Skipped = false,
    long GeneratedTaskVersion = 1, RecurrenceTemplateData? Template = null, bool IsException = false);
public sealed record RecurrenceOccurrenceDetails(DateOnly LocalDate, Guid TaskId, long TaskVersion, string Title, string Status, bool Skipped,
    RecurrenceTemplateData? Template = null);
public sealed record RecurrencePreviewItem(string OccurrenceKey, DateOnly LocalDate, DateTime? StartAtUtc,
    DateTime? DeadlineAt, string DstAdjustment);
public sealed record RecurrenceReply(int Status, long Version, string Json);
public sealed class RecurrenceRequestException(int status, string code, string message) : Exception(message)
{
    public int Status { get; } = status;
    public string Code { get; } = code;
}

public interface IRecurrenceTransaction
{
    IReadOnlyList<RecurrenceOccurrenceRecord> Occurrences { get; }
    TaskAggregate? GetTask(Guid id);
    void SaveTask(TaskAggregate task, int? expectedVersion);
    void SaveOccurrence(RecurrenceOccurrenceRecord occurrence);
    void SaveSeries(RecurrenceRecord series);
}

public interface IRecurrenceStore
{
    IReadOnlyList<RecurrenceRecord> List(Guid organizationId, Guid? actorId = null);
    bool CanAccess(Guid organizationId, Guid id, Guid actorId) => Get(organizationId, id)?.CreatedBy == actorId;
    IReadOnlyList<RecurrenceRecord> ListDue(DateOnly throughDate, int limit) => [];
    RecurrenceRecord? Get(Guid organizationId, Guid id);
    IReadOnlyList<RecurrenceOccurrenceDetails> GetOccurrences(Guid organizationId, Guid id, Guid? actorId = null);
    RecurrenceReply Execute(Guid organizationId, Guid actorId, Guid id, string operation,
        string idempotencyKey, string requestHash, Func<RecurrenceRecord?, IRecurrenceTransaction, RecurrenceReply> action);
}
