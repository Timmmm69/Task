using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Npgsql;
using NpgsqlTypes;
using Task.Application.ProductData;
using Task.Domain;

namespace Task.Infrastructure.Persistence;

internal sealed partial class PostgresProductApiStore(NpgsqlDataSource dataSource) : IProductApiStore
{
    private sealed record Resource(string Table, string Type, string ReadPermission, string Fields);
    private static readonly Dictionary<string, Resource> Resources = new()
    {
        ["projects"] = new("projects.projects", "project", "Project.Read",
            "name description ownerUserId managerUserId status startDate plannedEndDate actualEndAt defaultTimeZone colorCode"),
        ["contacts"] = new("crm.contacts", "contact", "Contact.Read", "firstName lastName middleName displayName notes status"),
        ["companies"] = new("crm.companies", "company", "Contact.Read", "name legalName industry website taxIdentifier notes status"),
        ["catalog-items"] = new("files.catalog_items", "catalog_item", "FileCatalog.Read",
            "parentItemId itemType name description noteContent webUrl mimeType fileExtension observedSizeBytes observedModifiedAt sortOrder"),
        ["network-resources"] = new("files.network_resources", "network_resource", "FileCatalog.Read", "name rootUncPath description status"),
        ["notifications"] = new("notify.notifications", "notification", "Notification.ReadOwn", ""),
        ["tasks"] = new("work.tasks", "task", "Task.Read", ""),
        ["calendar-events"] = new("calendar.events", "calendar_event", "Calendar.Read", ""),
        ["interactions"] = new("crm.interactions", "interaction", "Contact.Read", "counterpartyObjectId interactionType occurredAt subject details nextStep nextStepDueAt participantObjectIds"),
        ["employees"] = new("org.employee_profiles", "employee_profile", "Employee.Read", ""),
    };

    public ProductApiResponse Execute(ProductApiRequest request)
    {
        using var connection = dataSource.OpenConnection();
        using var transaction = connection.BeginTransaction();
        try
        {
            Run(connection, transaction, "SET LOCAL lock_timeout='3s'; SET LOCAL statement_timeout='15s';", request);
            // Serialize related commands; reads do not hold a tenant-wide lock.
            var write = request.Route.Method != "GET";
            if (write) Run(connection, transaction, "SELECT pg_advisory_xact_lock(hashtextextended('task-product-api:' || @org::text,0));", request);
            if (write && request.Route.Resource == "catalog-items")
                Run(connection, transaction, "SELECT pg_advisory_xact_lock(hashtextextended('task-catalog:' || @org::text,0));", request);
            var operation = request.Route.Method + " " + request.Route.Path + ":" + request.Id + ":" + request.ChildId;
            if (request.IdempotencyKey is not null)
            {
                var replay = One(connection, transaction,
                    "SELECT jsonb_build_object('hash',request_hash,'result',response) FROM iam.product_api_commands " +
                    "WHERE organization_id=@org AND user_account_id=@user AND operation=@operation AND idempotency_key=@key;",
                    request, ("operation", operation), ("key", request.IdempotencyKey));
                if (replay is not null)
                {
                    if (Text(replay, "hash") != request.RequestHash)
                        throw Error(409, "IDEMPOTENCY_KEY_REUSED", "Idempotency key belongs to a different request.");
                    // Do not replay data to a caller whose object membership has since been revoked.
                    if (request.Id is not null && Resources.TryGetValue(request.Route.Resource, out var replayResource))
                        RequireObject(connection, transaction, request, replayResource);
                    if (request.Id is not null && request.Route.Resource == "objects")
                        CheckLinkWrite(connection, transaction, request, RequireAny(connection, transaction, request, request.Id.Value));
                    var result = replay["result"]!.AsObject();
                    if (request.Id is null && request.Route.Operation == "create" && result["body"]?["id"] is { } replayId)
                        RequireObject(connection, transaction, request with { Id = Guid.Parse(replayId.ToString()) }, Resources[request.Route.Resource]);
                    return RedactResponse(request, new(result["body"]?.DeepClone(), result["status"]!.GetValue<int>(), result["version"]?.GetValue<int>()));
                }
            }
            var response = Dispatch(connection, transaction, request);
            if (request.IdempotencyKey is not null)
            {
                var result = new JsonObject { ["body"] = response.Body?.DeepClone(), ["status"] = response.Status, ["version"] = response.Version };
                Run(connection, transaction,
                    "INSERT INTO iam.product_api_commands(organization_id,user_account_id,operation,idempotency_key,request_hash,response) " +
                    "VALUES(@org,@user,@operation,@key,@hash,@response::jsonb);", request,
                    ("operation", operation), ("key", request.IdempotencyKey), ("hash", request.RequestHash), ("response", result.ToJsonString()));
            }
            transaction.Commit();
            return RedactResponse(request, response);
        }
        catch (PostgresException exception) when (exception.SqlState is "23503" or "23514" or "23502" or "22P02" or "22007" or "22008" or "22003" or "22001")
        { throw Error(422, "VALIDATION_FAILED", "A field or related object is invalid."); }
        catch (PostgresException exception) when (exception.SqlState == "23505")
        { throw Error(409, "CONFLICT", "The record already exists."); }
    }

    private ProductApiResponse Dispatch(NpgsqlConnection c, NpgsqlTransaction t, ProductApiRequest r)
    {
        if (r.Route.Resource is "user-settings" or "organization-settings" or "preferences") return Settings(c, t, r);
        if (r.Route.Resource == "search") return Search(c, t, r);
        if (r.Route.Resource == "objects") return Links(c, t, r);
        if (r.Route.Resource == "file-locations") return FileCheck(c, t, r);
        if (r.Route.Resource is "archive" or "trash") return Discovery(c, t, r);
        if (r.Route.Resource == "notifications" && r.Route.Operation is not ("list" or "get")) return Notifications(c, t, r);
        var resource = Resources[r.Route.Resource];
        if (r.Route.Operation is "list" or "tree") return List(c, t, r, resource);
        if (r.Route.Operation == "create") return Create(c, t, r, resource);
        var current = RequireObject(c, t, r, resource);
        if (resource.Type == "project" && r.Route.Operation is not ("get" or "history")) CheckProjectWrite(c, t, r, current);
        if (r.Route.Operation == "get")
        {
            Enrich(c, t, r, current);
            return new(current, Version: Version(current));
        }
        if (r.Route.Operation != "member-overrides" && r.ExpectedVersion is { } expected && expected != Version(current))
            throw Error(412, "VERSION_CONFLICT", "Object version has changed.");
        if (r.Route.Operation is "patch" or "archive" or "unarchive" or "trash" or "restore" or "move")
            return Change(c, t, r, resource, current);
        return Related(c, t, r, resource, current);
    }

    private ProductApiResponse Create(NpgsqlConnection c, NpgsqlTransaction t, ProductApiRequest r, Resource resource)
    {
        ValidateFields(r.Body, resource.Fields);
        var id = Guid.NewGuid();
        var body = (JsonObject)r.Body.DeepClone();
        if (r.Route.Resource == "projects")
        {
            if (!body.ContainsKey("ownerUserId")) throw Invalid("ownerUserId is required.");
            if (GuidValue(body, "ownerUserId") != r.UserId && !IsAdmin(r)) throw Error(403, "FORBIDDEN", "Only an administrator can create a project for another owner.");
        }
        ValidateContent(c, t, r, resource, body, null);
        Run(c, t, "INSERT INTO core.objects(id,organization_id,object_type,created_by,updated_by,created_at,updated_at) " +
            "VALUES(@id,@org,@type,@user,@user,statement_timestamp(),statement_timestamp());", r, ("id", id), ("type", resource.Type));
        body["id"] = id; body["organizationId"] = r.OrganizationId;
        if (resource.Type == "catalog_item") body["createdBy"] = r.UserId;
        Insert(c, t, r, resource.Table, body);
        var created = RequireObject(c, t, r with { Id = id }, resource);
        Record(c, t, r, resource.Type, id, 1, null, created);
        return new(created, 201, 1);
    }

    private ProductApiResponse Change(NpgsqlConnection c, NpgsqlTransaction t, ProductApiRequest r, Resource resource, JsonObject current)
    {
        var state = Text(current, "lifecycleState");
        var body = (JsonObject)r.Body.DeepClone();
        switch (r.Route.Operation)
        {
            case "patch":
                ValidateFields(body, resource.Fields);
                if (body.Count == 0) throw Invalid("At least one writable field is required.");
                if (body.ContainsKey("ownerUserId") && body["ownerUserId"]?.ToString() != current["ownerUserId"]?.ToString())
                    throw Invalid("Use transfer-ownership to change the project owner.");
                break;
            case "move": ValidateFields(body, "parentItemId sortOrder"); break;
            default:
                ValidateFields(body, "reason expectedVersion");
                if (body["expectedVersion"] is not null && body["expectedVersion"]!.GetValue<int>() != r.ExpectedVersion)
                    throw Invalid("expectedVersion must match If-Match.");
                if (body["reason"] is not null && Text(body, "reason")!.Length > 2000) throw Invalid("Reason is too long.");
                body.Clear();
                break;
        }
        if (r.Route.Operation is "patch" or "move")
        {
            if (state != "active") throw Error(409, "INVALID_STATE_TRANSITION", "Restore the object before editing.");
            var merged = (JsonObject)current.DeepClone();
            foreach (var pair in body) merged[pair.Key] = pair.Value?.DeepClone();
            ValidateContent(c, t, r, resource, merged, current);
            Update(c, t, r, resource.Table, body, r.Id!.Value);
        }
        else
        {
            var target = r.Route.Operation switch
            {
                "archive" when state == "active" => "archived",
                "unarchive" when state == "archived" => "active",
                "trash" when state != "trashed" => "trashed",
                "restore" when state == "trashed" => Text(current, "lifecycleStateBeforeTrash")!,
                _ => throw Error(409, "INVALID_STATE_TRANSITION", "Lifecycle transition is not permitted."),
            };
            if (resource.Type == "project" && target == "archived" && r.Route.Operation == "archive" && Text(current, "status") != "completed")
                throw Error(409, "INVALID_STATE_TRANSITION", "Complete the project before archiving.");
            if (resource.Type == "catalog_item" && r.Route.Operation == "restore") ValidateContent(c, t, r, resource, current, current);
            Run(c, t, "UPDATE core.objects SET lifecycle_state=@state, " +
                "lifecycle_state_before_trash=CASE WHEN @state='trashed' THEN lifecycle_state ELSE NULL END," +
                "archived_at=CASE WHEN @state='archived' AND archived_at IS NULL THEN statement_timestamp() WHEN @state='active' THEN NULL ELSE archived_at END," +
                "deleted_at=CASE WHEN @state='trashed' THEN statement_timestamp() ELSE NULL END," +
                "deleted_by=CASE WHEN @state='trashed' THEN @user ELSE NULL END," +
                "updated_by=@user,updated_at=statement_timestamp() WHERE organization_id=@org AND id=@id;", r, ("id", r.Id), ("state", target));
        }
        var updated = Bump(c, t, r, resource, current);
        if (r.Route.Operation == "trash")
            return new(new JsonObject { ["objectId"] = r.Id, ["objectType"] = resource.Type, ["version"] = Version(updated), ["lifecycleState"] = "trashed" }, 202, Version(updated));
        return new(updated, Version: Version(updated));
    }

    private JsonObject Bump(NpgsqlConnection c, NpgsqlTransaction t, ProductApiRequest r, Resource resource, JsonObject old)
    {
        var affected = Run(c, t, "UPDATE core.objects SET version=version+1,updated_by=@user,updated_at=statement_timestamp() " +
            "WHERE organization_id=@org AND id=@id AND version=@version;", r, ("id", r.Id), ("version", Version(old)));
        if (affected != 1) throw Error(412, "VERSION_CONFLICT", "Object version has changed.");
        var updated = RequireObject(c, t, r, resource);
        Record(c, t, r, resource.Type, r.Id!.Value, Version(updated), old, updated);
        return updated;
    }

    private JsonObject RequireObject(NpgsqlConnection c, NpgsqlTransaction t, ProductApiRequest r, Resource resource)
    {
        var current = One(c, t, $"SELECT to_jsonb(p)||to_jsonb(o) FROM {resource.Table} p JOIN core.objects o " +
            "ON o.id=p.id AND o.organization_id=p.organization_id WHERE o.organization_id=@org AND o.id=@id " +
            $"AND {Visibility(resource)}" + (r.Route.Method == "GET" ? ";" : " FOR UPDATE OF o;"), r, ("id", r.Id));
        return current ?? throw Error(404, "OBJECT_NOT_VISIBLE", "Object is not visible.");
    }

    private static string Visibility(Resource resource) => resource.Type switch
    {
        "project" => "(@admin OR p.owner_user_id=@user OR p.manager_user_id=@user OR EXISTS " +
            "(SELECT 1 FROM projects.members m WHERE m.organization_id=@org AND m.project_id=p.id AND m.user_account_id=@user AND m.status='active'))",
        "notification" => "p.recipient_user_id=@user",
        // Existing task/calendar personal rows and project membership are enforced for discovery too.
        "task" or "calendar_event" => "(to_jsonb(p)->>'project_id' IS NULL OR @admin OR EXISTS (SELECT 1 FROM projects.projects pr " +
            "WHERE pr.organization_id=@org AND pr.id::text=to_jsonb(p)->>'project_id' AND (pr.owner_user_id=@user OR pr.manager_user_id=@user OR EXISTS " +
            "(SELECT 1 FROM projects.members m WHERE m.organization_id=@org AND m.project_id=pr.id AND m.user_account_id=@user AND m.status='active'))))",
        _ => "TRUE",
    };

    private void ValidateContent(NpgsqlConnection c, NpgsqlTransaction t, ProductApiRequest r, Resource resource, JsonObject body, JsonObject? previous)
    {
        foreach (var (name, value) in body)
        {
            if (value is JsonValue v && v.TryGetValue<string>(out var text) && text.Contains('\0')) throw Invalid("NUL characters are not allowed.");
            if (name is "website" or "webUrl" && value is not null &&
                (!Uri.TryCreate(value.ToString(), UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") || uri.UserInfo.Length > 0))
                throw Invalid("Only HTTP(S) links without credentials are allowed.");
            if (name.EndsWith("TimeZone", StringComparison.Ordinal) && value is not null)
            {
                try { _ = TimeZoneInfo.FindSystemTimeZoneById(value.ToString()); }
                catch (TimeZoneNotFoundException) { throw Invalid("Unknown time zone."); }
            }
        }
        if (resource.Type == "project")
        {
            foreach (var name in new[] { "ownerUserId", "managerUserId" })
                if (body[name] is not null) RequireActiveUser(c, t, r, GuidValue(body, name));
        }
        if (resource.Type == "catalog_item")
        {
            if (previous is not null && Text(body, "itemType") != Text(previous, "itemType")) throw Invalid("Catalog item type is immutable.");
            var parent = body["parentItemId"] is null ? (Guid?)null : GuidValue(body, "parentItemId");
            if (parent is not null)
            {
                if (parent == r.Id) throw Invalid("An item cannot contain itself.");
                var folder = RequireObject(c, t, r with { Id = parent }, resource);
                if (Text(folder, "itemType") != "virtual_folder" || Text(folder, "lifecycleState") != "active")
                    throw Invalid("The parent must be an active virtual folder.");
                if (r.Id is not null && One(c, t,
                    "WITH RECURSIVE ancestors AS (SELECT id,parent_item_id FROM files.catalog_items WHERE organization_id=@org AND id=@parent " +
                    "UNION SELECT p.id,p.parent_item_id FROM files.catalog_items p JOIN ancestors a ON p.id=a.parent_item_id WHERE p.organization_id=@org) " +
                    "SELECT jsonb_build_object('cycle',true) FROM ancestors WHERE id=@id;", r, ("id", r.Id), ("parent", parent)) is not null)
                    throw Error(409, "CATALOG_CYCLE", "Catalog move would create a cycle.");
            }
        }
        if (resource.Type == "network_resource") ValidateUnc(Text(body, "rootUncPath")!, []);
        if (resource.Type == "interaction")
        {
            var target = RequireAny(c, t, r, GuidValue(body, "counterpartyObjectId"));
            if (Text(target.Object, "objectType") is not ("contact" or "company") || Text(target.Object, "lifecycleState") != "active") throw Invalid("Counterparty must be an active contact or company.");
            if (body["participantObjectIds"] is JsonArray participants)
            {
                if (participants.Count > 500 || participants.Select(p => p!.ToString()).Distinct().Count() != participants.Count) throw Invalid("Invalid interaction participants.");
                foreach (var participant in participants) RequireAny(c, t, r, Guid.Parse(participant!.ToString()));
            }
        }
    }

    private static void RequireActiveUser(NpgsqlConnection c, NpgsqlTransaction t, ProductApiRequest r, Guid user)
    {
        if (One(c, t, "SELECT jsonb_build_object('id',id) FROM iam.user_accounts WHERE organization_id=@org AND id=@target AND account_status='active' FOR SHARE;",
            r, ("target", user)) is null) throw Invalid("Related user is not active in this organization.");
    }

    private static void Insert(NpgsqlConnection c, NpgsqlTransaction t, ProductApiRequest r, string table, JsonObject body)
    {
        var columns = string.Join(',', body.Select(p => Snake(p.Key)));
        Run(c, t, $"INSERT INTO {table} ({columns}) SELECT {columns} FROM jsonb_populate_record(NULL::{table},@payload::jsonb);",
            r, ("payload", ToDatabase(body).ToJsonString()));
    }

    private static void Update(NpgsqlConnection c, NpgsqlTransaction t, ProductApiRequest r, string table, JsonObject body, Guid id)
    {
        if (body.Count == 0) return;
        var columns = body.Select(p => Snake(p.Key)).ToArray();
        Run(c, t, $"UPDATE {table} SET ({string.Join(',', columns)})=(SELECT {string.Join(',', columns)} " +
            $"FROM jsonb_populate_record(NULL::{table},@payload::jsonb)) WHERE organization_id=@org AND id=@id;", r,
            ("payload", ToDatabase(body).ToJsonString()), ("id", id));
    }

    private static void Record(NpgsqlConnection c, NpgsqlTransaction t, ProductApiRequest r, string type, Guid id, int version, JsonObject? old, JsonObject current)
    {
        var eventId = Guid.NewGuid();
        // Paths and contact values never enter public audit/outbox payloads.
        var state = new JsonObject { ["id"] = id, ["version"] = version, ["lifecycleState"] = current["lifecycleState"]?.DeepClone() };
        var changed = current.Where(pair => old is null || !JsonNode.DeepEquals(pair.Value, old[pair.Key])).Select(pair => pair.Key).ToArray();
        Run(c, t, "INSERT INTO governance.domain_events(id,organization_id,aggregate_id,aggregate_type,aggregate_version,event_type,actor_user_id,correlation_id,operation_id,idempotency_key,changed_fields,payload) " +
            "VALUES(@event,@org,@id,@type,@version,@action,@user,@correlation,@operation,@key,@fields,@payload::jsonb);", r,
            ("event", eventId), ("id", id), ("type", type), ("version", version), ("action", type + "." + r.Route.Operation),
            ("operation", r.Route.Method + ":" + r.Route.Resource + ":" + r.Route.Operation + ":" + id),
            ("key", r.IdempotencyKey ?? eventId.ToString("N")), ("fields", changed), ("payload", state.ToJsonString()));
        Run(c, t, "INSERT INTO governance.outbox_messages(id,organization_id,domain_event_id,destination,message_type,payload) " +
            "VALUES(@event,@org,@event,'sync',@type,@payload::jsonb);", r, ("event", eventId), ("type", type + "." + r.Route.Operation), ("payload", state.ToJsonString()));
        Run(c, t, "INSERT INTO governance.audit_entries(id,organization_id,actor_user_id,action_code,object_id,object_type,outcome,correlation_id,request_id,metadata,old_state,new_state) " +
            "VALUES(@event,@org,@user,@action,@id,@type,'success',@correlation,@event,@payload::jsonb,@old::jsonb,@new::jsonb);", r,
            ("event", eventId), ("action", type + "." + r.Route.Operation), ("id", id), ("type", type), ("payload", state.ToJsonString()),
            ("old", old is null ? "null" : new JsonObject { ["version"] = Version(old) }.ToJsonString()), ("new", state.ToJsonString()));
    }

    private static NpgsqlCommand Command(NpgsqlConnection c, NpgsqlTransaction t, string sql, ProductApiRequest r, params (string, object?)[] args)
    {
        var command = new NpgsqlCommand(sql, c, t);
        command.Parameters.AddWithValue("org", r.OrganizationId);
        command.Parameters.AddWithValue("user", r.UserId);
        command.Parameters.AddWithValue("admin", IsAdmin(r));
        command.Parameters.AddWithValue("correlation", r.CorrelationId);
        foreach (var (name, value) in args)
            if (value is null) command.Parameters.AddWithValue(name, NpgsqlDbType.Text, DBNull.Value);
            else command.Parameters.AddWithValue(name, value);
        return command;
    }
    private static int Run(NpgsqlConnection c, NpgsqlTransaction t, string sql, ProductApiRequest r, params (string, object?)[] args)
    { using var command = Command(c, t, sql, r, args); using var cancel = r.CancellationToken.Register(command.Cancel); r.CancellationToken.ThrowIfCancellationRequested(); return command.ExecuteNonQuery(); }
    private static JsonObject? One(NpgsqlConnection c, NpgsqlTransaction t, string sql, ProductApiRequest r, params (string, object?)[] args)
    { using var command = Command(c, t, sql, r, args); using var cancel = r.CancellationToken.Register(command.Cancel); r.CancellationToken.ThrowIfCancellationRequested(); var result = command.ExecuteScalar(); return result is string json ? FromDatabase(JsonNode.Parse(json)!).AsObject() : null; }
    private static List<JsonObject> Many(NpgsqlConnection c, NpgsqlTransaction t, string sql, ProductApiRequest r, params (string, object?)[] args)
    {
        using var command = Command(c, t, sql, r, args); using var cancel = r.CancellationToken.Register(command.Cancel); r.CancellationToken.ThrowIfCancellationRequested(); using var reader = command.ExecuteReader();
        var result = new List<JsonObject>();
        while (reader.Read()) result.Add(FromDatabase(JsonNode.Parse(reader.GetString(0))!).AsObject());
        return result;
    }
    private static JsonNode FromDatabase(JsonNode node) => node switch
    {
        JsonObject obj => new JsonObject(obj.Select(p => KeyValuePair.Create(Camel(p.Key), p.Value is null ? null :
            p.Key is "custom_preferences" or "customPreferences" or "action_payload" or "actionPayload" or "payload" or "permission_overrides" or "permissionOverrides"
                ? p.Value.DeepClone() : FromDatabase(p.Value)))),
        JsonArray arr => new JsonArray(arr.Select(p => p is null ? null : FromDatabase(p)).ToArray()),
        _ => node.DeepClone(),
    };
    private static JsonObject ToDatabase(JsonObject obj) => new(obj.Select(p => KeyValuePair.Create(Snake(p.Key), p.Value?.DeepClone())));
    private static string Snake(string name) => string.Concat(name.Select(c => char.IsUpper(c) ? "_" + char.ToLowerInvariant(c) : c.ToString()));
    private static string Camel(string name) => string.Join("", name.Split('_').Select((p, i) => i == 0 ? p : char.ToUpperInvariant(p[0]) + p[1..]));
    private static int Version(JsonObject obj) => obj["version"]!.GetValue<int>();
    private static string? Text(JsonObject obj, string name) => obj[name]?.GetValue<string>();
    private static Guid GuidValue(JsonObject obj, string name) => Guid.TryParse(obj[name]?.ToString(), out var id) && id != Guid.Empty ? id : throw Invalid("Invalid identifier: " + name);
    private static bool IsAdmin(ProductApiRequest r) => r.Permissions.Contains("organization.manage");
    private static ProductApiException Invalid(string message) => Error(422, "VALIDATION_FAILED", message);
    private static ProductApiException Error(int status, string code, string message) => new(status, code, message);
    private static void ValidateFields(JsonObject body, string fields)
    {
        var allowed = fields.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
        if (body.Any(p => !allowed.Contains(p.Key))) throw Invalid("Unknown or read-only field.");
        var nullable = "description lastName middleName notes legalName industry website taxIdentifier managerUserId startDate plannedEndDate actualEndAt defaultTimeZone colorCode parentItemId noteContent webUrl mimeType fileExtension observedSizeBytes observedModifiedAt joinedAt removedAt label countryCode region city street postalCode jobTitle departmentName validFrom validTo deviceId networkResourceId quietHoursStart quietHoursEnd quietHoursTimeZone reason details nextStep nextStepDueAt latencyMs osErrorCode".Split(' ').ToHashSet();
        var booleans = "isPrimary isEnabled isVerified enabled desktopEnabled soundEnabled autostartEnabled allowLocalPaths confirmCatalogDelete".Split(' ').ToHashSet();
        var numbers = "sortOrder observedSizeBytes priority expectedVersion expectedCatalogItemVersion expectedNewOwnerMembershipVersion trashRetentionDays historyRetentionDays changeFeedRetentionDays recurrenceHorizonDays recurrenceMinInstances firstDayOfWeek maxRequestBytes defaultTaskDurationMinutes defaultReminderOffsetMinutes defaultSnoozeMinutes latencyMs expectedLocationVersion expectedMemberVersion".Split(' ').ToHashSet();
        foreach (var (name, value) in body)
        {
            if (value is null) { if (!nullable.Contains(name)) throw Invalid(name + " cannot be null."); continue; }
            var kind = value.GetValueKind();
            if (name is "items" or "allow" or "deny" or "weekendDays" or "notificationIds" or "participantObjectIds")
            {
                if (value is not JsonArray array) throw Invalid(name + " must be an array.");
                if (name is "notificationIds" or "participantObjectIds" && array.Any(item => item?.GetValueKind() != JsonValueKind.String || !Guid.TryParse(item.ToString(), out var id) || id == Guid.Empty))
                    throw Invalid("Invalid related identifier.");
                if (name is "allow" or "deny" && array.Any(item => item?.GetValueKind() != JsonValueKind.String || item.GetValue<string>().Length is < 1 or > 100)) throw Invalid("Invalid permission code.");
                continue;
            }
            if (name == "customPreferences")
            { if (value is not JsonObject || value.ToJsonString().Length > 50000) throw Invalid("Invalid customPreferences."); continue; }
            if (booleans.Contains(name))
            { if (kind is not (JsonValueKind.True or JsonValueKind.False)) throw Invalid(name + " must be boolean."); continue; }
            if (numbers.Contains(name))
            { if (kind != JsonValueKind.Number || !((JsonValue)value).TryGetValue<long>(out _)) throw Invalid(name + " must be an integer."); continue; }
            if (kind != JsonValueKind.String) throw Invalid(name + " must be a string.");
            if (name.EndsWith("Id", StringComparison.Ordinal)) _ = GuidValue(body, name);
            if (name is "startDate" or "plannedEndDate" or "validFrom" or "validTo" &&
                !DateOnly.TryParseExact(value.ToString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)) throw Invalid("Invalid date.");
            if (name.EndsWith("At", StringComparison.Ordinal) &&
                (!DateTimeOffset.TryParse(value.ToString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out _) ||
                 !System.Text.RegularExpressions.Regex.IsMatch(value.ToString(), @"T.*(Z|[+-]\d{2}:\d{2})$"))) throw Invalid("Timestamp requires an explicit offset.");
        }
    }

    private static ProductApiResponse RedactResponse(ProductApiRequest r, ProductApiResponse response)
    {
        if (r.Route.Resource != "network-resources" || r.Permissions.Contains("FileLocation.ReadSensitivePath")) return response;
        void Redact(JsonNode? node)
        {
            if (node is JsonObject obj) { obj.Remove("rootUncPath"); foreach (var pair in obj) Redact(pair.Value); }
            if (node is JsonArray arr) foreach (var item in arr) Redact(item);
        }
        Redact(response.Body);
        return response;
    }
}
