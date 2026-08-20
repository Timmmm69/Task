using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Task.Api.Auth;
using Task.Api.Security;
using Task.Application.Audit;
using Task.Application.Security;

namespace Task.ServiceHosts.Tests;

public sealed class AuthEndpointsTests
{
    private const string Issuer = "https://task.example.internal";
    private const string Audience = "task-desktop";
    private const string LoginUrl = "/api/v1/auth/login";
    private const string RefreshUrl = "/api/v1/auth/refresh";

    private static readonly Guid OrganizationId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid UserId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid SessionId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid DeviceId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Lazy<string> SigningKeyPath = new(CreateSigningKeyFile);

    private static string CreateSigningKeyFile()
    {
        var keysDirectory = Path.Combine(Path.GetTempPath(), $"task-auth-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(keysDirectory);

        var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var path = Path.Combine(keysDirectory, "test-key.pem");
        File.WriteAllText(path, ecdsa.ExportPkcs8PrivateKeyPem());

        if (!File.Exists(path))
        {
            throw new InvalidOperationException($"Failed to create test signing key file at '{path}'.");
        }

        return path;
    }

    [Fact]
    public async global::System.Threading.Tasks.Task Login_Returns200_WithExpectedTokenShape()
    {
        var sessionRepository = new FakeSessionRepository();
        var deviceStore = new FakeDeviceRegistrationStore();
        var refreshService = new RefreshTokenRotationService(sessionRepository);
        using var server = CreateServer(services =>
        {
            RegisterCommonAuthServices(services, sessionRepository, deviceStore, refreshService);
            services.AddSingleton<IAccountLookupStore>(new FakeAccountLookupStore(ActiveAccount()));
            services.AddSingleton<IPasswordHasher>(new FakePasswordHasher(matches: true));
        });

        var client = server.CreateClient();
        var response = await client.PostAsJsonAsync(LoginUrl, ValidLoginBody());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var document = await ReadJsonAsync(response);
        Assert.True(document.RootElement.TryGetProperty("accessToken", out _));
        Assert.True(document.RootElement.TryGetProperty("accessExpiresAt", out _));
        Assert.True(document.RootElement.TryGetProperty("refreshToken", out _));
        Assert.True(document.RootElement.TryGetProperty("refreshExpiresAt", out _));
        Assert.True(document.RootElement.TryGetProperty("sessionId", out _));
    }

    [Fact]
    public async global::System.Threading.Tasks.Task Login_WithInvalidCredentials_Returns401()
    {
        var sessionRepository = new FakeSessionRepository();
        var deviceStore = new FakeDeviceRegistrationStore();
        var refreshService = new RefreshTokenRotationService(sessionRepository);
        using var server = CreateServer(services =>
        {
            RegisterCommonAuthServices(services, sessionRepository, deviceStore, refreshService);
            services.AddSingleton<IAccountLookupStore>(new FakeAccountLookupStore(null));
            services.AddSingleton<IPasswordHasher>(new FakePasswordHasher(matches: false));
        });

        var client = server.CreateClient();
        var response = await client.PostAsJsonAsync(LoginUrl, ValidLoginBody());

        await AssertProblemAsync(response, HttpStatusCode.Unauthorized, "INVALID_CREDENTIALS");
    }

    [Fact]
    public async global::System.Threading.Tasks.Task Login_WithBlockedAccount_Returns423_AccountBlocked()
    {
        var sessionRepository = new FakeSessionRepository();
        var deviceStore = new FakeDeviceRegistrationStore();
        var refreshService = new RefreshTokenRotationService(sessionRepository);
        using var server = CreateServer(services =>
        {
            RegisterCommonAuthServices(services, sessionRepository, deviceStore, refreshService);
            services.AddSingleton<IAccountLookupStore>(new FakeAccountLookupStore(ActiveAccount()));
            services.AddSingleton<IPasswordHasher>(new FakePasswordHasher(matches: true));
            services.AddSingleton<IAccountLockoutStore>(new FakeAccountLockoutStore(
                new LockoutState(0, AccountLockoutPolicy.BlockedAccountStatus, null, DateTimeOffset.UtcNow)));
        });

        var client = server.CreateClient();
        var response = await client.PostAsJsonAsync(LoginUrl, ValidLoginBody());

        await AssertProblemAsync(response, (HttpStatusCode)423, "ACCOUNT_BLOCKED");
    }

    [Fact]
    public async global::System.Threading.Tasks.Task Login_WithLockedAccount_Returns423_WithRetryAfter()
    {
        var sessionRepository = new FakeSessionRepository();
        var deviceStore = new FakeDeviceRegistrationStore();
        var refreshService = new RefreshTokenRotationService(sessionRepository);
        using var server = CreateServer(services =>
        {
            RegisterCommonAuthServices(services, sessionRepository, deviceStore, refreshService);
            services.AddSingleton<IAccountLookupStore>(new FakeAccountLookupStore(
                new AccountLoginRecord(
                    OrganizationId,
                    UserId,
                    "alice",
                    "hash",
                    "params",
                    1,
                    1,
                    "active",
                    5,
                    DateTimeOffset.UtcNow.AddMinutes(15),
                    DateTimeOffset.UtcNow)));
            services.AddSingleton<IPasswordHasher>(new FakePasswordHasher(matches: true));
            services.AddSingleton<IAccountLockoutStore>(new FakeAccountLockoutStore(
                new LockoutState(5, "active", DateTimeOffset.UtcNow.AddMinutes(15), DateTimeOffset.UtcNow)));
        });

        var client = server.CreateClient();
        var response = await client.PostAsJsonAsync(LoginUrl, ValidLoginBody());

        var document = await AssertProblemAsync(response, (HttpStatusCode)423, "ACCOUNT_LOCKED_TEMPORARILY");
        Assert.True(document.RootElement.TryGetProperty("retryAfterSeconds", out var retryAfter));
        Assert.True(retryAfter.GetInt32() > 0);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task Login_WithRevokedDevice_Returns403_DeviceRevoked()
    {
        var sessionRepository = new FakeSessionRepository();
        var deviceStore = new FakeDeviceRegistrationStore(revoked: true);
        var refreshService = new RefreshTokenRotationService(sessionRepository);
        using var server = CreateServer(services =>
        {
            RegisterCommonAuthServices(services, sessionRepository, deviceStore, refreshService);
            services.AddSingleton<IAccountLookupStore>(new FakeAccountLookupStore(ActiveAccount()));
            services.AddSingleton<IPasswordHasher>(new FakePasswordHasher(matches: true));
        });

        var client = server.CreateClient();
        var response = await client.PostAsJsonAsync(LoginUrl, ValidLoginBody());

        await AssertProblemAsync(response, HttpStatusCode.Forbidden, "DEVICE_REVOKED");
    }

    [Fact]
    public async global::System.Threading.Tasks.Task Login_WithMalformedJson_Returns400_MalformedJson()
    {
        using var server = CreateValidationServer();
        var client = server.CreateClient();
        var content = new StringContent("{ not valid json", Encoding.UTF8, "application/json");
        var response = await client.PostAsync(LoginUrl, content);

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "MALFORMED_JSON");
    }

    [Fact]
    public async global::System.Threading.Tasks.Task Login_WithEmptyPassword_Returns422_ValidationFailed()
    {
        using var server = CreateValidationServer();
        var client = server.CreateClient();
        var response = await client.PostAsJsonAsync(LoginUrl, new
        {
            login = "alice",
            password = "",
            device = new
            {
                deviceKey = "device-key-1234567890",
                deviceName = "Work PC",
                platform = "windows",
                appVersion = "1.0.0",
            },
        });

        await AssertProblemAsync(response, (HttpStatusCode)422, "VALIDATION_FAILED");
    }

    [Fact]
    public async global::System.Threading.Tasks.Task Refresh_Returns200_WithExpectedTokenShape()
    {
        var sessionRepository = new FakeSessionRepository();
        sessionRepository.SeedRefreshToken("known-token", new SessionRefreshLookup(
            OrganizationId,
            SessionId,
            UserId,
            DeviceId,
            1,
            1,
            TokenStatus.Active));

        var deviceStore = new FakeDeviceRegistrationStore();
        var refreshService = new RefreshTokenRotationService(sessionRepository);
        using var server = CreateServer(services =>
        {
            RegisterCommonAuthServices(services, sessionRepository, deviceStore, refreshService);
        });

        var client = server.CreateClient();
        var response = await client.PostAsJsonAsync(RefreshUrl, new
        {
            refreshToken = "known-token",
            deviceKey = "device-key-1234567890",
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var document = await ReadJsonAsync(response);
        Assert.True(document.RootElement.TryGetProperty("accessToken", out _));
        Assert.True(document.RootElement.TryGetProperty("accessExpiresAt", out _));
        Assert.True(document.RootElement.TryGetProperty("refreshToken", out _));
        Assert.True(document.RootElement.TryGetProperty("refreshExpiresAt", out _));
        Assert.True(document.RootElement.TryGetProperty("sessionId", out _));
    }

    [Fact]
    public async global::System.Threading.Tasks.Task Refresh_WithUnknownToken_Returns401_SessionExpired()
    {
        using var server = CreateServer(RegisterRefreshServices);
        var client = server.CreateClient();
        var response = await client.PostAsJsonAsync(RefreshUrl, new
        {
            refreshToken = "unknown-token",
            deviceKey = "device-key-1234567890",
        });

        await AssertProblemAsync(response, HttpStatusCode.Unauthorized, "SESSION_EXPIRED");
    }

    [Fact]
    public async global::System.Threading.Tasks.Task Refresh_WithConsumedToken_Returns401_RefreshTokenReuse()
    {
        var sessionRepository = new FakeSessionRepository();
        sessionRepository.SeedRefreshToken("consumed-token", new SessionRefreshLookup(
            OrganizationId,
            SessionId,
            UserId,
            DeviceId,
            1,
            1,
            TokenStatus.Consumed));
        sessionRepository.ActiveSession = ActiveSession();

        var deviceStore = new FakeDeviceRegistrationStore();
        var refreshService = new RefreshTokenRotationService(sessionRepository);
        using var server = CreateServer(services =>
        {
            RegisterCommonAuthServices(services, sessionRepository, deviceStore, refreshService);
        });

        var client = server.CreateClient();
        var response = await client.PostAsJsonAsync(RefreshUrl, new
        {
            refreshToken = "consumed-token",
            deviceKey = "device-key-1234567890",
        });

        await AssertProblemAsync(response, HttpStatusCode.Unauthorized, "REFRESH_TOKEN_REUSE");
    }

    [Fact]
    public async global::System.Threading.Tasks.Task Refresh_WithRevokedToken_Returns401_SessionRevoked()
    {
        var sessionRepository = new FakeSessionRepository();
        sessionRepository.SeedRefreshToken("revoked-token", new SessionRefreshLookup(
            OrganizationId,
            SessionId,
            UserId,
            DeviceId,
            1,
            1,
            TokenStatus.Revoked));

        var deviceStore = new FakeDeviceRegistrationStore();
        var refreshService = new RefreshTokenRotationService(sessionRepository);
        using var server = CreateServer(services =>
        {
            RegisterCommonAuthServices(services, sessionRepository, deviceStore, refreshService);
        });

        var client = server.CreateClient();
        var response = await client.PostAsJsonAsync(RefreshUrl, new
        {
            refreshToken = "revoked-token",
            deviceKey = "device-key-1234567890",
        });

        await AssertProblemAsync(response, HttpStatusCode.Unauthorized, "SESSION_REVOKED");
    }

    [Fact]
    public async global::System.Threading.Tasks.Task Login_WithoutConfiguredServices_Returns503()
    {
        using var server = CreateEmptyServer();
        var client = server.CreateClient();
        var response = await client.PostAsJsonAsync(LoginUrl, ValidLoginBody());

        await AssertProblemAsync(response, HttpStatusCode.ServiceUnavailable, "INTERNAL_ERROR");
    }

    private static TestServer CreateEmptyServer() => new(new WebHostBuilder()
        .ConfigureServices(services =>
        {
            services.AddRouting();
            services.AddProblemDetails();
        })
        .Configure(app =>
        {
            app.Use(async (context, next) =>
            {
                context.Items[TaskApiProblemResponse.CorrelationIdItemName] = Guid.NewGuid().ToString("D");
                await next();
            });
            app.UseRouting();
            app.UseEndpoints(endpoints => endpoints.MapAuthEndpoints());
        }));

    private static TestServer CreateServer(Action<IServiceCollection>? configure = null) =>
        new(CreateWebHostBuilder(configure));

    private static IWebHostBuilder CreateWebHostBuilder(Action<IServiceCollection>? configure)
    {
        return new WebHostBuilder()
            .ConfigureServices(services =>
            {
                services.AddRouting();
                services.AddProblemDetails();
                configure?.Invoke(services);
            })
            .Configure(app =>
            {
                app.Use(async (context, next) =>
                {
                    context.Items[TaskApiProblemResponse.CorrelationIdItemName] = Guid.NewGuid().ToString("D");
                    await next();
                });
                app.UseRouting();
                app.UseEndpoints(endpoints => endpoints.MapAuthEndpoints());
            });
    }

    private static TestServer CreateValidationServer()
    {
        var sessionRepository = new FakeSessionRepository();
        var deviceStore = new FakeDeviceRegistrationStore();
        var refreshService = new RefreshTokenRotationService(sessionRepository);
        return CreateServer(services =>
        {
            RegisterCommonAuthServices(services, sessionRepository, deviceStore, refreshService);
            services.AddSingleton<IAccountLookupStore>(new FakeAccountLookupStore(ActiveAccount()));
            services.AddSingleton<IPasswordHasher>(new FakePasswordHasher(matches: true));
        });
    }

    private static void RegisterRefreshServices(IServiceCollection services)
    {
        var sessionRepository = new FakeSessionRepository();
        var refreshService = new RefreshTokenRotationService(sessionRepository);
        var deviceStore = new FakeDeviceRegistrationStore();
        RegisterCommonAuthServices(services, sessionRepository, deviceStore, refreshService);
    }

    private static void RegisterCommonAuthServices(
        IServiceCollection services,
        ISessionRepository sessionRepository,
        IDeviceRegistrationStore deviceStore,
        RefreshTokenRotationService refreshService)
    {
        services.AddSingleton<AccountLockoutPolicy>();
        services.AddSingleton<IAccountLockoutStore>(new FakeAccountLockoutStore(
            new LockoutState(0, "active", null, DateTimeOffset.UtcNow)));
        services.AddSingleton<AccountLockoutService>();
        services.AddSingleton(deviceStore);
        services.AddSingleton(sessionRepository);
        services.AddSingleton(refreshService);
        services.AddSingleton<IAuditEntryStore>(new FakeAuditEntryStore());
        services.AddSingleton(new JwtAccessTokenIssuer(Issuer, Audience, $"file:{SigningKeyPath.Value}"));
        services.AddSingleton<LoginService>();
        services.AddSingleton<RefreshService>();
    }

    private static object ValidLoginBody() => new
    {
        login = "alice",
        password = "password",
        device = new
        {
            deviceKey = "device-key-1234567890",
            deviceName = "Work PC",
            platform = "windows",
            appVersion = "1.0.0",
        },
    };

    private static AccountLoginRecord ActiveAccount() => new(
        OrganizationId,
        UserId,
        "alice",
        "hash",
        "params",
        1,
        1,
        "active",
        0,
        null,
        DateTimeOffset.UtcNow);

    private static SessionSnapshot ActiveSession() => new(
        SessionId,
        OrganizationId,
        UserId,
        DeviceId,
        1,
        1,
        DateTimeOffset.UtcNow.AddMinutes(-1),
        DateTimeOffset.UtcNow,
        DateTimeOffset.UtcNow.AddMinutes(15),
        DateTimeOffset.UtcNow.AddDays(1),
        null,
        null);

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

    private sealed class FakeAccountLookupStore : IAccountLookupStore
    {
        private readonly AccountLoginRecord? _record;

        public FakeAccountLookupStore(AccountLoginRecord? record)
        {
            _record = record;
        }

        public global::System.Threading.Tasks.Task<AccountLoginRecord?> FindByLoginAsync(
            string login,
            CancellationToken cancellationToken = default) =>
            global::System.Threading.Tasks.Task.FromResult(_record);
    }

    private sealed class FakePasswordHasher : IPasswordHasher
    {
        private readonly bool _matches;

        public FakePasswordHasher(bool matches)
        {
            _matches = matches;
        }

        public PasswordHashRecord DummyPasswordHash => new("dummy-hash", "{}");

        public PasswordHashRecord HashPassword(string password) => new("hash", "{}");

        public bool VerifyPassword(string password, PasswordHashRecord stored)
        {
            if (stored.Hash == DummyPasswordHash.Hash)
            {
                return false;
            }

            return _matches;
        }
    }

    private sealed class FakeDeviceRegistrationStore : IDeviceRegistrationStore
    {
        private readonly bool _revoked;

        public FakeDeviceRegistrationStore(bool revoked = false)
        {
            _revoked = revoked;
        }

        public global::System.Threading.Tasks.Task<DeviceRegistrationRecord?> GetByIdAsync(
            Guid organizationId,
            Guid deviceId,
            CancellationToken cancellationToken = default) =>
            global::System.Threading.Tasks.Task.FromResult<DeviceRegistrationRecord?>(
                new DeviceRegistrationRecord(DeviceId, UserId, "fp", _revoked ? DateTimeOffset.UtcNow : null));

        public global::System.Threading.Tasks.Task<Guid> UpsertAsync(
            Guid organizationId,
            Guid userId,
            string fingerprintHash,
            string? displayName,
            CancellationToken cancellationToken = default) =>
            global::System.Threading.Tasks.Task.FromResult(DeviceId);
    }

    private sealed class FakeAccountLockoutStore : IAccountLockoutStore
    {
        private readonly LockoutState _state;

        public FakeAccountLockoutStore(LockoutState state)
        {
            _state = state;
        }

        public global::System.Threading.Tasks.Task<LockoutState?> GetLockoutStateAsync(
            Guid organizationId,
            Guid userId,
            CancellationToken cancellationToken = default) =>
            global::System.Threading.Tasks.Task.FromResult<LockoutState?>(_state);

        public global::System.Threading.Tasks.Task<int> RecordFailedLoginAsync(
            Guid organizationId,
            Guid userId,
            int newFailedCount,
            DateTimeOffset? lockedUntilUtcOrNull,
            CancellationToken cancellationToken = default) =>
            global::System.Threading.Tasks.Task.FromResult(newFailedCount);

        public global::System.Threading.Tasks.Task RecordSuccessfulLoginAsync(
            Guid organizationId,
            Guid userId,
            CancellationToken cancellationToken = default) =>
            global::System.Threading.Tasks.Task.CompletedTask;
    }

    private sealed class FakeSessionRepository : ISessionRepository
    {
        private readonly Dictionary<string, SessionRefreshLookup> _refreshLookups = new();

        public SessionSnapshot? ActiveSession { get; set; }

        public void SeedRefreshToken(string token, SessionRefreshLookup lookup)
        {
            _refreshLookups[ComputeHash(token)] = lookup;
        }

        public SessionSnapshot? GetActiveSession(Guid organizationId, Guid sessionId) =>
            ActiveSession;

        public SessionSnapshot? GetSession(Guid organizationId, Guid sessionId) => null;

        public IReadOnlyList<UserSessionListItem> GetUserSessions(Guid organizationId, Guid userId) =>
            Array.Empty<UserSessionListItem>();

        public SessionRefreshLookup? FindSessionByRefreshTokenHash(string tokenHash) =>
            _refreshLookups.TryGetValue(tokenHash, out var lookup) ? lookup : null;

        public SessionRequestState GetSessionRequestState(
            Guid organizationId,
            Guid sessionId,
            long expectedCredentialVersion,
            long expectedAuthorizationScopeVersion) =>
            SessionRequestState.Active;

        public void CreateSession(SessionSnapshot session, RefreshTokenRecord refreshToken)
        {
            ActiveSession = session;
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

        private static string ComputeHash(string rawToken)
        {
            var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(rawToken));
            return Convert.ToHexString(hashBytes).ToLowerInvariant();
        }
    }

    private sealed class FakeAuditEntryStore : IAuditEntryStore
    {
        public global::System.Threading.Tasks.Task AppendAsync(
            AuditEntryRecord entry,
            CancellationToken cancellationToken = default) =>
            global::System.Threading.Tasks.Task.CompletedTask;

        public global::System.Threading.Tasks.Task<AuditPage> ReadAsync(
            AuditQuery query,
            CancellationToken cancellationToken = default) =>
            global::System.Threading.Tasks.Task.FromResult(new AuditPage(Array.Empty<AuditEntryRecord>(), null));
    }
}
