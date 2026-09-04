using System.Globalization;
using System.Text;
using Npgsql;
using NpgsqlTypes;
using Task.Application.Audit;

namespace Task.Infrastructure.Postgres;

/// <summary>
/// PostgreSQL-backed implementation of IAuditEntryStore over governance.audit_entries
/// (migration 002; table named audit_entries, columns id, organization_id, occurred_at,
/// actor_user_id, actor_session_id, action_code, outcome, reason_code, correlation_id,
/// request_id, metadata, old_state, new_state, redaction_level).
///
/// Append-only timestamp policy: the caller-supplied AuditEntryRecord.OccurredAt is
/// IGNORED. occurred_at is always assigned by the server (the column is deliberately absent
/// from the INSERT column list, so the DEFAULT clock_timestamp() applies), because the
/// store cannot verify or control any client-supplied timestamp: accepting caller values
/// would let a caller backdate or forward-date journal entries. Append-only therefore also
/// means tamper-proof event time.
///
/// Immutability of existing rows is enforced by the database trigger
/// trg_audit_entries_append_only (migration 002), which rejects UPDATE and DELETE on
/// governance.audit_entries with ERRCODE '42501' (insufficient_privilege) and message
/// 'APPEND_ONLY_AUDIT_ENTRIES'. This store exposes no other mutation path. Entries never
/// carry passwords, tokens or secrets: the store treats Metadata/OldState/NewState as
/// opaque JSON (it neither interprets nor logs their content) and only bounds Metadata to
/// 16 KiB; excluding sensitive values from the journal is the writing service's
/// responsibility (separate package).
/// </summary>
public sealed class PostgresAuditEntryStore : IAuditEntryStore
{
    private const int DefaultPageSize = 50;
    private const int MaxPageSize = 200;
    private const int MaxMetadataBytes = 16 * 1024;

    private readonly NpgsqlDataSource _dataSource;

    public PostgresAuditEntryStore(NpgsqlDataSource dataSource)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        _dataSource = dataSource;
    }

    public async global::System.Threading.Tasks.Task AppendAsync(
        AuditEntryRecord entry,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        EnsureIdentifier(entry.Id, nameof(entry.Id));
        EnsureIdentifier(entry.OrganizationId, nameof(entry.OrganizationId));
        EnsureIdentifier(entry.CorrelationId, nameof(entry.CorrelationId));
        EnsureIdentifier(entry.RequestId, nameof(entry.RequestId));
        if (string.IsNullOrWhiteSpace(entry.ActionCode))
        {
            throw new ArgumentException("Audit action code must not be empty.", nameof(entry));
        }

        if (Encoding.UTF8.GetByteCount(entry.Metadata) > MaxMetadataBytes)
        {
            throw new ArgumentOutOfRangeException(
                nameof(entry),
                "Audit metadata must not exceed 16 KiB (UTF-8).");
        }

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO governance.audit_entries (
                id, organization_id, actor_user_id, actor_session_id, action_code, outcome,
                reason_code, correlation_id, request_id, metadata, old_state, new_state,
                redaction_level)
            VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10::jsonb, $11::jsonb, $12::jsonb, $13);
            """,
            connection);
        command.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = entry.Id });
        command.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = entry.OrganizationId });
        AddNullableGuidParameter(command, entry.ActorUserId);
        AddNullableGuidParameter(command, entry.ActorSessionId);
        command.Parameters.Add(new NpgsqlParameter<string> { TypedValue = entry.ActionCode });
        command.Parameters.Add(new NpgsqlParameter<string> { TypedValue = entry.Outcome });
        AddNullableTextParameter(command, entry.ReasonCode);
        command.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = entry.CorrelationId });
        command.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = entry.RequestId });
        command.Parameters.Add(new NpgsqlParameter<string> { TypedValue = entry.Metadata });
        AddNullableTextParameter(command, entry.OldState);
        AddNullableTextParameter(command, entry.NewState);
        command.Parameters.Add(new NpgsqlParameter<string> { TypedValue = entry.RedactionLevel });
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async global::System.Threading.Tasks.Task<AuditPage> ReadAsync(
        AuditQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        EnsureIdentifier(query.OrgId, nameof(query.OrgId));

        var pageSize = NormalizePageSize(query.PageSize);
        var actionFilter = NormalizeFilter(query.ActionFilter);
        var outcomeFilter = NormalizeFilter(query.OutcomeFilter);
        var (tokenOccurredAt, tokenId) = DecodePageToken(query.PageToken);

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            SELECT
                id,
                organization_id,
                occurred_at,
                actor_user_id,
                actor_session_id,
                action_code,
                outcome,
                reason_code,
                correlation_id,
                request_id,
                metadata::text,
                old_state::text,
                new_state::text,
                redaction_level
            FROM governance.audit_entries
            WHERE organization_id = $1
              AND ($2::text IS NULL OR action_code = $2)
              AND ($3::text IS NULL OR outcome = $3)
              AND ($4::timestamptz IS NULL OR occurred_at >= $4)
              AND ($5::timestamptz IS NULL OR occurred_at <= $5)
              AND ($6::timestamptz IS NULL
                   OR occurred_at < $6
                   OR (occurred_at = $6 AND id < $7::uuid))
            AND ($9::uuid IS NULL OR actor_user_id=$9)
              AND (NOT $10 OR action_code IN ('UserLoggedIn','LoginFailed'))
            ORDER BY occurred_at DESC, id DESC
            LIMIT $8;
            """,
            connection);
        command.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = query.OrgId });
        AddNullableTextParameter(command, actionFilter);
        AddNullableTextParameter(command, outcomeFilter);
        AddNullableTimestampParameter(command, query.FromUtc);
        AddNullableTimestampParameter(command, query.ToUtc);
        AddNullableTimestampParameter(command, tokenOccurredAt);
        AddNullableGuidParameter(command, tokenId);
        command.Parameters.Add(new NpgsqlParameter<int> { TypedValue = pageSize + 1 });

        AddNullableGuidParameter(command, query.ActorUserId);
        command.Parameters.Add(new NpgsqlParameter<bool> { TypedValue=query.LoginAttemptsOnly });

        var entries = new List<AuditEntryRecord>(pageSize + 1);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            entries.Add(new AuditEntryRecord(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.GetFieldValue<DateTimeOffset>(2),
                reader.IsDBNull(3) ? null : reader.GetGuid(3),
                reader.IsDBNull(4) ? null : reader.GetGuid(4),
                reader.GetString(5),
                reader.GetString(6),
                reader.IsDBNull(7) ? null : reader.GetString(7),
                reader.GetGuid(8),
                reader.GetGuid(9),
                reader.GetString(10),
                reader.IsDBNull(11) ? null : reader.GetString(11),
                reader.IsDBNull(12) ? null : reader.GetString(12),
                reader.GetString(13)));
        }

        // The SELECT fetches pageSize + 1 rows at most: an extra row proves that a further
        // page exists and is trimmed away, with the page boundary producing NextPageToken.
        if (entries.Count == pageSize + 1)
        {
            entries.RemoveAt(pageSize);
            return new AuditPage(
                entries,
                NextPageToken: EncodePageToken(entries[^1].OccurredAt, entries[^1].Id));
        }

        return new AuditPage(entries, NextPageToken: null);
    }

    private static int NormalizePageSize(int pageSize) => pageSize switch
    {
        < 1 => DefaultPageSize,
        > MaxPageSize => MaxPageSize,
        _ => pageSize,
    };

    private static string? NormalizeFilter(string? filter) =>
        string.IsNullOrWhiteSpace(filter) ? null : filter;

    /// <summary>
    /// Page tokens are opaque to callers but deterministic to this store: Base64 of
    /// "&#123;UtcTicks&#125;|&#123;id:N&#125;", i.e. the keyset boundary
    /// (occurred_at, id) of the last returned entry. Malformed tokens are rejected with
    /// ArgumentException instead of being silently treated as a first page.
    /// </summary>
    private static string EncodePageToken(DateTimeOffset occurredAt, Guid id) =>
        Convert.ToBase64String(
            Encoding.UTF8.GetBytes(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"{occurredAt.UtcTicks}|{id:N}")));

    private static (DateTimeOffset? OccurredAt, Guid? Id) DecodePageToken(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return (null, null);
        }

        string payload;
        try
        {
            payload = Encoding.UTF8.GetString(Convert.FromBase64String(token));
        }
        catch (FormatException)
        {
            throw new ArgumentException("Page token has an invalid format.", nameof(token));
        }

        var separator = payload.IndexOf('|');
        if (separator <= 0 ||
            !long.TryParse(
                payload.AsSpan(0, separator),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var utcTicks) ||
            !Guid.TryParseExact(payload.AsSpan(separator + 1), "N", out var id))
        {
            throw new ArgumentException("Page token has an invalid format.", nameof(token));
        }

        DateTimeOffset occurredAt;
        try
        {
            occurredAt = new DateTimeOffset(utcTicks, TimeSpan.Zero);
        }
        catch (ArgumentOutOfRangeException)
        {
            throw new ArgumentException("Page token has an invalid format.", nameof(token));
        }

        return (occurredAt, id);
    }

    private static void EnsureIdentifier(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("Identifier must not be empty.", parameterName);
        }
    }

    private static void AddNullableTextParameter(NpgsqlCommand command, string? value) =>
        command.Parameters.Add(new NpgsqlParameter
        {
            NpgsqlDbType = NpgsqlDbType.Text,
            Value = value is null ? DBNull.Value : value,
        });

    private static void AddNullableGuidParameter(NpgsqlCommand command, Guid? value) =>
        command.Parameters.Add(new NpgsqlParameter
        {
            NpgsqlDbType = NpgsqlDbType.Uuid,
            Value = value is null ? DBNull.Value : value.Value,
        });

    private static void AddNullableTimestampParameter(NpgsqlCommand command, DateTimeOffset? value) =>
        command.Parameters.Add(new NpgsqlParameter
        {
            NpgsqlDbType = NpgsqlDbType.TimestampTz,
            Value = value is null ? DBNull.Value : value.Value,
        });
}