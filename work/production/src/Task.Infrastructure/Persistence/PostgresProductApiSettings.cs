using System.Text.Json.Nodes;
using Npgsql;
using Task.Application.ProductData;

namespace Task.Infrastructure.Persistence;

internal sealed partial class PostgresProductApiStore
{
    private ProductApiResponse Settings(NpgsqlConnection c, NpgsqlTransaction t, ProductApiRequest r)
    {
        ValidateQuery(r, "");
        var organization = r.Route.Resource == "organization-settings";
        var table = organization ? "core.organization_settings" : "org.user_settings";
        var predicate = organization ? "organization_id=@org" : "organization_id=@org AND user_account_id=@user";
        var current = One(c, t, $"SELECT to_jsonb(s) FROM {table} s WHERE {predicate} FOR UPDATE;", r);
        var persisted = current is not null;
        current ??= organization ? new JsonObject
        {
            ["organizationId"] = r.OrganizationId,
            ["trashRetentionDays"] = 30,
            ["historyRetentionDays"] = 1095,
            ["changeFeedRetentionDays"] = 90,
            ["recurrenceHorizonDays"] = 90,
            ["recurrenceMinInstances"] = 20,
            ["defaultWorkdayStart"] = "09:00:00",
            ["defaultWorkdayEnd"] = "18:00:00",
            ["firstDayOfWeek"] = 1,
            ["maxRequestBytes"] = 1048576,
            ["version"] = 1,
        } : new JsonObject
        {
            ["organizationId"] = r.OrganizationId,
            ["userAccountId"] = r.UserId,
            ["language"] = "ru-RU",
            ["timeFormat"] = "24h",
            ["firstDayOfWeek"] = 1,
            ["workdayStart"] = "09:00:00",
            ["workdayEnd"] = "18:00:00",
            ["weekendDays"] = new JsonArray(6, 7),
            ["defaultTaskDurationMinutes"] = 60,
            ["defaultReminderOffsetMinutes"] = 15,
            ["autostartEnabled"] = true,
            ["allowLocalPaths"] = true,
            ["confirmCatalogDelete"] = true,
            ["missingFileBehavior"] = "show_actions",
            ["customPreferences"] = new JsonObject(),
            ["version"] = 1,
        };
        if (r.Route.Resource == "preferences")
        {
            if (r.Route.Method == "GET") return PreferenceResponse(c, t, r, Version(current));
            if (r.ExpectedVersion != Version(current)) throw Error(412, "VERSION_CONFLICT", "Settings version has changed.");
            ValidateFields(r.Body, "enabled desktopEnabled soundEnabled defaultSnoozeMinutes quietHoursStart quietHoursEnd quietHoursTimeZone");
            if (r.Body.Count == 0) throw Invalid("At least one notification preference is required.");
            var preferenceBody = (JsonObject)PreferenceResponse(c, t, r, Version(current)).Body!.DeepClone();
            preferenceBody.Remove("version");
            foreach (var pair in r.Body) preferenceBody[pair.Key] = pair.Value?.DeepClone();
            foreach (var preference in new[] { preferenceBody })
            {
                ValidatePreference(preference);
                var body = (JsonObject)preference.DeepClone();
                body["notificationType"] = "default";
                body["organizationId"] = r.OrganizationId; body["userAccountId"] = r.UserId; body["updatedAt"] = DateTimeOffset.UtcNow;
                var columns = body.Select(p => Snake(p.Key)).ToArray();
                var updates = columns.Where(name => name is not ("organization_id" or "user_account_id" or "notification_type"))
                    .Select(name => name + "=EXCLUDED." + name);
                Run(c, t, $"INSERT INTO notify.notification_preferences({string.Join(',', columns)}) SELECT {string.Join(',', columns)} " +
                    "FROM jsonb_populate_record(NULL::notify.notification_preferences,@payload::jsonb) " +
                    "ON CONFLICT(organization_id,user_account_id,notification_type) DO UPDATE SET " + string.Join(',', updates) + ",version=notify.notification_preferences.version+1;",
                    r, ("payload", ToDatabase(body).ToJsonString()));
            }
        }
        else
        {
            if (r.Route.Method == "GET") return new(current, Version: Version(current));
            if (r.ExpectedVersion != Version(current)) throw Error(412, "VERSION_CONFLICT", "Settings version has changed.");
            ValidateFields(r.Body, organization
                ? "trashRetentionDays historyRetentionDays changeFeedRetentionDays recurrenceHorizonDays recurrenceMinInstances defaultWorkdayStart defaultWorkdayEnd firstDayOfWeek maxRequestBytes"
                : "language timeFormat firstDayOfWeek workdayStart workdayEnd weekendDays defaultTaskDurationMinutes defaultReminderOffsetMinutes autostartEnabled allowLocalPaths confirmCatalogDelete missingFileBehavior customPreferences");
            if (r.Body.Count == 0) throw Invalid("At least one settings field is required.");
            if (r.Body["customPreferences"] is not null && r.Body["customPreferences"] is not JsonObject) throw Invalid("customPreferences must be an object.");
            if (r.Body["weekendDays"] is JsonArray days && days.Select(d => d!.GetValue<int>()).Distinct().Count() != days.Count) throw Invalid("Weekend days must be unique.");
            foreach (var pair in r.Body) current[pair.Key] = pair.Value?.DeepClone();
        }
        var oldVersion = Version(current);
        current["version"] = oldVersion + 1; current["updatedAt"] = DateTimeOffset.UtcNow;
        if (!persisted) Insert(c, t, r, table, current);
        else
        {
            var columns = current.Where(p => p.Key is not ("organizationId" or "userAccountId")).Select(p => Snake(p.Key)).ToArray();
            if (Run(c, t, $"UPDATE {table} SET ({string.Join(',', columns)})=(SELECT {string.Join(',', columns)} FROM jsonb_populate_record(NULL::{table},@payload::jsonb)) " +
                $"WHERE {predicate} AND version=@expected;", r, ("payload", ToDatabase(current).ToJsonString()), ("expected", oldVersion)) != 1)
                throw Error(412, "VERSION_CONFLICT", "Settings version has changed.");
        }
        Record(c, t, r, r.Route.Resource, organization ? r.OrganizationId : r.UserId, oldVersion + 1,
            new JsonObject { ["version"] = oldVersion }, current);
        return r.Route.Resource == "preferences" ? PreferenceResponse(c, t, r, oldVersion + 1) : new(current, Version: oldVersion + 1);
    }

    private static ProductApiResponse PreferenceResponse(NpgsqlConnection c, NpgsqlTransaction t, ProductApiRequest r, int version)
    {
        var preference = One(c, t, "SELECT to_jsonb(p)-'organization_id'-'user_account_id'-'notification_type'-'updated_at' FROM notify.notification_preferences p " +
            "WHERE organization_id=@org AND user_account_id=@user AND notification_type='default';", r) ??
            new JsonObject
            {
                ["enabled"] = true,
                ["desktopEnabled"] = true,
                ["soundEnabled"] = true,
                ["defaultSnoozeMinutes"] = 15,
                ["quietHoursStart"] = null,
                ["quietHoursEnd"] = null,
                ["quietHoursTimeZone"] = null
            };
        preference["version"] = version;
        return new(preference, Version: version);
    }

    private static void ValidatePreference(JsonObject body)
    {
        var start = Text(body, "quietHoursStart"); var end = Text(body, "quietHoursEnd"); var zone = Text(body, "quietHoursTimeZone");
        if ((start is null) != (end is null) || (start is null) != (zone is null)) throw Invalid("Quiet hours require start, end and time zone together.");
        if (zone is not null)
        {
            try { _ = TimeZoneInfo.FindSystemTimeZoneById(zone); }
            catch (TimeZoneNotFoundException) { throw Invalid("Unknown quiet-hours time zone."); }
        }
    }

    private ProductApiResponse Notifications(NpgsqlConnection c, NpgsqlTransaction t, ProductApiRequest r)
    {
        ValidateQuery(r, "");
        if (r.Route.Operation == "read-all")
        {
            ValidateFields(r.Body, "notificationIds");
            if (r.Body["notificationIds"] is not JsonArray ids || ids.Count is < 1 or > 500) throw Invalid("Specify 1-500 notification IDs.");
            var selected = ids.Select(id => Guid.TryParse(id?.ToString(), out var parsed) && parsed != Guid.Empty ? parsed : throw Invalid("Invalid notification ID.")).ToArray();
            if (selected.Distinct().Count() != selected.Length) throw Invalid("Notification IDs must be unique.");
            var rows = Many(c, t, "SELECT to_jsonb(p)||to_jsonb(o) FROM notify.notifications p JOIN core.objects o ON o.id=p.id AND o.organization_id=p.organization_id " +
                "WHERE p.organization_id=@org AND p.recipient_user_id=@user AND o.lifecycle_state='active' AND p.status IN ('pending','delivered') " +
                "AND p.id=ANY(@ids) AND p.not_before<=statement_timestamp() AND (p.expires_at IS NULL OR p.expires_at>statement_timestamp()) ORDER BY p.id FOR UPDATE OF o;", r, ("ids", selected));
            foreach (var row in rows) Mark(r with { Id = GuidValue(row, "id"), ExpectedVersion = Version(row) }, row, false);
            return new(new JsonObject { ["updatedCount"] = rows.Count, ["latestVersion"] = rows.Count == 0 ? 1 : rows.Max(row => Version(row)) + 1 });
        }
        var current = RequireObject(c, t, r, Resources["notifications"]);
        if (r.ExpectedVersion is { } expected && Version(current) != expected) throw Error(412, "VERSION_CONFLICT", "Notification version has changed.");
        ValidateFields(r.Body, r.Route.Operation == "action" ? "action expectedVersion" : "");
        var action = r.Route.Operation == "action" ? Text(r.Body, "action") : "mark_read";
        if (action is not ("mark_read" or "dismiss")) throw Invalid("Unsupported notification action.");
        if (r.Route.Operation == "action" && r.Body["expectedVersion"]?.GetValue<int>() != r.ExpectedVersion) throw Invalid("expectedVersion must match If-Match.");
        return Mark(r, current, action == "dismiss");

        ProductApiResponse Mark(ProductApiRequest request, JsonObject row, bool dismiss)
        {
            if (Text(row, "lifecycleState") != "active" || Text(row, "status") is "failed" or "expired")
                throw Error(409, "INVALID_STATE_TRANSITION", "Notification is not actionable.");
            if (Text(row, "status") == (dismiss ? "dismissed" : "read")) return new(row, Version: Version(row));
            if (Text(row, "status") == "dismissed") throw Error(409, "INVALID_STATE_TRANSITION", "Dismissed notifications cannot be read.");
            Run(c, t, "UPDATE notify.notifications SET status=@status,delivered_at=COALESCE(delivered_at,statement_timestamp())," +
                "read_at=COALESCE(read_at,statement_timestamp()),dismissed_at=CASE WHEN @dismiss THEN statement_timestamp() ELSE dismissed_at END " +
                "WHERE organization_id=@org AND id=@id AND recipient_user_id=@user;", request,
                ("status", dismiss ? "dismissed" : "read"), ("dismiss", dismiss), ("id", request.Id));
            var updated = Bump(c, t, request, Resources["notifications"], row);
            return new(updated, Version: Version(updated));
        }
    }
}
