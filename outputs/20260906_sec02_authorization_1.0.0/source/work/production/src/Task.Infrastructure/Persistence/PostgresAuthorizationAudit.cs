using Npgsql;

namespace Task.Infrastructure.Persistence;

internal static class PostgresAuthorizationAudit
{
    public static void AdministrativeRead(NpgsqlDataSource source, Guid org, Guid actor, string action)
    {
        using var connection = source.OpenConnection();
        AdministrativeRead(connection, null, org, actor, action);
    }

    public static void AdministrativeRead(NpgsqlConnection connection, NpgsqlTransaction? transaction, Guid org, Guid actor, string action)
    {
        using var command = new NpgsqlCommand("INSERT INTO governance.audit_entries(id,organization_id,actor_user_id,action_code,outcome,correlation_id,request_id,metadata) SELECT gen_random_uuid(),$1,$2,'authorization.administrative_read','success',gen_random_uuid(),gen_random_uuid(),jsonb_build_object('operation',$3::text) WHERE iam.permission_granted($1,$2,'organization.manage');", connection, transaction);
        command.Parameters.AddWithValue(org); command.Parameters.AddWithValue(actor); command.Parameters.AddWithValue(action);
        command.ExecuteNonQuery();
    }
}
