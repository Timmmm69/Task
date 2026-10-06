using System.IO;
using Microsoft.Data.Sqlite;

namespace Task.Desktop.Personal;

internal static class PersonalDatabaseValidation
{
    internal static SqliteConnection Open(string path, SqliteOpenMode mode)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = mode,
            Pooling = false,
            ForeignKeys = true,
            DefaultTimeout = 5,
        }.ToString());
        try { connection.Open(); return connection; }
        catch { connection.Dispose(); throw; }
    }
    internal static int ValidateFile(string path)
    {
        if (new FileInfo(path).Length < 100) throw new InvalidDataException("Personal DB повреждена или пуста. Файл сохранён.");
        using var connection = Open(path, SqliteOpenMode.ReadOnly);
        return Validate(connection);
    }
    internal static int Validate(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA integrity_check;";
        using (var reader = command.ExecuteReader())
        {
            if (!reader.Read() || reader.GetString(0) != "ok" || reader.Read())
                throw new InvalidDataException("Personal DB не прошла проверку целостности. Файл сохранён.");
        }
        command.CommandText = "PRAGMA user_version;";
        var version = Convert.ToInt32(command.ExecuteScalar());
        command.CommandText = "PRAGMA application_id;";
        if (version < 1 || version > PersonalDatabase.CurrentSchemaVersion || Convert.ToInt32(command.ExecuteScalar()) != PersonalDatabase.ApplicationId)
            throw new InvalidOperationException("Неподдерживаемая версия Personal DB. Обновите Task; база сохранена без изменений.");
        command.CommandText = "SELECT value FROM personal_metadata WHERE key='PersonalActorId';";
        if (!Guid.TryParse(command.ExecuteScalar()?.ToString(), out var actor) || actor == Guid.Empty)
            throw new InvalidDataException("Personal schema metadata invalid. Файл сохранён.");
        var tables = new HashSet<string>(StringComparer.Ordinal) { "personal_metadata", "tasks" };
        if (version >= 2) tables.Add("checklist");
        if (version >= 3) tables.UnionWith(["personal_projects", "personal_events", "personal_series", "personal_occurrences", "personal_reminders", "personal_notifications", "personal_recurrence_commands"]);
        if (version >= 4) tables.UnionWith(["personal_catalog", "personal_file_locations", "personal_contacts", "object_links", "personal_lifecycle", "personal_workspace_settings"]);
        command.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%';";
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
                if (!tables.Remove(reader.GetString(0))) throw new InvalidDataException("Unexpected Personal schema table. Файл сохранён.");
        }
        if (tables.Count != 0) throw new InvalidDataException("Incomplete Personal schema. Файл сохранён.");
        var columns = new Dictionary<string, string>
        {
            ["personal_metadata"] = "key,value",
            ["tasks"] = "id,version,created_at,updated_at,title,description,status,priority,start_at,deadline,scheduled_date,start_time,time_zone,duration,project_id,parent_task_id,counterparty_id,completed_at,archived_at,trashed_at",
        };
        if (version >= 2) columns["checklist"] = "id,task_id,text,completed,sort_order";
        if (version >= 3)
        {
            columns["personal_projects"] = "id,version,name,description,status,start_date,planned_end_date,actual_end_at,time_zone,color,lifecycle";
            columns["personal_events"] = "id,version,payload,project_id";
            columns["personal_series"] = "id,version,definition,horizon,project_id";
            columns["personal_occurrences"] = "series_id,local_date,task_id,generated_version,skipped,is_exception,template,dst_adjustment";
            columns["personal_reminders"] = "id,version,target_id,target_kind,trigger_type,offset_minutes,absolute_at,due_at,state,dedupe_key,delivered_at,last_delivery";
            columns["personal_notifications"] = "id,reminder_id,dedupe_key,target_id,title,due_at,delivered_at,is_read,presentation_state";
            columns["personal_recurrence_commands"] = "key,fingerprint,result";
        }
        if (version >= 4)
        {
            columns["personal_catalog"] = "id,version,name,item_type,description,parent_id,lifecycle,updated_at";
            columns["personal_file_locations"] = "id,catalog_id,raw_path";
            columns["personal_contacts"] = "id,version,first_name,last_name,display_name,notes,lifecycle,updated_at";
            columns["object_links"] = "id,source_id,target_id,link_type";
            columns["personal_lifecycle"] = "object_id,previous_state,archived_at,deleted_at,updated_at";
            columns["personal_workspace_settings"] = "key,version,payload";
        }
        foreach (var (table, expected) in columns)
        {
            command.CommandText = $"PRAGMA table_info({table});";
            using var reader = command.ExecuteReader(); var actual = new List<string>();
            while (reader.Read()) actual.Add(reader.GetString(1));
            if (!actual.SequenceEqual(expected.Split(','))) throw new InvalidDataException($"Invalid Personal schema: {table}. Файл сохранён.");
        }
        command.CommandText = "PRAGMA foreign_key_check;";
        using (var reader = command.ExecuteReader())
            if (reader.Read()) throw new InvalidDataException("Personal DB содержит повреждённые связи. Файл сохранён.");
        // Prepare application queries as well as checking the physical SQLite structure.
        foreach (var table in new[] { "tasks", "personal_metadata" })
        {
            command.CommandText = table == "tasks" ? "SELECT id,version,title,created_at,updated_at,status,priority,description,start_at,deadline,scheduled_date,start_time,time_zone,duration,project_id,parent_task_id,counterparty_id,completed_at,archived_at,trashed_at FROM tasks LIMIT 0;"
                : "SELECT key,value FROM personal_metadata LIMIT 0;";
            using var reader = command.ExecuteReader();
        }
        if (version == 4)
        {
            command.CommandText = "SELECT id,object_type,title,search_text,version,lifecycle,updated_at FROM personal_objects LIMIT 0;";
            using var reader = command.ExecuteReader();
        }
        return version;
    }
}
