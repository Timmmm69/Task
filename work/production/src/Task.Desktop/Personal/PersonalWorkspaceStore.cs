using Microsoft.Data.Sqlite;
using Task.Desktop.Work;
using Task.Desktop.Projects;

namespace Task.Desktop.Personal;

public sealed record PersonalObject(Guid Id, string ObjectType, string Title, long Version, string Lifecycle)
{
    public string LifecycleText => Lifecycle switch { "active" => "Активный", "archived" => "В архиве", _ => "В корзине" };
    public override string ToString() => $"{Title} · {ObjectType switch { "task" => "Задача", "project" => "Проект", "contact" => "Контакт", _ => "Каталог" }}";
}

public sealed partial class PersonalTaskStore
{
    public IReadOnlyList<PersonalObject> WorkspaceObjects() => Locked(() => Objects(null));
    private IReadOnlyList<PersonalObject> Objects(SqliteTransaction? tx)
    {
        using var c = Command("SELECT id,object_type,title,version,lifecycle FROM personal_objects ORDER BY title,id;", tx);
        using var r = c.ExecuteReader(); var rows = new List<PersonalObject>();
        while (r.Read()) rows.Add(new(Guid.Parse(r.GetString(0)), r.GetString(1), r.GetString(2), r.GetInt64(3), r.GetString(4)));
        return rows;
    }
    private PersonalObject RequireWorkspaceObject(Guid id, long? version, SqliteTransaction tx)
    {
        var item = Objects(tx).SingleOrDefault(o => o.Id == id) ?? throw new PersonalTaskNotFoundException();
        if (version is { } expected && item.Version != expected) throw new PersonalVersionConflictException();
        return item;
    }
    private static string WorkspaceTable(string type) => type switch
    {
        "task" => "tasks",
        "project" => "personal_projects",
        "contact" => "personal_contacts",
        "catalog_item" => "personal_catalog",
        _ => throw new ArgumentException("Неподдерживаемый тип личного объекта."),
    };
    private void BumpWorkspaceObject(PersonalObject item, SqliteTransaction tx)
    {
        var stamp = item.ObjectType == "project" ? "" : ",updated_at=$now";
        using var c = Command($"UPDATE {WorkspaceTable(item.ObjectType)} SET version=version+1{stamp} WHERE id=$id AND version=$version;", tx,
            ("$id", item.Id.ToString("D")), ("$version", item.Version), ("$now", Instant(_clock.GetUtcNow())));
        if (c.ExecuteNonQuery() != 1) throw new PersonalVersionConflictException();
    }
    public IReadOnlyList<DesktopCatalogItem> Catalog() => Locked<IReadOnlyList<DesktopCatalogItem>>(() =>
    {
        using var c = Command("SELECT id,version,name,item_type,description,lifecycle,parent_id FROM personal_catalog WHERE lifecycle='active' ORDER BY name,id;");
        using var r = c.ExecuteReader(); var rows = new List<DesktopCatalogItem>();
        while (r.Read()) rows.Add(new(Guid.Parse(r.GetString(0)), r.GetInt64(1), r.GetString(2), r.GetString(3), r.IsDBNull(4) ? null : r.GetString(4), null, r.GetString(5), r.IsDBNull(6) ? null : Guid.Parse(r.GetString(6))));
        return rows;
    });
    public DesktopCatalogItem CreateCatalog(string name, string itemType, string? description, Guid? parentId = null) => Locked(() =>
    {
        name = name.Trim();
        if (name.Length is < 1 or > 500 || description?.Length > 10000 || itemType is not ("file_reference" or "folder_reference" or "virtual_folder"))
            throw new ArgumentException("Проверьте название и тип записи каталога.");
        using var tx = _database.Connection.BeginTransaction();
        ValidateCatalogParent(parentId, tx);
        var id = Guid.NewGuid();
        using var c = Command("INSERT INTO personal_catalog(id,version,name,item_type,description,parent_id,updated_at) VALUES($id,1,$name,$type,$description,$parent,$now);", tx,
            ("$id", id.ToString("D")), ("$name", name), ("$type", itemType), ("$description", description), ("$parent", parentId?.ToString("D")), ("$now", Instant(_clock.GetUtcNow())));
        c.ExecuteNonQuery(); tx.Commit(); return new DesktopCatalogItem(id, 1, name, itemType, description, null, "active", parentId);
    });
    private void ValidateCatalogParent(Guid? parent, SqliteTransaction tx)
    {
        if (parent is null) return;
        using var c = Command("SELECT 1 FROM personal_catalog WHERE id=$id AND item_type='virtual_folder' AND lifecycle='active';", tx, ("$id", parent.Value.ToString("D")));
        if (c.ExecuteScalar() is null) throw new ArgumentException("Родитель должен быть активной виртуальной папкой.");
    }
    public void MoveCatalog(Guid id, long version, Guid? parent) => Locked(() =>
    {
        using var tx = _database.Connection.BeginTransaction(); var item = RequireWorkspaceObject(id, version, tx);
        if (item.ObjectType != "catalog_item" || item.Lifecycle != "active") throw new PersonalTransitionException();
        ValidateCatalogParent(parent, tx);
        using var cycle = Command("WITH RECURSIVE descendants(id) AS (SELECT $id UNION SELECT c.id FROM personal_catalog c JOIN descendants d ON c.parent_id=d.id) SELECT 1 FROM descendants WHERE id=$parent;", tx,
            ("$id", id.ToString("D")), ("$parent", parent?.ToString("D")));
        if (cycle.ExecuteScalar() is not null) throw new ArgumentException("Перемещение создаст цикл папок.");
        using var c = Command("UPDATE personal_catalog SET parent_id=$parent WHERE id=$id;", tx, ("$id", id.ToString("D")), ("$parent", parent?.ToString("D")));
        c.ExecuteNonQuery(); BumpWorkspaceObject(item, tx); tx.Commit(); return true;
    });
    public void AddPersonalLocation(Guid id, long version, string path) => Locked(() =>
    {
        path = path.Trim();
        if (!WindowsFileAccessAdapter.IsAllowedPath(path)) throw new ArgumentException("Укажите разрешённый абсолютный локальный или UNC-путь.");
        using var tx = _database.Connection.BeginTransaction(); var item = RequireWorkspaceObject(id, version, tx);
        using var type = Command("SELECT item_type FROM personal_catalog WHERE id=$id;", tx, ("$id", id.ToString("D")));
        if (item.Lifecycle != "active" || type.ExecuteScalar()?.ToString() is not ("file_reference" or "folder_reference")) throw new PersonalTransitionException();
        using var c = Command("INSERT INTO personal_file_locations VALUES($location,$id,$path) ON CONFLICT(catalog_id) DO UPDATE SET raw_path=$path;", tx,
            ("$location", Guid.NewGuid().ToString("D")), ("$id", id.ToString("D")), ("$path", path));
        c.ExecuteNonQuery(); BumpWorkspaceObject(item, tx); tx.Commit(); return true;
    });
    public DesktopFileLocation? PersonalLocation(Guid id) => Locked(() =>
    {
        using var c = Command("SELECT l.id,c.version,l.raw_path FROM personal_file_locations l JOIN personal_catalog c ON c.id=l.catalog_id WHERE c.id=$id AND c.lifecycle<>'trashed';", null, ("$id", id.ToString("D")));
        using var r = c.ExecuteReader();
        return r.Read() ? new DesktopFileLocation(Guid.Parse(r.GetString(0)), r.GetInt64(1), r.GetString(2).StartsWith(@"\\", StringComparison.Ordinal) ? "unc_path" : "local_path", r.GetString(2), true) : null;
    });
    public IReadOnlyList<DesktopContact> Contacts() => Locked<IReadOnlyList<DesktopContact>>(() =>
    {
        using var c = Command("SELECT id,version,display_name,first_name,last_name,notes,lifecycle FROM personal_contacts WHERE lifecycle='active' ORDER BY display_name,id;");
        using var r = c.ExecuteReader(); var rows = new List<DesktopContact>();
        while (r.Read()) rows.Add(new(Guid.Parse(r.GetString(0)), r.GetInt64(1), r.GetString(2), r.GetString(3), r.IsDBNull(4) ? null : r.GetString(4), r.IsDBNull(5) ? null : r.GetString(5), "active", r.GetString(6)));
        return rows;
    });
    public DesktopContact CreateContact(string firstName, string? lastName, string displayName) => Locked(() =>
    {
        firstName = firstName.Trim(); displayName = displayName.Trim(); lastName = lastName?.Trim();
        if (firstName.Length is < 1 or > 100 || displayName.Length is < 1 or > 300 || lastName?.Length > 100) throw new ArgumentException("Проверьте имя контакта.");
        var id = Guid.NewGuid();
        using var c = Command("INSERT INTO personal_contacts(id,version,first_name,last_name,display_name,updated_at) VALUES($id,1,$first,$last,$display,$now);", null,
            ("$id", id.ToString("D")), ("$first", firstName), ("$last", lastName), ("$display", displayName), ("$now", Instant(_clock.GetUtcNow())));
        c.ExecuteNonQuery(); return new DesktopContact(id, 1, displayName, firstName, lastName, null, "active", "active");
    });
    public IReadOnlyList<DesktopSearchResult> SearchPersonal(string query) => Locked<IReadOnlyList<DesktopSearchResult>>(() =>
    {
        query = query.Trim(); if (query.Length is < 2 or > 200) throw new ArgumentException("Введите от 2 до 200 символов.");
        using var c = Command("SELECT id,object_type,title,version,updated_at FROM personal_objects WHERE lifecycle='active' AND (personal_contains(title,$q) OR personal_contains(search_text,$q)) ORDER BY title,id LIMIT 100;", null, ("$q", query));
        using var r = c.ExecuteReader(); var rows = new List<DesktopSearchResult>();
        while (r.Read()) rows.Add(new(Guid.Parse(r.GetString(0)), r.GetString(1), r.GetString(2), null, r.GetInt64(3), DateTimeOffset.Parse(r.GetString(4))));
        return rows;
    });
    public IReadOnlyList<DesktopLifecycleItem> WorkspaceLifecycle(string state) => Locked<IReadOnlyList<DesktopLifecycleItem>>(() =>
    {
        using var c = Command("""
            SELECT o.id,o.object_type,o.title,o.version,o.lifecycle,o.updated_at,l.archived_at,l.deleted_at
            FROM personal_objects o LEFT JOIN personal_lifecycle l ON l.object_id=o.id
            WHERE o.lifecycle=$state ORDER BY o.title,o.id;
            """, null, ("$state", state));
        using var r = c.ExecuteReader(); var rows = new List<DesktopLifecycleItem>();
        while (r.Read()) rows.Add(new(Guid.Parse(r.GetString(0)), r.GetString(1), r.GetString(2), r.GetInt64(3), r.GetString(4), DateTimeOffset.Parse(r.GetString(5)), ReadInstant(r, 6), ReadInstant(r, 7), null, "personal"));
        return rows;
    });
    public bool NeedsLegacyProjectRecovery(Guid id) => Locked(() =>
    {
        using var c = Command("SELECT 1 FROM personal_projects p LEFT JOIN personal_lifecycle l ON l.object_id=p.id WHERE p.id=$id AND p.lifecycle='trashed' AND l.previous_state IS NULL;", null, ("$id", id.ToString("D")));
        return c.ExecuteScalar() is not null;
    });
    public void ChangeWorkspaceLifecycle(Guid id, long version, string action, string? legacyRestoreState = null) => Locked(() =>
    {
        using var tx = _database.Connection.BeginTransaction(); var item = RequireWorkspaceObject(id, version, tx);
        using var prior = Command("SELECT previous_state FROM personal_lifecycle WHERE object_id=$id;", tx, ("$id", id.ToString("D")));
        // Existing task timestamps already retain the archive state across trash.
        var previous = prior.ExecuteScalar()?.ToString();
        if (legacyRestoreState is not null && (action != "restore" || item.ObjectType != "project" || item.Lifecycle != "trashed" || previous is not null || legacyRestoreState is not ("active" or "archived")))
            throw new ArgumentException("Явное восстановление допустимо только для старого проекта с неизвестным исходным состоянием.");
        if (previous is null && item.ObjectType == "task")
        {
            using var archive = Command("SELECT archived_at FROM tasks WHERE id=$id;", tx, ("$id", id.ToString("D")));
            previous = archive.ExecuteScalar() is string ? "archived" : "active";
        }
        var target = action switch
        {
            "archive" when item.Lifecycle == "active" => "archived",
            "unarchive" when item.Lifecycle == "archived" => "active",
            "trash" when item.Lifecycle != "trashed" => "trashed",
            "restore" when item.Lifecycle == "trashed" => previous ?? (item.ObjectType == "project" ? legacyRestoreState ?? throw new ArgumentException("Предыдущее состояние старого проекта неизвестно. Используйте явное восстановление проекта.") : "active"),
            _ => throw new PersonalTransitionException(),
        };
        if (item.ObjectType == "project" && target == "archived" && action == "archive" && Project(id, tx)!.Status != DesktopProjectStatus.Completed)
            throw new ArgumentException("Завершите проект перед архивированием.");
        if (legacyRestoreState == "archived" && Project(id, tx)!.Status != DesktopProjectStatus.Completed)
            throw new ArgumentException("Незавершённый старый проект можно явно восстановить только как активный.");
        if (target != "trashed" && item.ObjectType == "catalog_item")
        {
            using var parent = Command("SELECT parent_id FROM personal_catalog WHERE id=$id;", tx, ("$id", id.ToString("D")));
            if (parent.ExecuteScalar() is string parentId) ValidateCatalogParent(Guid.Parse(parentId), tx);
        }
        if (target == "active" && item.ObjectType == "task")
        {
            using var parent = Command("SELECT parent_task_id FROM tasks WHERE id=$id;", tx, ("$id", id.ToString("D")));
            if (parent.ExecuteScalar() is string parentId && Get(Guid.Parse(parentId), tx) is null)
                throw new ArgumentException("Сначала восстановите родительскую задачу.");
        }
        var now = Instant(_clock.GetUtcNow());
        using var c = Command(item.ObjectType == "task"
            ? "UPDATE tasks SET archived_at=CASE WHEN $state='archived' THEN COALESCE(archived_at,$now) WHEN $state='active' THEN NULL ELSE archived_at END,trashed_at=CASE WHEN $state='trashed' THEN $now ELSE NULL END WHERE id=$id;"
            : $"UPDATE {WorkspaceTable(item.ObjectType)} SET lifecycle=$state WHERE id=$id;", tx, ("$id", id.ToString("D")), ("$state", target), ("$now", now));
        c.ExecuteNonQuery();
        using var ledger = Command("""
            INSERT INTO personal_lifecycle VALUES($id,$prior,CASE WHEN $state='archived' THEN $now ELSE NULL END,CASE WHEN $state='trashed' THEN $now ELSE NULL END,$now)
            ON CONFLICT(object_id) DO UPDATE SET previous_state=$prior,
                archived_at=CASE WHEN $state='archived' THEN COALESCE(archived_at,$now) WHEN $state='active' THEN NULL ELSE archived_at END,
                deleted_at=CASE WHEN $state='trashed' THEN $now ELSE NULL END,updated_at=$now;
            """, tx, ("$id", id.ToString("D")), ("$prior", target == "trashed" ? item.Lifecycle : null), ("$state", target), ("$now", now));
        ledger.ExecuteNonQuery(); BumpWorkspaceObject(item, tx); tx.Commit(); return true;
    });
    public void PurgeWorkspaceObject(Guid id, long version) => Locked(() =>
    {
        using var tx = _database.Connection.BeginTransaction(); var item = RequireWorkspaceObject(id, version, tx);
        if (item.Lifecycle != "trashed") throw new PersonalTransitionException();
        var blocked = item.ObjectType switch
        {
            "task" => "SELECT 1 FROM tasks WHERE parent_task_id=$id UNION ALL SELECT 1 FROM personal_occurrences WHERE task_id=$id",
            "project" => "SELECT 1 FROM tasks WHERE project_id=$id UNION ALL SELECT 1 FROM personal_events WHERE project_id=$id UNION ALL SELECT 1 FROM personal_series WHERE project_id=$id",
            "catalog_item" => "SELECT 1 FROM personal_catalog WHERE parent_id=$id",
            "contact" => "SELECT 1 FROM tasks WHERE counterparty_id=$id",
            _ => throw new ArgumentException("Неподдерживаемый тип объекта."),
        };
        using var dependencies = Command(blocked + " LIMIT 1;", tx, ("$id", id.ToString("D")));
        if (dependencies.ExecuteScalar() is not null) throw new ArgumentException("Объект используется. Сначала удалите или переназначьте зависимые записи Task.");
        using var c = Command("""
            DELETE FROM object_links WHERE source_id=$id OR target_id=$id;
            DELETE FROM personal_notifications WHERE target_id=$id;
            DELETE FROM personal_reminders WHERE target_id=$id;
            DELETE FROM personal_lifecycle WHERE object_id=$id;
            """ + (item.ObjectType == "task" ? "DELETE FROM checklist WHERE task_id=$id;" : "") + $"DELETE FROM {WorkspaceTable(item.ObjectType)} WHERE id=$id AND version=$version;", tx,
            ("$id", id.ToString("D")), ("$version", version));
        c.ExecuteNonQuery(); tx.Commit(); return true;
    });
}
