using Npgsql;
using NpgsqlTypes;
using System.Text.Json;
using Task.Application.Inventory;

namespace Task.Infrastructure.Postgres;

internal sealed class PostgresInventoryStore(NpgsqlDataSource source) : IInventoryStore
{
    public async System.Threading.Tasks.Task<StoredInventory> ReadAsync(Guid organizationId, CancellationToken cancellationToken)
    {
        await using var command = source.CreateCommand("""
            SELECT revision, document, manager_id FROM work.material_inventory WHERE organization_id = @org
            """);
        command.Parameters.AddWithValue("org", organizationId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new(reader.GetInt64(0), InventoryJson.Read(reader.GetString(1)), reader.IsDBNull(2) ? null : reader.GetGuid(2))
            : new(0, InventoryData.Empty(), null);
    }

    public async System.Threading.Tasks.Task<bool> TrySaveAsync(Guid organizationId, StoredInventory next, CancellationToken cancellationToken)
    {
        await using var command = source.CreateCommand(next.Revision == 1
            ? """
                INSERT INTO work.material_inventory (organization_id, revision, document, manager_id)
                VALUES (@org, @revision, @document, @manager) ON CONFLICT (organization_id) DO NOTHING
                """
            : """
                UPDATE work.material_inventory SET revision = @revision, document = @document, manager_id = @manager
                WHERE organization_id = @org AND revision = @revision - 1
                """);
        command.Parameters.AddWithValue("org", organizationId);
        command.Parameters.AddWithValue("revision", next.Revision);
        command.Parameters.AddWithValue("document", NpgsqlDbType.Jsonb, JsonSerializer.Serialize(next.Data, InventoryJson.Options));
        command.Parameters.AddWithValue("manager", NpgsqlDbType.Uuid, (object?)next.ManagerId ?? DBNull.Value);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }
}
