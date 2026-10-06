using System.Text.Json;
using Microsoft.Data.Sqlite;
using Task.Application.Calendar;
using Task.Desktop.Calendar;
using Task.Desktop.TaskApi;
using Task.Domain;
using Task.Domain.Recurrence;

namespace Task.Desktop.Personal;

internal sealed record PersonalOccurrence(DateOnly Date, Guid TaskId, long GeneratedVersion,
    bool Skipped, bool Exception, RecurrenceTemplateData Template, string DstAdjustment);

public sealed partial class PersonalTaskStore
{
    private DesktopRecurrenceSeries? Series(Guid id, SqliteTransaction? tx)
    {
        using var c = Command("SELECT version,definition FROM personal_series WHERE id=$id;", tx, ("$id", id.ToString("D")));
        using var r = c.ExecuteReader();
        return r.Read() ? new(id, r.GetInt64(0), JsonSerializer.Deserialize<RecurrenceDefinition>(r.GetString(1), PlanningJson)!) : null;
    }
    public IReadOnlyList<DesktopRecurrenceSeries> RecurrenceSeries() => Locked(() =>
    {
        using var c = Command("SELECT id FROM personal_series ORDER BY id;"); var ids = new List<Guid>();
        using (var r = c.ExecuteReader()) while (r.Read()) ids.Add(Guid.Parse(r.GetString(0)));
        return (IReadOnlyList<DesktopRecurrenceSeries>)ids.Select(id => Series(id, null)!).ToArray();
    });
    private IReadOnlyList<PersonalOccurrence> Occurrences(Guid id, SqliteTransaction? tx)
    {
        using var c = Command("SELECT local_date,task_id,generated_version,skipped,is_exception,template,dst_adjustment FROM personal_occurrences WHERE series_id=$id ORDER BY local_date;", tx, ("$id", id.ToString("D")));
        using var r = c.ExecuteReader(); var result = new List<PersonalOccurrence>();
        while (r.Read()) result.Add(new(DateOnly.Parse(r.GetString(0)), Guid.Parse(r.GetString(1)), r.GetInt64(2), r.GetBoolean(3), r.GetBoolean(4),
            JsonSerializer.Deserialize<RecurrenceTemplateData>(r.GetString(5), PlanningJson)!, r.GetString(6)));
        return result;
    }
    private void SaveSeries(DesktopRecurrenceSeries series, SqliteTransaction tx)
    {
        using var c = Command("""
            INSERT INTO personal_series VALUES ($id,$version,$definition,$horizon,$project)
            ON CONFLICT(id) DO UPDATE SET version=$version,definition=$definition,horizon=$horizon,project_id=$project;
            """, tx, ("$id", series.Id.ToString("D")), ("$version", series.Version),
            ("$definition", JsonSerializer.Serialize(series.Definition, PlanningJson)),
            ("$horizon", series.Definition.NextGenerationDate?.AddDays(-1).ToString("yyyy-MM-dd")),
            ("$project", series.Definition.Template.ProjectId?.ToString("D")));
        c.ExecuteNonQuery();
    }
    private void SaveOccurrence(Guid seriesId, PersonalOccurrence occurrence, SqliteTransaction tx)
    {
        using var c = Command("""
            INSERT INTO personal_occurrences VALUES ($series,$date,$task,$version,$skip,$exception,$template,$dst)
            ON CONFLICT(series_id,local_date) DO UPDATE SET generated_version=$version,skipped=$skip,
            is_exception=$exception,template=$template,dst_adjustment=$dst;
            """, tx, ("$series", seriesId.ToString("D")), ("$date", occurrence.Date.ToString("yyyy-MM-dd")), ("$task", occurrence.TaskId.ToString("D")),
            ("$version", occurrence.GeneratedVersion), ("$skip", occurrence.Skipped), ("$exception", occurrence.Exception),
            ("$template", JsonSerializer.Serialize(occurrence.Template, PlanningJson)), ("$dst", occurrence.DstAdjustment));
        c.ExecuteNonQuery();
    }
    private void ValidateDefinition(RecurrenceDefinition definition, SqliteTransaction tx)
    {
        definition.Validate(); var t = definition.Template;
        if (t.AuthorUserId != _actor || t.RequesterUserId is not null || t.AssigneeIds.Length != 0 || t.WatcherIds.Length != 0 || t.PrimaryCounterpartyObjectId is not null)
            throw new ArgumentException("Участники и контакты компании недоступны в Personal.");
        ValidateProjectLink(t.ProjectId, tx);
    }
    private DesktopTaskDto OccurrenceTask(RecurrenceDefinition definition, DateOnly date, DesktopTaskDto? previous = null)
    {
        var preview = RecurrenceService.PreviewDate(definition, date);
        DateTimeOffset? start = preview.StartAtUtc is { } utc ? new(DateTime.SpecifyKind(utc, DateTimeKind.Utc)) : null;
        DateTimeOffset? deadline = preview.DeadlineAt is { } end ? new(DateTime.SpecifyKind(end, DateTimeKind.Utc)) : null;
        var local = start is { } s ? TimeZoneInfo.ConvertTime(s, TimeZoneInfo.FindSystemTimeZoneById(definition.TimeZone)).DateTime : (DateTime?)null;
        var t = definition.Template; var now = _clock.GetUtcNow();
        var card = (previous?.Card ?? new TaskCardContent()) with
        {
            Description = t.Description,
            ProjectId = t.ProjectId,
            ScheduledDate = local is { } ld ? DateOnly.FromDateTime(ld) : date,
            StartTimeLocal = local is { } lt ? TimeOnly.FromDateTime(lt) : null,
            ScheduleTimeZone = start is null ? null : definition.TimeZone,
            PlannedDurationMinutes = t.PlannedDurationMinutes
        };
        return previous is null
            ? new(Guid.NewGuid(), Guid.Empty, 1, now, now, TaskRules.NormalizeTitle(t.Title), _actor, DesktopTaskStatus.New,
                (DesktopTaskPriority)RecurrenceTemplateData.ParsePriority(t.Priority), start, deadline, [], [], null, card)
            : previous with
            {
                Version = checked(previous.Version + 1),
                UpdatedAtUtc = now,
                Title = TaskRules.NormalizeTitle(t.Title),
                Priority = (DesktopTaskPriority)RecurrenceTemplateData.ParsePriority(t.Priority),
                StartAtUtc = start,
                DeadlineAtUtc = deadline,
                Card = card
            };
    }
    private DesktopRecurrenceSeries Generate(DesktopRecurrenceSeries series, DateOnly through, SqliteTransaction tx, out int count)
    {
        count = 0; var definition = series.Definition;
        if (definition.Status != "active") return series;
        var start = definition.NextGenerationDate ?? definition.OccurrenceStartDate;
        if (through < definition.OccurrenceStartDate || through == DateOnly.MaxValue) throw new ArgumentException("Invalid horizon.");
        if (through < start) return series;
        if (through.DayNumber - start.DayNumber > 366) throw new ArgumentException("Extend the horizon in windows of at most 366 days.");
        var existing = Occurrences(series.Id, tx).Select(o => OccurrenceKey.FromLocalDate(o.Date)).ToArray();
        foreach (var date in RecurrenceGenerator.GenerateMissing(definition.ToRule(), start, through, existing))
        {
            var task = OccurrenceTask(definition, date); Validate(task, tx); Save(task, tx, true);
            SaveOccurrence(series.Id, new(date, task.Id, task.Version, false, false, definition.Template,
                RecurrenceService.PreviewDate(definition, date).DstAdjustment), tx); count++;
        }
        return series with { Definition = definition with { NextGenerationDate = through.AddDays(1) } };
    }
    private static bool CanRegenerate(PersonalOccurrence o, DesktopTaskDto? t) =>
        !o.Skipped && !o.Exception && t is not null && t.Version == o.GeneratedVersion && !TaskRules.IsTerminal((TaskWorkStatus)t.Status);
    private void CancelOccurrence(Guid seriesId, PersonalOccurrence o, DesktopTaskDto task, SqliteTransaction tx)
    {
        var cancelled = task with { Status = DesktopTaskStatus.Cancelled, Version = task.Version + 1, UpdatedAtUtc = _clock.GetUtcNow() };
        Save(cancelled, tx, false); SaveOccurrence(seriesId, o with { Skipped = true, GeneratedVersion = cancelled.Version }, tx);
    }
    public DesktopRecurrenceReply ExecuteRecurrence(DesktopRecurrenceCommand command) => Locked(() =>
    {
        if (command is DesktopRecurrenceCommand.List) return new(Items: RecurrenceSeries());
        if (command is DesktopRecurrenceCommand.Preview preview)
            return new(Preview: RecurrenceService.Preview(preview.Definition, preview.From, preview.Limit));
        if (command is DesktopRecurrenceCommand.Occurrences query)
        {
            _ = Series(query.SeriesId, null) ?? throw new PersonalTaskNotFoundException();
            return new(Occurrences: Occurrences(query.SeriesId, null).Select(o =>
            {
                var task = Get(o.TaskId, null) ?? throw new PersonalTaskNotFoundException();
                return new RecurrenceOccurrenceDetails(o.Date, o.TaskId, task.Version, task.Title, TaskStatusCode(task.Status), o.Skipped, o.Template);
            }).ToArray());
        }
        var key = command switch
        { DesktopRecurrenceCommand.Save c => c.Key, DesktopRecurrenceCommand.Generate c => c.Key, DesktopRecurrenceCommand.SetStatus c => c.Key, DesktopRecurrenceCommand.Apply c => c.Key, _ => throw new ArgumentException("Unknown recurrence operation.") };
        if (key.Length is < 8 or > 200) throw new ArgumentException("Invalid command key.");
        var fingerprint = JsonSerializer.Serialize(command, command.GetType(), PlanningJson);
        using var tx = _database.Connection.BeginTransaction();
        using (var c = Command("SELECT fingerprint,result FROM personal_recurrence_commands WHERE key=$key;", tx, ("$key", key)))
        using (var r = c.ExecuteReader()) if (r.Read())
            return r.GetString(0) == fingerprint ? JsonSerializer.Deserialize<DesktopRecurrenceReply>(r.GetString(1), PlanningJson)!
                : throw new ArgumentException("Command key has already been used for another request.");
        DesktopRecurrenceSeries Require(Guid id, long version)
        {
            var found = Series(id, tx) ?? throw new PersonalTaskNotFoundException();
            if (found.Version != version) throw new PersonalVersionConflictException(); return found;
        }
        DesktopRecurrenceReply result;
        switch (command)
        {
            case DesktopRecurrenceCommand.Save save:
                {
                    ValidateDefinition(save.Definition, tx);
                    var old = save.SeriesId is { } id ? Require(id, save.Version ?? 0) : null;
                    if (save.Definition.Status is not ("active" or "paused") || old?.Definition.Status is "cancelled" or "completed")
                        throw new ArgumentException("A terminal series cannot be edited.");
                    var definition = save.Definition with { NextGenerationDate = save.Definition.OccurrenceStartDate };
                    var series = new DesktopRecurrenceSeries(old?.Id ?? Guid.NewGuid(), (old?.Version ?? 0) + 1, definition);
                    SaveSeries(series, tx); // referenced by occurrences in this same transaction
                    var occurrences = old is null ? [] : Occurrences(series.Id, tx);
                    if (occurrences.Count > 500) throw new ArgumentException("Rule update exceeds 500 generated tasks.");
                    var through = occurrences.Count == 0 ? definition.OccurrenceStartDate.AddDays(62) : occurrences.Max(o => o.Date);
                    if (through < definition.OccurrenceStartDate) through = definition.OccurrenceStartDate.AddDays(62);
                    var dates = new HashSet<DateOnly>();
                    for (var cursor = definition.OccurrenceStartDate; cursor <= through;)
                    {
                        var end = cursor.AddDays(Math.Min(366, through.DayNumber - cursor.DayNumber));
                        dates.UnionWith(RecurrenceGenerator.GenerateDates(definition.ToRule(), cursor, end)); cursor = end.AddDays(1);
                    }
                    foreach (var o in occurrences)
                    {
                        var task = Get(o.TaskId, tx); if (!CanRegenerate(o, task)) continue;
                        if (!dates.Contains(o.Date)) { CancelOccurrence(series.Id, o, task!, tx); continue; }
                        var updated = OccurrenceTask(definition, o.Date, task); Validate(updated, tx); Save(updated, tx, false);
                        SaveOccurrence(series.Id, o with
                        {
                            GeneratedVersion = updated.Version,
                            Template = definition.Template,
                            DstAdjustment = RecurrenceService.PreviewDate(definition, o.Date).DstAdjustment
                        }, tx);
                    }
                    var total = 0;
                    while (series.Definition.Status == "active" && series.Definition.NextGenerationDate <= through)
                    {
                        var cursor = series.Definition.NextGenerationDate!.Value;
                        series = Generate(series, cursor.AddDays(Math.Min(366, through.DayNumber - cursor.DayNumber)), tx, out var generated); total += generated;
                    }
                    SaveSeries(series, tx); result = new(Series: series, GeneratedCount: total, SeriesVersion: series.Version); break;
                }
            case DesktopRecurrenceCommand.Generate generate:
                {
                    var old = Require(generate.SeriesId, generate.Version);
                    var series = Generate(old, generate.Through, tx, out var generated);
                    if (series != old) series = series with { Version = old.Version + 1 };
                    SaveSeries(series, tx); result = new(Series: series, GeneratedCount: generated, SeriesVersion: series.Version); break;
                }
            case DesktopRecurrenceCommand.SetStatus status:
                {
                    var old = Require(status.SeriesId, status.Version);
                    var allowed = status.Status switch { "active" => old.Definition.Status == "paused", "paused" => old.Definition.Status == "active", "cancelled" => old.Definition.Status is "active" or "paused", _ => false };
                    if (!allowed) throw new ArgumentException("Invalid series status transition.");
                    if (status.Status == "cancelled") foreach (var o in Occurrences(old.Id, tx))
                    { var task = Get(o.TaskId, tx); if (CanRegenerate(o, task)) CancelOccurrence(old.Id, o, task!, tx); }
                    var series = old with { Version = old.Version + 1, Definition = old.Definition with { Status = status.Status } };
                    SaveSeries(series, tx); result = new(Series: series); break;
                }
            case DesktopRecurrenceCommand.Apply apply:
                {
                    var old = Require(apply.SeriesId, apply.Version);
                    if (old.Definition.Status is "cancelled" or "completed" || !Enum.IsDefined(apply.Scope)) throw new ArgumentException("Invalid series change.");
                    var occurrences = Occurrences(old.Id, tx);
                    var target = occurrences.SingleOrDefault(o => o.Date == apply.Date) ?? throw new PersonalTaskNotFoundException();
                    if (Get(target.TaskId, tx)?.Version != apply.TaskVersion) throw new PersonalVersionConflictException();
                    var template = old.Definition.Template with { Title = apply.Title, Priority = apply.Priority, PlannedDurationMinutes = apply.Duration, TemplateVersion = old.Definition.Template.TemplateVersion + 1 };
                    _ = template.ToDomain();
                    var selected = occurrences.Where(o => apply.Scope == RecurrenceChangeScope.EntireSeries || (apply.Scope == RecurrenceChangeScope.ThisOccurrence ? o.Date == apply.Date : o.Date >= apply.Date)).ToArray();
                    if (selected.Length > 500) throw new ArgumentException("Change window exceeds 500 tasks.");
                    foreach (var o in selected)
                    {
                        var task = Get(o.TaskId, tx);
                        if (o.Skipped || task is null || TaskRules.IsTerminal((TaskWorkStatus)task.Status) || apply.Scope != RecurrenceChangeScope.ThisOccurrence && !CanRegenerate(o, task)) continue;
                        var changedTemplate = o.Template with { Title = apply.Title, Priority = apply.Priority, PlannedDurationMinutes = apply.Duration };
                        var updated = task with
                        {
                            Version = task.Version + 1,
                            UpdatedAtUtc = _clock.GetUtcNow(),
                            Title = TaskRules.NormalizeTitle(apply.Title),
                            Priority = (DesktopTaskPriority)RecurrenceTemplateData.ParsePriority(apply.Priority),
                            Card = (task.Card ?? new()) with { PlannedDurationMinutes = apply.Duration },
                            DeadlineAtUtc = apply.Duration is { } minutes && task.StartAtUtc is { } start ? start.AddMinutes(minutes) : task.DeadlineAtUtc
                        };
                        Validate(updated, tx); Save(updated, tx, false);
                        SaveOccurrence(old.Id, o with
                        {
                            GeneratedVersion = updated.Version,
                            Template = changedTemplate,
                            Exception = o.Exception || apply.Scope == RecurrenceChangeScope.ThisOccurrence
                        }, tx);
                    }
                    var series = old with { Version = old.Version + 1, Definition = apply.Scope == RecurrenceChangeScope.ThisOccurrence ? old.Definition : old.Definition with { Template = template } };
                    SaveSeries(series, tx); result = new(Series: series); break;
                }
            default: throw new ArgumentException("Unknown recurrence operation.");
        }
        using var persist = Command("INSERT INTO personal_recurrence_commands VALUES ($key,$fingerprint,$result);", tx,
            ("$key", key), ("$fingerprint", fingerprint), ("$result", JsonSerializer.Serialize(result, PlanningJson)));
        persist.ExecuteNonQuery(); tx.Commit(); return result;
    });
}

public sealed class PersonalRecurrenceClient(PersonalTaskStore store) : IDesktopRecurrenceClient
{
    public System.Threading.Tasks.Task<DesktopCalendarResult<DesktopRecurrenceReply>> ExecuteAsync(DesktopRecurrenceCommand command, CancellationToken cancellationToken) =>
        PersonalCalendarClient.Execute(() => store.ExecuteRecurrence(command), cancellationToken);
}
