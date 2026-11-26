using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Task.Api.Inventory;
using Task.Api.Security;
using Task.Application.Inventory;
using Task.Application.Security;
using Async = System.Threading.Tasks;

namespace Task.ServiceHosts.Tests;

public sealed class InventoryEndpointsTests
{
    private static readonly Guid Organization = Guid.NewGuid();
    private static readonly Guid Manager = Guid.NewGuid();
    private static readonly Guid Admin = Guid.NewGuid();
    private static readonly Guid Viewer = Guid.NewGuid();
    private const string Route = "/api/v1/material-inventory";

    [Fact]
    public async Async.Task PricesAndCommandsFollowServerPermissions()
    {
        var seed = InventoryData.Empty() with
        {
            Materials = [new("mask", "Маски", "уп.", 1)],
            Suppliers = [new("supplier", "Стомснаб")],
            Movements = [new("receipt", "receipt", new(2026, 1, 1), 1, [new("line", "mask", 5, 12)], "supplier")],
        };
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddAuthentication("Test").AddScheme<AuthenticationSchemeOptions, TestAuthentication>("Test", null);
        builder.Services.AddAuthorization(options => options.AddPolicy(TaskPermissionAuthorization.OrganizationManagePolicyName,
            policy => policy.RequireRole("administrator")));
        builder.Services.AddSingleton<IInventoryStore>(new TestStore(new(1, seed, Manager)));
        await using var app = builder.Build();
        app.MapInventoryEndpoints();
        await app.StartAsync();
        using var client = app.GetTestClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(Route)).StatusCode);
        SetActor(client, Viewer);
        using var basic = await client.GetAsync(Route);
        var publicBody = await basic.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, basic.StatusCode);
        Assert.Contains("\"quantity\": 5", publicBody);
        Assert.DoesNotContain("\"averagePrice\"", publicBody);
        Assert.DoesNotContain("\"value\"", publicBody);
        Assert.DoesNotContain("\"details\"", publicBody);
        Assert.True(basic.Headers.CacheControl?.NoStore);
        var movement = new InventoryMovement("issue", "issue", new(2026, 1, 2), 1, [new("line", "mask", 2)]);
        Assert.Equal(HttpStatusCode.Forbidden, await PostAsync(client, new(1, "move", Movement: movement)));
        var updatedSupplier = seed.Suppliers[0] with { Phone = "00 (123) 45-67", TaxDetails = "00123456" };
        Assert.Equal(HttpStatusCode.Forbidden, await PostAsync(client, new(1, "supplier", Supplier: updatedSupplier)));
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(Route + "/managers")).StatusCode);

        SetActor(client, Manager);
        var privileged = await client.GetFromJsonAsync<InventorySnapshot>(Route, InventoryJson.Options);
        Assert.True(privileged!.CanManage);
        Assert.False(privileged.CanAdminister);
        Assert.Equal(60m, Assert.Single(privileged.Stock).Value);
        Assert.NotNull(privileged.Details);
        Assert.Equal(HttpStatusCode.OK, await PostAsync(client, new(1, "move", Movement: movement)));
        Assert.Equal(HttpStatusCode.Conflict, await PostAsync(client, new(1, "move", Movement: movement)));
        Assert.Equal(HttpStatusCode.Forbidden, await PostAsync(client, new(2, "cancel", MovementId: "issue")));
        Assert.Equal(HttpStatusCode.Forbidden, await PostAsync(client, new(2, "import", Backup: InventoryData.Empty())));

        SetActor(client, Admin);
        Assert.Equal(HttpStatusCode.OK, await PostAsync(client, new(2, "cancel", MovementId: "issue")));
        var restored = await client.GetFromJsonAsync<InventorySnapshot>(Route, InventoryJson.Options);
        Assert.True(restored!.CanAdminister);
        Assert.Equal(5m, Assert.Single(restored.Stock).Quantity);

        SetActor(client, Manager);
        Assert.Equal(HttpStatusCode.OK, await PostAsync(client, new(3, "supplier", Supplier: updatedSupplier)));
        Assert.Equal(HttpStatusCode.Conflict, await PostAsync(client, new(3, "supplier", Supplier: seed.Suppliers[0])));
        var saved = await client.GetFromJsonAsync<InventorySnapshot>(Route, InventoryJson.Options);
        Assert.Equal(updatedSupplier, Assert.Single(saved!.Details!.Suppliers));
        Assert.Equal(updatedSupplier.Id, Assert.Single(saved.Details.Movements).SupplierId);
        Assert.Equal(restored.Stock, saved.Stock);

        SetActor(client, Viewer);
        var restricted = await client.GetFromJsonAsync<InventorySnapshot>(Route, InventoryJson.Options);
        Assert.Null(restricted!.Details);
    }

    private static void SetActor(HttpClient client, Guid actor)
    {
        client.DefaultRequestHeaders.Remove("Inventory-Test-Actor");
        client.DefaultRequestHeaders.Add("Inventory-Test-Actor", actor.ToString());
    }

    private static async Async.Task<HttpStatusCode> PostAsync(HttpClient client, InventoryCommand command)
    {
        using var result = await client.PostAsJsonAsync(Route, command, InventoryJson.Options);
        return result.StatusCode;
    }

    private sealed class TestStore(StoredInventory current) : IInventoryStore
    {
        public Async.Task<StoredInventory> ReadAsync(Guid organizationId, CancellationToken cancellationToken)
        {
            Assert.Equal(Organization, organizationId);
            return Async.Task.FromResult(current);
        }

        public Async.Task<bool> TrySaveAsync(Guid organizationId, StoredInventory next, CancellationToken cancellationToken)
        {
            if (next.Revision != current.Revision + 1) return Async.Task.FromResult(false);
            current = next;
            return Async.Task.FromResult(true);
        }
    }

    private sealed class TestAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger, UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Async.Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Guid.TryParse(Request.Headers["Inventory-Test-Actor"].ToString(), out var actor))
                return Async.Task.FromResult(AuthenticateResult.NoResult());
            Context.Items[TaskJwtAuthenticationHandler.AuthenticatedRequestContextItemName] =
                new AuthenticatedRequestContext(actor, Guid.NewGuid(), Organization, 1, 1, "test", "test");
            var identity = new ClaimsIdentity([new(ClaimTypes.Role, actor == Admin ? "administrator" : "employee")], Scheme.Name);
            return Async.Task.FromResult(AuthenticateResult.Success(new(new ClaimsPrincipal(identity), Scheme.Name)));
        }
    }
}
