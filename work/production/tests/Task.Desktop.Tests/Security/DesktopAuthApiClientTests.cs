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

        var result = await client.LoginAsync("ivan", password, CorrelationId, CancellationToken.None);

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

        using var json = JsonDocument.Parse(captured.Body);
        Assert.Equal("ivan", json.RootElement.GetProperty("login").GetString());
        Assert.Equal(password, json.RootElement.GetProperty("password").GetString());
        Assert.DoesNotContain("password", captured.RequestUri.ToString());

        AssertNoHeaderContainsSecret(captured, password);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task Login_401_InvalidCredentials_ReturnsAuthError()
    {
        var handler = new FakeHttpMessageHandler(_ =>
            global::System.Threading.Tasks.Task.FromResult(
                ProblemResponse(HttpStatusCode.Unauthorized, "INVALID_CREDENTIALS")));
        var client = new DesktopAuthApiClient(new HttpClient(handler), BaseUrl);

        var result = await client.LoginAsync("ivan", "wrong", CorrelationId, CancellationToken.None);

        var authError = Assert.IsType<LoginResult.AuthError>(result);
        Assert.Equal(AuthProblemCode.InvalidCredentials, authError.Error.ProblemCode);
        Assert.Null(authError.Error.RetryAfterSeconds);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task Login_423_AccountLockedTemporarily_ReturnsAuthError_WithRetryHint()
    {
        var handler = new FakeHttpMessageHandler(_ =>
            global::System.Threading.Tasks.Task.FromResult(
                ProblemResponse(HttpStatusCode.Locked, "ACCOUNT_LOCKED_TEMPORARILY", retryAfterSeconds: 90)));
        var client = new DesktopAuthApiClient(new HttpClient(handler), BaseUrl);

        var result = await client.LoginAsync("ivan", "wrong", CorrelationId, CancellationToken.None);

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

        var result = await client.LoginAsync("ivan", "wrong", CorrelationId, CancellationToken.None);

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

        var result = await client.LoginAsync("ivan", "wrong", CorrelationId, CancellationToken.None);

        var authError = Assert.IsType<LoginResult.AuthError>(result);
        Assert.Equal(AuthProblemCode.AccountBlocked, authError.Error.ProblemCode);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task Login_UnrecognizedProblemCode_ReturnsUnknown()
    {
        var handler = new FakeHttpMessageHandler(_ =>
            global::System.Threading.Tasks.Task.FromResult(
                ProblemResponse(HttpStatusCode.Unauthorized, "AUTHENTICATION_REQUIRED")));
        var client = new DesktopAuthApiClient(new HttpClient(handler), BaseUrl);

        var result = await client.LoginAsync("ivan", "wrong", CorrelationId, CancellationToken.None);

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

        var result = await client.LoginAsync("ivan", "wrong", CorrelationId, CancellationToken.None);

        Assert.IsType<LoginResult.MalformedResponse>(result);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task Login_BrokenJsonBody_ReturnsMalformedResponse()
    {
        var handler = new FakeHttpMessageHandler(_ =>
            global::System.Threading.Tasks.Task.FromResult(JsonResponse(HttpStatusCode.OK, "{not json")));
        var client = new DesktopAuthApiClient(new HttpClient(handler), BaseUrl);

        var result = await client.LoginAsync("ivan", "pw", CorrelationId, CancellationToken.None);

        Assert.IsType<LoginResult.MalformedResponse>(result);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task Login_200_MissingRequiredFields_ReturnsMalformedResponse()
    {
        const string json = """{"accessToken":"AT_x","refreshToken":"RT_y"}""";
        var handler = new FakeHttpMessageHandler(_ =>
            global::System.Threading.Tasks.Task.FromResult(JsonResponse(HttpStatusCode.OK, json)));
        var client = new DesktopAuthApiClient(new HttpClient(handler), BaseUrl);

        var result = await client.LoginAsync("ivan", "pw", CorrelationId, CancellationToken.None);

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

        var result = await client.LoginAsync("ivan", "pw", CorrelationId, CancellationToken.None);

        Assert.IsType<LoginResult.MalformedResponse>(result);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task Login_NetworkError_ReturnsNetworkFailure()
    {
        var handler = new FakeHttpMessageHandler(_ => throw new HttpRequestException("connection refused"));
        var client = new DesktopAuthApiClient(new HttpClient(handler), BaseUrl);

        var result = await client.LoginAsync("ivan", "pw", CorrelationId, CancellationToken.None);

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

        var result = await client.LoginAsync("ivan", "pw", CorrelationId, CancellationToken.None);

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
            () => client.LoginAsync("ivan", "pw", CorrelationId, cts.Token));
    }

    [Fact]
    public async global::System.Threading.Tasks.Task Refresh_200_ParsesTokens()
    {
        var handler = new FakeHttpMessageHandler(_ =>
            global::System.Threading.Tasks.Task.FromResult(JsonResponse(HttpStatusCode.OK, SessionTokensJson)));
        var client = new DesktopAuthApiClient(new HttpClient(handler), BaseUrl);

        var result = await client.RefreshAsync("RT_9f8e7d6c5b4a3e2d1c0b9a8f7e6d5c4b", CancellationToken.None);

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

        var result = await client.RefreshAsync(refreshToken, CancellationToken.None);

        var authError = Assert.IsType<RefreshResult.AuthError>(result);
        Assert.Equal(AuthProblemCode.RefreshTokenReuse, authError.Error.ProblemCode);

        Assert.NotNull(captured);
        Assert.Equal(HttpMethod.Post, captured!.Method);
        Assert.Equal(new Uri($"{BaseUrl}/api/v1/auth/refresh"), captured.RequestUri);
        var correlationId = Assert.Single(captured.Headers["X-Correlation-ID"]);
        Assert.True(Guid.TryParseExact(correlationId, "D", out _), "client must send a uuid-format correlation id");

        using var json = JsonDocument.Parse(captured.Body);
        Assert.Equal(refreshToken, json.RootElement.GetProperty("refreshToken").GetString());

        AssertNoHeaderContainsSecret(captured, refreshToken);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task Refresh_NetworkError_ReturnsNetworkFailure()
    {
        var handler = new FakeHttpMessageHandler(_ => throw new HttpRequestException("connection refused"));
        var client = new DesktopAuthApiClient(new HttpClient(handler), BaseUrl);

        var result = await client.RefreshAsync("RT_token", CancellationToken.None);

        Assert.IsType<RefreshResult.NetworkFailure>(result);
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

        await client.LoginAsync("ivan", "pw", CorrelationId, CancellationToken.None);

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

        await Assert.ThrowsAsync<ArgumentException>(() => client.LoginAsync("", "pw", CorrelationId, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => client.LoginAsync("ivan", "", CorrelationId, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => client.LoginAsync("ivan", "pw", "", CancellationToken.None));
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

        return new CapturedRequest(request.Method, request.RequestUri, headers, body, contentType);
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
        string? ContentType);

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