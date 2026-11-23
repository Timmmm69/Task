using System.Diagnostics;
using System.Text.Json.Nodes;
using Npgsql;
using Task.Application.ProductData;

namespace Task.Infrastructure.Persistence;

internal sealed partial class PostgresProductApiStore
{
    private const int NotificationPollLimit = 2000;
    private const int NotificationPollSessions = 512;
    private static readonly TimeSpan NotificationPollRetention = TimeSpan.FromMinutes(10);
    private readonly Dictionary<(Guid Organization, Guid User, Guid Session), NotificationPollState> _notificationPolls = [];

    private ProductApiResponse PollNotifications(NpgsqlConnection connection, NpgsqlTransaction transaction, ProductApiRequest request)
    {
        ValidateQuery(request, "revision notifications settings");
        if (!request.Route.Allows(request.Permissions)) throw Error(403, "FORBIDDEN", "Permission is required.");
        var revisionText = request.Query.GetValueOrDefault("revision");
        Guid revision = default;
        if (revisionText is not null && (!Guid.TryParseExact(revisionText, "N", out revision) || revision == Guid.Empty))
            throw Invalid("Invalid notification revision.");
        var includeNotifications = Include("notifications") && request.Permissions.Contains("Notification.ReadOwn");
        var includeSettings = Include("settings") && request.Permissions.Contains("Settings.ReadOwn");
        var result = new JsonObject
        {
            ["revision"] = null,
            ["reset"] = true,
            ["historyRequired"] = false,
            ["notificationsIncluded"] = includeNotifications,
            ["items"] = new JsonArray(),
            ["removedIds"] = new JsonArray(),
            ["preferences"] = null,
            ["autostartEnabled"] = null,
        };
        if (includeSettings)
        {
            var settings = One(connection, transaction, $"""
                SELECT jsonb_build_object(
                    'preferences', ({NotificationPreferencesSelect}),
                    'autostart_enabled', COALESCE(
                        (SELECT autostart_enabled FROM org.user_settings WHERE organization_id=@org AND user_account_id=@user),
                        @defaultAutostart));
                """, request, ("defaultAutostart", DefaultAutostartEnabled))!;
            result["preferences"] = settings["preferences"]?.DeepClone() ?? DefaultNotificationPreferences();
            result["autostartEnabled"] = settings["autostartEnabled"]?.DeepClone();
        }
        if (!includeNotifications) return new(result);

        var key = (request.OrganizationId, request.UserId, request.SessionId);
        var current = ReadNotificationPollStamps(connection, transaction, request);
        if (current.Count > NotificationPollLimit)
        {
            result["historyRequired"] = true;
            return new(result);
        }

        var now = Stopwatch.GetTimestamp();
        NotificationPollState? previous;
        lock (_notificationPolls)
        {
            previous = _notificationPolls.GetValueOrDefault(key);
            if (previous is not null &&
                (previous.Revision != revision || Stopwatch.GetElapsedTime(previous.LastUsed, now) > NotificationPollRetention))
                previous = null;
        }

        var stamps = current.ToArray();
        var unchanged = previous is not null && stamps.AsSpan().SequenceEqual(previous.Stamps);
        var nextRevision = unchanged ? previous!.Revision : Guid.NewGuid();
        result["revision"] = nextRevision.ToString("N");
        result["reset"] = previous is null;
        if (!unchanged)
        {
            var known = previous?.Stamps.ToDictionary(stamp => stamp.Id);
            var changed = stamps.Where(stamp => known is null || !known.TryGetValue(stamp.Id, out var old) || old != stamp)
                .Select(stamp => stamp.Id).ToArray();
            if (changed.Length > 0)
            {
                // The visibility read and this payload read share one repeatable-read snapshot.
                var items = Many(connection, transaction, """
                    SELECT to_jsonb(p)||to_jsonb(o)
                    FROM notify.notifications p JOIN core.objects o ON o.organization_id=p.organization_id AND o.id=p.id
                    WHERE p.organization_id=@org AND p.recipient_user_id=@user AND p.id=ANY(@ids)
                    ORDER BY p.id;
                    """, request, ("ids", changed));
                result["items"] = new JsonArray(items.ToArray<JsonNode?>());
            }
            if (known is not null)
            {
                var visible = stamps.Select(stamp => stamp.Id).ToHashSet();
                result["removedIds"] = new JsonArray(known.Keys.Where(id => !visible.Contains(id))
                    .Select(id => (JsonNode?)JsonValue.Create(id.ToString("D"))).ToArray());
            }
        }

        lock (_notificationPolls)
        {
            if (_notificationPolls.Count >= NotificationPollSessions && !_notificationPolls.ContainsKey(key))
                _notificationPolls.Remove(_notificationPolls.MinBy(entry => entry.Value.LastUsed).Key);
            _notificationPolls[key] = new(nextRevision, unchanged ? previous!.Stamps : stamps, now);
        }
        return new(result);

        bool Include(string name) => request.Query.GetValueOrDefault(name, "true") switch
        {
            "true" => true,
            "false" => false,
            _ => throw Invalid($"Invalid {name} flag."),
        };
    }

    private static List<NotificationStamp> ReadNotificationPollStamps(NpgsqlConnection connection, NpgsqlTransaction transaction, ProductApiRequest request)
    {
        var visibility = Visibility(Resources["notifications"]);
        // The tuple version also changes for reminder dismissals that update the notification row directly.
        using var command = Command(connection, transaction, $"""
            SELECT p.id,o.version,p.xmin::text::bigint
            FROM notify.notifications p JOIN core.objects o ON o.organization_id=p.organization_id AND o.id=p.id
            WHERE p.organization_id=@org AND o.lifecycle_state='active' AND {visibility} AND {NotificationDue}
            ORDER BY p.id LIMIT @limit;
            """, request, ("limit", NotificationPollLimit + 1));
        using var cancellation = request.CancellationToken.Register(command.Cancel);
        request.CancellationToken.ThrowIfCancellationRequested();
        using var reader = command.ExecuteReader();
        var result = new List<NotificationStamp>();
        while (reader.Read()) result.Add(new(reader.GetGuid(0), reader.GetInt64(1), reader.GetInt64(2)));
        return result;
    }

    private readonly record struct NotificationStamp(Guid Id, long Version, long RowVersion);
    private sealed record NotificationPollState(Guid Revision, NotificationStamp[] Stamps, long LastUsed);
}
