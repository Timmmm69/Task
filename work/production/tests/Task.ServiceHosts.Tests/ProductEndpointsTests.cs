using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Task.Api.ProductData;
using Task.Api.Security;
using Task.Application.ProductData;
using Task.Application.Security;

namespace Task.ServiceHosts.Tests;

public sealed class ProductEndpointsTests
{
    private static readonly Guid Organization = Guid.NewGuid();
    private static readonly Guid User = Guid.NewGuid();
    private static readonly Guid Session = Guid.NewGuid();
    private static readonly Guid ObjectId = Guid.NewGuid();
    public static IEnumerable<object[]> Routes => ProductApiRoutes.All.Select(route => new object[] { route.Method, route.Path });

    [Theory]
    [MemberData(nameof(Routes))]
    public async System.Threading.Tasks.Task EveryRouteDeniesMissingPermissionBeforeStore(string method, string path)
    {
        var store = new RecordingStore();
        using var server = Server(store, permitted: false);
        using var client = server.CreateClient();
        var response = await client.SendAsync(Request(method, path.Replace("{id}", ObjectId.ToString()).Replace("{childId}", Guid.NewGuid().ToString()), "{}"));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Null(store.LastRequest);
    }

    [Theory]
    [InlineData(null, 428)]
    [InlineData("*", 400)]
    [InlineData("W/\"v1\"", 400)]
    [InlineData("\"v01\"", 400)]
    [InlineData("\"v1\",\"v2\"", 400)]
    [InlineData("\"v0\"", 400)]
    public async System.Threading.Tasks.Task StrictIfMatchRejectsInvalidHeaders(string? etag, int expected)
    {
        var store = new RecordingStore(); using var server = Server(store); using var client = server.CreateClient();
        using var request = Request("PATCH", $"/api/v1/projects/{ObjectId}", "{\"name\":\"Changed\"}");
        if (etag is not null) request.Headers.TryAddWithoutValidation("If-Match", etag);
        var response = await client.SendAsync(request);
        Assert.Equal(expected, (int)response.StatusCode); Assert.Null(store.LastRequest);
    }

    [Theory]
    [InlineData("{\"name\":\"a\",\"name\":\"b\"}", 400)]
    [InlineData("[]", 400)]
    [InlineData("null", 400)]
    [InlineData("{", 400)]
    [InlineData("{}", 422)]
    public async System.Threading.Tasks.Task InvalidBodiesNeverReachStore(string body, int status)
    {
        var store = new RecordingStore(); using var server = Server(store); using var client = server.CreateClient();
        using var request = Request("POST", "/api/v1/contacts", body);
        request.Headers.Add("Idempotency-Key", "valid-create-001");
        Assert.Equal(status, (int)(await client.SendAsync(request)).StatusCode); Assert.Null(store.LastRequest);
    }

    [Fact]
    public async System.Threading.Tasks.Task ResponseAndIdentityAreForwardedWithEtag()
    {
        var store = new RecordingStore(); using var server = Server(store); using var client = server.CreateClient();
        using var request = Request("PATCH", $"/api/v1/projects/{ObjectId}", "{\"name\":\"Changed\"}");
        request.Headers.TryAddWithoutValidation("If-Match", "\"v1\"");
        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode); Assert.Equal("\"v2\"", response.Headers.ETag?.Tag);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("saved", json.RootElement.GetProperty("name").GetString());
        Assert.Equal(Organization, store.LastRequest!.OrganizationId); Assert.Equal(User, store.LastRequest.UserId);
        Assert.Equal(Session, store.LastRequest.SessionId); Assert.Equal(1, store.LastRequest.ExpectedVersion);
    }

    [Fact]
    public async System.Threading.Tasks.Task MissingKeyAndOversizedPayloadAreRejected()
    {
        var store = new RecordingStore(); using var server = Server(store); using var client = server.CreateClient();
        Assert.Equal(HttpStatusCode.BadRequest, (await client.SendAsync(Request("POST", "/api/v1/contacts", "{\"firstName\":\"A\"}"))).StatusCode);
        using var request = Request("POST", "/api/v1/contacts", "{\"notes\":\"" + new string('x', 1048576) + "\"}");
        request.Headers.Add("Idempotency-Key", "large-request-001");
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, (await client.SendAsync(request)).StatusCode);
        Assert.Null(store.LastRequest);
    }

    [Theory]
    [InlineData(404, "OBJECT_NOT_VISIBLE")]
    [InlineData(412, "VERSION_CONFLICT")]
    [InlineData(409, "IDEMPOTENCY_KEY_REUSED")]
    [InlineData(422, "VALIDATION_FAILED")]
    public async System.Threading.Tasks.Task DomainErrorsUseProblemEnvelope(int status, string code)
    {
        using var server = Server(new RecordingStore(new ProductApiException(status, code, "Safe message")));
        using var client = server.CreateClient(); var response = await client.GetAsync("/api/v1/projects");
        Assert.Equal(status, (int)response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(code, json.RootElement.GetProperty("code").GetString());
        Assert.True(json.RootElement.TryGetProperty("correlationId", out _));
    }

    [Fact]
    public async System.Threading.Tasks.Task InfrastructureErrorsDoNotDiscloseInternals()
    {
        using var server = Server(new RecordingStore(new InvalidOperationException("password=hidden; SELECT secret")));
        using var client = server.CreateClient(); var response = await client.GetAsync("/api/v1/projects");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.DoesNotContain("hidden", await response.Content.ReadAsStringAsync());
    }

    private static HttpRequestMessage Request(string method, string url, string body) => new(new HttpMethod(method), url)
    { Content = new StringContent(body, Encoding.UTF8, "application/json") };

#pragma warning disable ASPDEPR004 // Match the established service-host TestServer harness.
    private static TestServer Server(RecordingStore store, bool permitted = true) => new(new WebHostBuilder()
        .ConfigureServices(services =>
        {
            services.AddRouting(); services.AddAuthorization(); services.AddLogging();
            services.AddSingleton<IProductApiStore>(store);
            services.AddSingleton<IAuthorizationPolicyStore>(new Policy(permitted)); services.AddSingleton<PermissionDecisionService>();
        })
        .Configure(app =>
        {
            app.UseRouting();
            app.Use(async (context, next) =>
            {
                context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, User.ToString())], "test"));
                context.Items[TaskJwtAuthenticationHandler.AuthenticatedRequestContextItemName] = new AuthenticatedRequestContext(User, Session, Organization, 1, 1, Guid.NewGuid().ToString(), "test");
                await next(context);
            });
            app.UseAuthorization(); app.UseEndpoints(endpoints => endpoints.MapProductEndpoints());
        }));
#pragma warning restore ASPDEPR004

    private sealed class RecordingStore(Exception? failure = null) : IProductApiStore
    {
        public ProductApiRequest? LastRequest { get; private set; }
        public ProductApiResponse Execute(ProductApiRequest request)
        { LastRequest = request; if (failure is not null) throw failure; return new(new JsonObject { ["name"] = "saved" }, Version: 2); }
    }
    private sealed class Policy(bool allowed) : IAuthorizationPolicyStore
    {
        public Task<Guid?> GetUserOrgAsync(Guid userId, CancellationToken cancellationToken = default) => System.Threading.Tasks.Task.FromResult<Guid?>(Organization);
        public Task<IReadOnlyList<PolicyGrantRow>> GetUserGrantsAsync(Guid orgId, Guid userId, string permissionCode, CancellationToken cancellationToken = default) =>
            System.Threading.Tasks.Task.FromResult<IReadOnlyList<PolicyGrantRow>>(allowed ? [new(true)] : []);
        public Task<IReadOnlyList<PolicyDenyRow>> GetUserDeniesAsync(Guid orgId, Guid userId, string permissionCode, CancellationToken cancellationToken = default) =>
            System.Threading.Tasks.Task.FromResult<IReadOnlyList<PolicyDenyRow>>([]);
    }
}
