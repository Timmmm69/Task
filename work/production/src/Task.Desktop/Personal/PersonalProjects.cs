using Microsoft.Data.Sqlite;
using Task.Desktop.Projects;

namespace Task.Desktop.Personal;

// Personal project has no employee, membership, ownership or corporate permission fields.
public sealed record PersonalProject(Guid Id, long Version, string Name, string? Description,
    DesktopProjectStatus Status, DateOnly? StartDate, DateOnly? PlannedEndDate,
    DateTimeOffset? ActualEndAt = null, string? DefaultTimeZone = null, string? ColorCode = null,
    string Lifecycle = "active")
{
    public override string ToString() => Name;
    public string StatusText => Status switch { DesktopProjectStatus.Planning => "Планирование", DesktopProjectStatus.Active => "Активен", DesktopProjectStatus.Paused => "Приостановлен", _ => "Завершён" };
    public string LifecycleText => Lifecycle switch { "active" => "Активный", "archived" => "В архиве", _ => "В корзине" };
}

public sealed partial class PersonalTaskStore
{
    private PersonalProject? Project(Guid id, SqliteTransaction? tx)
    {
        using var cmd = Command("SELECT * FROM personal_projects WHERE id=$id;", tx, ("$id", id.ToString("D")));
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;
        string? Text(int n) => r.IsDBNull(n) ? null : r.GetString(n);
        DateOnly? Date(int n) => Text(n) is { } v ? DateOnly.Parse(v) : null;
        return new(Guid.Parse(r.GetString(0)), r.GetInt64(1), r.GetString(2), Text(3),
            (DesktopProjectStatus)r.GetInt32(4), Date(5), Date(6), ReadInstant(r, 7), Text(8), Text(9), r.GetString(10));
    }
    public IReadOnlyList<PersonalProject> Projects() => Projects(false);
    public IReadOnlyList<PersonalProject> Projects(bool includeInactive) => Locked(() =>
    {
        using var cmd = Command("SELECT id FROM personal_projects " + (includeInactive ? "" : "WHERE lifecycle='active' ") + "ORDER BY name,id;");
        var ids = new List<Guid>();
        using (var r = cmd.ExecuteReader()) while (r.Read()) ids.Add(Guid.Parse(r.GetString(0)));
        return (IReadOnlyList<PersonalProject>)ids.Select(id => Project(id, null)!).ToArray();
    });
    private void ValidateProjectLink(Guid? id, SqliteTransaction tx)
    {
        if (id is { } project && Project(project, tx)?.Lifecycle != "active")
            throw new ArgumentException("Выберите существующий активный личный проект.");
    }
    public PersonalProject SaveProject(PersonalProject draft) => Locked(() =>
    {
        if (string.IsNullOrWhiteSpace(draft.Name) || draft.Name.Trim().Length > 300 || draft.Description?.Length > 10000
            || !Enum.IsDefined(draft.Status) || draft.Lifecycle is not ("active" or "archived" or "trashed")
            || draft.PlannedEndDate < draft.StartDate || draft.ActualEndAt is { Offset: var offset } && offset != TimeSpan.Zero
            || draft.Status == DesktopProjectStatus.Completed && draft.ActualEndAt is null)
            throw new ArgumentException("Проверьте название, статус и даты проекта.");
        if (draft.ColorCode is { } color && !System.Text.RegularExpressions.Regex.IsMatch(color, "^#[0-9A-Fa-f]{6}([0-9A-Fa-f]{2})?$"))
            throw new ArgumentException("Некорректный цвет проекта.");
        if (draft.DefaultTimeZone is { } zone && !TimeZoneInfo.TryFindSystemTimeZoneById(zone, out _))
            throw new ArgumentException("Неизвестный часовой пояс.");
        using var tx = _database.Connection.BeginTransaction();
        var old = draft.Id == Guid.Empty ? null : Project(draft.Id, tx) ?? throw new PersonalTaskNotFoundException();
        if (old is not null && old.Version != draft.Version) throw new PersonalVersionConflictException();
        if (old is null && draft.Lifecycle != "active") throw new ArgumentException("Новый проект должен быть активным.");
        if (old is not null && old.Lifecycle != draft.Lifecycle)
        {
            if ((old.Lifecycle, draft.Lifecycle) is not (("active", "archived") or ("active", "trashed") or ("archived", "trashed") or ("archived", "active")))
                throw new ArgumentException("Восстановите проект из корзины в разделе рабочего пространства.");
            if (draft.Lifecycle == "archived" && draft.Status != DesktopProjectStatus.Completed) throw new ArgumentException("Завершите проект перед архивированием.");
            using var ledger = Command("INSERT INTO personal_lifecycle(object_id,previous_state,archived_at,deleted_at,updated_at) VALUES($id,$prior,$archive,$delete,$now) ON CONFLICT(object_id) DO UPDATE SET previous_state=$prior,archived_at=COALESCE($archive,archived_at),deleted_at=$delete,updated_at=$now;", tx,
                ("$id", old.Id.ToString("D")), ("$prior", draft.Lifecycle == "trashed" ? old.Lifecycle : null), ("$archive", draft.Lifecycle == "archived" ? Instant(_clock.GetUtcNow()) : null), ("$delete", draft.Lifecycle == "trashed" ? Instant(_clock.GetUtcNow()) : null), ("$now", Instant(_clock.GetUtcNow())));
            ledger.ExecuteNonQuery();
        }
        else if (old?.Lifecycle is "archived" or "trashed") throw new PersonalTransitionException();
        var saved = draft with { Id = old?.Id ?? Guid.NewGuid(), Version = (old?.Version ?? 0) + 1, Name = draft.Name.Trim() };
        using var cmd = Command("""
            INSERT INTO personal_projects VALUES ($id,$version,$name,$description,$status,$start,$end,$actual,$zone,$color,$lifecycle)
            ON CONFLICT(id) DO UPDATE SET version=$version,name=$name,description=$description,status=$status,
            start_date=$start,planned_end_date=$end,actual_end_at=$actual,time_zone=$zone,color=$color,lifecycle=$lifecycle;
            """, tx, ("$id", saved.Id.ToString("D")), ("$version", saved.Version), ("$name", saved.Name),
            ("$description", saved.Description), ("$status", (int)saved.Status), ("$start", saved.StartDate?.ToString("yyyy-MM-dd")),
            ("$end", saved.PlannedEndDate?.ToString("yyyy-MM-dd")), ("$actual", Instant(saved.ActualEndAt)),
            ("$zone", saved.DefaultTimeZone), ("$color", saved.ColorCode), ("$lifecycle", saved.Lifecycle));
        cmd.ExecuteNonQuery(); tx.Commit(); return saved;
    });
}
