using Npgsql;
using NpgsqlTypes;
using Task.Application.Security;

namespace Task.Infrastructure.Persistence;

/// <summary>
/// PostgreSQL-backed implementation of ISessionRepository over the iam.sessions and
/// iam.refresh_tokens tables (migration 002). All expiry checks use the database clock.
/// </summary>
public sealed class PostgresSessionRepository : ISessionRepository
{
    private readonly NpgsqlDataSource _dataSource;

    public PostgresSessionRepository(NpgsqlDataSource dataSource)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        _dataSource = dataSource;
    }

    public SessionSnapshot? GetActiveSession(Guid organizationId, Guid sessionId)
    {
        EnsureIdentifier(organizationId, nameof(organizationId));
        EnsureIdentifier(sessionId, nameof(sessionId));

        using var command = _dataSource.CreateCommand(
            """
            SELECT
                id,
                organization_id,
                user_account_id,
                device_id,
                credential_version,
                authorization_scope_version,
                created_at,
                last_seen_at,
                idle_expires_at,
                absolute_expires_at,
                revoked_at,
                revoke_reason
            FROM iam.sessions
            WHERE organization_id = $1 AND id = $2 AND revoked_at IS NULL
                AND absolute_expires_at > clock_timestamp() AND idle_expires_at > clock_timestamp();
            """);
        command.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = organizationId });
        command.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = sessionId });

        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return null;
        }

        var snapshot = new SessionSnapshot(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetGuid(2),
            reader.IsDBNull(3) ? null : reader.GetGuid(3),
            reader.GetInt64(4),
            reader.GetInt64(5),
            reader.GetFieldValue<DateTimeOffset>(6),
            reader.GetFieldValue<DateTimeOffset>(7),
            reader.GetFieldValue<DateTimeOffset>(8),
            reader.GetFieldValue<DateTimeOffset>(9),
            ReadNullableTimestamp(reader, 10),
            reader.IsDBNull(11) ? null : reader.GetString(11));
        reader.Close();
        return snapshot;
    }

    public void CreateSession(SessionSnapshot session, RefreshTokenRecord refreshToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(refreshToken);
        EnsureIdentifier(session.OrganizationId, nameof(session.OrganizationId));
        EnsureIdentifier(session.SessionId, nameof(session.SessionId));
        EnsureIdentifier(session.UserAccountId, nameof(session.UserAccountId));
        EnsureIdentifier(refreshToken.Id, nameof(refreshToken.Id));
        EnsureIdentifier(refreshToken.SessionId, nameof(refreshToken.SessionId));
        EnsureHash(refreshToken.TokenHash, nameof(refreshToken));

        using var connection = _dataSource.OpenConnection();
        using var transaction = connection.BeginTransaction();

        using (var sessionCommand = new NpgsqlCommand(
            """
            INSERT INTO iam.sessions (
                id, organization_id, user_account_id, device_id, credential_version,
                authorization_scope_version, created_at, last_seen_at, idle_expires_at,
                absolute_expires_at, revoked_at, revoke_reason)
            VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10, $11, $12);
            """,
            connection,
            transaction))
        {
            sessionCommand.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = session.SessionId });
            sessionCommand.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = session.OrganizationId });
            sessionCommand.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = session.UserAccountId });
            AddNullableGuid(sessionCommand, session.DeviceId);
            sessionCommand.Parameters.Add(new NpgsqlParameter<long> { TypedValue = session.CredentialVersion });
            sessionCommand.Parameters.Add(new NpgsqlParameter<long> { TypedValue = session.AuthorizationScopeVersion });
            sessionCommand.Parameters.Add(new NpgsqlParameter<DateTimeOffset> { TypedValue = session.CreatedAtUtc });
            sessionCommand.Parameters.Add(new NpgsqlParameter<DateTimeOffset> { TypedValue = session.LastSeenAtUtc });
            sessionCommand.Parameters.Add(new NpgsqlParameter<DateTimeOffset> { TypedValue = session.IdleExpiresAtUtc });
            sessionCommand.Parameters.Add(new NpgsqlParameter<DateTimeOffset> { TypedValue = session.AbsoluteExpiresAtUtc });
            AddNullableTimestamp(sessionCommand, session.RevokedAtUtc);
            AddNullableText(sessionCommand, session.RevokeReason);
            sessionCommand.ExecuteNonQuery();
        }

        using (var tokenCommand = new NpgsqlCommand(
            """
            INSERT INTO iam.refresh_tokens (
                id, session_id, token_hash, issued_at, expires_at, consumed_at, replaced_by_id, revoked_at)
            VALUES ($1, $2, $3, clock_timestamp(), $4, NULL, NULL, NULL);
            """,
            connection,
            transaction))
        {
            tokenCommand.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = refreshToken.Id });
            tokenCommand.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = session.SessionId });
            tokenCommand.Parameters.Add(new NpgsqlParameter<string> { TypedValue = refreshToken.TokenHash });
            tokenCommand.Parameters.Add(new NpgsqlParameter<DateTimeOffset> { TypedValue = refreshToken.ExpiresAtUtc });
            tokenCommand.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    public bool RotateRefreshToken(
        Guid organizationId,
        Guid sessionId,
        string consumedTokenHash,
        RefreshTokenRecord newRefreshToken)
    {
        EnsureIdentifier(organizationId, nameof(organizationId));
        EnsureIdentifier(sessionId, nameof(sessionId));
        EnsureHash(consumedTokenHash, nameof(consumedTokenHash));
        ArgumentNullException.ThrowIfNull(newRefreshToken);
        EnsureIdentifier(newRefreshToken.Id, nameof(newRefreshToken.Id));
        EnsureHash(newRefreshToken.TokenHash, nameof(newRefreshToken));

        using var connection = _dataSource.OpenConnection();
        using var transaction = connection.BeginTransaction();

        using (var consumeCommand = new NpgsqlCommand(
            """
            UPDATE iam.refresh_tokens
            SET consumed_at = clock_timestamp(), replaced_by_id = $3
            WHERE session_id = $1 AND token_hash = $2 AND consumed_at IS NULL AND revoked_at IS NULL;
            """,
            connection,
            transaction))
        {
            consumeCommand.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = sessionId });
            consumeCommand.Parameters.Add(new NpgsqlParameter<string> { TypedValue = consumedTokenHash });
            consumeCommand.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = newRefreshToken.Id });
            if (consumeCommand.ExecuteNonQuery() == 0)
            {
                transaction.Rollback();
                return false;
            }
        }

        using (var insertCommand = new NpgsqlCommand(
            """
            INSERT INTO iam.refresh_tokens (
                id, session_id, token_hash, issued_at, expires_at, consumed_at, replaced_by_id, revoked_at)
            VALUES ($1, $2, $3, clock_timestamp(), $4, NULL, NULL, NULL);
            """,
            connection,
            transaction))
        {
            insertCommand.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = newRefreshToken.Id });
            insertCommand.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = sessionId });
            insertCommand.Parameters.Add(new NpgsqlParameter<string> { TypedValue = newRefreshToken.TokenHash });
            insertCommand.Parameters.Add(new NpgsqlParameter<DateTimeOffset> { TypedValue = newRefreshToken.ExpiresAtUtc });
            insertCommand.ExecuteNonQuery();
        }

        transaction.Commit();
        return true;
    }

    public void TouchSession(Guid organizationId, Guid sessionId)
    {
        EnsureIdentifier(organizationId, nameof(organizationId));
        EnsureIdentifier(sessionId, nameof(sessionId));

        using var command = _dataSource.CreateCommand(
            """
            UPDATE iam.sessions
            SET last_seen_at = clock_timestamp()
            WHERE organization_id = $1 AND id = $2 AND revoked_at IS NULL
                AND absolute_expires_at > clock_timestamp() AND idle_expires_at > clock_timestamp();
            """);
        command.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = organizationId });
        command.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = sessionId });
        command.ExecuteNonQuery();
    }

    public void RevokeSession(Guid organizationId, Guid sessionId, string? reason)
    {
        EnsureIdentifier(organizationId, nameof(organizationId));
        EnsureIdentifier(sessionId, nameof(sessionId));

        using var command = _dataSource.CreateCommand(
            """
            UPDATE iam.sessions
            SET revoked_at = clock_timestamp(), revoke_reason = $3
            WHERE organization_id = $1 AND id = $2 AND revoked_at IS NULL;
            """);
        command.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = organizationId });
        command.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = sessionId });
        command.Parameters.Add(new NpgsqlParameter
        {
            NpgsqlDbType = NpgsqlDbType.Text,
            Value = reason is null ? DBNull.Value : reason,
        });
        command.ExecuteNonQuery();
    }

    public int RevokeAllUserSessions(Guid organizationId, Guid userId, Guid? exceptSessionId, string? reason)
    {
        EnsureIdentifier(organizationId, nameof(organizationId));
        EnsureIdentifier(userId, nameof(userId));

        using var command = _dataSource.CreateCommand(
            """
            UPDATE iam.sessions
            SET revoked_at = clock_timestamp(), revoke_reason = $4
            WHERE organization_id = $1 AND user_account_id = $2 AND revoked_at IS NULL
                AND ($3 IS NULL OR id <> $3);
            """);
        command.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = organizationId });
        command.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = userId });
        command.Parameters.Add(new NpgsqlParameter
        {
            NpgsqlDbType = NpgsqlDbType.Uuid,
            Value = exceptSessionId is null || exceptSessionId == Guid.Empty ? DBNull.Value : exceptSessionId.Value,
        });
        command.Parameters.Add(new NpgsqlParameter
        {
            NpgsqlDbType = NpgsqlDbType.Text,
            Value = reason is null ? DBNull.Value : reason,
        });
        return command.ExecuteNonQuery();
    }

    private static void EnsureIdentifier(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("Identifier must not be empty.", parameterName);
        }
    }

    private static void EnsureHash(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Token hash must not be empty.", parameterName);
        }
    }

    private static void AddNullableText(NpgsqlCommand command, string? value) =>
        command.Parameters.Add(new NpgsqlParameter
        {
            NpgsqlDbType = NpgsqlDbType.Text,
            Value = value is null ? DBNull.Value : value,
        });

    private static void AddNullableTimestamp(NpgsqlCommand command, DateTimeOffset? value) =>
        command.Parameters.Add(new NpgsqlParameter
        {
            NpgsqlDbType = NpgsqlDbType.TimestampTz,
            Value = value is null ? DBNull.Value : value.Value,
        });

    private static void AddNullableGuid(NpgsqlCommand command, Guid? value) =>
        command.Parameters.Add(new NpgsqlParameter
        {
            NpgsqlDbType = NpgsqlDbType.Uuid,
            Value = value is null ? DBNull.Value : value.Value,
        });

    private static DateTimeOffset? ReadNullableTimestamp(NpgsqlDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetFieldValue<DateTimeOffset>(ordinal);
}
