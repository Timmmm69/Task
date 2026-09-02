using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Task.Domain;
using Task.Domain.Recurrence;

namespace Task.Application.Calendar;

/// <summary>Series and generated tasks change in one durable, tenant-scoped transaction.</summary>
public sealed class RecurrenceService(IRecurrenceStore store)
{
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    public IReadOnlyList<RecurrenceRecord> List(Guid organizationId) => store.List(organizationId);
    public RecurrenceRecord Get(Guid organizationId, Guid id) => store.Get(organizationId, id) ?? throw Missing();
    public IReadOnlyList<RecurrenceOccurrenceDetails> GetOccurrences(Guid organizationId, Guid id)
    { _ = Get(organizationId, id); return store.GetOccurrences(organizationId, id); }

    public static object ToResponse(RecurrenceRecord r) => new
    {
        r.Id, r.OrganizationId, r.Version, createdAt = r.CreatedAt.UtcDateTime, updatedAt = r.UpdatedAt.UtcDateTime,
        r.Definition.Status, r.Definition.Frequency, r.Definition.Interval, r.Definition.Weekdays,
        r.Definition.MonthDays, r.Definition.MonthOfYear, r.Definition.OccurrenceStartDate,
        r.Definition.LocalStartTime, r.Definition.TimeZone, r.Definition.UntilDate,
        r.Definition.MaxOccurrences, r.Definition.NextGenerationDate, r.Definition.Template,
    };

    public RecurrenceReply Create(Guid org, Guid actor, string key, string body)
    {
        var id = Guid.NewGuid();
        return Execute(org, actor, id, "create", key, body, (current, tx) =>
        {
            var definition = ParseDefinition(body);
            if (definition.Status is not ("active" or "paused")) throw Invalid("A new series must be active or paused.");
            if (definition.Template.AuthorUserId != actor) throw Invalid("The author of a new series must be the authenticated user.");
            if (definition.NextGenerationDate is not null && definition.NextGenerationDate != definition.OccurrenceStartDate)
                throw Invalid("The generation cursor is managed by the server.");
            var now = DateTimeOffset.UtcNow;
            var record = new RecurrenceRecord(id, org, 1, now, now, actor,
                definition with { NextGenerationDate = definition.OccurrenceStartDate });
            tx.SaveSeries(record);
            if (definition.Status == "active")
            {
                var through = AddDaysClamped(definition.OccurrenceStartDate, 62);
                record = Materialize(record, actor, through, tx, out _);
                tx.SaveSeries(record);
            }
            return Reply(record, 201);
        });
    }

    public RecurrenceReply Patch(Guid org, Guid actor, Guid id, long version, string key, string body) =>
        Execute(org, actor, id, "patch", key, body + version, (current, tx) =>
        {
            var record = RequireVersion(current, version);
            if (record.Definition.Status is "cancelled" or "completed") throw Invalid("A terminal series cannot be edited.");
            var patch = JsonNode.Parse(body) as JsonObject ?? throw Invalid("Expected a JSON object.");
            if (patch.Count == 0 || patch.ContainsKey("nextGenerationDate")) throw Invalid("The generation cursor is managed by the server.");
            var original = JsonSerializer.SerializeToNode(record.Definition, JsonOptions)!.AsObject();
            foreach (var property in patch) original[property.Key] = property.Value?.DeepClone();
            var definition = ParseDefinition(original.ToJsonString());
            if (definition.Status is "cancelled" or "completed") throw Invalid("Use the series lifecycle action.");
            if (definition.Template.AuthorUserId != record.Definition.Template.AuthorUserId)
                throw Invalid("The series author cannot be changed.");
            var now = DateTimeOffset.UtcNow;
            // Updating a rule re-evaluates the generated horizon atomically; completed,
            // cancelled and individually modified tasks are never overwritten.
            var occurrences = tx.Occurrences;
            if (occurrences.Count > 500) throw Invalid("Rule update exceeds 500 generated tasks.");
            var through = occurrences.Count == 0 ? AddDaysClamped(definition.OccurrenceStartDate, 62)
                : occurrences.Max(o => o.LocalDate);
            var start = definition.OccurrenceStartDate;
            var dates = through < start ? new HashSet<DateOnly>()
                : RecurrenceGenerator.GenerateDates(definition.ToRule(), start, through).ToHashSet();
            foreach (var occurrence in occurrences)
            {
                var task = tx.GetTask(occurrence.TaskId);
                if (occurrence.Skipped || !CanRegenerate(occurrence, task)) continue;
                if (!dates.Contains(occurrence.LocalDate))
                {
                    var updated = task!.Cancel(actor, now);
                    tx.SaveTask(updated, task.Metadata.Version);
                    tx.SaveOccurrence(occurrence with { Skipped = true });
                }
                else
                {
                    var preview = PreviewDate(definition, occurrence.LocalDate);
                    var schedule = Schedule(definition, preview);
                    var updated = task!.UpdateEditableFields(actor, now, definition.Template.Title,
                        RecurrenceTemplateData.ParsePriority(definition.Template.Priority),
                        new(true, schedule.StartsAtUtc), new(true, schedule.DeadlineUtc));
                    tx.SaveTask(updated, task.Metadata.Version);
                    tx.SaveOccurrence(occurrence with { GeneratedTaskVersion = updated.Metadata.Version, Template = definition.Template });
                }
            }
            record = record with { Version = checked(record.Version + 1), UpdatedAt = now,
                Definition = definition with { NextGenerationDate = start } };
            record = Materialize(record, actor, through < start ? AddDaysClamped(start, 62) : through, tx, out _);
            tx.SaveSeries(record);
            return Reply(record);
        });

    public RecurrenceReply Generate(Guid org, Guid actor, Guid id, long version, string key, DateOnly throughDate) =>
        Execute(org, actor, id, "generate", key, $"{version}:{throughDate:yyyy-MM-dd}", (current, tx) =>
        {
            var record = RequireVersion(current, version);
            var updated = Materialize(record, actor, throughDate, tx, out var count);
            if (updated != record) { updated = updated with { Version = checked(record.Version + 1), UpdatedAt = DateTimeOffset.UtcNow }; tx.SaveSeries(updated); }
            return new(200, updated.Version, JsonSerializer.Serialize(new
            { seriesId = id, generatedCount = count, skippedCount = 0, throughDate, seriesVersion = updated.Version }, JsonOptions));
        });

    public RecurrenceReply SetStatus(Guid org, Guid actor, Guid id, long version, string key, string status) =>
        Execute(org, actor, id, status, key, version.ToString(), (current, tx) =>
        {
            var record = RequireVersion(current, version);
            var allowed = status switch { "paused" => record.Definition.Status == "active",
                "active" => record.Definition.Status == "paused", "cancelled" => record.Definition.Status is "active" or "paused", _ => false };
            if (!allowed) throw Invalid("Invalid recurrence status transition.");
            if (status == "cancelled")
                foreach (var occurrence in tx.Occurrences)
                {
                    var task = tx.GetTask(occurrence.TaskId);
                    if (!CanRegenerate(occurrence, task)) continue;
                    tx.SaveTask(task!.Cancel(actor, DateTimeOffset.UtcNow), task.Metadata.Version);
                }
            record = record with { Version = checked(record.Version + 1), UpdatedAt = DateTimeOffset.UtcNow,
                Definition = record.Definition with { Status = status } };
            tx.SaveSeries(record);
            return Reply(record);
        });

    public RecurrenceReply ApplyChange(Guid org, Guid actor, Guid id, long version, string key,
        DateOnly targetDate, int expectedTaskVersion, RecurrenceChangeScope scope, string title,
        string priority, int? duration, string? requestFingerprint = null) => Execute(org, actor, id, "apply-change", key,
            requestFingerprint ?? JsonSerializer.Serialize(new { version, targetDate, expectedTaskVersion, scope, title, priority, duration }, JsonOptions), (current, tx) =>
        {
            var record = RequireVersion(current, version);
            if (!Enum.IsDefined(scope)) throw Invalid("Choose a change scope.");
            if (record.Definition.Status is "cancelled" or "completed") throw Invalid("A terminal series cannot be edited.");
            var target = tx.Occurrences.SingleOrDefault(o => o.LocalDate == targetDate) ?? throw Missing();
            var targetTask = tx.GetTask(target.TaskId) ?? throw Missing();
            if (targetTask.Metadata.Version != expectedTaskVersion) throw Conflict();
            var template = record.Definition.Template with { Title = title, Priority = priority,
                PlannedDurationMinutes = duration, TemplateVersion = record.Definition.Template.TemplateVersion + 1 };
            _ = template.ToDomain();
            var selected = tx.Occurrences.Where(o => scope == RecurrenceChangeScope.EntireSeries
                || (scope == RecurrenceChangeScope.ThisOccurrence ? o.LocalDate == targetDate : o.LocalDate >= targetDate)).ToArray();
            if (selected.Length > 500) throw Invalid("Change window exceeds 500 tasks.");
            var changed = new List<Guid>();
            foreach (var occurrence in selected)
            {
                var task = tx.GetTask(occurrence.TaskId);
                if (task is null || occurrence.Skipped || task.Metadata.LifecycleState != EntityLifecycleState.Active
                    || task.WorkStatus is TaskWorkStatus.Completed or TaskWorkStatus.Cancelled) continue;
                if (scope != RecurrenceChangeScope.ThisOccurrence && !CanRegenerate(occurrence, task)) continue;
                var end = duration.HasValue && task.Schedule.StartsAtUtc.HasValue
                    ? task.Schedule.StartsAtUtc.Value.AddMinutes(duration.Value) : task.Schedule.DeadlineUtc;
                var updated = task.UpdateEditableFields(actor, DateTimeOffset.UtcNow, title, RecurrenceTemplateData.ParsePriority(priority),
                    new(true, task.Schedule.StartsAtUtc), new(true, end));
                tx.SaveTask(updated, task.Metadata.Version); changed.Add(task.Metadata.Id);
                tx.SaveOccurrence(occurrence with
                {
                    GeneratedTaskVersion = updated.Metadata.Version,
                    Template = scope == RecurrenceChangeScope.ThisOccurrence
                        ? (occurrence.Template ?? record.Definition.Template) with { Title = title, Priority = priority, PlannedDurationMinutes = duration }
                        : template,
                    IsException = occurrence.IsException || scope == RecurrenceChangeScope.ThisOccurrence,
                });
            }
            // Future template changes start at the selected occurrence. Already generated
            // earlier tasks are untouched. New dates always lie after the generation cursor.
            record = record with { Version = checked(record.Version + 1), UpdatedAt = DateTimeOffset.UtcNow,
                Definition = scope == RecurrenceChangeScope.ThisOccurrence ? record.Definition : record.Definition with { Template = template } };
            tx.SaveSeries(record);
            return new(200, record.Version, JsonSerializer.Serialize(new { series = ToResponse(record), changedTaskIds = changed,
                regeneratedOccurrenceCount = 0 }, JsonOptions));
        });

    public static IReadOnlyList<RecurrencePreviewItem> Preview(RecurrenceDefinition definition, DateOnly from, int limit)
    {
        definition.Validate();
        if (limit is < 1 or > 500) throw Invalid("Preview limit must be 1–500.");
        var rule = definition.ToRule();
        var first = from > rule.OccurrenceStartDate ? from : rule.OccurrenceStartDate;
        var result = new List<RecurrencePreviewItem>();
        // Bounded windows also allow sparse yearly rules without materializing years of tasks.
        var maximum = AddDaysClamped(rule.OccurrenceStartDate, RecurrenceGenerator.MaxScanDays);
        for (var start = first; start <= maximum && result.Count < limit;)
        {
            var end = AddDaysClamped(start, 365);
            if (end > maximum) end = maximum;
            if (rule.UntilDate.HasValue && start > rule.UntilDate) break;
            var dates = RecurrenceGenerator.GenerateDates(rule, start, end);
            result.AddRange(dates.Take(limit - result.Count).Select(d => PreviewDate(definition, d)));
            if (end == DateOnly.MaxValue || end == maximum) break;
            start = end.AddDays(1);
        }
        return result;
    }

    public static RecurrencePreviewItem PreviewDate(RecurrenceDefinition definition, DateOnly date)
    {
        if (definition.LocalStartTime is null) return new(date.ToString("yyyy-MM-dd"), date, null, null, "none");
        var zone = TimeZoneInfo.FindSystemTimeZoneById(definition.TimeZone);
        var local = date.ToDateTime(definition.LocalStartTime.Value, DateTimeKind.Unspecified);
        var adjustment = "none";
        // Deterministic wall-clock policy: advance gaps to the first valid minute;
        // choose the earlier instant at an overlap.
        for (var i = 0; zone.IsInvalidTime(local) && i < 1440; i++) { local = local.AddMinutes(1); adjustment = "shifted_forward"; }
        if (zone.IsInvalidTime(local)) throw Invalid("The occurrence falls in an unsupported time-zone gap.");
        DateTime utc;
        if (zone.IsAmbiguousTime(local))
        { utc = new DateTimeOffset(local, zone.GetAmbiguousTimeOffsets(local).Max()).UtcDateTime; adjustment = "earlier_offset"; }
        else utc = TimeZoneInfo.ConvertTimeToUtc(local, zone);
        var minutes = definition.Template.DeadlineOffsetMinutes ?? definition.Template.PlannedDurationMinutes;
        return new(date.ToString("yyyy-MM-dd"), date, utc, minutes.HasValue ? utc.AddMinutes(minutes.Value) : null, adjustment);
    }

    private static RecurrenceRecord Materialize(RecurrenceRecord record, Guid actor, DateOnly through,
        IRecurrenceTransaction tx, out int count)
    {
        count = 0;
        if (record.Definition.Status != "active") return record;
        var definition = record.Definition;
        var start = definition.NextGenerationDate ?? definition.OccurrenceStartDate;
        if (through < definition.OccurrenceStartDate || through == DateOnly.MaxValue) throw Invalid("Invalid generation horizon.");
        if (through < start) return record;
        if (through.DayNumber - start.DayNumber > 366) throw Invalid("Extend the generation horizon in windows of at most 366 days.");
        var dates = RecurrenceGenerator.GenerateDates(definition.ToRule(), start, through);
        var existing = tx.Occurrences.Select(o => o.LocalDate).ToHashSet();
        foreach (var date in dates.Where(d => !existing.Contains(d)))
        {
            var preview = PreviewDate(definition, date);
            var task = TaskAggregate.Create(Guid.NewGuid(), record.OrganizationId, definition.Template.AuthorUserId, definition.Template.Title,
                DateTimeOffset.UtcNow, RecurrenceTemplateData.ParsePriority(definition.Template.Priority), Schedule(definition, preview));
            tx.SaveTask(task, null);
            tx.SaveOccurrence(new(date, task.Metadata.Id, GeneratedTaskVersion: task.Metadata.Version, Template: definition.Template));
            count++;
        }
        // Keep the series editable after the last scheduled occurrence. Completion is a
        // separate lifecycle state; reaching a generation horizon is not task completion.
        return record with { Definition = definition with { NextGenerationDate = through.AddDays(1) } };
    }

    private static TaskSchedule Schedule(RecurrenceDefinition definition, RecurrencePreviewItem preview)
    {
        if (preview.StartAtUtc.HasValue)
            return TaskSchedule.Create(new DateTimeOffset(preview.StartAtUtc.Value), preview.DeadlineAt.HasValue ? new DateTimeOffset(preview.DeadlineAt.Value) : null);
        var zone = TimeZoneInfo.FindSystemTimeZoneById(definition.TimeZone);
        var start = TimeZoneInfo.ConvertTimeToUtc(preview.LocalDate.ToDateTime(TimeOnly.MinValue), zone);
        var end = TimeZoneInfo.ConvertTimeToUtc(preview.LocalDate.AddDays(1).ToDateTime(TimeOnly.MinValue), zone);
        return TaskSchedule.Create(new DateTimeOffset(start), new DateTimeOffset(end));
    }
    private RecurrenceReply Execute(Guid org, Guid actor, Guid id, string operation, string key, string body,
        Func<RecurrenceRecord?, IRecurrenceTransaction, RecurrenceReply> action)
    {
        if (org == Guid.Empty || actor == Guid.Empty || key.Length is < 8 or > 200 || key.Any(c => c < '!' || c > '~'))
            throw Invalid("An authenticated organization and an 8–200 character idempotency key are required.");
        return store.Execute(org, actor, id, operation, key,
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(body))), action);
    }
    private static bool CanRegenerate(RecurrenceOccurrenceRecord occurrence, TaskAggregate? task) => task is not null
        && !occurrence.IsException && task.Metadata.Version == occurrence.GeneratedTaskVersion
        && task.Metadata.LifecycleState == EntityLifecycleState.Active && task.WorkStatus == TaskWorkStatus.New;
    private static RecurrenceDefinition ParseDefinition(string json)
    { var definition = JsonSerializer.Deserialize<RecurrenceDefinition>(json, JsonOptions) ?? throw Invalid("Missing recurrence definition."); definition.Validate(); return definition; }
    private static RecurrenceRecord RequireVersion(RecurrenceRecord? record, long version)
    { if (record is null) throw Missing(); if (record.Version != version) throw Conflict(); return record; }
    private static RecurrenceReply Reply(RecurrenceRecord record, int status = 200) => new(status, record.Version, JsonSerializer.Serialize(ToResponse(record), JsonOptions));
    private static DateOnly AddDaysClamped(DateOnly date, int days) => DateOnly.FromDayNumber(Math.Min(DateOnly.MaxValue.DayNumber, date.DayNumber + days));
    private static RecurrenceRequestException Invalid(string message) => new(422, "VALIDATION_FAILED", message);
    private static RecurrenceRequestException Missing() => new(404, "OBJECT_NOT_VISIBLE", "The recurrence or occurrence is absent or not visible.");
    private static RecurrenceRequestException Conflict() => new(412, "VERSION_CONFLICT", "The series or task has changed. Reload before editing.");
}
