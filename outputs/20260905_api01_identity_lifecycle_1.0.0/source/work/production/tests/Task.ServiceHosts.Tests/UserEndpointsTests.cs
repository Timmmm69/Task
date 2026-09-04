using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Task.Api.Security;
using Task.Application.Security;

namespace Task.ServiceHosts.Tests;

public sealed partial class TaskEndpointsTests
{
    private const string UsersUrl = "/api/v1/users";
    private static readonly Guid TargetUserAccountId = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly UserAccountReadProjection UserProjection = new(
        TargetUserAccountId, OrganizationId, 4,
        DateTimeOffset.Parse("2026-08-10T08:00:00Z"), DateTimeOffset.Parse("2026-08-24T12:30:00Z"),
        "Anna Petrova", "Anna", "Petrova", "a.petrova", "anna.petrova@example.test", null,
        "Operations Manager", UserAccountStatus.Blocked);

    [Fact]
    public async global::System.Threading.Tasks.Task GetUser_WithoutToken_Returns401()
    {
        using var server = CreateServer(null, userReadStore: new FakeUserAccountReadStore(UserProjection));
        var response = await server.CreateClient().GetAsync(UsersUrl + "/" + TargetUserAccountId.ToString("D"));
        await AssertProblemAsync(response, HttpStatusCode.Unauthorized, "AUTHENTICATION_REQUIRED");
    }

    [Fact]
    public async global::System.Threading.Tasks.Task GetUser_WithoutUserReadPermission_Returns403()
    {
        using var server = CreateServer(null, userReadStore: new FakeUserAccountReadStore(UserProjection), grantUserRead: false);
        using var client = await CreateAuthenticatedClientAsync(server, OrganizationId);
        var response = await client.GetAsync(UsersUrl + "/" + TargetUserAccountId.ToString("D"));
        await AssertProblemAsync(response, HttpStatusCode.Forbidden, "FORBIDDEN");
    }

    [Fact]
    public async global::System.Threading.Tasks.Task GetUser_WithPermission_ReturnsCanonicalUserAndEtag()
    {
        var store = new FakeUserAccountReadStore(UserProjection);
        using var server = CreateServer(null, userReadStore: store);
        using var client = await CreateAuthenticatedClientAsync(server, OrganizationId);
        var response = await client.GetAsync(UsersUrl + "/" + TargetUserAccountId.ToString("D"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("\"v4\"", response.Headers.ETag?.Tag);
        Assert.Equal((OrganizationId, TargetUserAccountId), store.LastRequest);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var user = document.RootElement;
        Assert.Equal(TargetUserAccountId.ToString("D"), user.GetProperty("id").GetString());
        Assert.Equal(OrganizationId.ToString("D"), user.GetProperty("organizationId").GetString());
        Assert.Equal(4, user.GetProperty("version").GetInt64());
        Assert.EndsWith("Z", user.GetProperty("createdAt").GetString());
        Assert.EndsWith("Z", user.GetProperty("updatedAt").GetString());
        Assert.Equal("Anna Petrova", user.GetProperty("displayName").GetString());
        Assert.Equal("Anna", user.GetProperty("firstName").GetString());
        Assert.Equal("Petrova", user.GetProperty("lastName").GetString());
        Assert.Equal("a.petrova", user.GetProperty("login").GetString());
        Assert.Equal("anna.petrova@example.test", user.GetProperty("workEmail").GetString());
        Assert.Equal(JsonValueKind.Null, user.GetProperty("departmentId").ValueKind);
        Assert.Equal("Operations Manager", user.GetProperty("jobTitle").GetString());
        Assert.Equal("blocked", user.GetProperty("accountStatus").GetString());
    }

    [Fact]
    public async global::System.Threading.Tasks.Task GetUser_FromForeignOrganization_ReturnsObjectNotVisible()
    {
        using var server = CreateServer(null, tokenOrganizationId: ForeignOrganizationId, userReadStore: new FakeUserAccountReadStore(UserProjection));
        using var client = await CreateAuthenticatedClientAsync(server, ForeignOrganizationId);
        var response = await client.GetAsync(UsersUrl + "/" + TargetUserAccountId.ToString("D"));
        await AssertProblemAsync(response, HttpStatusCode.NotFound, "OBJECT_NOT_VISIBLE");
    }

    [Theory]
    [InlineData("not-a-uuid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    [InlineData("66666666-6666-6666-6666-666666666666")]
    public async global::System.Threading.Tasks.Task GetUser_WhenMalformedOrAbsent_ReturnsObjectNotVisible(string id)
    {
        using var server = CreateServer(null, userReadStore: new FakeUserAccountReadStore(UserProjection));
        using var client = await CreateAuthenticatedClientAsync(server, OrganizationId);
        var response = await client.GetAsync(UsersUrl + "/" + id);
        await AssertProblemAsync(response, HttpStatusCode.NotFound, "OBJECT_NOT_VISIBLE");
    }

    [Fact]
    public async global::System.Threading.Tasks.Task GetUser_WithoutReadStore_Returns503()
    {
        using var server = CreateServer(null);
        using var client = await CreateAuthenticatedClientAsync(server, OrganizationId);
        var response = await client.GetAsync(UsersUrl + "/" + TargetUserAccountId.ToString("D"));
        await AssertProblemAsync(response, HttpStatusCode.ServiceUnavailable, "INTERNAL_ERROR");
    }

    [Fact]
    public async global::System.Threading.Tasks.Task GetUsers_ReturnsTenantPage()
    {
        using var server=CreateServer(null,userReadStore:new FakeUserAccountReadStore(UserProjection));
        using var client=await CreateAuthenticatedClientAsync(server,OrganizationId);
        var response=await client.GetAsync(UsersUrl);
        Assert.Equal(HttpStatusCode.OK,response.StatusCode);
        using var json=JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Single(json.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal(1,json.RootElement.GetProperty("total").GetInt64());
    }

    [Fact]
    public async global::System.Threading.Tasks.Task CreateUser_RequiresIdempotencyKey_AndReturnsPendingUser()
    {
        var commands=new FakeUserAccountCommandStore();
        using var server=CreateServer(null,userReadStore:new FakeUserAccountReadStore(UserProjection),userCommandStore:commands,passwordHasher:new FakePasswordHasher(),grantedPermissions:Grant(TaskPermissionAuthorization.UserCreateBackingPermissionCode));
        using var client=await CreateAuthenticatedClientAsync(server,OrganizationId);
        var body=new{firstName="Ivan",lastName="Sidorov",login="i.sidorov"};
        var missing=await client.PostAsJsonAsync(UsersUrl,body);
        await AssertProblemAsync(missing,HttpStatusCode.BadRequest,"VALIDATION_FAILED");
        using var request=new HttpRequestMessage(HttpMethod.Post,UsersUrl){Content=JsonContent.Create(body)};
        request.Headers.Add("Idempotency-Key","create-user-1");
        var response=await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created,response.StatusCode);
        Assert.Equal("\"v1\"",response.Headers.ETag?.Tag);
        Assert.Equal(UserAccountStatus.PendingActivation,commands.LastCreate!.AccountStatus);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task PatchUser_RequiresIfMatch_AndMapsVersionConflict()
    {
        var commands=new FakeUserAccountCommandStore{NextDisposition=IdentityCommandDisposition.VersionConflict};
        using var server=CreateServer(null,userReadStore:new FakeUserAccountReadStore(UserProjection),userCommandStore:commands,grantedPermissions:Grant(TaskPermissionAuthorization.UserUpdateBackingPermissionCode));
        using var client=await CreateAuthenticatedClientAsync(server,OrganizationId);
        using var missing=new HttpRequestMessage(HttpMethod.Patch,$"{UsersUrl}/{TargetUserAccountId:D}"){Content=JsonContent.Create(new{displayName="New Name"})};
        missing.Headers.Add("Idempotency-Key","patch-user-1");
        await AssertProblemAsync(await client.SendAsync(missing),(HttpStatusCode)428,"PRECONDITION_REQUIRED");
        using var request=new HttpRequestMessage(HttpMethod.Patch,$"{UsersUrl}/{TargetUserAccountId:D}"){Content=JsonContent.Create(new{displayName="New Name"})};
        request.Headers.TryAddWithoutValidation("If-Match","\"v4\"");request.Headers.Add("Idempotency-Key","patch-user-2");
        await AssertProblemAsync(await client.SendAsync(request),HttpStatusCode.PreconditionFailed,"VERSION_CONFLICT");
    }

    [Fact]
    public async global::System.Threading.Tasks.Task BlockUser_UsesProtectedTransition()
    {
        var commands=new FakeUserAccountCommandStore();
        using var server=CreateServer(null,userReadStore:new FakeUserAccountReadStore(UserProjection),userCommandStore:commands,grantedPermissions:Grant(TaskPermissionAuthorization.UserBlockBackingPermissionCode));
        using var client=await CreateAuthenticatedClientAsync(server,OrganizationId);
        using var request=new HttpRequestMessage(HttpMethod.Post,$"{UsersUrl}/{TargetUserAccountId:D}/block"){Content=JsonContent.Create(new{reason="Security incident",expectedVersion=4})};
        request.Headers.TryAddWithoutValidation("If-Match","\"v4\"");request.Headers.Add("Idempotency-Key","block-user-1");
        var response=await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK,response.StatusCode);
        Assert.Equal(UserAccountTransition.Block,commands.LastTransition);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task AdminResetPassword_ReturnsOneTimeReceipt()
    {
        var commands=new FakeUserAccountCommandStore();
        using var server=CreateServer(null,userReadStore:new FakeUserAccountReadStore(UserProjection),userCommandStore:commands,passwordHasher:new FakePasswordHasher(),grantedPermissions:Grant(TaskPermissionAuthorization.UserResetPasswordBackingPermissionCode));
        using var client=await CreateAuthenticatedClientAsync(server,OrganizationId);
        using var request=new HttpRequestMessage(HttpMethod.Post,"/api/v1/auth/admin-reset-password"){Content=JsonContent.Create(new{targetUserId=TargetUserAccountId,temporaryPassword="Temporary-Password-123!",expectedVersion=4})};
        request.Headers.Add("Idempotency-Key","reset-password-1");
        var response=await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK,response.StatusCode);
        using var json=JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(json.RootElement.GetProperty("mustChangePassword").GetBoolean());
        Assert.Equal("Temporary-Password-123!",json.RootElement.GetProperty("temporaryPassword").GetString());
    }

    private static IReadOnlySet<string> Grant(params string[] permissions)=>new HashSet<string>(permissions,StringComparer.Ordinal);

    private sealed class FakeUserAccountReadStore(UserAccountReadProjection? projection) : IUserAccountReadStore
    {
        public (Guid OrganizationId, Guid UserId)? LastRequest { get; private set; }

        public global::System.Threading.Tasks.Task<UserAccountReadProjection?> GetByIdAsync(Guid organizationId, Guid userId, CancellationToken cancellationToken = default)
        {
            LastRequest = (organizationId, userId);
            return global::System.Threading.Tasks.Task.FromResult(
                projection is not null && projection.OrganizationId == organizationId && projection.Id == userId ? projection : null);
        }

        public global::System.Threading.Tasks.Task<UserAccountReadPage> GetPageAsync(UserAccountReadPageRequest request,CancellationToken cancellationToken=default)=>
            global::System.Threading.Tasks.Task.FromResult(new UserAccountReadPage(projection is not null&&projection.OrganizationId==request.OrganizationId?[projection]:[],null,projection is null?0:1));
    }

    private sealed class FakeUserAccountCommandStore:IUserAccountCommandStore
    {
        public IdentityCommandDisposition NextDisposition{get;set;}=IdentityCommandDisposition.Executed;
        public UserAccountReadProjection? LastCreate{get;private set;}
        public UserAccountTransition? LastTransition{get;private set;}
        public global::System.Threading.Tasks.Task<UserAccountCommandResult> CreateAsync(IdentityCommandContext context,UserAccountCreateCommand command,CancellationToken cancellationToken=default){LastCreate=UserProjection with{Version=1,DisplayName=command.DisplayName,FirstName=command.FirstName,LastName=command.LastName,Login=command.Login,AccountStatus=UserAccountStatus.PendingActivation};return Result(LastCreate);}
        public global::System.Threading.Tasks.Task<UserAccountCommandResult> UpdateAsync(IdentityCommandContext context,Guid userId,long expectedVersion,UserAccountPatchCommand command,CancellationToken cancellationToken=default)=>Result(UserProjection with{Version=5});
        public global::System.Threading.Tasks.Task<UserAccountCommandResult> TransitionAsync(IdentityCommandContext context,Guid userId,long expectedVersion,UserAccountTransition transition,string? reason,CancellationToken cancellationToken=default){LastTransition=transition;return Result(UserProjection with{Version=5,AccountStatus=transition==UserAccountTransition.Block?UserAccountStatus.Blocked:UserAccountStatus.Active});}
        public global::System.Threading.Tasks.Task<PasswordResetCommandResult> ResetPasswordAsync(IdentityCommandContext context,Guid userId,long expectedVersion,PasswordHashRecord credential,CancellationToken cancellationToken=default)=>global::System.Threading.Tasks.Task.FromResult(new PasswordResetCommandResult(NextDisposition,5,ExpiresAtUtc:DateTimeOffset.UtcNow.AddHours(24)));
        private global::System.Threading.Tasks.Task<UserAccountCommandResult> Result(UserAccountReadProjection user)=>global::System.Threading.Tasks.Task.FromResult(new UserAccountCommandResult(NextDisposition,NextDisposition==IdentityCommandDisposition.Executed?user:null));
    }

    private sealed class FakePasswordHasher:IPasswordHasher
    {
        public PasswordHashRecord HashPassword(string password)=>new(new string('a',64),"{}");
        public bool VerifyPassword(string password,PasswordHashRecord stored)=>true;
    }
}
