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
        Load(5, "task_write_transaction_foundation", "005_task_write_transaction_foundation.sql"),
        Load(6, "task_capability_permissions", "006_task_capability_permissions.sql"),
        Load(7, "calendar_event_capability_permissions", "007_calendar_event_capability_permissions.sql"),
        Load(8, "calendar_recurrence", "008_calendar_recurrence.sql"),
        Load(9, "product_entity_stores", "009_product_entity_stores.sql"),
        Load(10, "product_api", "010_product_api.sql"),
        Load(11, "task_card", "011_task_card.sql"),
        Load(12, "user_read_permission", "012_user_read_permission.sql"),
        Load(13, "object_authorization", "013_object_authorization.sql"),
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
