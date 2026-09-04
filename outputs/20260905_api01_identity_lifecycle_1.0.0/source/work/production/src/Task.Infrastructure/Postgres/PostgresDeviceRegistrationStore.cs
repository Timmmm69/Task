using Npgsql;
using NpgsqlTypes;
using Task.Application.Security;

namespace Task.Infrastructure.Postgres;

/// <summary>
/// PostgreSQL-backed IDeviceRegistrationStore over iam.devices (migration 002).
/// New devices insert a core.objects row (object_type = 'device') in the same transaction.
/// </summary>
public sealed partial class PostgresDeviceRegistrationStore : IDeviceRegistrationStore
{
    private const int MinFingerprintLength = 32;
    private const int MaxFingerprintLength = 256;

    private readonly NpgsqlDataSource _dataSource;

    public PostgresDeviceRegistrationStore(NpgsqlDataSource dataSource)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        _dataSource = dataSource;
    }

    public async global::System.Threading.Tasks.Task<Guid> UpsertAsync(
        Guid organizationId,
        Guid userId,
        string fingerprintHash,
        string? displayName,
        CancellationToken cancellationToken = default)
    {
        EnsureIdentifier(organizationId, nameof(organizationId));
        EnsureIdentifier(userId, nameof(userId));
        EnsureFingerprintHash(fingerprintHash);

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);

        var existingId = await TryUpdateExistingAsync(
            connection,
            organizationId,
            userId,
            fingerprintHash,
            displayName,
            cancellationToken);
        if (existingId is not null)
        {
            return existingId.Value;
        }

        var deviceId = Guid.NewGuid();
        var now = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds());

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            await using (var objectCommand = new NpgsqlCommand(
                """
                INSERT INTO core.objects (
                    id, organization_id, object_type, version,
                    created_at, created_by, updated_at, updated_by)
                VALUES ($1, $2, 'device', 1, $3, $4, $3, $4);
                """,
                connection,
                transaction))
            {
                objectCommand.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = deviceId });
                objectCommand.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = organizationId });
                objectCommand.Parameters.Add(new NpgsqlParameter<DateTimeOffset> { TypedValue = now });
                objectCommand.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = userId });
                await objectCommand.ExecuteNonQueryAsync(cancellationToken);
            }

            await using (var deviceCommand = new NpgsqlCommand(
                """
                INSERT INTO iam.devices (
                    id, organization_id, user_account_id, device_fingerprint_hash,
                    display_name, first_seen_at, last_seen_at)
                VALUES ($1, $2, $3, $4, $5, $6, $6);
                """,
                connection,
                transaction))
            {
                deviceCommand.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = deviceId });
                deviceCommand.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = organizationId });
                deviceCommand.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = userId });
                deviceCommand.Parameters.Add(new NpgsqlParameter<string> { TypedValue = fingerprintHash });
                deviceCommand.Parameters.Add(CreateNullableTextParameter(displayName));
                deviceCommand.Parameters.Add(new NpgsqlParameter<DateTimeOffset> { TypedValue = now });
                await deviceCommand.ExecuteNonQueryAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
            return deviceId;
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            await transaction.RollbackAsync(cancellationToken);

            var racedId = await TryUpdateExistingAsync(
                connection,
                organizationId,
                userId,
                fingerprintHash,
                displayName,
                cancellationToken);
            if (racedId is not null)
            {
                return racedId.Value;
            }

            throw;
        }
    }

    public async global::System.Threading.Tasks.Task<DeviceRegistrationRecord?> GetByIdAsync(
        Guid organizationId,
        Guid deviceId,
        CancellationToken cancellationToken = default)
    {
        EnsureIdentifier(organizationId, nameof(organizationId));
        EnsureIdentifier(deviceId, nameof(deviceId));

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            SELECT id, user_account_id, device_fingerprint_hash, revoked_at
            FROM iam.devices
            WHERE organization_id = $1 AND id = $2;
            """,
            connection);
        command.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = organizationId });
        command.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = deviceId });

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        DateTimeOffset? revokedAt = reader.IsDBNull(3)
            ? null
            : reader.GetFieldValue<DateTimeOffset>(3);

        return new DeviceRegistrationRecord(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetString(2),
            revokedAt);
    }

    private static async global::System.Threading.Tasks.Task<Guid?> TryUpdateExistingAsync(
        NpgsqlConnection connection,
        Guid organizationId,
        Guid userId,
        string fingerprintHash,
        string? displayName,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            UPDATE iam.devices
            SET last_seen_at = clock_timestamp(),
                display_name = CASE WHEN $4::text IS NULL THEN display_name ELSE $4 END
            WHERE organization_id = $1
              AND user_account_id = $2
              AND device_fingerprint_hash = $3
            RETURNING id;
            """,
            connection);
        command.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = organizationId });
        command.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = userId });
        command.Parameters.Add(new NpgsqlParameter<string> { TypedValue = fingerprintHash });
        command.Parameters.Add(CreateNullableTextParameter(displayName));

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is Guid id ? id : null;
    }

    private static NpgsqlParameter CreateNullableTextParameter(string? value) =>
        new()
        {
            NpgsqlDbType = NpgsqlDbType.Text,
            Value = value is null ? DBNull.Value : value,
        };

    private static void EnsureFingerprintHash(string fingerprintHash)
    {
        ArgumentNullException.ThrowIfNull(fingerprintHash);
        if (fingerprintHash.Length is < MinFingerprintLength or > MaxFingerprintLength)
        {
            throw new ArgumentException(
                $"Fingerprint hash length must be between {MinFingerprintLength} and {MaxFingerprintLength}.",
                nameof(fingerprintHash));
        }
    }

    private static void EnsureIdentifier(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("Identifier must not be empty.", parameterName);
        }
    }
}
