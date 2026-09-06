using System.Net;
using System.Net.Http;
using System.IO;
using System.Security.Authentication;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;

namespace Task.Desktop.Security;

/// <summary>Reason a server address cannot be used by the desktop client.</summary>
public enum ServerAddressError
{
    Missing,
    Invalid,
    NotHttps,
    UserInfoNotAllowed,
    QueryNotAllowed,
    FragmentNotAllowed,
}

/// <summary>Validates and normalizes Task server base addresses.</summary>
public static class DesktopServerAddress
{
    public static bool TryNormalize(string? value, out Uri? endpoint, out ServerAddressError? error)
    {
        endpoint = null;
        error = null;

        if (string.IsNullOrWhiteSpace(value))
        {
            error = ServerAddressError.Missing;
            return false;
        }

        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var parsed)
            || string.IsNullOrWhiteSpace(parsed.Host))
        {
            error = ServerAddressError.Invalid;
            return false;
        }

        if (!string.Equals(parsed.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            error = ServerAddressError.NotHttps;
            return false;
        }

        if (!string.IsNullOrEmpty(parsed.UserInfo))
        {
            error = ServerAddressError.UserInfoNotAllowed;
            return false;
        }

        if (!string.IsNullOrEmpty(parsed.Query))
        {
            error = ServerAddressError.QueryNotAllowed;
            return false;
        }

        if (!string.IsNullOrEmpty(parsed.Fragment))
        {
            error = ServerAddressError.FragmentNotAllowed;
            return false;
        }

        var normalized = parsed.GetLeftPart(UriPartial.Path).TrimEnd('/');
        endpoint = new Uri(normalized, UriKind.Absolute);
        return true;
    }
}

/// <summary>Typed outcome of probing a Task server endpoint.</summary>
public abstract record ServerProbeResult
{
    public sealed record Succeeded(Uri Endpoint) : ServerProbeResult;
    public sealed record InvalidAddress(ServerAddressError Error) : ServerProbeResult;
    public sealed record TlsFailure : ServerProbeResult;
    public sealed record Unreachable : ServerProbeResult;
    public sealed record NotReady : ServerProbeResult;
    public sealed record UnexpectedResponse : ServerProbeResult;
}

/// <summary>
/// Checks the anonymous liveness and readiness endpoints without changing TLS validation,
/// retrying requests or persisting the candidate address.
/// </summary>
public sealed class DesktopServerProbeClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;

    public DesktopServerProbeClient(HttpClient httpClient)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    }

    public async global::System.Threading.Tasks.Task<ServerProbeResult> ProbeAsync(
        string candidate,
        CancellationToken cancellationToken)
    {
        if (!DesktopServerAddress.TryNormalize(candidate, out var endpoint, out var error))
        {
            return new ServerProbeResult.InvalidAddress(error!.Value);
        }

        var live = await ReadHealthAsync(endpoint!, "health/live", cancellationToken).ConfigureAwait(false);
        if (live.Failure is not null)
        {
            return live.Failure;
        }

        if (live.StatusCode != HttpStatusCode.OK
            || !string.Equals(live.Status, "Alive", StringComparison.OrdinalIgnoreCase))
        {
            return new ServerProbeResult.UnexpectedResponse();
        }

        var ready = await ReadHealthAsync(endpoint!, "health/ready", cancellationToken).ConfigureAwait(false);
        if (ready.Failure is not null)
        {
            return ready.Failure;
        }

        if (ready.StatusCode == HttpStatusCode.ServiceUnavailable
            || string.Equals(ready.Status, "NotReady", StringComparison.OrdinalIgnoreCase))
        {
            return new ServerProbeResult.NotReady();
        }

        return ready.StatusCode == HttpStatusCode.OK
            && string.Equals(ready.Status, "Ready", StringComparison.OrdinalIgnoreCase)
            ? new ServerProbeResult.Succeeded(endpoint!)
            : new ServerProbeResult.UnexpectedResponse();
    }

    private async global::System.Threading.Tasks.Task<HealthReadResult> ReadHealthAsync(
        Uri endpoint,
        string relativePath,
        CancellationToken cancellationToken)
    {
        var requestUri = new Uri($"{endpoint.AbsoluteUri.TrimEnd('/')}/{relativePath}", UriKind.Absolute);
        try
        {
            using var response = await _httpClient
                .GetAsync(requestUri, HttpCompletionOption.ResponseContentRead, cancellationToken)
                .ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            HealthPayload? payload;
            try
            {
                payload = JsonSerializer.Deserialize<HealthPayload>(body, JsonOptions);
            }
            catch (JsonException)
            {
                return new HealthReadResult(response.StatusCode, null, new ServerProbeResult.UnexpectedResponse());
            }

            return string.IsNullOrWhiteSpace(payload?.Status)
                ? new HealthReadResult(response.StatusCode, null, new ServerProbeResult.UnexpectedResponse())
                : new HealthReadResult(response.StatusCode, payload.Status, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (IsTlsFailure(exception))
        {
            return new HealthReadResult(null, null, new ServerProbeResult.TlsFailure());
        }
        catch (HttpRequestException)
        {
            return new HealthReadResult(null, null, new ServerProbeResult.Unreachable());
        }
        catch (TaskCanceledException)
        {
            return new HealthReadResult(null, null, new ServerProbeResult.Unreachable());
        }
    }

    private static bool IsTlsFailure(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException!)
        {
            if (current is AuthenticationException)
            {
                return true;
            }

            if (current.InnerException is null)
            {
                break;
            }
        }

        return false;
    }

    private sealed record HealthReadResult(
        HttpStatusCode? StatusCode,
        string? Status,
        ServerProbeResult? Failure);

    private sealed record HealthPayload([property: JsonPropertyName("status")] string? Status);
}

/// <summary>
/// Stores the verified server address as non-secret local configuration. Writes are atomic;
/// corrupt settings fail closed. Changing the endpoint clears the credential vault before
/// the new address is published, so credentials issued by one server cannot reach another.
/// </summary>
public sealed class DesktopServerSettingsStore
{
    private const int CurrentVersion = 1;
    private const string FileName = "server-settings.json";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly object _sync = new();
    private readonly string _filePath;

    public DesktopServerSettingsStore()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Task"))
    {
    }

    public DesktopServerSettingsStore(string storageDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storageDirectory);
        StorageDirectory = storageDirectory;
        _filePath = Path.Combine(storageDirectory, FileName);
    }

    public string StorageDirectory { get; }

    public Uri? Load()
    {
        lock (_sync)
        {
            if (!File.Exists(_filePath))
            {
                return null;
            }

            try
            {
                var settings = JsonSerializer.Deserialize<ServerSettings>(
                    File.ReadAllBytes(_filePath),
                    JsonOptions);
                if (settings?.Version != CurrentVersion
                    || !DesktopServerAddress.TryNormalize(settings.BaseUrl, out var endpoint, out _))
                {
                    IsolateCorruptFile();
                    return null;
                }

                return endpoint;
            }
            catch (JsonException)
            {
                IsolateCorruptFile();
                return null;
            }
            catch (IOException)
            {
                return null;
            }
            catch (UnauthorizedAccessException)
            {
                return null;
            }
        }
    }

    public void SaveVerifiedEndpoint(Uri endpoint, DesktopCredentialVault vault)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentNullException.ThrowIfNull(vault);
        if (!DesktopServerAddress.TryNormalize(endpoint.AbsoluteUri, out var normalized, out var error))
        {
            throw new ArgumentException($"The server endpoint is invalid: {error}.", nameof(endpoint));
        }

        lock (_sync)
        {
            var existing = Load();
            if (existing is not null && existing == normalized)
            {
                return;
            }

            // Security first: after this point no credential from the old endpoint remains.
            vault.Clear();
            Directory.CreateDirectory(StorageDirectory);
            var tempPath = Path.Combine(StorageDirectory, $"{FileName}.{Guid.NewGuid():N}.tmp");
            try
            {
                File.WriteAllBytes(
                    tempPath,
                    JsonSerializer.SerializeToUtf8Bytes(
                        new ServerSettings(CurrentVersion, normalized!.AbsoluteUri),
                        JsonOptions));
                File.Move(tempPath, _filePath, overwrite: true);
            }
            finally
            {
                try
                {
                    File.Delete(tempPath);
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }
    }

    private void IsolateCorruptFile()
    {
        try
        {
            var corruptPath = Path.Combine(
                StorageDirectory,
                $"{FileName}.corrupt-{DateTime.UtcNow:yyyyMMddHHmmssfff}");
            File.Move(_filePath, corruptPath);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private sealed record ServerSettings(
        [property: JsonPropertyName("version")] int Version,
        [property: JsonPropertyName("baseUrl")] string BaseUrl);
}
