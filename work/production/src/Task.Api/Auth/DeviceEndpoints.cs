using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Task.Api.Security;
using Task.Application.Security;

namespace Task.Api.Auth;

internal static class DeviceEndpoints
{
    public static IEndpointRouteBuilder MapDeviceEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/devices", GetDevicesAsync).RequireAuthorization(TaskPermissionAuthorization.DeviceReadPolicyName);
        app.MapGet("/api/v1/devices/{id}", GetDeviceAsync).RequireAuthorization(TaskPermissionAuthorization.DeviceReadPolicyName);
        app.MapPatch("/api/v1/devices/{id}", PatchDeviceAsync).RequireAuthorization(TaskPermissionAuthorization.DeviceUpdatePolicyName);
        app.MapPost("/api/v1/devices/{id}/heartbeat", HeartbeatAsync).RequireAuthorization();
        app.MapPost("/api/v1/devices/{id}/revoke", RevokeAsync).RequireAuthorization(TaskPermissionAuthorization.DeviceRevokePolicyName);
        return app;
    }

    private static async Task<IResult> GetDevicesAsync(HttpContext context,string? filter,string? sort,int? page,string? cursor,CancellationToken ct)
    {
        var auth=ReadContext(context);var store=context.RequestServices.GetService<IDeviceRegistrationStore>();
        if(auth is null||store is null)return await Problem(context,auth is null?500:503,"INTERNAL_ERROR","Device access is not configured.",true);
        if((filter?.Length??0)>2000||(sort?.Length??0)>500||page is <1 or >100000||!TryCursor(cursor,out var parsed)||(!string.IsNullOrWhiteSpace(sort)&&sort is not("id" or "+id")))return await Problem(context,400,"VALIDATION_FAILED","The device query is invalid.",false);
        var result=await store.GetPageAsync(auth.OrganizationId,auth.UserAccountId,await IdentityScope.CanManageAllAsync(context,auth),filter,page??1,parsed,ct);
        return Results.Json(new DevicePageResponse(result.Items.Select(ToResponse).ToArray(),result.NextCursor is null?null:EncodeCursor(result.NextCursor.Value),result.Total));
    }

    private static async Task<IResult> GetDeviceAsync(HttpContext context,string id,CancellationToken ct)
    {
        var auth=ReadContext(context);var store=context.RequestServices.GetService<IDeviceRegistrationStore>();
        if(auth is null||store is null)return await Problem(context,auth is null?500:503,"INTERNAL_ERROR","Device access is not configured.",true);
        if(!TryId(id,out var deviceId))return await NotVisible(context);
        var device=await store.GetReadModelAsync(auth.OrganizationId,deviceId,ct);
        return device is null || (device.UserId != auth.UserAccountId && !await IdentityScope.CanManageAllAsync(context,auth)) ? await NotVisible(context) : DeviceResult(context,device,false);
    }

    private static async Task<IResult> PatchDeviceAsync(HttpContext context,string id,CancellationToken ct)
    {
        if(!TryId(id,out var deviceId))return await NotVisible(context);
        if(!TryIfMatch(context,out var version,out var error))return await error!;
        var bodyRead=await ReadBodyAsync(context,ct);if(bodyRead.Malformed)return await Problem(context,400,"MALFORMED_JSON","The request body is not valid JSON.",false);var body=bodyRead.Body;
        using(body)
        {
            if(!TryPatch(body!.RootElement,out var patch))return await Problem(context,422,"VALIDATION_FAILED","The device patch is invalid.",false);
            var command=BuildContext(context,"device.update",$"device.update:{deviceId:D}:v{version}",body.RootElement);
            var store=context.RequestServices.GetService<IDeviceRegistrationStore>();if(command is null||store is null)return await Problem(context,503,"INTERNAL_ERROR","Device commands are not configured.",true);
            command = command with { CanManageAllDevices = await IdentityScope.CanManageAllAsync(context, ReadContext(context)!) };
            return await DeviceCommandResult(context,await store.PatchAsync(command,deviceId,version,patch,ct));
        }
    }

    private static async Task<IResult> HeartbeatAsync(HttpContext context,string id,CancellationToken ct)
    {
        if(!TryId(id,out var deviceId))return await NotVisible(context);var bodyRead=await ReadBodyAsync(context,ct);if(bodyRead.Malformed)return await Problem(context,400,"MALFORMED_JSON","The request body is not valid JSON.",false);var body=bodyRead.Body;
        using(body)
        {
            var root=body!.RootElement;if(root.ValueKind!=JsonValueKind.Object||root.EnumerateObject().Any(p=>p.Name is not("appVersion" or "osVersion" or "observedAt"))||!RequiredText(root,"appVersion",32,out var appVersion)||!root.TryGetProperty("observedAt",out var observed)||observed.ValueKind!=JsonValueKind.String||!DateTimeOffset.TryParse(observed.GetString(),CultureInfo.InvariantCulture,DateTimeStyles.AssumeUniversal,out var at)||at>DateTimeOffset.UtcNow.AddMinutes(5)||!OptionalNullableText(root,"osVersion",100,out var osVersion))return await Problem(context,422,"VALIDATION_FAILED","The device heartbeat is invalid.",false);
            var auth=ReadContext(context);var store=context.RequestServices.GetService<IDeviceRegistrationStore>();if(auth is null||store is null)return await Problem(context,503,"INTERNAL_ERROR","Device commands are not configured.",true);
            return await store.HeartbeatAsync(auth.OrganizationId,auth.UserAccountId,deviceId,appVersion,osVersion,at.ToUniversalTime(),ct)?Results.NoContent():await NotVisible(context);
        }
    }

    private static async Task<IResult> RevokeAsync(HttpContext context,string id,CancellationToken ct)
    {
        if(!TryId(id,out var deviceId))return await NotVisible(context);if(!TryIfMatch(context,out var version,out var error))return await error!;
        var key=context.Request.Headers["Idempotency-Key"].ToString();if(!ValidKey(key))return await Problem(context,400,"VALIDATION_FAILED","A printable 8-200 character Idempotency-Key is required.",false);
        var bodyRead=await ReadBodyAsync(context,ct);if(bodyRead.Malformed)return await Problem(context,400,"MALFORMED_JSON","The request body is not valid JSON.",false);var body=bodyRead.Body;
        using(body)
        {
            var root=body!.RootElement;if(root.ValueKind!=JsonValueKind.Object||root.EnumerateObject().Any(p=>p.Name is not("reason" or "expectedVersion"))||!RequiredText(root,"reason",2000,out var reason)||!root.TryGetProperty("expectedVersion",out var expected)||expected.ValueKind!=JsonValueKind.Number||!expected.TryGetInt64(out var bodyVersion)||bodyVersion!=version)return await Problem(context,422,"VALIDATION_FAILED","expectedVersion must match If-Match and reason is required.",false);
            var command=BuildContext(context,"device.revoke",key,root);var store=context.RequestServices.GetService<IDeviceRegistrationStore>();if(command is null||store is null)return await Problem(context,503,"INTERNAL_ERROR","Device commands are not configured.",true);
            command = command with { CanManageAllDevices = await IdentityScope.CanManageAllAsync(context, ReadContext(context)!) };
            return await DeviceCommandResult(context,await store.RevokeAsync(command,deviceId,version,reason,ct));
        }
    }

    private static async Task<IResult> DeviceCommandResult(HttpContext context,DeviceCommandResult result)
    {
        if(result.Disposition is IdentityCommandDisposition.Executed or IdentityCommandDisposition.Replayed)return DeviceResult(context,result.Device!,result.Disposition==IdentityCommandDisposition.Replayed);
        return result.Disposition switch{IdentityCommandDisposition.NotFound=>await NotVisible(context),IdentityCommandDisposition.VersionConflict=>await Problem(context,412,"VERSION_CONFLICT","The device version does not match If-Match.",false),IdentityCommandDisposition.IdempotencyKeyReused=>await Problem(context,409,"IDEMPOTENCY_KEY_REUSED","The idempotency key was already used for another request.",false),IdentityCommandDisposition.RequestInProgress=>await Problem(context,409,"REQUEST_IN_PROGRESS","The request is already being processed.",true,result.RetryAfterSeconds),_=>await Problem(context,503,"INTERNAL_ERROR","The device command failed safely.",true)};
    }

    private static IResult DeviceResult(HttpContext context,DeviceReadProjection device,bool replay){context.Response.Headers.ETag=$"\"v{device.Version}\"";if(replay)context.Response.Headers["Idempotency-Replayed"]="true";return Results.Json(ToResponse(device));}
    internal static DeviceResponse ToResponse(DeviceReadProjection d)=>new(d.Id,d.OrganizationId,d.Version,d.CreatedAtUtc.UtcDateTime,d.UpdatedAtUtc.UtcDateTime,d.DeviceName,d.Platform,d.AppVersion,d.RevokedAtUtc is null?"active":"revoked",d.LastSeenAtUtc?.UtcDateTime);
    private static IdentityCommandContext? BuildContext(HttpContext context,string operation,string key,JsonElement root){var auth=ReadContext(context);if(auth is null)return null;var correlation=Guid.TryParse(TaskApiProblemResponse.GetCorrelationId(context),out var parsed)?parsed:Guid.NewGuid();return new(auth.OrganizationId,auth.UserAccountId,auth.SessionId,correlation,operation,key,IdentityRequestHash.Compute(context,root));}
    private static async global::System.Threading.Tasks.Task<(JsonDocument? Body,bool Malformed)> ReadBodyAsync(HttpContext context,CancellationToken ct){try{return(await JsonDocument.ParseAsync(context.Request.Body,cancellationToken:ct),false);}catch(JsonException){return(null,true);}}
    private static bool TryPatch(JsonElement root,out DevicePatchCommand patch){patch=default!;if(root.ValueKind!=JsonValueKind.Object||!root.EnumerateObject().Any()||root.EnumerateObject().Any(p=>p.Name is not("deviceName" or "platform" or "appVersion")))return false;var ns=root.TryGetProperty("deviceName",out var n);var ps=root.TryGetProperty("platform",out var p);var avs=root.TryGetProperty("appVersion",out var av);var name=ns&&n.ValueKind==JsonValueKind.String?n.GetString():null;var platform=ps&&p.ValueKind==JsonValueKind.String?p.GetString():null;var app=avs&&av.ValueKind==JsonValueKind.String?av.GetString():null;if(ns&&!ValidText(name,1,200)||ps&&platform is not("windows" or "linux" or "macos")||avs&&!ValidText(app,1,32))return false;patch=new(name,platform,app,ns,ps,avs);return true;}
    private static bool TryIfMatch(HttpContext context,out long version,out Task<IResult>? error){version=0;error=null;var value=context.Request.Headers.IfMatch.ToString();if(string.IsNullOrWhiteSpace(value)){error=Problem(context,428,"PRECONDITION_REQUIRED","If-Match is required.",false);return false;}if(value.Length<4||value[0]!='\"'||value[^1]!='\"'||value[1]!='v'||!long.TryParse(value.AsSpan(2,value.Length-3),NumberStyles.None,CultureInfo.InvariantCulture,out version)||version<1){error=Problem(context,400,"VALIDATION_FAILED","If-Match must be a strong device ETag.",false);return false;}return true;}
    private static bool RequiredText(JsonElement root,string name,int max,out string value){value="";return root.TryGetProperty(name,out var p)&&p.ValueKind==JsonValueKind.String&&ValidText(value=p.GetString()!,1,max);}
    private static bool OptionalNullableText(JsonElement root,string name,int max,out string? value){value=null;if(!root.TryGetProperty(name,out var p)||p.ValueKind==JsonValueKind.Null)return true;if(p.ValueKind!=JsonValueKind.String||!ValidText(p.GetString(),1,max))return false;value=p.GetString();return true;}
    private static bool ValidText(string? value,int min,int max)=>!string.IsNullOrWhiteSpace(value)&&value.Trim().Length>=min&&value.Length<=max;
    private static bool ValidKey(string value)=>value.Length is >=8 and <=200&&value.All(c=>c is >= '!' and <= '~');
    private static bool TryId(string value,out Guid id)=>Guid.TryParseExact(value,"D",out id)&&id!=Guid.Empty;
    private static bool TryCursor(string? value,out Guid? cursor){cursor=null;if(string.IsNullOrWhiteSpace(value))return true;try{var text=System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(value.Replace('-','+').Replace('_','/')+new string('=',(4-value.Length%4)%4)));if(Guid.TryParseExact(text,"D",out var id)){cursor=id;return true;}}catch(FormatException){}return false;}
    private static string EncodeCursor(Guid id)=>Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(id.ToString("D"))).TrimEnd('=').Replace('+','-').Replace('/','_');
    private static AuthenticatedRequestContext? ReadContext(HttpContext context)=>context.Items.TryGetValue(TaskJwtAuthenticationHandler.AuthenticatedRequestContextItemName,out var value)&&value is AuthenticatedRequestContext auth?auth:null;
    private static Task<IResult> NotVisible(HttpContext context)=>Problem(context,404,"OBJECT_NOT_VISIBLE","The requested object is absent or not visible.",false);
    private static async Task<IResult> Problem(HttpContext context,int status,string code,string title,bool retryable,int? retryAfter=null){await TaskApiProblemResponse.WriteAsync(context,status,code,title,retryable,retryAfter);return Results.Empty;}

    private sealed record DevicePageResponse([property:JsonPropertyName("items")]IReadOnlyList<DeviceResponse> Items,[property:JsonPropertyName("nextCursor")]string? NextCursor,[property:JsonPropertyName("total")]long Total);
    internal sealed record DeviceResponse([property:JsonPropertyName("id")]Guid Id,[property:JsonPropertyName("organizationId")]Guid OrganizationId,[property:JsonPropertyName("version")]long Version,[property:JsonPropertyName("createdAt")]DateTime CreatedAt,[property:JsonPropertyName("updatedAt")]DateTime UpdatedAt,[property:JsonPropertyName("deviceName")]string DeviceName,[property:JsonPropertyName("platform")]string Platform,[property:JsonPropertyName("appVersion")]string AppVersion,[property:JsonPropertyName("status")]string Status,[property:JsonPropertyName("lastSeenAt")]DateTime? LastSeenAt);
}
