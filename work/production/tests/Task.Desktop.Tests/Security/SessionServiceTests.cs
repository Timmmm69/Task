using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Task.Desktop.Security;

namespace Task.Desktop.Tests;

/// <summary>
/// Tests for <see cref="SessionService"/> against the real <see cref="DesktopAuthApiClient"/>
/// with a fake <see cref="HttpMessageHandler"/> and a vault in a temporary directory: login
/// persistence and scheduling, refresh rotation and failure handling, single-flight refresh
/// and best-effort logout.
/// </summary>
public class SessionServiceTests : IDisposable
{
    private const string BaseUrl = "https://task.local";
    private const string DeviceName = "Work PC";
    private const string AppVersion = "1.0.0";
    private const string OsVersion = "10.0.26100";
    private const string Login = "ivan";
    private const string Password = "S3cr3t-p4ssword!";
    private const string CorrelationId = "019fb732-ad08-7de1-b27d-c86bae8a2937";
    private const string SessionId = "019fa078-3f10-7ec1-99e2-7c1cba4ee3d4";

    private static readonly TimeSpan RefreshMargin = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan DefaultRetryDelay = TimeSpan.FromMinutes(5);

    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "Task.Desktop.Tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    [Fact]
    public async global::System.Threading.Tasks.Task Login_Success_FillsVault_StateSignedIn_AndSchedulesRefresh()
    {
        var handler = new FakeHttpMessageHandler(_ =>
            global::System.Threading.Tasks.Task.FromResult(JsonResponse(HttpStatusCode.OK, TokensJson(RefreshMargin * 2))));
        var vault = new DesktopCredentialVault(_directory);
        using var service = CreateService(handler, vault);
        var result = await service.LoginAsync(Login, Password, CorrelationId, CancellationToken.None);

        Assert.IsType<LoginResult.Succeeded>(result);
        Assert.Equal(SessionAuthState.SignedIn, service.CurrentState);
        Assert.NotNull(service.CurrentSession);
        Assert.NotNull(service.NextRefreshDelay);
        Assert.InRange(service.NextRefreshDelay!.Value, TimeSpan.Zero, RefreshMargin);

        var entry = vault.GetRefreshToken();
        Assert.NotNull(entry);
        Assert.Equal(SessionId, entry.DeviceId);
        Assert.Equal(string.Empty, entry.OrgId);
        Assert.Equal(Login, entry.Login);
        Assert.Equal("RT_9f8e7d6c5b4a3e2d1c0b9a8f7e6d5c4b", entry.RefreshToken);
        Assert.NotNull(entry.DeviceKey);
        Assert.Matches("^[A-Za-z0-9_-]+$", entry.DeviceKey);
        Assert.DoesNotContain("=", entry.DeviceKey);
        Assert.Equal("AT_header.payload.sig", vault.GetAccessToken());
    }

    [Fact]
    public async global::System.Threading.Tasks.Task Login_SessionReportsPasswordChange_SetsCurrentMustChangePassword()
    {
        var handler = new FakeHttpMessageHandler(request =>
            IsSessionRequest(request)
                ? global::System.Threading.Tasks.Task.FromResult(JsonResponse(HttpStatusCode.OK, SessionJson(true)))
                : global::System.Threading.Tasks.Task.FromResult(JsonResponse(HttpStatusCode.OK, TokensJson(RefreshMargin * 2))));
        using var service = CreateService(handler);

        var result = await service.LoginAsync(Login, Password, CorrelationId, CancellationToken.None);

        Assert.IsType<LoginResult.Succeeded>(result);
        Assert.True(service.CurrentMustChangePassword);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task Login_SessionNetworkFailure_DoesNotBlockLogin_AndFailsClosed()
    {
        var handler = new FakeHttpMessageHandler(request =>
        {
            if (IsSessionRequest(request))
            {
                throw new HttpRequestException("connection refused");
            }

            return global::System.Threading.Tasks.Task.FromResult(JsonResponse(HttpStatusCode.OK, TokensJson(RefreshMargin * 2)));
        });
        using var service = CreateService(handler);

        var result = await service.LoginAsync(Login, Password, CorrelationId, CancellationToken.None);

        Assert.IsType<LoginResult.Succeeded>(result);
        Assert.False(service.CurrentMustChangePassword);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task Login_AuthError_StateStaysSignedOut()
    {
        var handler = new FakeHttpMessageHandler(_ =>
            global::System.Threading.Tasks.Task.FromResult(ProblemResponse(HttpStatusCode.Unauthorized, "INVALID_CREDENTIALS")));
        var vault = new DesktopCredentialVault(_directory);
        using var service = CreateService(handler, vault);
        var result = await service.LoginAsync(Login, Password, CorrelationId, CancellationToken.None);

        Assert.IsType<LoginResult.AuthError>(result);
        Assert.Equal(SessionAuthState.SignedOut, service.CurrentState);
        Assert.Null(service.CurrentSession);
        Assert.Null(service.NextRefreshDelay);
        Assert.Null(vault.GetRefreshToken());
    }

    [Fact]
    public async global::System.Threading.Tasks.Task Login_NetworkFailure_StateStaysSignedOut()
    {
        var handler = new FakeHttpMessageHandler(_ => throw new HttpRequestException("connection refused"));
        var vault = new DesktopCredentialVault(_directory);
        using var service = CreateService(handler, vault);
        var result = await service.LoginAsync(Login, Password, CorrelationId, CancellationToken.None);

        Assert.IsType<LoginResult.NetworkFailure>(result);
        Assert.Equal(SessionAuthState.SignedOut, service.CurrentState);
        Assert.Null(service.NextRefreshDelay);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task Refresh_Success_UpdatesVaultAndReschedules()
    {
        var refreshCount = 0;
        var handler = new FakeHttpMessageHandler(request =>
        {
            if (IsLoginRequest(request))
            {
                return global::System.Threading.Tasks.Task.FromResult(JsonResponse(HttpStatusCode.OK, TokensJson(RefreshMargin * 2, accessToken: "AT_first")));
            }

            if (IsSessionRequest(request))
            {
                return global::System.Threading.Tasks.Task.FromResult(JsonResponse(HttpStatusCode.OK, SessionJson(false)));
            }

            refreshCount++;
            return global::System.Threading.Tasks.Task.FromResult(JsonResponse(
                HttpStatusCode.OK,
                TokensJson(RefreshMargin * 2, accessToken: "AT_rotated", refreshToken: "RT_rotated")));
        });
        var vault = new DesktopCredentialVault(_directory);
        using var service = CreateService(handler, vault);
        await service.LoginAsync(Login, Password, CorrelationId, CancellationToken.None);

        var result = await service.RefreshAsync();

        var succeeded = Assert.IsType<RefreshResult.Succeeded>(result);
        Assert.Equal("AT_rotated", succeeded.Tokens.AccessToken);
        Assert.Equal(1, refreshCount);
        Assert.Equal(SessionAuthState.SignedIn, service.CurrentState);
        Assert.Equal("AT_rotated", vault.GetAccessToken());
        var entry = vault.GetRefreshToken();
        Assert.NotNull(entry);
        Assert.Equal("RT_rotated", entry.RefreshToken);
        Assert.NotNull(service.NextRefreshDelay);
        Assert.InRange(service.NextRefreshDelay!.Value, TimeSpan.Zero, RefreshMargin);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task Refresh_SessionExpired_SignsOutAndClearsVault()
    {
        var handler = new FakeHttpMessageHandler(request =>
            IsLoginRequest(request)
                ? global::System.Threading.Tasks.Task.FromResult(JsonResponse(HttpStatusCode.OK, TokensJson(RefreshMargin * 2)))
                : global::System.Threading.Tasks.Task.FromResult(ProblemResponse(HttpStatusCode.Unauthorized, "SESSION_EXPIRED")));
        var vault = new DesktopCredentialVault(_directory);
        using var service = CreateService(handler, vault);
        await service.LoginAsync(Login, Password, CorrelationId, CancellationToken.None);

        var result = await service.RefreshAsync();

        var authError = Assert.IsType<RefreshResult.AuthError>(result);
        Assert.Equal(AuthProblemCode.SessionExpired, authError.Error.ProblemCode);
        Assert.Equal(SessionAuthState.SignedOut, service.CurrentState);
        Assert.Null(service.CurrentSession);
        Assert.Null(service.NextRefreshDelay);
        Assert.Null(vault.GetRefreshToken());
        Assert.Null(vault.GetAccessToken());
    }

    [Fact]
    public async global::System.Threading.Tasks.Task Refresh_RefreshTokenReuse_SignsOut()
    {
        var handler = new FakeHttpMessageHandler(request =>
            IsLoginRequest(request)
                ? global::System.Threading.Tasks.Task.FromResult(JsonResponse(HttpStatusCode.OK, TokensJson(RefreshMargin * 2)))
                : global::System.Threading.Tasks.Task.FromResult(ProblemResponse(HttpStatusCode.Unauthorized, "REFRESH_TOKEN_REUSE")));
        var vault = new DesktopCredentialVault(_directory);
        using var service = CreateService(handler, vault);
        await service.LoginAsync(Login, Password, CorrelationId, CancellationToken.None);

        await service.RefreshAsync();

        Assert.Equal(SessionAuthState.SignedOut, service.CurrentState);
        Assert.Null(vault.GetRefreshToken());
    }

    [Fact]
    public async global::System.Threading.Tasks.Task Refresh_RateLimited_StaysSignedIn_AndSchedulesRetry()
    {
        var retryDelay = TimeSpan.FromMinutes(1);
        var handler = new FakeHttpMessageHandler(request =>
            IsLoginRequest(request)
                ? global::System.Threading.Tasks.Task.FromResult(JsonResponse(HttpStatusCode.OK, TokensJson(RefreshMargin * 2, accessToken: "AT_first")))
                : global::System.Threading.Tasks.Task.FromResult(ProblemResponse(HttpStatusCode.TooManyRequests, "RATE_LIMITED", retryAfterSeconds: 30)));
        var vault = new DesktopCredentialVault(_directory);
        using var service = CreateService(handler, vault, retryDelay);
        await service.LoginAsync(Login, Password, CorrelationId, CancellationToken.None);

        var result = await service.RefreshAsync();

        Assert.IsType<RefreshResult.AuthError>(result);
        Assert.Equal(SessionAuthState.SignedIn, service.CurrentState);
        Assert.Equal(retryDelay, service.NextRefreshDelay);
        Assert.Equal("AT_first", vault.GetAccessToken());
        Assert.NotNull(vault.GetRefreshToken());
    }

    [Fact]
    public async global::System.Threading.Tasks.Task Refresh_NetworkFailure_StaysSignedIn_AndSchedulesRetry()
    {
        var retryDelay = TimeSpan.FromMinutes(1);
        var handler = new FakeHttpMessageHandler(request =>
            IsLoginRequest(request)
                ? global::System.Threading.Tasks.Task.FromResult(JsonResponse(HttpStatusCode.OK, TokensJson(RefreshMargin * 2)))
                : throw new HttpRequestException("connection refused"));
        var vault = new DesktopCredentialVault(_directory);
        using var service = CreateService(handler, vault, retryDelay);
        await service.LoginAsync(Login, Password, CorrelationId, CancellationToken.None);

        var result = await service.RefreshAsync();

        Assert.IsType<RefreshResult.NetworkFailure>(result);
        Assert.Equal(SessionAuthState.SignedIn, service.CurrentState);
        Assert.Equal(retryDelay, service.NextRefreshDelay);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task Refresh_ConcurrentCalls_MakeSingleRequest()
    {
        var refreshCount = 0;
        var handler = new FakeHttpMessageHandler(async request =>
        {
            if (IsLoginRequest(request))
            {
                return JsonResponse(HttpStatusCode.OK, TokensJson(RefreshMargin * 2));
            }

            if (IsSessionRequest(request))
            {
                return JsonResponse(HttpStatusCode.OK, SessionJson(false));
            }

            refreshCount++;
            await global::System.Threading.Tasks.Task.Delay(200);
            return JsonResponse(HttpStatusCode.OK, TokensJson(RefreshMargin * 2, accessToken: "AT_rotated"));
        });
        var vault = new DesktopCredentialVault(_directory);
        using var service = CreateService(handler, vault);
        await service.LoginAsync(Login, Password, CorrelationId, CancellationToken.None);

        var first = service.RefreshAsync();
        var second = service.RefreshAsync();
        var results = await global::System.Threading.Tasks.Task.WhenAll(first, second);

        Assert.Equal(1, refreshCount);
        Assert.IsType<RefreshResult.Succeeded>(results[0]);
        Assert.IsType<RefreshResult.Succeeded>(results[1]);
        Assert.Equal("AT_rotated", vault.GetAccessToken());
    }

    [Fact]
    public async global::System.Threading.Tasks.Task Refresh_NoStoredSession_SignsOut()
    {
        using var service = CreateService(new FakeHttpMessageHandler(
            _ => throw new InvalidOperationException("no request expected")));

        var result = await service.RefreshAsync();

        var authError = Assert.IsType<RefreshResult.AuthError>(result);
        Assert.Equal(AuthProblemCode.SessionExpired, authError.Error.ProblemCode);
        Assert.Equal(SessionAuthState.SignedOut, service.CurrentState);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task Refresh_StoredEntryWithoutDeviceKey_SignsOut()
    {
        // The vault rejects entries without a device key at read time (fail-closed), so the
        // service observes them as "no usable stored session" and signs out without a request.
        Directory.CreateDirectory(_directory);
        var entry = new RefreshTokenEntry(
            "device-1", string.Empty, Login, null, "RT_token", DateTime.UtcNow, Version: 2);
        File.WriteAllBytes(
            Path.Combine(_directory, "credentials.bin"),
            ProtectedData.Protect(
                JsonSerializer.SerializeToUtf8Bytes(entry),
                optionalEntropy: null,
                DataProtectionScope.CurrentUser));

        using var service = CreateService(new FakeHttpMessageHandler(
            _ => throw new InvalidOperationException("no request expected")));

        var result = await service.RefreshAsync();

        Assert.IsType<RefreshResult.AuthError>(result);
        Assert.Equal(SessionAuthState.SignedOut, service.CurrentState);
        Assert.False(File.Exists(Path.Combine(_directory, "credentials.bin")));
    }

    [Fact]
    public async global::System.Threading.Tasks.Task Login_ReusesExistingDeviceKeyFromVault()
    {
        var capturedKeys = new System.Collections.Concurrent.ConcurrentQueue<string>();
        var handler = new FakeHttpMessageHandler(async request =>
        {
            if (IsSessionRequest(request))
            {
                return JsonResponse(HttpStatusCode.OK, SessionJson(false));
            }

            using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            capturedKeys.Enqueue(json.RootElement.GetProperty("device").GetProperty("deviceKey").GetString()!);
            return JsonResponse(HttpStatusCode.OK, TokensJson(RefreshMargin * 2));
        });
        var vault = new DesktopCredentialVault(_directory);
        using var service = CreateService(handler, vault);
        await service.LoginAsync(Login, Password, CorrelationId, CancellationToken.None);
        await service.LoginAsync(Login, Password, CorrelationId, CancellationToken.None);

        Assert.Equal(2, capturedKeys.Count);
        var keys = capturedKeys.ToArray();
        Assert.False(string.IsNullOrWhiteSpace(keys[0]));
        Assert.Equal(keys[0], keys[1]);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task Logout_ClearsVault_AndSignsOut()
    {
        var logoutCount = 0;
        var handler = new FakeHttpMessageHandler(request =>
        {
            if (IsLoginRequest(request))
            {
                return global::System.Threading.Tasks.Task.FromResult(JsonResponse(HttpStatusCode.OK, TokensJson(RefreshMargin * 2)));
            }

            if (IsSessionRequest(request))
            {
                return global::System.Threading.Tasks.Task.FromResult(JsonResponse(HttpStatusCode.OK, SessionJson(true)));
            }

            logoutCount++;
            return global::System.Threading.Tasks.Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
        });
        var vault = new DesktopCredentialVault(_directory);
        using var service = CreateService(handler, vault);
        await service.LoginAsync(Login, Password, CorrelationId, CancellationToken.None);
        Assert.True(service.CurrentMustChangePassword);

        var result = await service.LogoutAsync();

        Assert.IsType<LogoutResult.Succeeded>(result);
        Assert.Equal(1, logoutCount);
        Assert.Equal(SessionAuthState.SignedOut, service.CurrentState);
        Assert.Null(service.CurrentSession);
        Assert.Null(service.NextRefreshDelay);
        Assert.Null(vault.GetRefreshToken());
        Assert.Null(vault.GetAccessToken());
        Assert.Empty(Directory.GetFiles(_directory));
        Assert.False(service.CurrentMustChangePassword);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task Logout_NetworkFailure_StillSignsOutLocally()
    {
        var handler = new FakeHttpMessageHandler(request =>
            IsLoginRequest(request)
                ? global::System.Threading.Tasks.Task.FromResult(JsonResponse(HttpStatusCode.OK, TokensJson(RefreshMargin * 2)))
                : throw new HttpRequestException("connection refused"));
        var vault = new DesktopCredentialVault(_directory);
        using var service = CreateService(handler, vault);
        await service.LoginAsync(Login, Password, CorrelationId, CancellationToken.None);

        var result = await service.LogoutAsync();

        Assert.IsType<LogoutResult.NetworkFailure>(result);
        Assert.Equal(SessionAuthState.SignedOut, service.CurrentState);
        Assert.Null(vault.GetRefreshToken());
    }

    [Fact]
    public async global::System.Threading.Tasks.Task Logout_WithoutAccessToken_SucceedsLocally()
    {
        using var service = CreateService(new FakeHttpMessageHandler(
            _ => throw new InvalidOperationException("no request expected")));

        var result = await service.LogoutAsync();

        Assert.IsType<LogoutResult.Succeeded>(result);
        Assert.Equal(SessionAuthState.SignedOut, service.CurrentState);
    }

private SessionService CreateService(
        FakeHttpMessageHandler handler,
        DesktopCredentialVault? vault = null,
        TimeSpan? retryDelay = null)
    {
        vault ??= new DesktopCredentialVault(_directory);
        var client = new DesktopAuthApiClient(new HttpClient(handler), BaseUrl);
        return new SessionService(
            client,
            vault,
            DeviceName,
            ClientPlatform.Windows,
            AppVersion,
            OsVersion,
            RefreshMargin,
            deviceKeyByteLength: 32,
            retryDelay: retryDelay ?? DefaultRetryDelay);
    }

    private static bool IsLoginRequest(HttpRequestMessage request) =>
        request.RequestUri?.AbsolutePath.EndsWith("/api/v1/auth/login", StringComparison.Ordinal) == true;

    private static bool IsSessionRequest(HttpRequestMessage request) =>
        request.RequestUri?.AbsolutePath.EndsWith("/api/v1/auth/session", StringComparison.Ordinal) == true;

    private static string SessionJson(bool mustChangePassword) =>
        $$"""{"userId":"{{SessionId}}","sessionId":"{{SessionId}}","organizationId":"019fb732-ad08-7de1-b27d-c86bae8a2937","credentialVersion":1,"authorizationScopeVersion":1,"mustChangePassword":{{mustChangePassword.ToString().ToLowerInvariant()}}}""";

    private static string TokensJson(
        TimeSpan accessLifetime,
        string accessToken = "AT_header.payload.sig",
        string refreshToken = "RT_9f8e7d6c5b4a3e2d1c0b9a8f7e6d5c4b")
    {
        var accessExpiresAt = DateTimeOffset.UtcNow.Add(accessLifetime).ToString("O");
        var refreshExpiresAt = DateTimeOffset.UtcNow.AddDays(30).ToString("O");
        return $$"""{"accessToken":"{{accessToken}}","accessExpiresAt":"{{accessExpiresAt}}","refreshToken":"{{refreshToken}}","refreshExpiresAt":"{{refreshExpiresAt}}","sessionId":"{{SessionId}}"}""";
    }

    private static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, string json) =>
        new(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };

    private static HttpResponseMessage ProblemResponse(
        HttpStatusCode statusCode,
        string code,
        int? retryAfterSeconds = null)
    {
        var retry = retryAfterSeconds is null ? string.Empty : $",\"retryAfterSeconds\":{retryAfterSeconds}";
        var json =
            $$"""{"type":"https://task.local/errors/{{code.ToLowerInvariant()}}","title":"Error","status":{{(int)statusCode}},"code":"{{code}}","traceId":"t-1","correlationId":"{{CorrelationId}}","fieldErrors":[]{{retry}}}""";
        return new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/problem+json"),
        };
    }

    /// <summary>Fake transport: answers from a responder function.</summary>
    private sealed class FakeHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, global::System.Threading.Tasks.Task<HttpResponseMessage>> _respond;

        public FakeHttpMessageHandler(Func<HttpRequestMessage, global::System.Threading.Tasks.Task<HttpResponseMessage>> respond)
        {
            _respond = respond;
        }

        protected override global::System.Threading.Tasks.Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => _respond(request);
    }
}
