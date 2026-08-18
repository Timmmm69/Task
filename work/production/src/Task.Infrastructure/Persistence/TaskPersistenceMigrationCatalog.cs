using System.Security.Cryptography;
using System.Text;

namespace Task.Infrastructure.Persistence;

internal static class TaskPersistenceMigrationCatalog
{
    public static IReadOnlyList<TaskPersistenceMigration> All { get; } =
    [
        Load(1, "task_persistence_foundation", "001_task_persistence_foundation.sql"),
        Load(2, "identity_audit_foundation", "002_identity_audit_foundation.sql"),
        Load(3, "calendar_event_persistence", "003_calendar_event_persistence.sql"),
        Load(4, "role_permissions_effect", "004_role_permissions_effect.sql"),
    ];

    public static int LatestVersion => All[^1].Version;

    private static TaskPersistenceMigration Load(int version, string name, string resourceFileName)
    {
        var assembly = typeof(TaskPersistenceMigrationCatalog).Assembly;
        var resourceName = assembly.GetManifestResourceNames()
            .Single(candidate => candidate.EndsWith(resourceFileName, StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded migration '{resourceName}' was not found.");
        using var reader = new StreamReader(stream);
        var sql = reader.ReadToEnd();
        var checksum = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sql)));
        return new TaskPersistenceMigration(version, name, sql, checksum);
    }
}

internal sealed record TaskPersistenceMigration(
    int Version,
    string Name,
    string Sql,
    string Sha256);
