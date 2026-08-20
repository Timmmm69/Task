using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Task.Api.Capabilities;
using Task.Api.Security;
using Task.Application.Security;
using Task.Application.Server;

namespace Task.ServiceHosts.Tests;

public sealed class CapabilitiesEndpointsTests
{
    private const string Issuer = "https://task.example.internal";
    private const string Audience = "task-desktop";
    private const string CapabilitiesUrl = "/api/v1/capabilities";

    private static readonly Guid OrganizationId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid UserId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid SessionId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static readonly Lazy<TestKeyMaterial> KeyMaterial = new(CreateKeyMaterial);

    private sealed record TestKeyMaterial(string PrivateKeyPath, string VerificationKeysDirectory);

    private static TestKeyMaterial CreateKeyMaterial()
    {
        var baseDirectory = Path.Combine(Path.GetTempPath(), $"task-capabilities-tests-{Guid.NewGuid():N}");
        var signingDirectory = Path.Combine(baseDirectory, "signing");
        var verificationDirectory = Path.Combine(baseDirectory, "verification");
        Directory.CreateDirectory(signingDirectory);
        Directory.CreateDirectory(verificationDirectory);

        var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var privateKeyPath = Path.Combine(signingDirectory, "task-signing.pem");
        File.WriteAllText(privateKeyPath, ecdsa.ExportPkcs8PrivateKeyPem());
        File.WriteAllText(
            Path.Combine(verificationDirectory, "task-signing.pem"),
            ecdsa.ExportSubjectPublicKeyInfoPem());

        return new TestKeyMaterial(privateKeyPath, verificationDirectory);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task GetCapabilities_WithoutToken_Returns401()
    {
        using var server = CreateServer();
        var client = server.CreateClient();

        var response = await client.GetAsync(CapabilitiesUrl);

        await AssertProblemAsync(response, HttpStatusCode.Unauthorized, "AUTHENTICATION_REQUIRED");
    }

    [Fact]
    public async global::System.Threading.Tasks.Task GetCapabilities_WithValidToken_Returns200_WithExpectedFields()
    {
        using var server = CreateServer();
        using var client = await CreateAuthenticatedClientAsync(server);

        var response = await client.GetAsync(CapabilitiesUrl);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var document = await ReadJsonAsync(response);
        var apiVersions = document.RootElement.GetProperty("apiVersions").EnumerateArray()
            .Select(element => element.GetString()).ToArray();
        Assert.Equal(new[] { "v1" }, apiVersions);
        Assert.Equal("1.0.0", document.RootElement.GetProperty("minimumClientVersion").GetString());
        Assert.Equal("1.0.0", document.RootElement.GetProperty("recommendedClientVersion").GetString());
        Assert.Equal(1, document.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.Empty(document.RootElement.GetProperty("featureFlags").EnumerateArray());
    }

    [Fact]
    public async global::System.Threading.Tasks.Task GetCapabilities_ServiceNotRegistered_Returns503()
    {
        using var server = CreateServerWithoutCapabilitiesService();
        using var client = await CreateAuthenticatedClientAsync(server);

        var response = await client.GetAsync(CapabilitiesUrl);

        await AssertProblemAsync(response, HttpStatusCode.ServiceUnavailable, "INTERNAL_ERROR");
    }

    private static TestServer CreateServer()
    {
        var keyMaterial = KeyMaterial.Value;
        return new TestServer(new WebHostBuilder()
            .ConfigureServices(services =>
            {
                services.AddRouting();
                services.AddProblemDetails();
                services.AddTaskApiSecurityFoundation();
                services.AddSingleton<IOptions<TaskIdentityFoundationOptions>>(
                    new OptionsWrapper<TaskIdentityFoundationOptions>(new TaskIdentityFoundationOptions
                    {
                        Issuer = Issuer,
                        Audience = Audience,
                        SigningKeyReference = $"file:{keyMaterial.PrivateKeyPath}",
                        PepperReference = "file:/run/secrets/task-pepper",
                        VerificationKeysDirectory = $"file:{keyMaterial.VerificationKeysDirectory}",
                    }));
                services.AddSingleton<ISessionRepository>(new FakeSessionRepository());
                services.AddSingleton(
                    new JwtAccessTokenIssuer(Issuer, Audience, $"file:{keyMaterial.PrivateKeyPath}"));
                services.AddSingleton<ServerCapabilitiesService>();
            })
            .Configure(app =>
            {
                app.Use(async (context, next) =>
                {
                    context.Items[TaskApiProblemResponse.CorrelationIdItemName] = Guid.NewGuid().ToString("D");
                    await next();
                });
                app.UseRouting();
                app.UseAuthentication();
                app.UseAuthorization();
                app.UseEndpoints(endpoints => endpoints.MapCapabilitiesEndpoints());
            }));
    }

    private static TestServer CreateServerWithoutCapabilitiesService()
    {
        var keyMaterial = KeyMaterial.Value;
        return new TestServer(new WebHostBuilder()
            .ConfigureServices(services =>
            {
                services.AddRouting();
                services.AddProblemDetails();
                services.AddTaskApiSecurityFoundation();
                services.AddSingleton<IOptions<TaskIdentityFoundationOptions>>(
                    new OptionsWrapper<TaskIdentityFoundationOptions>(new TaskIdentityFoundationOptions
                    {
                        Issuer = Issuer,
                        Audience = Audience,
                        SigningKeyReference = $"file:{keyMaterial.PrivateKeyPath}",
                        PepperReference = "file:/run/secrets/task-pepper",
                        VerificationKeysDirectory = $"file:{keyMaterial.VerificationKeysDirectory}",
                    }));
                services.AddSingleton<ISessionRepository>(new FakeSessionRepository());
                services.AddSingleton(
                    new JwtAccessTokenIssuer(Issuer, Audience, $"file:{keyMaterial.PrivateKeyPath}"));
            })
            .Configure(app =>
            {
                app.Use(async (context, next) =>
                {
                    context.Items[TaskApiProblemResponse.CorrelationIdItemName] = Guid.NewGuid().ToString("D");
                    await next();
                });
                app.UseRouting();
                app.UseAuthentication();
                app.UseAuthorization();
                app.UseEndpoints(endpoints => endpoints.MapCapabilitiesEndpoints());
            }));
    }

    private static async Task<HttpClient> CreateAuthenticatedClientAsync(TestServer server)
    {
        var client = server.CreateClient();
        var issuer = server.Host.Services.GetRequiredService<JwtAccessTokenIssuer>();
        var token = await issuer.IssueAsync(
            new JwtIssuanceRequest(UserId, SessionId, OrganizationId, 1, 1),
            CancellationToken.None);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(body);
    }

    private static async Task<JsonDocument> AssertProblemAsync(
        HttpResponseMessage response,
        HttpStatusCode expectedStatus,
        string expectedCode)
    {
        Assert.Equal(expectedStatus, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var document = await ReadJsonAsync(response);
        Assert.Equal(expectedCode, document.RootElement.GetProperty("code").GetString());
        return document;
    }

    private sealed class FakeSessionRepository : ISessionRepository
    {
        public SessionSnapshot? GetActiveSession(Guid organizationId, Guid sessionId) => null;

        public SessionSnapshot? GetSession(Guid organizationId, Guid sessionId) => null;

        public IReadOnlyList<UserSessionListItem> GetUserSessions(Guid organizationId, Guid userId) =>
            Array.Empty<UserSessionListItem>();

        public SessionRefreshLookup? FindSessionByRefreshTokenHash(string tokenHash) => null;

        public SessionRequestState GetSessionRequestState(
            Guid organizationId,
            Guid sessionId,
            long expectedCredentialVersion,
            long expectedAuthorizationScopeVersion) =>
            SessionRequestState.Active;

        public void CreateSession(SessionSnapshot session, RefreshTokenRecord refreshToken)
        {
        }

        public bool RotateRefreshToken(
            Guid organizationId,
            Guid sessionId,
            string consumedTokenHash,
            RefreshTokenRecord newRefreshToken) =>
            true;

        public void TouchSession(Guid organizationId, Guid sessionId)
        {
        }

        public void RevokeSession(Guid organizationId, Guid sessionId, string? reason)
        {
        }

        public int RevokeAllUserSessions(Guid organizationId, Guid userId, Guid? exceptSessionId, string? reason) =>
            0;

        public global::System.Threading.Tasks.Task<int> RevokeAllUserSessionsExceptAsync(
            Guid organizationId,
            Guid userId,
            Guid? exceptSessionId,
            CancellationToken cancellationToken = default) =>
            global::System.Threading.Tasks.Task.FromResult(0);

        public global::System.Threading.Tasks.Task<int> PurgeExpiredRefreshTokensAsync(
            DateTimeOffset olderThanUtc,
            int maxCount,
            CancellationToken cancellationToken = default) =>
            global::System.Threading.Tasks.Task.FromResult(0);

        public global::System.Threading.Tasks.Task<int> PurgeExpiredSessionsAsync(
            DateTimeOffset olderThanUtc,
            int maxCount,
            CancellationToken cancellationToken = default) =>
            global::System.Threading.Tasks.Task.FromResult(0);
    }
}
