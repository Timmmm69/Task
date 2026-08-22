using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Task.Desktop.Security;

namespace Task.Desktop.Tests;

/// <summary>
/// Tests for <see cref="DesktopAuthApiClient"/> against a fake <see cref="HttpMessageHandler"/>:
/// contract requests (paths, methods, headers, serialized bodies), response mapping for
/// success / problem codes / network failures / timeouts / malformed bodies, and guaranteed
/// absence of the password outside the JSON body.
/// </summary>
public class DesktopAuthApiClientTests
{
    private const string BaseUrl = "https://task.local";
    private const string CorrelationId = "019fb732-ad08-7de1-b27d-c86bae8a2937";
    private const string SessionId = "019fa078-3f10-7ec1-99e2-7c1cba4ee3d4";
    private const string DeviceKey = "dvc-019fb732-ad08-7de1-b27d";
    private const string DeviceName = "Work PC";
    private const string AppVersion = "1.0.0";
    private const string OsVersion = "10.0.26100";

    private static readonly DeviceRegistrationInfo DeviceInfo =
        new(DeviceKey, DeviceName, ClientPlatform.Windows, AppVersion, OsVersion);

    private const string SessionTokensJson =
        """{"accessToken":"AT_header.payload.sig","accessExpiresAt":"2026-08-19T12:00:00Z","refreshToken":"RT_9f8e7d6c5b4a3e2d1c0b9a8f7e6d5c4b","refreshExpiresAt":"2026-09-19T12:00:00Z","sessionId":"019fa078-3f10-7ec1-99e2-7c1cba4ee3d4"}""";

    [Fact]
    public async global::System.Threading.Tasks.Task Login_200_ParsesFullResponse_AndSendsContractRequest()
    {
        const string password = "S3cr3t-p4ssword!";
        CapturedRequest? captured = null;
        var handler = new FakeHttpMessageHandler(async request =>
        {
            captured = await CaptureAsync(request);
            return JsonResponse(HttpStatusCode.OK, SessionTokensJson);
        });
        var client = new DesktopAuthApiClient(new HttpClient(handler), BaseUrl);

        var result = await client.LoginAsync("ivan", password, DeviceInfo, CorrelationId, CancellationToken.None);

        var succeeded = Assert.IsType<LoginResult.Succeeded>(result);
        Assert.Equal("AT_header.payload.sig", succeeded.Tokens.AccessToken);
        Assert.Equal(new DateTimeOffset(2026, 8, 19, 12, 0, 0, TimeSpan.Zero), succeeded.Tokens.AccessExpiresAt);
        Assert.Equal("RT_9f8e7d6c5b4a3e2d1c0b9a8f7e6d5c4b", succeeded.Tokens.RefreshToken);
        Assert.Equal(new DateTimeOffset(2026, 9, 19, 12, 0, 0, TimeSpan.Zero), succeeded.Tokens.RefreshExpiresAt);
        Assert.Equal(Guid.Parse(SessionId), succeeded.Tokens.SessionId);

        Assert.NotNull(captured);
        Assert.Equal(HttpMethod.Post, captured!.Method);
        Assert.Equal(new Uri($"{BaseUrl}/api/v1/auth/login"), captured.RequestUri);
        Assert.Equal(CorrelationId, Assert.Single(captured.Headers["X-Correlation-ID"]));
        Assert.Contains("application/json", captured.Headers["Accept"]);
        Assert.Contains("application/problem+json", captured.Headers["Accept"]);
        Assert.Equal("application/json", captured.ContentType);

        using var json = JsonDocument.Parse(captured!.Body);
        Assert.Equal("ivan", json.RootElement.GetProperty("login").GetString());
        Assert.Equal(password, json.RootElement.GetProperty("password").GetString());
        Assert.DoesNotContain("password", captured.RequestUri!.ToString());

        var device = json.RootElement.GetProperty("device");
        Assert.Equal(DeviceKey, device.GetProperty("deviceKey").GetString());
        Assert.Equal(DeviceName, device.GetProperty("deviceName").GetString());
        Assert.Equal("windows", device.GetProperty("platform").GetString());
        Assert.Equal(AppVersion, device.GetProperty("appVersion").GetString());
        Assert.Equal(OsVersion, device.GetProperty("osVersion").GetString());
        Assert.DoesNotContain(DeviceKey, captured.RequestUri.ToString());

        AssertNoHeaderContainsSecret(captured, password);
        AssertNoHeaderContainsSecret(captured, DeviceKey);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task Login_401_InvalidCredentials_ReturnsAuthError()
    {
        var handler = new FakeHttpMessageHandler(_ =>
            global::System.Threading.Tasks.Task.FromResult(
                ProblemResponse(HttpStatusCode.Unauthorized, "INVALID_CREDENTIALS")));
        var client = new DesktopAuthApiClient(new HttpClient(handler), BaseUrl);

        var result = await client.LoginAsync("ivan", "wrong", DeviceInfo, CorrelationId, CancellationToken.None);

        var authError = Assert.IsType<LoginResult.AuthError>(result);
        Assert.Equal(AuthProblemCode.InvalidCredentials, authError.Error.ProblemCode);
        Assert.Null(authError.Error.RetryAfterSeconds);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task GetSession_200_ParsesResponse_AndSendsBearerRequest()
    {
        CapturedRequest? captured = null;
        var handler = new FakeHttpMessageHandler(async request =>
        {
            captured = await CaptureAsync(request);
            return JsonResponse(HttpStatusCode.OK,
                """{"userId":"019fa078-3f10-7ec1-99e2-7c1cba4ee3d4","sessionId":"019fa078-3f10-7ec1-99e2-7c1cba4ee3d4","organizationId":"019fb732-ad08-7de1-b27d-c86bae8a2937","credentialVersion":4,"authorizationScopeVersion":9,"mustChangePassword":true}""");
        });
        var client = new DesktopAuthApiClient(new HttpClient(handler), BaseUrl);

        var result = await client.GetSessionAsync("AT_header.payload.sig", CancellationToken.None);

        var succeeded = Assert.IsType<GetSessionResult.Succeeded>(result);
        Assert.True(succeeded.Session.MustChangePassword);
        Assert.Equal(4, succeeded.Session.CredentialVersion);
        Assert.Equal(9, succeeded.Session.AuthorizationScopeVersion);
        Assert.NotNull(captured);
        Assert.Equal(HttpMethod.Get, captured!.Method);
        Assert.Equal(new Uri($"{BaseUrl}/api/v1/auth/session"), captured.RequestUri);
        Assert.Equal("Bearer AT_header.payload.sig", Assert.Single(captured.Headers["Authorization"]));
        Assert.True(Guid.TryParseExact(Assert.Single(captured.Headers["X-Correlation-ID"]), "D", out _));
    }

    [Fact]
    public async global::System.Threading.Tasks.Task GetSession_401Problem_ReturnsAuthError()
    {
        var handler = new FakeHttpMessageHandler(_ => global::System.Threading.Tasks.Task.FromResult(
            ProblemResponse(HttpStatusCode.Unauthorized, "SESSION_EXPIRED")));
        var client = new DesktopAuthApiClient(new HttpClient(handler), BaseUrl);

        var result = await client.GetSessionAsync("AT_header.payload.sig", CancellationToken.None);

        var authError = Assert.IsType<GetSessionResult.AuthError>(result);
        Assert.Equal(AuthProblemCode.SessionExpired, authError.Error.ProblemCode);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task GetSession_NetworkError_ReturnsNetworkFailure()
    {
        var client = new DesktopAuthApiClient(new HttpClient(new FakeHttpMessageHandler(
            _ => throw new HttpRequestException("connection refused"))), BaseUrl);

        var result = await client.GetSessionAsync("AT_header.payload.sig", CancellationToken.None);

        Assert.IsType<GetSessionResult.NetworkFailure>(result);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task Login_423_AccountLockedTemporarily_ReturnsAuthError_WithRetryHint()
    {
        var handler = new FakeHttpMessageHandler(_ =>
            global::System.Threading.Tasks.Task.FromResult(
                ProblemResponse(HttpStatusCode.Locked, "ACCOUNT_LOCKED_TEMPORARILY", retryAfterSeconds: 90)));
        var client = new DesktopAuthApiClient(new HttpClient(handler), BaseUrl);

        var result = await client.LoginAsync("ivan", "wrong", DeviceInfo, CorrelationId, CancellationToken.None);

        var authError = Assert.IsType<LoginResult.AuthError>(result);
        Assert.Equal(AuthProblemCode.AccountLockedTemporarily, authError.Error.ProblemCode);
        Assert.Equal(90, authError.Error.RetryAfterSeconds);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task Login_429_RateLimited_ReturnsAuthError_WithRetryHint()
    {
        var handler = new FakeHttpMessageHandler(_ =>
            global::System.Threading.Tasks.Task.FromResult(
                ProblemResponse(HttpStatusCode.TooManyRequests, "RATE_LIMITED", retryAfterSeconds: 30)));
        var client = new DesktopAuthApiClient(new HttpClient(handler), BaseUrl);

        var result = await client.LoginAsync("ivan", "wrong", DeviceInfo, CorrelationId, CancellationToken.None);

        var authError = Assert.IsType<LoginResult.AuthError>(result);
        Assert.Equal(AuthProblemCode.RateLimited, authError.Error.ProblemCode);
        Assert.Equal(30, authError.Error.RetryAfterSeconds);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task Login_403_AccountBlocked_ReturnsAuthError()
    {
        var handler = new FakeHttpMessageHandler(_ =>
            global::System.Threading.Tasks.Task.FromResult(
                ProblemResponse(HttpStatusCode.Forbidden, "ACCOUNT_BLOCKED")));
        var client = new DesktopAuthApiClient(new HttpClient(handler), BaseUrl);

        var result = await client.LoginAsync("ivan", "wrong", DeviceInfo, CorrelationId, CancellationToken.None);

        var authError = Assert.IsType<LoginResult.AuthError>(result);
        Assert.Equal(AuthProblemCode.AccountBlocked, authError.Error.ProblemCode);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task Login_UnrecognizedProblemCode_ReturnsUnknown()
    {
        var handler = new FakeHttpMessageHandler(_ =>
            global::System.Threading.Tasks.Task.FromResult(
                ProblemResponse(HttpStatusCode.Unauthorized, "FUTURE_SECURITY_CODE")));
        var client = new DesktopAuthApiClient(new HttpClient(handler), BaseUrl);

        var result = await client.LoginAsync("ivan", "wrong", DeviceInfo, CorrelationId, CancellationToken.None);

        var authError = Assert.IsType<LoginResult.AuthError>(result);
        Assert.Equal(AuthProblemCode.Unknown, authError.Error.ProblemCode);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task Login_ProblemJsonWithoutCode_ReturnsMalformedResponse()
    {
        var handler = new FakeHttpMessageHandler(_ =>
            global::System.Threading.Tasks.Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)
            {
                Content = new StringContent("""{"status":500,"title":"boom"}""", Encoding.UTF8, "application/problem+json"),
            }));
        var client = new DesktopAuthApiClient(new HttpClient(handler), BaseUrl);

        var result = await client.LoginAsync("ivan", "wrong", DeviceInfo, CorrelationId, CancellationToken.None);

        Assert.IsType<LoginResult.MalformedResponse>(result);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task Login_BrokenJsonBody_ReturnsMalformedResponse()
    {
        var handler = new FakeHttpMessageHandler(_ =>
            global::System.Threading.Tasks.Task.FromResult(JsonResponse(HttpStatusCode.OK, "{not json")));
        var client = new DesktopAuthApiClient(new HttpClient(handler), BaseUrl);

        var result = await client.LoginAsync("ivan", "pw", DeviceInfo, CorrelationId, CancellationToken.None);

        Assert.IsType<LoginResult.MalformedResponse>(result);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task Login_200_MissingRequiredFields_ReturnsMalformedResponse()
    {
        const string json = """{"accessToken":"AT_x","refreshToken":"RT_y"}""";
        var handler = new FakeHttpMessageHandler(_ =>
            global::System.Threading.Tasks.Task.FromResult(JsonResponse(HttpStatusCode.OK, json)));
        var client = new DesktopAuthApiClient(new HttpClient(handler), BaseUrl);

        var result = await client.LoginAsync("ivan", "pw", DeviceInfo, CorrelationId, CancellationToken.None);

        Assert.IsType<LoginResult.MalformedResponse>(result);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task Login_NonJsonErrorBody_ReturnsMalformedResponse()
    {
        var handler = new FakeHttpMessageHandler(_ =>
            global::System.Threading.Tasks.Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized)
            {
                Content = new StringContent("<html>proxy error</html>", Encoding.UTF8, "text/html"),
            }));
        var client = new DesktopAuthApiClient(new HttpClient(handler), BaseUrl);

        var result = await client.LoginAsync("ivan", "pw", DeviceInfo, CorrelationId, CancellationToken.None);

        Assert.IsType<LoginResult.MalformedResponse>(result);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task Login_DeviceWithoutOsVersion_SerializesOsVersionAsNull()
    {
        var device = new DeviceRegistrationInfo(DeviceKey, DeviceName, ClientPlatform.Linux, AppVersion);
        CapturedRequest? captured = null;
        var handler = new FakeHttpMessageHandler(async request =>
        {
            captured = await CaptureAsync(request);
            return JsonResponse(HttpStatusCode.OK, SessionTokensJson);
        });
        var client = new DesktopAuthApiClient(new HttpClient(handler), BaseUrl);

        await client.LoginAsync("ivan", "pw", device, CorrelationId, CancellationToken.None);

        using var json = JsonDocument.Parse(captured!.Body);
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("device").GetProperty("osVersion").ValueKind);
    }

    [Theory]
    [InlineData(ClientPlatform.Windows, "windows")]
    [InlineData(ClientPlatform.Linux, "linux")]
    [InlineData(ClientPlatform.MacOs, "macos")]
    public async global::System.Threading.Tasks.Task Login_DevicePlatform_SerializesToContractString(ClientPlatform platform, string expected)
    {
        var device = new DeviceRegistrationInfo(DeviceKey, DeviceName, platform, AppVersion);
        CapturedRequest? captured = null;
        var handler = new FakeHttpMessageHandler(async request =>
        {
            captured = await CaptureAsync(request);
            return JsonResponse(HttpStatusCode.OK, SessionTokensJson);
        });
        var client = new DesktopAuthApiClient(new HttpClient(handler), BaseUrl);

        await client.LoginAsync("ivan", "pw", device, CorrelationId, CancellationToken.None);

        using var json = JsonDocument.Parse(captured!.Body);
        Assert.Equal(expected, json.RootElement.GetProperty("device").GetProperty("platform").GetString());
    }

    [Fact]
    public void DeviceRegistrationInfo_RejectsNullOrWhitespaceMembers()
    {
        Assert.Throws<ArgumentException>(() => new DeviceRegistrationInfo(" ", DeviceName, ClientPlatform.Windows, AppVersion));
        Assert.Throws<ArgumentException>(() => new DeviceRegistrationInfo(DeviceKey, " ", ClientPlatform.Windows, AppVersion));
        Assert.Throws<ArgumentException>(() => new DeviceRegistrationInfo(DeviceKey, DeviceName, ClientPlatform.Windows, " "));
        Assert.NotNull(new DeviceRegistrationInfo(DeviceKey, DeviceName, ClientPlatform.Windows, AppVersion));
    }

    [Fact]
    public async global::System.Threading.Tasks.Task Login_NetworkError_ReturnsNetworkFailure()
    {
        var handler = new FakeHttpMessageHandler(_ => throw new HttpRequestException("connection refused"));
        var client = new DesktopAuthApiClient(new HttpClient(handler), BaseUrl);

        var result = await client.LoginAsync("ivan", "pw", DeviceInfo, CorrelationId, CancellationToken.None);

        Assert.IsType<LoginResult.NetworkFailure>(result);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task Login_HttpClientTimeout_ReturnsNetworkFailure()
    {
        var handler = new FakeHttpMessageHandler(
            _ => global::System.Threading.Tasks.Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)),
            delay: TimeSpan.FromSeconds(5));
        var httpClient = new HttpClient(handler) { Timeout = TimeSpan.FromMilliseconds(50) };
        var client = new DesktopAuthApiClient(httpClient, BaseUrl);

        var result = await client.LoginAsync("ivan", "pw", DeviceInfo, CorrelationId, CancellationToken.None);

        Assert.IsType<LoginResult.NetworkFailure>(result);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task Login_CallerCancellation_IsPropagated_NotMappedToNetworkFailure()
    {
        var handler = new FakeHttpMessageHandler(_ => throw new TaskCanceledException());
        var client = new DesktopAuthApiClient(new HttpClient(handler), BaseUrl);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.LoginAsync("ivan", "pw", DeviceInfo, CorrelationId, cts.Token));
    }

    [Fact]
    public async global::System.Threading.Tasks.Task Refresh_200_ParsesTokens()
    {
        var handler = new FakeHttpMessageHandler(_ =>
            global::System.Threading.Tasks.Task.FromResult(JsonResponse(HttpStatusCode.OK, SessionTokensJson)));
        var client = new DesktopAuthApiClient(new HttpClient(handler), BaseUrl);

        var result = await client.RefreshAsync("RT_9f8e7d6c5b4a3e2d1c0b9a8f7e6d5c4b", DeviceKey, CancellationToken.None);

        var succeeded = Assert.IsType<RefreshResult.Succeeded>(result);
        Assert.Equal("AT_header.payload.sig", succeeded.Tokens.AccessToken);
        Assert.Equal("RT_9f8e7d6c5b4a3e2d1c0b9a8f7e6d5c4b", succeeded.Tokens.RefreshToken);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task Refresh_401_RefreshTokenReuse_ReturnsAuthError_AndSendsContractRequest()
    {
        const string refreshToken = "RT_9f8e7d6c5b4a3e2d1c0b9a8f7e6d5c4b";
        CapturedRequest? captured = null;
        var handler = new FakeHttpMessageHandler(async request =>
        {
            captured = await CaptureAsync(request);
            return ProblemResponse(HttpStatusCode.Unauthorized, "REFRESH_TOKEN_REUSE");
        });
        var client = new DesktopAuthApiClient(new HttpClient(handler), BaseUrl);

        var result = await client.RefreshAsync(refreshToken, DeviceKey, CancellationToken.None);

        var authError = Assert.IsType<RefreshResult.AuthError>(result);
        Assert.Equal(AuthProblemCode.RefreshTokenReuse, authError.Error.ProblemCode);

        Assert.NotNull(captured);
        Assert.Equal(HttpMethod.Post, captured!.Method);
        Assert.Equal(new Uri($"{BaseUrl}/api/v1/auth/refresh"), captured.RequestUri);
        var correlationId = Assert.Single(captured.Headers["X-Correlation-ID"]);
        Assert.True(Guid.TryParseExact(correlationId, "D", out _), "client must send a uuid-format correlation id");

        using var json = JsonDocument.Parse(captured.Body);
        Assert.Equal(refreshToken, json.RootElement.GetProperty("refreshToken").GetString());
        Assert.Equal(DeviceKey, json.RootElement.GetProperty("deviceKey").GetString());

        AssertNoHeaderContainsSecret(captured, refreshToken);
        AssertNoHeaderContainsSecret(captured, DeviceKey);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task Refresh_NetworkError_ReturnsNetworkFailure()
    {
        var handler = new FakeHttpMessageHandler(_ => throw new HttpRequestException("connection refused"));
        var client = new DesktopAuthApiClient(new HttpClient(handler), BaseUrl);

        var result = await client.RefreshAsync("RT_token", DeviceKey, CancellationToken.None);

        Assert.IsType<RefreshResult.NetworkFailure>(result);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task Logout_204_ReturnsSucceeded_AndSendsContractRequest()
    {
        const string accessToken = "AT_header.payload.sig";
        CapturedRequest? captured = null;
        var handler = new FakeHttpMessageHandler(async request =>
        {
            captured = await CaptureAsync(request);
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        });
        var client = new DesktopAuthApiClient(new HttpClient(handler), BaseUrl);

        var result = await client.LogoutAsync(accessToken, CancellationToken.None);

        Assert.IsType<LogoutResult.Succeeded>(result);

        Assert.NotNull(captured);
        Assert.Equal(HttpMethod.Post, captured!.Method);
        Assert.Equal(new Uri($"{BaseUrl}/api/v1/auth/logout"), captured.RequestUri);
        var correlationId = Assert.Single(captured.Headers["X-Correlation-ID"]);
        Assert.True(Guid.TryParseExact(correlationId, "D", out _), "client must send a uuid-format correlation id");
        Assert.Equal($"Bearer {accessToken}", Assert.Single(captured.Headers["Authorization"]));
        Assert.Contains("application/json", captured.Headers["Accept"]);
        Assert.Contains("application/problem+json", captured.Headers["Accept"]);
        Assert.Null(captured.ContentType);
        Assert.DoesNotContain(accessToken, captured.RequestUri!.ToString());
    }

    [Fact]
    public async global::System.Threading.Tasks.Task Logout_401_Problem_ReturnsAuthError()
    {
        var handler = new FakeHttpMessageHandler(_ =>
            global::System.Threading.Tasks.Task.FromResult(
                ProblemResponse(HttpStatusCode.Unauthorized, "SESSION_EXPIRED")));
        var client = new DesktopAuthApiClient(new HttpClient(handler), BaseUrl);

        var result = await client.LogoutAsync("AT_token", CancellationToken.None);

        var authError = Assert.IsType<LogoutResult.AuthError>(result);
        Assert.Equal(AuthProblemCode.SessionExpired, authError.Error.ProblemCode);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task Logout_NetworkError_ReturnsNetworkFailure()
    {
        var handler = new FakeHttpMessageHandler(_ => throw new HttpRequestException("connection refused"));
        var client = new DesktopAuthApiClient(new HttpClient(handler), BaseUrl);

        var result = await client.LogoutAsync("AT_token", CancellationToken.None);

        Assert.IsType<LogoutResult.NetworkFailure>(result);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task Logout_NonProblemBody_ReturnsMalformedResponse()
    {
        var handler = new FakeHttpMessageHandler(_ =>
            global::System.Threading.Tasks.Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)
            {
                Content = new StringContent("<html>boom</html>", Encoding.UTF8, "text/html"),
            }));
        var client = new DesktopAuthApiClient(new HttpClient(handler), BaseUrl);

        var result = await client.LogoutAsync("AT_token", CancellationToken.None);

        Assert.IsType<LogoutResult.MalformedResponse>(result);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task Ctor_TrailingSlashBaseUrl_IsNormalized()
    {
        CapturedRequest? captured = null;
        var handler = new FakeHttpMessageHandler(async request =>
        {
            captured = await CaptureAsync(request);
            return JsonResponse(HttpStatusCode.OK, SessionTokensJson);
        });
        var client = new DesktopAuthApiClient(new HttpClient(handler), $"{BaseUrl}/");

        await client.LoginAsync("ivan", "pw", DeviceInfo, CorrelationId, CancellationToken.None);

        Assert.Equal(new Uri($"{BaseUrl}/api/v1/auth/login"), captured!.RequestUri);
    }

    [Fact]
    public void Ctor_RejectsNullOrWhitespaceBaseUrl()
    {
        Assert.Throws<ArgumentNullException>(() => new DesktopAuthApiClient(null!, BaseUrl));
        Assert.Throws<ArgumentException>(() => new DesktopAuthApiClient(
            new HttpClient(new FakeHttpMessageHandler(_ =>
                global::System.Threading.Tasks.Task.FromResult(new HttpResponseMessage()))), "  "));
    }

    [Fact]
    public async global::System.Threading.Tasks.Task Login_EmptyArguments_Throw()
    {
        var client = new DesktopAuthApiClient(
            new HttpClient(new FakeHttpMessageHandler(_ =>
                global::System.Threading.Tasks.Task.FromResult(new HttpResponseMessage()))), BaseUrl);

        await Assert.ThrowsAsync<ArgumentException>(() => client.LoginAsync("", "pw", DeviceInfo, CorrelationId, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => client.LoginAsync("ivan", "", DeviceInfo, CorrelationId, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentNullException>(() => client.LoginAsync("ivan", "pw", null!, CorrelationId, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => client.LoginAsync("ivan", "pw", DeviceInfo, "", CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => client.RefreshAsync("RT_token", "", CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => client.RefreshAsync("", DeviceKey, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => client.LogoutAsync("", CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => client.LogoutAsync("   ", CancellationToken.None));
    }

    [Fact]
    public async global::System.Threading.Tasks.Task ChangePassword_204_SendsBearerAndContractBody()
    {
        CapturedRequest? captured = null;
        var handler = new FakeHttpMessageHandler(async request =>
        {
            captured = await CaptureAsync(request);
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        });
        var client = new DesktopAuthApiClient(new HttpClient(handler), BaseUrl);

        var result = await client.ChangePasswordAsync(
            "AT_secret",
            "Current!234",
            "Replacement!234",
            CancellationToken.None);

        Assert.IsType<ChangePasswordResult.Succeeded>(result);
        Assert.NotNull(captured);
        Assert.Equal(HttpMethod.Post, captured.Method);
        Assert.Equal("/api/v1/auth/change-password", captured.RequestUri!.AbsolutePath);
        Assert.Equal("Bearer", captured.AuthorizationScheme);
        Assert.Equal("AT_secret", captured.AuthorizationParameter);
        using var body = JsonDocument.Parse(captured.Body);
        Assert.Equal("Current!234", body.RootElement.GetProperty("currentPassword").GetString());
        Assert.Equal("Replacement!234", body.RootElement.GetProperty("newPassword").GetString());
        Assert.DoesNotContain("Current!234", captured.RequestUri.AbsoluteUri);
        Assert.DoesNotContain("Replacement!234", captured.RequestUri.AbsoluteUri);
        AssertNoHeaderContainsSecret(captured, "Current!234");
        AssertNoHeaderContainsSecret(captured, "Replacement!234");
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "INVALID_CREDENTIALS", AuthProblemCode.InvalidCredentials)]
    [InlineData(HttpStatusCode.UnprocessableEntity, "VALIDATION_FAILED", AuthProblemCode.ValidationFailed)]
    [InlineData(HttpStatusCode.Locked, "ACCOUNT_BLOCKED", AuthProblemCode.AccountBlocked)]
    [InlineData(HttpStatusCode.Unauthorized, "SESSION_EXPIRED", AuthProblemCode.SessionExpired)]
    [InlineData(HttpStatusCode.Unauthorized, "SESSION_REVOKED", AuthProblemCode.SessionRevoked)]
    [InlineData(HttpStatusCode.Unauthorized, "AUTHENTICATION_REQUIRED", AuthProblemCode.AuthenticationRequired)]
    [InlineData(HttpStatusCode.TooManyRequests, "RATE_LIMITED", AuthProblemCode.RateLimited)]
    public async global::System.Threading.Tasks.Task ChangePassword_Problem_MapsStableCode(
        HttpStatusCode status,
        string code,
        AuthProblemCode expected)
    {
        var client = new DesktopAuthApiClient(
            new HttpClient(new FakeHttpMessageHandler(
                _ => global::System.Threading.Tasks.Task.FromResult(ProblemResponse(status, code)))),
            BaseUrl);

        var result = await client.ChangePasswordAsync("AT", "old", "new", CancellationToken.None);

        var authError = Assert.IsType<ChangePasswordResult.AuthError>(result);
        Assert.Equal(expected, authError.Error.ProblemCode);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task ChangePassword_NetworkAndMalformed_AreTyped()
    {
        var networkClient = new DesktopAuthApiClient(
            new HttpClient(new FakeHttpMessageHandler(_ => throw new HttpRequestException("offline"))),
            BaseUrl);
        var malformedClient = new DesktopAuthApiClient(
            new HttpClient(new FakeHttpMessageHandler(
                _ => global::System.Threading.Tasks.Task.FromResult(JsonResponse(HttpStatusCode.OK, "{}")))),
            BaseUrl);

        Assert.IsType<ChangePasswordResult.NetworkFailure>(
            await networkClient.ChangePasswordAsync("AT", "old", "new", CancellationToken.None));
        Assert.IsType<ChangePasswordResult.MalformedResponse>(
            await malformedClient.ChangePasswordAsync("AT", "old", "new", CancellationToken.None));
    }

    [Fact]
    public async global::System.Threading.Tasks.Task ChangePassword_CallerCancellation_IsPropagated()
    {
        var client = new DesktopAuthApiClient(
            new HttpClient(new FakeHttpMessageHandler(
                _ => global::System.Threading.Tasks.Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent)),
                TimeSpan.FromSeconds(5))),
            BaseUrl);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(20));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            client.ChangePasswordAsync("AT", "old", "new", cts.Token));
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
            $$"""{"type":"https://task.local/errors/{{code.ToLowerInvariant()}}","title":"Error","status":{{(int)statusCode}},"code":"{{code}}","traceId":"t-1","correlationId":"019fa078-3f10-7ec1-99e2-7c1cba4ee3d4","fieldErrors":[]{{retry}}}""";
        return new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/problem+json"),
        };
    }

    private static async global::System.Threading.Tasks.Task<CapturedRequest> CaptureAsync(HttpRequestMessage request)
    {
        var headers = request.Headers
            .ToDictionary(
                header => header.Key,
                header => (IReadOnlyList<string>)header.Value.ToList(),
                StringComparer.OrdinalIgnoreCase);

        string body = string.Empty;
        string? contentType = null;
        if (request.Content is not null)
        {
            body = await request.Content.ReadAsStringAsync();
            contentType = request.Content.Headers.ContentType?.MediaType;
        }

        return new CapturedRequest(
            request.Method,
            request.RequestUri,
            headers,
            body,
            contentType,
            request.Headers.Authorization?.Scheme,
            request.Headers.Authorization?.Parameter);
    }

    private static void AssertNoHeaderContainsSecret(CapturedRequest request, string secret)
    {
        foreach (var value in request.Headers.Values.SelectMany(values => values))
        {
            Assert.DoesNotContain(secret, value);
        }
    }

    /// <summary>Immutable snapshot of an outgoing request, taken while the message is alive.</summary>
    private sealed record CapturedRequest(
        HttpMethod Method,
        Uri? RequestUri,
        IReadOnlyDictionary<string, IReadOnlyList<string>> Headers,
        string Body,
        string? ContentType,
        string? AuthorizationScheme,
        string? AuthorizationParameter);

    /// <summary>Fake transport: answers from a responder function, optionally after a delay.</summary>
    private sealed class FakeHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, global::System.Threading.Tasks.Task<HttpResponseMessage>> _respond;
        private readonly TimeSpan? _delay;

        public FakeHttpMessageHandler(
            Func<HttpRequestMessage, global::System.Threading.Tasks.Task<HttpResponseMessage>> respond,
            TimeSpan? delay = null)
        {
            _respond = respond;
            _delay = delay;
        }

        protected override async global::System.Threading.Tasks.Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (_delay is { } delay)
            {
                await global::System.Threading.Tasks.Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }

            return await _respond(request).ConfigureAwait(false);
        }
    }
}
