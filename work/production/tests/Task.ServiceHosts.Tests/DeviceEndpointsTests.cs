using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Task.Api.Security;
using Task.Application.Security;

namespace Task.ServiceHosts.Tests;

public sealed partial class TaskEndpointsTests
{
    private static readonly Guid DeviceId=Guid.Parse("77777777-7777-7777-7777-777777777777");
    private static DeviceReadProjection DeviceProjection => new(DeviceId,OrganizationId,UserId,3,DateTimeOffset.Parse("2026-08-01T10:00:00Z"),DateTimeOffset.Parse("2026-08-20T10:00:00Z"),"Workstation","windows","1.2.3","Windows 11",DateTimeOffset.Parse("2026-08-20T10:00:00Z"),null);

    [Fact]
    public async global::System.Threading.Tasks.Task Devices_ListAndDetail_ReturnCanonicalProjection()
    {
        var store=new FakeDeviceStore();using var server=CreateServer(null,deviceStore:store,grantedPermissions:Grant(TaskPermissionAuthorization.DeviceReadBackingPermissionCode));using var client=await CreateAuthenticatedClientAsync(server,OrganizationId);
        var list=await client.GetAsync("/api/v1/devices");Assert.Equal(HttpStatusCode.OK,list.StatusCode);
        using(var json=JsonDocument.Parse(await list.Content.ReadAsStringAsync()))Assert.Single(json.RootElement.GetProperty("items").EnumerateArray());
        var detail=await client.GetAsync($"/api/v1/devices/{DeviceId:D}");Assert.Equal(HttpStatusCode.OK,detail.StatusCode);Assert.Equal("\"v3\"",detail.Headers.ETag?.Tag);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task DevicePatch_RequiresIfMatch_AndUpdates()
    {
        var store=new FakeDeviceStore();using var server=CreateServer(null,deviceStore:store,grantedPermissions:Grant(TaskPermissionAuthorization.DeviceUpdateBackingPermissionCode));using var client=await CreateAuthenticatedClientAsync(server,OrganizationId);
        using var request=new HttpRequestMessage(HttpMethod.Patch,$"/api/v1/devices/{DeviceId:D}"){Content=JsonContent.Create(new{deviceName="Renamed"})};request.Headers.TryAddWithoutValidation("If-Match","\"v3\"");
        var response=await client.SendAsync(request);Assert.Equal(HttpStatusCode.OK,response.StatusCode);Assert.Equal("Renamed",store.LastPatch!.DeviceName);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task DeviceHeartbeat_IsRestrictedToAuthenticatedOwner()
    {
        var store=new FakeDeviceStore();using var server=CreateServer(null,deviceStore:store,grantedPermissions:new HashSet<string>());using var client=await CreateAuthenticatedClientAsync(server,OrganizationId);
        var response=await client.PostAsJsonAsync($"/api/v1/devices/{DeviceId:D}/heartbeat",new{appVersion="1.2.4",osVersion="Windows 11",observedAt=DateTimeOffset.UtcNow});
        Assert.Equal(HttpStatusCode.NoContent,response.StatusCode);Assert.True(store.HeartbeatCalled);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task DeviceRevoke_RequiresIdempotency_AndRevokesSessions()
    {
        var store=new FakeDeviceStore();using var server=CreateServer(null,deviceStore:store,grantedPermissions:Grant(TaskPermissionAuthorization.DeviceRevokeBackingPermissionCode));using var client=await CreateAuthenticatedClientAsync(server,OrganizationId);
        using var request=new HttpRequestMessage(HttpMethod.Post,$"/api/v1/devices/{DeviceId:D}/revoke"){Content=JsonContent.Create(new{reason="Lost device",expectedVersion=3})};request.Headers.TryAddWithoutValidation("If-Match","\"v3\"");request.Headers.Add("Idempotency-Key","revoke-device-1");
        var response=await client.SendAsync(request);Assert.Equal(HttpStatusCode.OK,response.StatusCode);Assert.True(store.RevokeCalled);
    }

    private sealed class FakeDeviceStore:IDeviceRegistrationStore
    {
        public DevicePatchCommand? LastPatch{get;private set;}public bool HeartbeatCalled{get;private set;}public bool RevokeCalled{get;private set;}
        public global::System.Threading.Tasks.Task<Guid> UpsertAsync(Guid organizationId,Guid userId,string fingerprintHash,string? displayName,CancellationToken cancellationToken=default)=>global::System.Threading.Tasks.Task.FromResult(DeviceId);
        public global::System.Threading.Tasks.Task<DeviceRegistrationRecord?> GetByIdAsync(Guid organizationId,Guid deviceId,CancellationToken cancellationToken=default)=>global::System.Threading.Tasks.Task.FromResult<DeviceRegistrationRecord?>(new(DeviceId,UserId,new string('a',64),null));
        public global::System.Threading.Tasks.Task<DeviceReadProjection?> GetReadModelAsync(Guid organizationId,Guid deviceId,CancellationToken cancellationToken=default)=>global::System.Threading.Tasks.Task.FromResult<DeviceReadProjection?>(organizationId==OrganizationId&&deviceId==DeviceId?DeviceProjection:null);
        public global::System.Threading.Tasks.Task<DeviceReadPage> GetPageAsync(Guid organizationId,Guid requestingUserId,bool includeAll,string? filter,int page,Guid? cursor,CancellationToken cancellationToken=default)=>global::System.Threading.Tasks.Task.FromResult(new DeviceReadPage(organizationId==OrganizationId?[DeviceProjection]:[],null,organizationId==OrganizationId?1:0));
        public global::System.Threading.Tasks.Task<DeviceCommandResult> PatchAsync(IdentityCommandContext context,Guid deviceId,long expectedVersion,DevicePatchCommand patch,CancellationToken cancellationToken=default){LastPatch=patch;return global::System.Threading.Tasks.Task.FromResult(new DeviceCommandResult(IdentityCommandDisposition.Executed,DeviceProjection with{Version=4,DeviceName=patch.DeviceName??DeviceProjection.DeviceName}));}
        public global::System.Threading.Tasks.Task<bool> HeartbeatAsync(Guid organizationId,Guid userId,Guid deviceId,string appVersion,string? osVersion,DateTimeOffset observedAtUtc,CancellationToken cancellationToken=default){HeartbeatCalled=true;return global::System.Threading.Tasks.Task.FromResult(userId==UserId);}
        public global::System.Threading.Tasks.Task<DeviceCommandResult> RevokeAsync(IdentityCommandContext context,Guid deviceId,long expectedVersion,string reason,CancellationToken cancellationToken=default){RevokeCalled=true;return global::System.Threading.Tasks.Task.FromResult(new DeviceCommandResult(IdentityCommandDisposition.Executed,DeviceProjection with{Version=4,RevokedAtUtc=DateTimeOffset.UtcNow}));}
    }
}
