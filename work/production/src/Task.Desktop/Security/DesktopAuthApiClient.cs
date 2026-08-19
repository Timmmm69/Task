using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Task.Desktop.Security;

/// <summary>
/// Stable problem codes returned by the API in RFC 7807 problem+json error responses.
/// Codes are defined by the technical specification (2.2) and are mapped from the
/// <c>code</c> field of the problem document; an unrecognized code maps to
/// <see cref="Unknown"/>.
/// </summary>
public enum AuthProblemCode
{
    /// <summary>The login/password pair does not match an active account.</summary>
    InvalidCredentials,

    /// <summary>The account is blocked and cannot sign in.</summary>
    AccountBlocked,

    /// <summary>The account is temporarily locked after repeated failed attempts.</summary>
    AccountLockedTemporarily,

    /// <summary>The client exceeded the server rate limit.</summary>
    RateLimited,

    /// <summary>The session has expired and the refresh token is no longer valid.</summary>
    SessionExpired,

    /// <summary>The refresh token was used more than once; the token family is revoked.</summary>
    RefreshTokenReuse,

    /// <summary>The request body was not valid JSON.</summary>
    MalformedJson,

    /// <summary>The server returned a problem code this client does not recognize.</summary>
    Unknown,
}

/// <summary>
/// Structured auth error derived from a problem+json response: the stable problem code
/// plus the optional server-provided retry hint.
/// </summary>
/// <param name="ProblemCode">Mapped problem code of the failed request.</param>
/// <param name="RetryAfterSeconds">
/// Optional <c>retryAfterSeconds</c> hint from the problem document (used for
/// <see cref="AuthProblemCode.RateLimited"/> and <see cref="AuthProblemCode.AccountLockedTemporarily"/>);
/// <c>null</c> when the server did not provide it.
/// </param>
public sealed record AuthErrorResult(AuthProblemCode ProblemCode, int? RetryAfterSeconds);

/// <summary>
/// Session token pair returned by <c>POST /api/v1/auth/login</c> and
/// <c>POST /api/v1/auth/refresh</c>. Field names follow the <c>SessionTokens</c> schema of the
/// technical specification (2.2).
/// </summary>
/// <param name="AccessToken">Bearer access token for authorized API calls.</param>
/// <param name="AccessExpiresAt">
/// UTC instant when the access token expires. RFC 3339 field <c>accessExpiresAt</c> of the
/// response; the contract has no <c>expiresIn</c> field, so the client performs no
/// expiry calculation.
/// </param>
/// <param name="RefreshToken">Single-use refresh token for the next rotation.</param>
/// <param name="RefreshExpiresAt">
/// UTC instant when the refresh token expires. RFC 3339 field <c>refreshExpiresAt</c> of the
/// response.
/// </param>
/// <param name="SessionId">Identifier of the established session.</param>
public sealed record SessionTokensResponse(
    [property: JsonPropertyName("accessToken")] string AccessToken,
    [property: JsonPropertyName("accessExpiresAt")] DateTimeOffset AccessExpiresAt,
    [property: JsonPropertyName("refreshToken")] string RefreshToken,
    [property: JsonPropertyName("refreshExpiresAt")] DateTimeOffset RefreshExpiresAt,
    [property: JsonPropertyName("sessionId")] Guid SessionId);

/// <summary>
/// Outcome of <see cref="DesktopAuthApiClient.LoginAsync"/>: either the issued session tokens,
/// a structured auth error, or a transport/response-level failure.
/// </summary>
public abstract record LoginResult
{
    /// <summary>The server issued a session; <see cref="Tokens"/> holds the full token pair.</summary>
    /// <param name="Tokens">Session tokens returned by the server.</param>
    public sealed record Succeeded(SessionTokensResponse Tokens) : LoginResult;

    /// <summary>The server rejected the request with a problem+json error carrying a code.</summary>
    /// <param name="Error">Mapped problem code and optional retry hint.</param>
    public sealed record AuthError(AuthErrorResult Error) : LoginResult;

    /// <summary>
    /// The request did not reach the server or no response was received in time
    /// (transport failure or client timeout). The client does not retry automatically;
    /// retries are a decision of the session service.
    /// </summary>
    public sealed record NetworkFailure : LoginResult;

    /// <summary>The server responded, but the body was not the expected JSON payload.</summary>
    public sealed record MalformedResponse : LoginResult;
}

/// <summary>
/// Outcome of <see cref="DesktopAuthApiClient.RefreshAsync"/>; mirrors <see cref="LoginResult"/>.
/// </summary>
public abstract record RefreshResult
{
    /// <summary>The server rotated the session; <see cref="Tokens"/> holds the new token pair.</summary>
    /// <param name="Tokens">Session tokens returned by the server.</param>
    public sealed record Succeeded(SessionTokensResponse Tokens) : RefreshResult;

    /// <summary>The server rejected the request with a problem+json error carrying a code.</summary>
    /// <param name="Error">Mapped problem code and optional retry hint.</param>
    public sealed record AuthError(AuthErrorResult Error) : RefreshResult;

    /// <summary>
    /// The request did not reach the server or no response was received in time.
    /// The client does not retry automatically; retries are a decision of the session service.
    /// </summary>
    public sealed record NetworkFailure : RefreshResult;

    /// <summary>The server responded, but the body was not the expected JSON payload.</summary>
    public sealed record MalformedResponse : RefreshResult;
}

/// <summary>
/// HTTP client for the desktop authentication endpoints <c>POST /api/v1/auth/login</c> and
/// <c>POST /api/v1/auth/refresh</c> (technical specification 2.2).
///
/// Responsibilities and limits:
/// <list type="bullet">
/// <item>Maps HTTP responses to typed outcomes: <c>200</c> with a valid <c>SessionTokens</c>
/// body becomes a success; problem+json errors with a <c>code</c> field become
/// <see cref="AuthErrorResult"/>; unparseable bodies become <see cref="LoginResult.MalformedResponse"/>;
/// transport failures and client timeouts become <see cref="LoginResult.NetworkFailure"/>.</item>
/// <item>Never retries a request and never stores tokens; retry policy and token persistence
/// belong to the session service and the credential vault.</item>
/// <item>Never logs or persists the password. The client has no logging at all; the password
/// travels only inside the serialized JSON request body.</item>
/// </list>
///
/// Every request carries an <c>X-Correlation-ID</c> header (uuid format): the caller supplies
/// it for login, the client generates one for refresh.
/// </summary>
public sealed class DesktopAuthApiClient
{
    private const string CorrelationIdHeader = "X-Correlation-ID";
    private const string JsonMediaType = "application/json";
    private const string ProblemJsonMediaType = "application/problem+json";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly string _loginUrl;
    private readonly string _refreshUrl;

    /// <summary>
    /// Creates a client for the given HTTP pipeline and API base URL.
    /// </summary>
    /// <param name="httpClient">
    /// HTTP pipeline used for all requests. Timeout, proxy and TLS behavior are configured by
    /// the caller on this instance.
    /// </param>
    /// <param name="baseUrl">
    /// API base URL without a trailing slash, for example <c>https://task.local</c>.
    /// A trailing slash is tolerated and normalized away.
    /// </param>
    public DesktopAuthApiClient(HttpClient httpClient, string baseUrl)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentException.ThrowIfNullOrWhiteSpace(baseUrl);

        _httpClient = httpClient;
        var normalizedBaseUrl = baseUrl.TrimEnd('/');
        _loginUrl = $"{normalizedBaseUrl}/api/v1/auth/login";
        _refreshUrl = $"{normalizedBaseUrl}/api/v1/auth/refresh";
    }

    /// <summary>
    /// Signs the user in with login and password.
    /// </summary>
    /// <param name="login">Account login.</param>
    /// <param name="password">Account password. Sent only inside the JSON body of the request
    /// and never written to logs, headers, URLs or any storage.</param>
    /// <param name="correlationId">Correlation identifier sent in the <c>X-Correlation-ID</c>
    /// header, used to trace the request through server logs and audit.</param>
    /// <param name="cancellationToken">Cancellation token; cancellation is propagated to the
    /// caller and is not reported as a network failure.</param>
    /// <returns>Typed outcome of the login attempt, never <c>null</c>.</returns>
    public async global::System.Threading.Tasks.Task<LoginResult> LoginAsync(
        string login,
        string password,
        string correlationId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(login);
        ArgumentException.ThrowIfNullOrWhiteSpace(password);
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);

        using var request = CreatePostRequest(
            _loginUrl,
            correlationId,
            new LoginRequestBody(login, password));

        return await SendAsync<LoginResult>(
            request,
            succeeded: tokens => new LoginResult.Succeeded(tokens),
            authError: error => new LoginResult.AuthError(error),
            networkFailure: () => new LoginResult.NetworkFailure(),
            malformedResponse: () => new LoginResult.MalformedResponse(),
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Rotates the session using the given refresh token. A correlation identifier is
    /// generated by the client and sent in the <c>X-Correlation-ID</c> header.
    /// </summary>
    /// <param name="refreshToken">Single-use refresh token of the current session.</param>
    /// <param name="cancellationToken">Cancellation token; cancellation is propagated to the
    /// caller and is not reported as a network failure.</param>
    /// <returns>Typed outcome of the refresh attempt, never <c>null</c>.</returns>
    public async global::System.Threading.Tasks.Task<RefreshResult> RefreshAsync(string refreshToken, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(refreshToken);

        using var request = CreatePostRequest(
            _refreshUrl,
            Guid.NewGuid().ToString("D"),
            new RefreshRequestBody(refreshToken));

        return await SendAsync<RefreshResult>(
            request,
            succeeded: tokens => new RefreshResult.Succeeded(tokens),
            authError: error => new RefreshResult.AuthError(error),
            networkFailure: () => new RefreshResult.NetworkFailure(),
            malformedResponse: () => new RefreshResult.MalformedResponse(),
            cancellationToken).ConfigureAwait(false);
    }

    private HttpRequestMessage CreatePostRequest(string url, string correlationId, object body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.TryAddWithoutValidation(CorrelationIdHeader, correlationId);
        request.Headers.Accept.ParseAdd(JsonMediaType);
        request.Headers.Accept.ParseAdd(ProblemJsonMediaType);
        request.Content = new StringContent(
            JsonSerializer.Serialize(body, JsonOptions),
            Encoding.UTF8,
            JsonMediaType);
        return request;
    }

    private async global::System.Threading.Tasks.Task<TResult> SendAsync<TResult>(
        HttpRequestMessage request,
        Func<SessionTokensResponse, TResult> succeeded,
        Func<AuthErrorResult, TResult> authError,
        Func<TResult> networkFailure,
        Func<TResult> malformedResponse,
        CancellationToken cancellationToken)
        where TResult : class
    {
        HttpResponseMessage response;
        try
        {
            response = await _httpClient
                .SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException)
        {
            return networkFailure();
        }
        catch (TaskCanceledException)
        {
            // The HttpClient timeout elapsed without a response.
            return networkFailure();
        }

        using (response)
        {
            string body;
            try
            {
                body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (HttpRequestException)
            {
                return networkFailure();
            }
            catch (TaskCanceledException)
            {
                return networkFailure();
            }

            return response.IsSuccessStatusCode
                ? TryReadSuccess(body, succeeded, malformedResponse)
                : TryReadProblem(body, authError, malformedResponse);
        }
    }

    private static TResult TryReadSuccess<TResult>(
        string body,
        Func<SessionTokensResponse, TResult> succeeded,
        Func<TResult> malformedResponse)
    {
        SessionTokensResponse? tokens;
        try
        {
            tokens = JsonSerializer.Deserialize<SessionTokensResponse>(body, JsonOptions);
        }
        catch (JsonException)
        {
            return malformedResponse();
        }

        if (tokens is null
            || string.IsNullOrWhiteSpace(tokens.AccessToken)
            || tokens.AccessExpiresAt == default
            || string.IsNullOrWhiteSpace(tokens.RefreshToken)
            || tokens.RefreshExpiresAt == default
            || tokens.SessionId == Guid.Empty)
        {
            return malformedResponse();
        }

        return succeeded(tokens);
    }

    private static TResult TryReadProblem<TResult>(
        string body,
        Func<AuthErrorResult, TResult> authError,
        Func<TResult> malformedResponse)
    {
        AuthProblemPayload? problem;
        try
        {
            problem = JsonSerializer.Deserialize<AuthProblemPayload>(body, JsonOptions);
        }
        catch (JsonException)
        {
            return malformedResponse();
        }

        if (problem is null || string.IsNullOrWhiteSpace(problem.Code))
        {
            return malformedResponse();
        }

        return authError(new AuthErrorResult(MapProblemCode(problem.Code), problem.RetryAfterSeconds));
    }

    private static AuthProblemCode MapProblemCode(string code) => code switch
    {
        "INVALID_CREDENTIALS" => AuthProblemCode.InvalidCredentials,
        "ACCOUNT_BLOCKED" => AuthProblemCode.AccountBlocked,
        "ACCOUNT_LOCKED_TEMPORARILY" => AuthProblemCode.AccountLockedTemporarily,
        "RATE_LIMITED" => AuthProblemCode.RateLimited,
        "SESSION_EXPIRED" => AuthProblemCode.SessionExpired,
        "REFRESH_TOKEN_REUSE" => AuthProblemCode.RefreshTokenReuse,
        "MALFORMED_JSON" => AuthProblemCode.MalformedJson,
        _ => AuthProblemCode.Unknown,
    };

    /// <summary>
    /// JSON body of the login request: <c>{ "login": ..., "password": ... }</c>. The optional
    /// <c>device</c> member of the LoginRequest schema is not sent by this client yet: the
    /// public login contract of this increment is login + password only, and device
    /// registration belongs to the session service.
    /// </summary>
    private sealed record LoginRequestBody(string Login, string Password);

    /// <summary>
    /// JSON body of the refresh request: <c>{ "refreshToken": ... }</c>. The optional
    /// <c>deviceKey</c> member of the RefreshRequest schema is not sent by this client yet;
    /// it belongs to the session service alongside device registration.
    /// </summary>
    private sealed record RefreshRequestBody(string RefreshToken);

    /// <summary>
    /// The subset of the RFC 7807 problem document that the client consumes: the stable
    /// <c>code</c> and the optional <c>retryAfterSeconds</c> hint.
    /// </summary>
    private sealed class AuthProblemPayload
    {
        [JsonPropertyName("code")]
        public string? Code { get; init; }

        [JsonPropertyName("retryAfterSeconds")]
        public int? RetryAfterSeconds { get; init; }
    }
}