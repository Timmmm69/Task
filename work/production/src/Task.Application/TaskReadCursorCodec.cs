using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Task.Application;

/// <summary>
/// Internal protocol helper for the opaque task-list cursor. The serialized
/// payload is not an external API contract and may change with CursorVersion.
/// Authorization never relies on cursor values: stores still apply the
/// request's organization filter independently.
/// </summary>
public static class TaskReadCursorCodec
{
    public const int CursorVersion = 1;
    public const int MaximumEncodedLength = 512;

    private const string NormalizedQuery =
        "task-read:v1|lifecycle=active|order=updated_at:desc,id:desc|page-size=50";

    private static readonly string NormalizedQueryHash = Base64UrlEncode(
        SHA256.HashData(Encoding.UTF8.GetBytes(NormalizedQuery)));

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    public static string Create(
        Guid organizationId,
        Guid userAccountId,
        long authorizationScopeVersion,
        DateTimeOffset snapshotBoundaryUtc,
        DateTimeOffset lastUpdatedAtUtc,
        Guid lastId)
    {
        EnsureBinding(organizationId, userAccountId, authorizationScopeVersion);
        EnsureContinuation(snapshotBoundaryUtc, lastUpdatedAtUtc, lastId);

        var payload = new CursorPayload(
            CursorVersion,
            organizationId,
            userAccountId,
            authorizationScopeVersion,
            snapshotBoundaryUtc,
            lastUpdatedAtUtc,
            lastId,
            NormalizedQueryHash);
        var encoded = Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(payload, SerializerOptions));
        if (encoded.Length > MaximumEncodedLength)
        {
            throw new InvalidOperationException("Generated task cursor exceeds the supported length.");
        }

        return encoded;
    }

    public static TaskReadContinuation Parse(
        string cursor,
        Guid organizationId,
        Guid userAccountId,
        long authorizationScopeVersion)
    {
        EnsureBinding(organizationId, userAccountId, authorizationScopeVersion);
        if (string.IsNullOrWhiteSpace(cursor) || cursor.Length > MaximumEncodedLength)
        {
            throw new TaskReadCursorException();
        }

        try
        {
            var payload = JsonSerializer.Deserialize<CursorPayload>(
                Base64UrlDecode(cursor),
                SerializerOptions);
            if (payload is null ||
                payload.Version != CursorVersion ||
                payload.OrganizationId != organizationId ||
                payload.UserAccountId != userAccountId ||
                payload.AuthorizationScopeVersion != authorizationScopeVersion ||
                payload.QueryHash != NormalizedQueryHash)
            {
                throw new TaskReadCursorException();
            }

            EnsureContinuation(
                payload.SnapshotBoundaryUtc,
                payload.LastUpdatedAtUtc,
                payload.LastId);
            return new TaskReadContinuation(
                payload.SnapshotBoundaryUtc,
                payload.LastUpdatedAtUtc,
                payload.LastId);
        }
        catch (TaskReadCursorException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is FormatException or JsonException or ArgumentException)
        {
            throw new TaskReadCursorException(exception);
        }
    }

    private static void EnsureBinding(
        Guid organizationId,
        Guid userAccountId,
        long authorizationScopeVersion)
    {
        if (organizationId == Guid.Empty)
        {
            throw new ArgumentException("Identifier must not be empty.", nameof(organizationId));
        }

        if (userAccountId == Guid.Empty)
        {
            throw new ArgumentException("Identifier must not be empty.", nameof(userAccountId));
        }

        if (authorizationScopeVersion < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(authorizationScopeVersion),
                "Authorization scope version must be positive.");
        }
    }

    private static void EnsureContinuation(
        DateTimeOffset snapshotBoundaryUtc,
        DateTimeOffset lastUpdatedAtUtc,
        Guid lastId)
    {
        if (snapshotBoundaryUtc.Offset != TimeSpan.Zero ||
            lastUpdatedAtUtc.Offset != TimeSpan.Zero ||
            lastUpdatedAtUtc > snapshotBoundaryUtc ||
            lastId == Guid.Empty)
        {
            throw new TaskReadCursorException();
        }
    }

    private static string Base64UrlEncode(ReadOnlySpan<byte> value) =>
        Convert.ToBase64String(value)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private static byte[] Base64UrlDecode(string value)
    {
        foreach (var character in value)
        {
            if (!char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_')
            {
                throw new FormatException("Cursor is not base64url encoded.");
            }
        }

        var base64 = value.Replace('-', '+').Replace('_', '/');
        base64 += (base64.Length % 4) switch
        {
            0 => string.Empty,
            2 => "==",
            3 => "=",
            _ => throw new FormatException("Cursor has invalid base64url length."),
        };
        return Convert.FromBase64String(base64);
    }

    private sealed record CursorPayload(
        [property: JsonPropertyName("v")] int Version,
        [property: JsonPropertyName("o")] Guid OrganizationId,
        [property: JsonPropertyName("u")] Guid UserAccountId,
        [property: JsonPropertyName("s")] long AuthorizationScopeVersion,
        [property: JsonPropertyName("b")] DateTimeOffset SnapshotBoundaryUtc,
        [property: JsonPropertyName("t")] DateTimeOffset LastUpdatedAtUtc,
        [property: JsonPropertyName("i")] Guid LastId,
        [property: JsonPropertyName("q")] string QueryHash);
}

/// <summary>Validated keyset boundary returned by <see cref="TaskReadCursorCodec"/>.</summary>
public sealed record TaskReadContinuation(
    DateTimeOffset SnapshotBoundaryUtc,
    DateTimeOffset LastUpdatedAtUtc,
    Guid LastId);
