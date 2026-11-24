using System.Globalization;
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
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string[]? ChecklistSteps { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Guid[]? CatalogItemIds { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? DeadlineReminderOffsetMinutes { get; init; }
    public long TemplateVersion { get; init; } = 1;

    public RecurrenceTaskTemplate ToDomain()
    {
        // Production has one ordered checklist per task and a deadline reminder preference.
        // Grouped checklists and standalone reminder templates remain unsupported.
        if (Checklists is null || ReminderRules is null || Checklists.Length != 0 || ReminderRules.Length != 0)
            throw new ArgumentException("Checklist and reminder templates are not supported by the task store yet.");
        if (ChecklistSteps is { } steps && (steps.Length > 500
            || steps.Any(step => string.IsNullOrWhiteSpace(step) || step.Length > 2000 || step.Contains('\0'))))
            throw new ArgumentException("A series supports up to 500 checklist steps of 1–2000 characters.");
        if (DeadlineReminderOffsetMinutes is < -1 or > 525600)
            throw new ArgumentException("Reminder interval is outside the accepted range.");
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
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool MonthOnly { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Guid? SourceTaskId { get; init; }

    public string? PlanningMonthFor(DateOnly date) => Frequency is "monthly" or "yearly"
        ? date.ToString("yyyy-MM", CultureInfo.InvariantCulture) : null;

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
        if (MonthOnly && (Frequency is not ("monthly" or "yearly") || OccurrenceStartDate.Day != 1
            || LocalStartTime is not null || MonthDays is not [1]
            || Frequency == "yearly" && MonthOfYear != OccurrenceStartDate.Month
            || Template.DeadlineOffsetMinutes is not null || Template.PlannedDurationMinutes is not null
            || Template.DeadlineReminderOffsetMinutes is not null))
            throw new ArgumentException("A month-only series must use a monthly or yearly period without a time or deadline.");
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
    RecurrenceTemplateData CaptureTaskTemplate(Guid sourceId, RecurrenceTemplateData template, bool monthOnly) =>
        throw new ArgumentException("Source task is unavailable.");
    void ReplaceChecklist(Guid taskId, IReadOnlyList<string> steps)
    {
        if (steps.Count != 0) throw new InvalidOperationException("Checklist persistence is unavailable.");
    }
    void AddCatalogLinks(Guid taskId, IReadOnlyList<Guid> catalogItemIds)
    {
        if (catalogItemIds.Count != 0) throw new InvalidOperationException("Catalog link persistence is unavailable.");
    }
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
