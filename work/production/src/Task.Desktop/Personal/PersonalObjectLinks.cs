using Task.Desktop.Work;

namespace Task.Desktop.Personal;

public sealed partial class PersonalTaskStore
{
    public DesktopObjectLinks PersonalLinks(Guid source) => Locked(() =>
    {
        using var tx = _database.Connection.BeginTransaction();
        var item = RequireWorkspaceObject(source, null, tx);
        if (item.Lifecycle == "trashed") return new DesktopObjectLinks([], item.Version);
        using var c = Command("""
            SELECT l.id,l.source_id,l.target_id,l.link_type,CASE WHEN l.source_id=$id THEN t.title ELSE s.title END FROM object_links l
            JOIN personal_objects s ON s.id=l.source_id AND s.lifecycle<>'trashed'
            JOIN personal_objects t ON t.id=l.target_id AND t.lifecycle<>'trashed'
            WHERE l.source_id=$id OR l.target_id=$id ORDER BY l.id;
            """, tx, ("$id", source.ToString("D")));
        using var r = c.ExecuteReader(); var rows = new List<DesktopObjectLink>();
        while (r.Read()) rows.Add(new(Guid.Parse(r.GetString(0)), Guid.Parse(r.GetString(1)), Guid.Parse(r.GetString(2)), r.GetString(3), r.GetString(4)));
        return new DesktopObjectLinks(rows, item.Version);
    });
    public void AddPersonalLink(Guid source, long version, Guid target, string type) => Locked(() =>
    {
        using var tx = _database.Connection.BeginTransaction(); var s = RequireWorkspaceObject(source, version, tx); var t = RequireWorkspaceObject(target, null, tx);
        CheckPersonalLinkWrite(s, tx);
        if (t.Lifecycle != "active" || source == target) throw new ArgumentException("Выберите другой активный объект.");
        var valid = (s.ObjectType, t.ObjectType, type) is ("task", "catalog_item", "task_file") or ("project", "catalog_item", "project_file") or ("contact", "catalog_item", "contact_file") or ("task", "contact", "task_contact");
        if (!valid) throw new ArgumentException("Тип связи не соответствует объектам.");
        using var c = Command("INSERT INTO object_links VALUES($id,$source,$target,$type);", tx,
            ("$id", Guid.NewGuid().ToString("D")), ("$source", source.ToString("D")), ("$target", target.ToString("D")), ("$type", type));
        c.ExecuteNonQuery(); BumpWorkspaceObject(s, tx); tx.Commit(); return true;
    });
    private void CheckPersonalLinkWrite(PersonalObject source, Microsoft.Data.Sqlite.SqliteTransaction tx)
    {
        if (source.Lifecycle != "active") throw new PersonalTransitionException();
        if (source.ObjectType == "task")
        {
            var task = Get(source.Id, tx)!;
            if (Task.Domain.TaskRules.IsTerminal((Task.Domain.TaskWorkStatus)task.Status)) throw new PersonalTransitionException();
        }
    }
    public void RemovePersonalLink(Guid source, long version, Guid linkId) => Locked(() =>
    {
        using var tx = _database.Connection.BeginTransaction(); var s = RequireWorkspaceObject(source, version, tx); CheckPersonalLinkWrite(s, tx);
        using var c = Command("DELETE FROM object_links WHERE id=$link AND source_id=$source;", tx, ("$link", linkId.ToString("D")), ("$source", source.ToString("D")));
        if (c.ExecuteNonQuery() != 1) throw new PersonalTaskNotFoundException();
        BumpWorkspaceObject(s, tx); tx.Commit(); return true;
    });
}
