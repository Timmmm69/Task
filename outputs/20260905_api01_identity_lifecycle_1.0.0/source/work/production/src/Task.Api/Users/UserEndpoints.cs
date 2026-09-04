using System.Globalization;
using System.Net.Mail;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Task.Api.Security;
using Task.Application.Security;

namespace Task.Api.Users;

internal static class UserEndpoints
{
    public static IEndpointRouteBuilder MapUserEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        app.MapGet("/api/v1/users", GetUsersAsync).RequireAuthorization(TaskPermissionAuthorization.UserReadPolicyName);
        app.MapGet("/api/v1/users/{id}", GetUserByIdAsync).RequireAuthorization(TaskPermissionAuthorization.UserReadPolicyName);
        app.MapPost("/api/v1/users", CreateUserAsync).RequireAuthorization(TaskPermissionAuthorization.UserCreatePolicyName);
        app.MapPatch("/api/v1/users/{id}", PatchUserAsync).RequireAuthorization(TaskPermissionAuthorization.UserUpdatePolicyName);
        app.MapPost("/api/v1/users/{id}/activate", (HttpContext c, string id, CancellationToken ct) => TransitionAsync(c,id,UserAccountTransition.Activate,true,true,ct)).RequireAuthorization(TaskPermissionAuthorization.UserCreatePolicyName);
        app.MapPost("/api/v1/users/{id}/block", (HttpContext c, string id, CancellationToken ct) => TransitionAsync(c,id,UserAccountTransition.Block,true,true,ct)).RequireAuthorization(TaskPermissionAuthorization.UserBlockPolicyName);
        app.MapPost("/api/v1/users/{id}/deactivate", (HttpContext c, string id, CancellationToken ct) => TransitionAsync(c,id,UserAccountTransition.Deactivate,true,true,ct)).RequireAuthorization(TaskPermissionAuthorization.UserBlockPolicyName);
        app.MapPost("/api/v1/users/{id}/reactivate", (HttpContext c, string id, CancellationToken ct) => TransitionAsync(c,id,UserAccountTransition.Reactivate,false,false,ct)).RequireAuthorization(TaskPermissionAuthorization.UserBlockPolicyName);
        app.MapPost("/api/v1/users/{id}/unblock", (HttpContext c, string id, CancellationToken ct) => TransitionAsync(c,id,UserAccountTransition.Unblock,false,false,ct)).RequireAuthorization(TaskPermissionAuthorization.UserBlockPolicyName);
        app.MapPost("/api/v1/auth/admin-reset-password", ResetPasswordAsync).RequireAuthorization(TaskPermissionAuthorization.UserResetPasswordPolicyName);
        return app;
    }

    private static async Task<IResult> GetUsersAsync(HttpContext context, string? filter, string? sort, int? page, string? cursor, CancellationToken cancellationToken)
    {
        var requestContext = ReadRequestContext(context);
        var store = context.RequestServices.GetService<IUserAccountReadStore>();
        if (requestContext is null || store is null) return await MissingContextOrStoreAsync(context, requestContext is null);
        if ((filter?.Length ?? 0) > 2000 || (sort?.Length ?? 0) > 500 || page is < 1 or > 100000 || !TryParseCursor(cursor, out var parsedCursor))
            return await ProblemAsync(context,400,"VALIDATION_FAILED","The user query is invalid.",false);
        if (!string.IsNullOrWhiteSpace(sort) && sort is not ("id" or "+id"))
            return await ProblemAsync(context,400,"VALIDATION_FAILED","Only the stable id sort is supported.",false);
        var result = await store.GetPageAsync(new UserAccountReadPageRequest(requestContext.OrganizationId,filter,page ?? 1,parsedCursor),cancellationToken);
        return Results.Json(new UserPageResponse(result.Items.Select(ToResponse).ToArray(),result.NextCursor is null?null:EncodeCursor(result.NextCursor.Value),result.Total));
    }

    private static async Task<IResult> GetUserByIdAsync(HttpContext context, string id, CancellationToken cancellationToken)
    {
        var requestContext = ReadRequestContext(context);
        var store = context.RequestServices.GetService<IUserAccountReadStore>();
        if (requestContext is null || store is null) return await MissingContextOrStoreAsync(context, requestContext is null);
        if (!TryId(id,out var userId)) return await NotVisibleAsync(context);
        var user = await store.GetByIdAsync(requestContext.OrganizationId,userId,cancellationToken);
        return user is null ? await NotVisibleAsync(context) : UserResult(context,user,200,false);
    }

    private static async Task<IResult> CreateUserAsync(HttpContext context, CancellationToken cancellationToken)
    {
        var commandRead = await ReadCommandContextAsync(context,"user.create",true,true,cancellationToken);
        if (commandRead.ErrorCode is not null) return await ProblemAsync(context,commandRead.Status,commandRead.ErrorCode,commandRead.Message,false);
        using var commandBody = commandRead.Value?.Body;
        var commandContext=commandRead.Value;
        if (!TryReadCreate(commandContext!.Value.Body!.RootElement,out var request,out var message)) return await ProblemAsync(context,422,"VALIDATION_FAILED",message,false);
        var store = context.RequestServices.GetService<IUserAccountCommandStore>();
        var hasher = context.RequestServices.GetService<IPasswordHasher>();
        if (store is null || hasher is null) return await ProblemAsync(context,503,"INTERNAL_ERROR","User account commands are not configured.",true);
        var bootstrapSecret = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var result = await store.CreateAsync(commandContext.Value.Context,new UserAccountCreateCommand(request.DisplayName,request.FirstName,request.LastName,request.Login,request.WorkEmail,request.DepartmentId,request.JobTitle,hasher.HashPassword(bootstrapSecret)),cancellationToken);
        return await CommandResultAsync(context,result,201);
    }

    private static async Task<IResult> PatchUserAsync(HttpContext context, string id, CancellationToken cancellationToken)
    {
        if (!TryId(id,out var userId)) return await NotVisibleAsync(context);
        if (!TryReadIfMatch(context,out var expectedVersion,out var ifMatchError)) return await ifMatchError!;
        var commandRead = await ReadCommandContextAsync(context,"user.update",true,true,cancellationToken);
        if (commandRead.ErrorCode is not null) return await ProblemAsync(context,commandRead.Status,commandRead.ErrorCode,commandRead.Message,false);
        using var commandBody = commandRead.Value?.Body;
        var commandContext=commandRead.Value;
        if (!TryReadPatch(commandContext!.Value.Body!.RootElement,out var patch,out var message)) return await ProblemAsync(context,422,"VALIDATION_FAILED",message,false);
        var store = context.RequestServices.GetService<IUserAccountCommandStore>();
        if (store is null) return await ProblemAsync(context,503,"INTERNAL_ERROR","User account commands are not configured.",true);
        return await CommandResultAsync(context,await store.UpdateAsync(commandContext.Value.Context,userId,expectedVersion,patch,cancellationToken),200);
    }

    private static async Task<IResult> TransitionAsync(HttpContext context, string id, UserAccountTransition transition, bool bodyRequired, bool requireKey, CancellationToken cancellationToken)
    {
        if (!TryId(id,out var userId)) return await NotVisibleAsync(context);
        if (!TryReadIfMatch(context,out var expectedVersion,out var ifMatchError)) return await ifMatchError!;
        var operation = "user." + transition.ToString().ToLowerInvariant();
        var commandRead = await ReadCommandContextAsync(context,operation,requireKey,bodyRequired,cancellationToken,$"{operation}:{userId:D}:v{expectedVersion}");
        if (commandRead.ErrorCode is not null) return await ProblemAsync(context,commandRead.Status,commandRead.ErrorCode,commandRead.Message,false);
        using var commandBody = commandRead.Value?.Body;
        var commandContext=commandRead.Value;
        string? reason = null;
        if (bodyRequired && !TryReadTransition(commandContext!.Value.Body!.RootElement,expectedVersion,transition,out reason,out var message)) return await ProblemAsync(context,422,"VALIDATION_FAILED",message,false);
        var store = context.RequestServices.GetService<IUserAccountCommandStore>();
        if (store is null) return await ProblemAsync(context,503,"INTERNAL_ERROR","User account commands are not configured.",true);
        return await CommandResultAsync(context,await store.TransitionAsync(commandContext!.Value.Context,userId,expectedVersion,transition,reason,cancellationToken),200);
    }

    private static async Task<IResult> ResetPasswordAsync(HttpContext context, CancellationToken cancellationToken)
    {
        var commandRead = await ReadCommandContextAsync(context,"user.reset-password",true,true,cancellationToken);
        if (commandRead.ErrorCode is not null) return await ProblemAsync(context,commandRead.Status,commandRead.ErrorCode,commandRead.Message,false);
        using var commandBody = commandRead.Value?.Body;
        var commandContext=commandRead.Value;
        if (!TryReadReset(commandContext!.Value.Body!.RootElement,out var request,out var message)) return await ProblemAsync(context,422,"VALIDATION_FAILED",message,false);
        var store = context.RequestServices.GetService<IUserAccountCommandStore>();
        var hasher = context.RequestServices.GetService<IPasswordHasher>();
        if (store is null || hasher is null) return await ProblemAsync(context,503,"INTERNAL_ERROR","User account commands are not configured.",true);
        var result = await store.ResetPasswordAsync(commandContext.Value.Context,request.UserId,request.ExpectedVersion,hasher.HashPassword(request.TemporaryPassword),cancellationToken);
        if (result.Disposition is IdentityCommandDisposition.Executed or IdentityCommandDisposition.Replayed)
        {
            context.Response.Headers.CacheControl = "no-store";
            context.Response.Headers.ETag=$"\"v{result.Version}\"";
            if (result.Disposition==IdentityCommandDisposition.Replayed) context.Response.Headers["Idempotency-Replayed"]="true";
            return Results.Json(new TemporaryCredentialReceipt(request.UserId,request.TemporaryPassword,result.ExpiresAtUtc!.Value.UtcDateTime,true));
        }
        return await CommandFailureAsync(context,result.Disposition,result.RetryAfterSeconds);
    }

    private static async Task<IResult> CommandResultAsync(HttpContext context, UserAccountCommandResult result, int status)
    {
        if (result.Disposition is IdentityCommandDisposition.Executed or IdentityCommandDisposition.Replayed) return UserResult(context,result.User!,status,result.Disposition==IdentityCommandDisposition.Replayed);
        return await CommandFailureAsync(context,result.Disposition,result.RetryAfterSeconds);
    }

    private static IResult UserResult(HttpContext context, UserAccountReadProjection user, int status, bool replayed)
    {
        context.Response.Headers.ETag=$"\"v{user.Version.ToString(CultureInfo.InvariantCulture)}\"";
        if (replayed) context.Response.Headers["Idempotency-Replayed"]="true";
        if (status==201) context.Response.Headers.Location=$"/api/v1/users/{user.Id:D}";
        return Results.Json(ToResponse(user),statusCode:status);
    }

    private static Task<IResult> CommandFailureAsync(HttpContext context, IdentityCommandDisposition disposition, int? retryAfter) => disposition switch
    {
        IdentityCommandDisposition.NotFound => NotVisibleAsync(context),
        IdentityCommandDisposition.VersionConflict => ProblemAsync(context,412,"VERSION_CONFLICT","The user version does not match If-Match.",false),
        IdentityCommandDisposition.DuplicateResource => ProblemAsync(context,409,"DUPLICATE_RESOURCE","The login or work email is already in use.",false),
        IdentityCommandDisposition.InvalidStateTransition => ProblemAsync(context,409,"INVALID_STATE_TRANSITION","The requested account transition is not allowed.",false),
        IdentityCommandDisposition.IdempotencyKeyReused => ProblemAsync(context,409,"IDEMPOTENCY_KEY_REUSED","The idempotency key was already used for a different request.",false),
        IdentityCommandDisposition.RequestInProgress => ProblemAsync(context,409,"REQUEST_IN_PROGRESS","The request is already being processed.",true,retryAfter),
        _ => ProblemAsync(context,503,"INTERNAL_ERROR","The user account command failed safely.",true),
    };

    private static async global::System.Threading.Tasks.Task<CommandContextRead> ReadCommandContextAsync(HttpContext context, string operation, bool requireKey, bool bodyRequired, CancellationToken cancellationToken, string? implicitKey=null)
    {
        var requestContext=ReadRequestContext(context);
        if (requestContext is null) return new(null,500,"INTERNAL_ERROR","The authenticated request context is unavailable.");
        var key=context.Request.Headers["Idempotency-Key"].ToString();
        if (requireKey && !ValidIdempotencyKey(key)) return new(null,400,"VALIDATION_FAILED","A printable 8-200 character Idempotency-Key is required.");
        JsonDocument? body=null;
        if (bodyRequired)
        {
            try { body=await JsonDocument.ParseAsync(context.Request.Body,cancellationToken:cancellationToken); }
            catch (JsonException) { return new(null,400,"MALFORMED_JSON","The request body is not valid JSON."); }
        }
        var hash=IdentityRequestHash.Compute(context,body?.RootElement);
        var correlation=Guid.TryParse(TaskApiProblemResponse.GetCorrelationId(context),out var parsed)?parsed:Guid.NewGuid();
        return new((new IdentityCommandContext(requestContext.OrganizationId,requestContext.UserAccountId,requestContext.SessionId,correlation,operation,requireKey?key:implicitKey!,hash),body),0,null,"");
    }

    private static bool TryReadCreate(JsonElement root, out UserCreateRequest request, out string message)
    {
        request=default!; message="The user request is invalid.";
        if (!IsObjectWithOnly(root,["displayName","firstName","lastName","login","workEmail","departmentId","jobTitle","accountStatus"])) return false;
        if (!RequiredString(root,"firstName",100,out var first)||!RequiredString(root,"lastName",100,out var last)||!RequiredString(root,"login",100,out var login,3)) return false;
        if (!PatchText(root,"displayName",300,1,out var displayField)) return false;
        var display=displayField.IsSpecified ? displayField.Value! : $"{first} {last}";
        if (!ValidText(display,1,300)||!TryNullableString(root,"workEmail",320,out var email)||!TryNullableString(root,"jobTitle",200,out var title)||!TryNullableGuid(root,"departmentId",out var department)) return false;
        if (email is not null && !MailAddress.TryCreate(email,out _)) { message="workEmail must be a valid email address."; return false; }
        if (root.TryGetProperty("accountStatus",out var status) && (status.ValueKind != JsonValueKind.String || status.GetString()!="pending_activation")) { message="New users must start in pending_activation."; return false; }
        request=new(display,first,last,login,email,department,title); return true;
    }

    private static bool TryReadPatch(JsonElement root, out UserAccountPatchCommand patch, out string message)
    {
        patch=default!; message="The user patch is invalid.";
        if (!IsObjectWithOnly(root,["displayName","firstName","lastName","login","workEmail","departmentId","jobTitle"])||!root.EnumerateObject().Any()) return false;
        if (!PatchText(root,"displayName",300,1,out var display)||!PatchText(root,"firstName",100,1,out var first)||!PatchText(root,"lastName",100,1,out var last)||!PatchText(root,"login",100,3,out var login)||!PatchNullableText(root,"workEmail",320,out var email)||!PatchNullableText(root,"jobTitle",200,out var title)||!PatchNullableGuid(root,"departmentId",out var department)) return false;
        if (email.IsSpecified && email.Value is not null && !MailAddress.TryCreate(email.Value,out _)) { message="workEmail must be a valid email address."; return false; }
        patch=new(display,first,last,login,email,department,title); return true;
    }

    private static bool TryReadTransition(JsonElement root, long headerVersion, UserAccountTransition transition, out string? reason, out string message)
    {
        reason=null; message="The transition request is invalid.";
        var reasonRequired=transition is UserAccountTransition.Block or UserAccountTransition.Deactivate;
        if (!IsObjectWithOnly(root,reasonRequired?["reason","expectedVersion"]:["expectedVersion"])) return false;
        if (!root.TryGetProperty("expectedVersion",out var version)||version.ValueKind!=JsonValueKind.Number||!version.TryGetInt64(out var expected)||expected!=headerVersion) { message="expectedVersion must match If-Match."; return false; }
        if (reasonRequired && !RequiredString(root,"reason",2000,out reason!)) return false;
        return true;
    }

    private static bool TryReadReset(JsonElement root, out ResetRequest request, out string message)
    {
        request=default!; message="The password reset request is invalid.";
        if (!IsObjectWithOnly(root,["targetUserId","temporaryPassword","expectedVersion"])||!root.TryGetProperty("targetUserId",out var id)||id.ValueKind!=JsonValueKind.String||!Guid.TryParse(id.GetString(),out var userId)||userId==Guid.Empty||!RequiredString(root,"temporaryPassword",1024,out var password,12)||!root.TryGetProperty("expectedVersion",out var version)||version.ValueKind!=JsonValueKind.Number||!version.TryGetInt64(out var expected)||expected<1) return false;
        request=new(userId,password,expected); return true;
    }

    private static bool TryReadIfMatch(HttpContext context, out long version, out Task<IResult>? error)
    {
        version=0; error=null; var value=context.Request.Headers.IfMatch.ToString();
        if (string.IsNullOrWhiteSpace(value)) { error=ProblemAsync(context,428,"PRECONDITION_REQUIRED","If-Match is required.",false); return false; }
        if (value.Length<4||value[0]!='\"'||value[^1]!='\"'||value[1]!='v'||!long.TryParse(value.AsSpan(2,value.Length-3),NumberStyles.None,CultureInfo.InvariantCulture,out version)||version<1) { error=ProblemAsync(context,400,"VALIDATION_FAILED","If-Match must be a strong user ETag.",false); return false; }
        return true;
    }

    private static bool ValidIdempotencyKey(string value)=>value.Length is >=8 and <=200&&value.All(c=>c is >= '!' and <= '~');
    private static bool TryId(string value,out Guid id)=>Guid.TryParseExact(value,"D",out id)&&id!=Guid.Empty;
    private static bool TryParseCursor(string? value,out Guid? cursor){cursor=null;if(string.IsNullOrWhiteSpace(value))return true;try{var text=System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(value.Replace('-','+').Replace('_','/')+new string('=',(4-value.Length%4)%4)));if(Guid.TryParseExact(text,"D",out var id)){cursor=id;return true;}}catch(FormatException){}return false;}
    private static string EncodeCursor(Guid id)=>Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(id.ToString("D"))).TrimEnd('=').Replace('+','-').Replace('/','_');
    private static bool IsObjectWithOnly(JsonElement root,string[] names)=>root.ValueKind==JsonValueKind.Object&&root.EnumerateObject().All(p=>names.Contains(p.Name,StringComparer.Ordinal))&&root.EnumerateObject().Select(p=>p.Name).Distinct(StringComparer.Ordinal).Count()==root.EnumerateObject().Count();
    private static bool RequiredString(JsonElement root,string name,int max,out string value,int min=1){value="";return root.TryGetProperty(name,out var p)&&p.ValueKind==JsonValueKind.String&&ValidText(value=p.GetString()!,min,max);}
    private static string? OptionalString(JsonElement root,string name,int max)=>root.TryGetProperty(name,out var p)&&p.ValueKind==JsonValueKind.String&&ValidText(p.GetString()!,1,max)?p.GetString():null;
    private static bool TryNullableString(JsonElement root,string name,int max,out string? value){value=null;if(!root.TryGetProperty(name,out var p)||p.ValueKind==JsonValueKind.Null)return true;if(p.ValueKind!=JsonValueKind.String||!ValidText(p.GetString()!,1,max))return false;value=p.GetString();return true;}
    private static bool TryNullableGuid(JsonElement root,string name,out Guid? value){value=null;if(!root.TryGetProperty(name,out var p)||p.ValueKind==JsonValueKind.Null)return true;if(p.ValueKind!=JsonValueKind.String||!Guid.TryParse(p.GetString(),out var id)||id==Guid.Empty)return false;value=id;return true;}
    private static bool PatchText(JsonElement root,string name,int max,int min,out OptionalUserField<string> value){value=new(false,null);if(!root.TryGetProperty(name,out var p))return true;if(p.ValueKind!=JsonValueKind.String||!ValidText(p.GetString()!,min,max))return false;value=new(true,p.GetString());return true;}
    private static bool PatchNullableText(JsonElement root,string name,int max,out OptionalUserField<string?> value){value=new(false,null);if(!root.TryGetProperty(name,out var p))return true;if(p.ValueKind==JsonValueKind.Null){value=new(true,null);return true;}if(p.ValueKind!=JsonValueKind.String||!ValidText(p.GetString()!,1,max))return false;value=new(true,p.GetString());return true;}
    private static bool PatchNullableGuid(JsonElement root,string name,out OptionalUserField<Guid?> value){value=new(false,null);if(!root.TryGetProperty(name,out var p))return true;if(p.ValueKind==JsonValueKind.Null){value=new(true,null);return true;}if(p.ValueKind!=JsonValueKind.String||!Guid.TryParse(p.GetString(),out var id)||id==Guid.Empty)return false;value=new(true,id);return true;}
    private static bool ValidText(string value,int min,int max)=>!string.IsNullOrWhiteSpace(value)&&value.Trim().Length>=min&&value.Length<=max;
    private static AuthenticatedRequestContext? ReadRequestContext(HttpContext context)=>context.Items.TryGetValue(TaskJwtAuthenticationHandler.AuthenticatedRequestContextItemName,out var value)&&value is AuthenticatedRequestContext requestContext?requestContext:null;
    private static Task<IResult> MissingContextOrStoreAsync(HttpContext context,bool contextMissing)=>ProblemAsync(context,contextMissing?500:503,"INTERNAL_ERROR",contextMissing?"The authenticated request context is unavailable.":"User account access is not configured.",true);
    private static Task<IResult> NotVisibleAsync(HttpContext context)=>ProblemAsync(context,404,"OBJECT_NOT_VISIBLE","The requested object is absent or not visible.",false);
    private static async Task<IResult> ProblemAsync(HttpContext context,int status,string code,string title,bool retryable,int? retryAfter=null){await TaskApiProblemResponse.WriteAsync(context,status,code,title,retryable,retryAfter);return Results.Empty;}
    internal static UserResponse ToResponse(UserAccountReadProjection u)=>new(u.Id,u.OrganizationId,u.Version,u.CreatedAtUtc.UtcDateTime,u.UpdatedAtUtc.UtcDateTime,u.DisplayName,u.FirstName,u.LastName,u.Login,u.WorkEmail,u.DepartmentId,u.JobTitle,u.AccountStatus switch{UserAccountStatus.PendingActivation=>"pending_activation",UserAccountStatus.Active=>"active",UserAccountStatus.Blocked=>"blocked",UserAccountStatus.Deactivated=>"deactivated",_=>throw new ArgumentOutOfRangeException()});

    private sealed record UserCreateRequest(string DisplayName,string FirstName,string LastName,string Login,string? WorkEmail,Guid? DepartmentId,string? JobTitle);
    private sealed record ResetRequest(Guid UserId,string TemporaryPassword,long ExpectedVersion);
    private sealed record CommandContextRead((IdentityCommandContext Context,JsonDocument? Body)? Value,int Status,string? ErrorCode,string Message);
    private sealed record TemporaryCredentialReceipt([property:JsonPropertyName("userId")]Guid UserId,[property:JsonPropertyName("temporaryPassword")]string TemporaryPassword,[property:JsonPropertyName("expiresAt")]DateTime ExpiresAt,[property:JsonPropertyName("mustChangePassword")]bool MustChangePassword);
    private sealed record UserPageResponse([property:JsonPropertyName("items")]IReadOnlyList<UserResponse> Items,[property:JsonPropertyName("nextCursor")]string? NextCursor,[property:JsonPropertyName("total")]long Total);
    internal sealed record UserResponse([property:JsonPropertyName("id")]Guid Id,[property:JsonPropertyName("organizationId")]Guid OrganizationId,[property:JsonPropertyName("version")]long Version,[property:JsonPropertyName("createdAt")]DateTime CreatedAt,[property:JsonPropertyName("updatedAt")]DateTime UpdatedAt,[property:JsonPropertyName("displayName")]string DisplayName,[property:JsonPropertyName("firstName")]string FirstName,[property:JsonPropertyName("lastName")]string LastName,[property:JsonPropertyName("login")]string Login,[property:JsonPropertyName("workEmail")]string? WorkEmail,[property:JsonPropertyName("departmentId")]Guid? DepartmentId,[property:JsonPropertyName("jobTitle")]string? JobTitle,[property:JsonPropertyName("accountStatus")]string AccountStatus);
}
