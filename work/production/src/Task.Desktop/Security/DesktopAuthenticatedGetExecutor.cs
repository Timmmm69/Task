using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;

namespace Task.Desktop.Security;

/// <summary>Transport-level outcome of one logical authenticated request.</summary>
public abstract record AuthenticatedGetResult
{
    private AuthenticatedGetResult()
    {
    }

    public sealed record Response(
        HttpStatusCode StatusCode,
        string Body,
        string? EntityTag = null,
        string? IdempotencyReplayed = null,
        string? CorrelationId = null) : AuthenticatedGetResult;

    public sealed record AuthenticationFailure : AuthenticatedGetResult;

    public sealed record ServerUnavailable : AuthenticatedGetResult;

    public sealed record MalformedResponse : AuthenticatedGetResult;
}

/// <summary>
/// Executes authenticated requests with the current desktop session. A 401 response triggers at
/// most one refresh and, only after a successful refresh, one byte-equivalent request replay.
/// </summary>
public sealed class DesktopAuthenticatedGetExecutor
{
    private const string CorrelationIdHeader = "X-Correlation-ID";

    private readonly HttpClient _httpClient;
    private readonly SessionService _sessionService;

    public DesktopAuthenticatedGetExecutor(HttpClient httpClient, SessionService sessionService)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _sessionService = sessionService ?? throw new ArgumentNullException(nameof(sessionService));
    }

    public async global::System.Threading.Tasks.Task<AuthenticatedGetResult> GetAsync(
        Uri requestUri,
        string correlationId,
        CancellationToken cancellationToken) =>
        await SendAsync(
            HttpMethod.Get,
            requestUri,
            body: null,
            correlationId,
            ifMatch: null,
            idempotencyKey: null,
            cancellationToken).ConfigureAwait(false);

    internal async global::System.Threading.Tasks.Task<AuthenticatedGetResult> SendAsync(
        HttpMethod method,
        Uri requestUri,
        byte[]? body,
        string correlationId,
        string? ifMatch,
        string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(method);
        ArgumentNullException.ThrowIfNull(requestUri);
        if (!requestUri.IsAbsoluteUri)
        {
            throw new ArgumentException("The request URI must be absolute.", nameof(requestUri));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);

        var accessToken = _sessionService.GetAccessTokenForRequest();
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return new AuthenticatedGetResult.AuthenticationFailure();
        }

        var firstAttempt = await SendOnceAsync(
                method, requestUri, body, correlationId, ifMatch, idempotencyKey, accessToken, cancellationToken)
            .ConfigureAwait(false);
        if (firstAttempt is not AuthenticatedGetResult.Response
            { StatusCode: HttpStatusCode.Unauthorized })
        {
            return firstAttempt;
        }

        var currentToken = _sessionService.GetAccessTokenForRequest();
        if (!string.IsNullOrWhiteSpace(currentToken)
            && !string.Equals(currentToken, accessToken, StringComparison.Ordinal))
        {
            var concurrentRefreshRetry = await SendOnceAsync(
                    method, requestUri, body, correlationId, ifMatch, idempotencyKey, currentToken, cancellationToken)
                .ConfigureAwait(false);
            return concurrentRefreshRetry is AuthenticatedGetResult.Response
            { StatusCode: HttpStatusCode.Unauthorized }
                    ? new AuthenticatedGetResult.AuthenticationFailure()
                    : concurrentRefreshRetry;
        }

        var refresh = await _sessionService
            .RefreshAsync(cancellationToken)
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);
        switch (refresh)
        {
            case RefreshResult.Succeeded:
                var readiness = _sessionService.CurrentReadiness;
                if (readiness != SessionReadinessState.Ready)
                {
                    return readiness == SessionReadinessState.Unavailable
                        ? new AuthenticatedGetResult.ServerUnavailable()
                        : new AuthenticatedGetResult.AuthenticationFailure();
                }

                currentToken = _sessionService.GetAccessTokenForRequest();
                if (string.IsNullOrWhiteSpace(currentToken))
                {
                    return new AuthenticatedGetResult.AuthenticationFailure();
                }

                var retry = await SendOnceAsync(
                        method, requestUri, body, correlationId, ifMatch, idempotencyKey, currentToken, cancellationToken)
                    .ConfigureAwait(false);
                return retry is AuthenticatedGetResult.Response
                { StatusCode: HttpStatusCode.Unauthorized }
                        ? new AuthenticatedGetResult.AuthenticationFailure()
                        : retry;

            case RefreshResult.AuthError:
                return _sessionService.CurrentState == SessionAuthState.SignedOut
                    ? new AuthenticatedGetResult.AuthenticationFailure()
                    : new AuthenticatedGetResult.ServerUnavailable();

            case RefreshResult.NetworkFailure:
                return new AuthenticatedGetResult.ServerUnavailable();

            case RefreshResult.MalformedResponse:
            default:
                return new AuthenticatedGetResult.MalformedResponse();
        }
    }

    private async global::System.Threading.Tasks.Task<AuthenticatedGetResult> SendOnceAsync(
        HttpMethod method,
        Uri requestUri,
        byte[]? body,
        string correlationId,
        string? ifMatch,
        string? idempotencyKey,
        string accessToken,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, requestUri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.TryAddWithoutValidation(CorrelationIdHeader, correlationId);
        if (ifMatch is not null)
        {
            request.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        }

        if (idempotencyKey is not null)
        {
            request.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey);
        }

        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.Accept.ParseAdd("application/problem+json");
        if (body is not null)
        {
            request.Content = new ByteArrayContent(body);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json")
            {
                CharSet = "utf-8",
            };
        }

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
            return new AuthenticatedGetResult.ServerUnavailable();
        }
        catch (TaskCanceledException)
        {
            return new AuthenticatedGetResult.ServerUnavailable();
        }

        using (response)
        {
            try
            {
                var responseBody = await response.Content
                    .ReadAsStringAsync(cancellationToken)
                    .ConfigureAwait(false);
                var replayed = response.Headers.TryGetValues("Idempotency-Replayed", out var values)
                    ? string.Join(",", values)
                    : null;
                var entityTag = response.Headers.TryGetValues("ETag", out var entityTags)
                    ? string.Join(",", entityTags)
                    : null;
                var responseCorrelationId = response.Headers.TryGetValues(CorrelationIdHeader, out var correlationIds)
                    ? string.Join(",", correlationIds)
                    : correlationId;
                return new AuthenticatedGetResult.Response(
                    response.StatusCode,
                    responseBody,
                    entityTag,
                    replayed,
                    responseCorrelationId);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (HttpRequestException)
            {
                return new AuthenticatedGetResult.ServerUnavailable();
            }
            catch (TaskCanceledException)
            {
                return new AuthenticatedGetResult.ServerUnavailable();
            }
        }
    }
}
