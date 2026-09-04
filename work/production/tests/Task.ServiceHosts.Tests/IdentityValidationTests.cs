using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Task.Api.Security;
using Task.Application.Security;

namespace Task.ServiceHosts.Tests;

public sealed partial class TaskEndpointsTests
{
    [Fact]
    public void IdentityRequestHash_BindsTargetAndVersionAndIgnoresPropertyOrder()
    {
        var context=new Microsoft.AspNetCore.Http.DefaultHttpContext();
        context.Request.Method="PATCH";context.Request.Path="/api/v1/users/first";context.Request.Headers.IfMatch="\"v1\"";
        using var first=JsonDocument.Parse("{\"login\":\"test.user\",\"displayName\":\"Name\"}");
        using var reordered=JsonDocument.Parse("{\"displayName\":\"Name\",\"login\":\"test.user\"}");
        var hash=IdentityRequestHash.Compute(context,first.RootElement);
        Assert.Equal(hash,IdentityRequestHash.Compute(context,reordered.RootElement));
        context.Request.Path="/api/v1/users/second";
        Assert.NotEqual(hash,IdentityRequestHash.Compute(context,first.RootElement));
        context.Request.Path="/api/v1/users/first";context.Request.Headers.IfMatch="\"v2\"";
        Assert.NotEqual(hash,IdentityRequestHash.Compute(context,first.RootElement));
    }
    [Theory]
    [InlineData("/api/v1/users", "{\"firstName\":\"Test\",\"lastName\":\"User\",\"login\":\"test.user\",\"accountStatus\":42}")]
    [InlineData("/api/v1/users", "{\"firstName\":\"Test\",\"lastName\":\"User\",\"login\":\"test.user\",\"displayName\":false}")]
    [InlineData("/api/v1/users", "{\"firstName\":\"Test\",\"firstName\":\"Other\",\"lastName\":\"User\",\"login\":\"test.user\"}")]
    [InlineData("/api/v1/auth/admin-reset-password", "{\"targetUserId\":42,\"temporaryPassword\":\"Temporary-Password-123!\",\"expectedVersion\":1}")]
    [InlineData("/api/v1/auth/admin-reset-password", "{\"targetUserId\":\"55555555-5555-5555-5555-555555555555\",\"temporaryPassword\":\"Temporary-Password-123!\",\"expectedVersion\":\"1\"}")]
    public async global::System.Threading.Tasks.Task IdentityCommands_RejectWrongJsonTypes(string path,string body)
    {
        using var server=CreateServer(null,userCommandStore:new FakeUserAccountCommandStore(),passwordHasher:new FakePasswordHasher(),grantedPermissions:Grant("user.create","user.resetpassword"));
        using var client=await CreateAuthenticatedClientAsync(server,OrganizationId);
        using var request=new HttpRequestMessage(HttpMethod.Post,path){Content=new StringContent(body,Encoding.UTF8,"application/json")};
        request.Headers.Add("Idempotency-Key","validation-test-01");
        await AssertProblemAsync(await client.SendAsync(request),HttpStatusCode.UnprocessableEntity,"VALIDATION_FAILED");
    }

    [Fact]
    public async global::System.Threading.Tasks.Task Heartbeat_RejectsNonStringTimestamp()
    {
        using var server=CreateServer(null,deviceStore:new FakeDeviceStore());
        using var client=await CreateAuthenticatedClientAsync(server,OrganizationId);
        await AssertProblemAsync(await client.PostAsJsonAsync($"/api/v1/devices/{DeviceId}/heartbeat",new{appVersion="1.0",observedAt=42}),HttpStatusCode.UnprocessableEntity,"VALIDATION_FAILED");
    }

    [Fact]
    public async global::System.Threading.Tasks.Task DevicePermission_DoesNotGrantAccessToForeignOwner()
    {
        using var server=CreateServer(null,deviceStore:new ForeignDeviceStore(),grantedPermissions:Grant("device.readownorall"));
        using var client=await CreateAuthenticatedClientAsync(server,OrganizationId);
        await AssertProblemAsync(await client.GetAsync($"/api/v1/devices/{DeviceId}"),HttpStatusCode.NotFound,"OBJECT_NOT_VISIBLE");
    }

    [Fact]
    public async global::System.Threading.Tasks.Task IdentityAdministrator_CanReadForeignOwnerDevice()
    {
        using var server=CreateServer(null,deviceStore:new ForeignDeviceStore(),grantedPermissions:Grant("device.readownorall","identity.account.manage"));
        using var client=await CreateAuthenticatedClientAsync(server,OrganizationId);
        Assert.Equal(HttpStatusCode.OK,(await client.GetAsync($"/api/v1/devices/{DeviceId}")).StatusCode);
    }

    private sealed class ForeignDeviceStore : IDeviceRegistrationStore
    {
        public Task<Guid> UpsertAsync(Guid organizationId,Guid userId,string fingerprintHash,string? displayName,CancellationToken ct=default)=>throw new NotSupportedException();
        public Task<DeviceRegistrationRecord?> GetByIdAsync(Guid organizationId,Guid deviceId,CancellationToken ct=default)=>throw new NotSupportedException();
        public Task<DeviceReadProjection?> GetReadModelAsync(Guid organizationId,Guid deviceId,CancellationToken ct=default)=>global::System.Threading.Tasks.Task.FromResult<DeviceReadProjection?>(DeviceProjection with{UserId=Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb")});
    }
}
