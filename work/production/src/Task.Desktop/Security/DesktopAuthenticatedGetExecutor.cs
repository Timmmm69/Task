using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;

namespace Task.Desktop.Security;

/// <summary>Transport-level outcome of one logical authenticated GET operation.</summary>
public abstract record AuthenticatedGetResult
{
    private AuthenticatedGetResult()
    {
    }

    public sealed record Response(HttpStatusCode StatusCode, string Body) : AuthenticatedGetResult;

    public sealed record AuthenticationFailure : AuthenticatedGetResult;

    public sealed record ServerUnavailable : AuthenticatedGetResult;

    public sealed record MalformedResponse : AuthenticatedGetResult;
}

/// <summary>
/// Executes safe authenticated GET requests with the current desktop session. A 401 response
/// triggers at most one refresh and, only after a successful refresh, one replay of the GET.
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
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(requestUri);
        if (!requestUri.IsAbsoluteUri)
        {
            throw new ArgumentException("The request URI must be absolute.", nameof(requestUri));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);

        var firstAttempt = await SendOnceAsync(requestUri, correlationId, cancellationToken)
            .ConfigureAwait(false);
        if (firstAttempt is not AuthenticatedGetResult.Response
            { StatusCode: HttpStatusCode.Unauthorized })
        {
            return firstAttempt;
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

                var retry = await SendOnceAsync(requestUri, correlationId, cancellationToken)
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
        Uri requestUri,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var accessToken = _sessionService.GetAccessTokenForRequest();
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return new AuthenticatedGetResult.AuthenticationFailure();
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.TryAddWithoutValidation(CorrelationIdHeader, correlationId);
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.Accept.ParseAdd("application/problem+json");

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
                var body = await response.Content
                    .ReadAsStringAsync(cancellationToken)
                    .ConfigureAwait(false);
                return new AuthenticatedGetResult.Response(response.StatusCode, body);
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
