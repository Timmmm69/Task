using System.IO;
using Microsoft.Data.Sqlite;

namespace Task.Desktop.Personal;

/// <summary>Owns one non-pooled connection. Unknown databases are never reset.</summary>
internal sealed class PersonalDatabase : IDisposable
{
    internal const int ApplicationId = 0x54534B50;
    internal const int CurrentSchemaVersion = 4;
    internal SqliteConnection Connection { get; }

    public PersonalDatabase(PersonalDataPaths paths, Action<string>? fault = null, bool restoring = false, int targetVersion = CurrentSchemaVersion)
    {
        if (targetVersion is < 1 or > CurrentSchemaVersion) throw new ArgumentOutOfRangeException(nameof(targetVersion));
        Directory.CreateDirectory(paths.DirectoryPath);
        if (!restoring && File.Exists(Path.Combine(paths.DirectoryPath, "restore-pending.json")))
            throw new InvalidDataException("Восстановление прервано. Выберите safety copy или резервную копию; обе базы сохранены.");
        if (File.Exists(paths.DatabasePath))
        {
            var oldVersion = PersonalDatabaseValidation.ValidateFile(paths.DatabasePath);
            if (oldVersion > targetVersion) throw new InvalidOperationException("Неподдерживаемая версия Personal DB. База сохранена.");
            if (oldVersion < targetVersion)
            {
                var recovery = Path.Combine(paths.DirectoryPath, "recovery");
                Directory.CreateDirectory(recovery);
                var safety = Path.Combine(recovery, $"migration-v{oldVersion}-{Guid.NewGuid():N}.db");
                using var source = PersonalDatabaseValidation.Open(paths.DatabasePath, SqliteOpenMode.ReadOnly);
                using (var destination = PersonalDatabaseValidation.Open(safety, SqliteOpenMode.ReadWriteCreate)) source.BackupDatabase(destination);
                PersonalDatabaseValidation.ValidateFile(safety);
            }
        }
        Connection = new(new SqliteConnectionStringBuilder
        {
            DataSource = paths.DatabasePath,
            Pooling = false,
            ForeignKeys = true,
            DefaultTimeout = 5,
        }.ToString());
        try
        {
            Connection.Open();
            Connection.CreateFunction<string?, string, bool>("personal_contains",
                (text, query) => text?.Contains(query, StringComparison.OrdinalIgnoreCase) == true, isDeterministic: true);
            using var settings = Connection.CreateCommand();
            settings.CommandText = "PRAGMA synchronous=FULL;";
            settings.ExecuteNonQuery();
            Migrate(fault, targetVersion);
            PersonalDatabaseValidation.Validate(Connection);
            settings.CommandText = "PRAGMA journal_mode=DELETE;";
            if (!string.Equals(settings.ExecuteScalar()?.ToString(), "delete", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Personal DB journal mode unavailable.");
        }
        catch { Connection.Dispose(); throw; }
    }

    private void Migrate(Action<string>? fault, int targetVersion)
    {
        using var transaction = Connection.BeginTransaction();
        using var command = Connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "PRAGMA user_version;";
        var version = Convert.ToInt32(command.ExecuteScalar());
        command.CommandText = "PRAGMA application_id;";
        var appId = Convert.ToInt32(command.ExecuteScalar());
        if (version > targetVersion || version < 0 || (version > 0 && appId != ApplicationId))
            throw new InvalidOperationException("Unsupported Personal database schema. The database has been preserved.");
        if (version == 0)
        {
            command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE name NOT LIKE 'sqlite_%';";
            if (appId != 0 || Convert.ToInt32(command.ExecuteScalar()) != 0)
                throw new InvalidOperationException("Unrecognized database. The database has been preserved.");
            command.CommandText = """
                CREATE TABLE personal_metadata (key TEXT PRIMARY KEY, value TEXT NOT NULL);
                CREATE TABLE tasks (
                    id TEXT PRIMARY KEY, version INTEGER NOT NULL CHECK(version > 0),
                    created_at TEXT NOT NULL, updated_at TEXT NOT NULL,
                    title TEXT NOT NULL, description TEXT,
                    status INTEGER NOT NULL CHECK(status BETWEEN 0 AND 4),
                    priority INTEGER NOT NULL CHECK(priority BETWEEN 0 AND 3),
                    start_at TEXT, deadline TEXT, scheduled_date TEXT, start_time TEXT,
                    time_zone TEXT, duration INTEGER,
                    project_id TEXT, parent_task_id TEXT REFERENCES tasks(id), counterparty_id TEXT,
                    completed_at TEXT, archived_at TEXT, trashed_at TEXT);
                CREATE INDEX tasks_parent ON tasks(parent_task_id);
                """;
            command.ExecuteNonQuery();
            command.CommandText = "INSERT INTO personal_metadata VALUES ('PersonalActorId', $actor);";
            command.Parameters.AddWithValue("$actor", Guid.NewGuid().ToString("D"));
            command.ExecuteNonQuery();
            command.Parameters.Clear();
            command.CommandText = $"PRAGMA application_id={ApplicationId}; PRAGMA user_version=1;";
            command.ExecuteNonQuery();
            version = 1;
        }
        if (version == 1 && targetVersion >= 2)
        {
            command.CommandText = """
                CREATE TABLE checklist (
                    id TEXT PRIMARY KEY, task_id TEXT NOT NULL REFERENCES tasks(id),
                    text TEXT NOT NULL, completed INTEGER NOT NULL CHECK(completed IN (0,1)),
                    sort_order INTEGER NOT NULL);
                CREATE INDEX checklist_task ON checklist(task_id, sort_order, id);
                PRAGMA user_version=2;
                """;
            command.ExecuteNonQuery();
            version = 2;
        }
        if (version == 2 && targetVersion >= 3)
        {
            command.CommandText = PersonalPlanningMigration.Sql;
            command.ExecuteNonQuery();
            version = 3;
        }
        if (version == 3 && targetVersion >= 4)
        {
            command.CommandText = PersonalWorkspaceMigration.Sql;
            command.ExecuteNonQuery();
        }
        command.CommandText = "SELECT value FROM personal_metadata WHERE key='PersonalActorId';";
        if (!Guid.TryParse(command.ExecuteScalar()?.ToString(), out var actor) || actor == Guid.Empty)
            throw new InvalidOperationException("Personal database metadata is invalid. The database has been preserved.");
        fault?.Invoke("migration-commit");
        transaction.Commit();
    }

    public void Dispose() => Connection.Dispose();
}
