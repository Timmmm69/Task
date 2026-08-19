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

    public SessionRefreshLookup? FindSessionByRefreshTokenHash(string tokenHash)
    {
        EnsureHash(tokenHash, nameof(tokenHash));

        using var command = _dataSource.CreateCommand(
            """
            SELECT
                s.organization_id,
                s.id,
                s.user_account_id,
                s.device_id,
                s.credential_version,
                s.authorization_scope_version,
                CASE
                    WHEN rt.consumed_at IS NOT NULL THEN 'consumed'
                    WHEN rt.revoked_at IS NOT NULL THEN 'revoked'
                    WHEN rt.expires_at <= clock_timestamp() THEN 'expired'
                    WHEN s.revoked_at IS NOT NULL
                        OR s.absolute_expires_at <= clock_timestamp()
                        OR s.idle_expires_at <= clock_timestamp() THEN 'expired'
                    ELSE 'active'
                END
            FROM iam.refresh_tokens rt
            INNER JOIN iam.sessions s ON s.id = rt.session_id
            WHERE rt.token_hash = $1;
            """);
        command.Parameters.Add(new NpgsqlParameter<string> { TypedValue = tokenHash });

        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return null;
        }

        var status = reader.GetString(6) switch
        {
            "consumed" => TokenStatus.Consumed,
            "revoked" => TokenStatus.Revoked,
            "expired" => TokenStatus.Expired,
            _ => TokenStatus.Active,
        };

        var lookup = new SessionRefreshLookup(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetGuid(2),
            reader.IsDBNull(3) ? null : reader.GetGuid(3),
            reader.GetInt64(4),
            reader.GetInt64(5),
            status);
        reader.Close();
        return lookup;
    }

    public SessionRequestState GetSessionRequestState(
        Guid organizationId,
        Guid sessionId,
        long expectedCredentialVersion,
        long expectedAuthorizationScopeVersion)
    {
        EnsureIdentifier(organizationId, nameof(organizationId));
        EnsureIdentifier(sessionId, nameof(sessionId));
        EnsurePositiveVersion(expectedCredentialVersion, nameof(expectedCredentialVersion));
        EnsurePositiveVersion(expectedAuthorizationScopeVersion, nameof(expectedAuthorizationScopeVersion));

        using var command = _dataSource.CreateCommand(
            """
            SELECT
                CASE
                    WHEN s.id IS NULL THEN 'expired'
                    WHEN s.revoked_at IS NOT NULL THEN 'revoked'
                    WHEN s.absolute_expires_at <= clock_timestamp()
                        OR s.idle_expires_at <= clock_timestamp() THEN 'expired'
                    WHEN ua.account_status <> 'active' THEN 'blocked'
                    WHEN ua.credential_version <> $3 THEN 'version_mismatch'
                    WHEN av.version IS NULL OR av.version <> $4 THEN 'version_mismatch'
                    ELSE 'active'
                END
            FROM iam.sessions s
            LEFT JOIN iam.user_accounts ua
                ON ua.id = s.user_account_id AND ua.organization_id = s.organization_id
            LEFT JOIN iam.authorization_scope_versions av ON av.user_account_id = ua.id
            WHERE s.organization_id = $1 AND s.id = $2;
            """);
        command.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = organizationId });
        command.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = sessionId });
        command.Parameters.Add(new NpgsqlParameter<long> { TypedValue = expectedCredentialVersion });
        command.Parameters.Add(new NpgsqlParameter<long> { TypedValue = expectedAuthorizationScopeVersion });

        var state = command.ExecuteScalar();
        return state switch
        {
            "active" => SessionRequestState.Active,
            "revoked" => SessionRequestState.SessionRevoked,
            "blocked" => SessionRequestState.AccountBlocked,
            "version_mismatch" => SessionRequestState.VersionMismatch,
            _ => SessionRequestState.SessionExpired,
        };
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

    /// <summary>
    /// Hard-deletes all non-revoked sessions of the user except the optional kept session,
    /// together with their refresh tokens, in one transaction. Returns the number of deleted
    /// sessions. Deleted sessions are treated as expired by
    /// GetActiveSession/GetSessionRequestState, so a password change clears the user's other
    /// sessions immediately. Refresh tokens are removed first because iam.refresh_tokens
    /// references iam.sessions with ON DELETE RESTRICT.
    /// </summary>
    public async global::System.Threading.Tasks.Task<int> RevokeAllUserSessionsExceptAsync(
        Guid organizationId,
        Guid userId,
        Guid? exceptSessionId,
        CancellationToken cancellationToken = default)
    {
        EnsureIdentifier(organizationId, nameof(organizationId));
        EnsureIdentifier(userId, nameof(userId));

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await using (var tokenCommand = new NpgsqlCommand(
            """
            DELETE FROM iam.refresh_tokens
            WHERE session_id IN (
                SELECT id FROM iam.sessions
                WHERE organization_id = $1 AND user_account_id = $2 AND revoked_at IS NULL
                    AND ($3::uuid IS NULL OR id <> $3));
            """,
            connection,
            transaction))
        {
            AddRevocationParameters(tokenCommand, organizationId, userId, exceptSessionId);
            await tokenCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        int deletedSessions;
        await using (var sessionCommand = new NpgsqlCommand(
            """
            DELETE FROM iam.sessions
            WHERE organization_id = $1 AND user_account_id = $2 AND revoked_at IS NULL
                AND ($3::uuid IS NULL OR id <> $3);
            """,
            connection,
            transaction))
        {
            AddRevocationParameters(sessionCommand, organizationId, userId, exceptSessionId);
            deletedSessions = await sessionCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return deletedSessions;
    }

    /// <summary>
    /// Hard-deletes up to maxCount expired refresh tokens (expires_at older than the cutoff) in
    /// oldest-first order. Returns the actual number of deleted tokens.
    /// </summary>
    public async global::System.Threading.Tasks.Task<int> PurgeExpiredRefreshTokensAsync(
        DateTimeOffset olderThanUtc,
        int maxCount,
        CancellationToken cancellationToken = default)
    {
        EnsurePositiveCount(maxCount, nameof(maxCount));

        await using var command = _dataSource.CreateCommand(
            """
            DELETE FROM iam.refresh_tokens
            WHERE id IN (
                SELECT id
                FROM iam.refresh_tokens
                WHERE expires_at < $1
                ORDER BY expires_at
                LIMIT $2);
            """);
        command.Parameters.Add(new NpgsqlParameter<DateTimeOffset> { TypedValue = olderThanUtc });
        command.Parameters.Add(new NpgsqlParameter<int> { TypedValue = maxCount });
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// Hard-deletes up to maxCount expired sessions (absolute_expires_at older than the cutoff)
    /// in oldest-first order. Sessions still referenced by append-only audit entries are
    /// skipped: governance.audit_entries.actor_session_id references iam.sessions with
    /// ON DELETE RESTRICT, and audit retention is handled by a separate policy. Returns the
    /// actual number of deleted sessions.
    /// </summary>
    public async global::System.Threading.Tasks.Task<int> PurgeExpiredSessionsAsync(
        DateTimeOffset olderThanUtc,
        int maxCount,
        CancellationToken cancellationToken = default)
    {
        EnsurePositiveCount(maxCount, nameof(maxCount));

        await using var command = _dataSource.CreateCommand(
            """
            DELETE FROM iam.sessions
            WHERE id IN (
                SELECT target.id
                FROM iam.sessions AS target
                WHERE target.absolute_expires_at < $1
                    AND NOT EXISTS (
                        SELECT 1 FROM governance.audit_entries AS audit_entry
                        WHERE audit_entry.actor_session_id = target.id)
                ORDER BY target.absolute_expires_at
                LIMIT $2);
            """);
        command.Parameters.Add(new NpgsqlParameter<DateTimeOffset> { TypedValue = olderThanUtc });
        command.Parameters.Add(new NpgsqlParameter<int> { TypedValue = maxCount });
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void AddRevocationParameters(
        NpgsqlCommand command,
        Guid organizationId,
        Guid userId,
        Guid? exceptSessionId)
    {
        command.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = organizationId });
        command.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = userId });
        command.Parameters.Add(new NpgsqlParameter
        {
            NpgsqlDbType = NpgsqlDbType.Uuid,
            Value = exceptSessionId is null || exceptSessionId == Guid.Empty ? DBNull.Value : exceptSessionId.Value,
        });
    }

    private static void EnsureIdentifier(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("Identifier must not be empty.", parameterName);
        }
    }

    private static void EnsurePositiveVersion(long value, string parameterName)
    {
        if (value <= 0)
        {
            throw new ArgumentException("Version must be positive.", parameterName);
        }
    }

    private static void EnsurePositiveCount(int value, string parameterName)
    {
        if (value <= 0)
        {
            throw new ArgumentException("Batch size must be positive.", parameterName);
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
