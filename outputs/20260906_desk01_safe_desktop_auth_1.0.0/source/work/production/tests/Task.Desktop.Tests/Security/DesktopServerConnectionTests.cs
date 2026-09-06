using System.Net;
using System.Net.Http;
using System.Security.Authentication;
using System.Text;
using System.IO;
using Task.Desktop.Security;

namespace Task.Desktop.Tests.Security;

public sealed class DesktopServerConnectionTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        $"task-server-settings-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Theory]
    [InlineData(null, ServerAddressError.Missing)]
    [InlineData("", ServerAddressError.Missing)]
    [InlineData("not a url", ServerAddressError.Invalid)]
    [InlineData("http://task.local", ServerAddressError.NotHttps)]
    [InlineData("https://user:pw@task.local", ServerAddressError.UserInfoNotAllowed)]
    [InlineData("https://task.local?x=1", ServerAddressError.QueryNotAllowed)]
    [InlineData("https://task.local#part", ServerAddressError.FragmentNotAllowed)]
    public void Address_InvalidValues_AreRejected(string? value, ServerAddressError expected)
    {
        Assert.False(DesktopServerAddress.TryNormalize(value, out var endpoint, out var error));
        Assert.Null(endpoint);
        Assert.Equal(expected, error);
    }

    [Fact]
    public void Address_TrailingSlash_IsNormalized()
    {
        Assert.True(DesktopServerAddress.TryNormalize(
            "  https://task.local/base///  ",
            out var endpoint,
            out var error));

        Assert.Null(error);
        Assert.Equal("https://task.local/base", endpoint!.AbsoluteUri);
    }

    [Fact]
    public void Settings_SaveLoad_IsAtomic_AndSameEndpointKeepsVault()
    {
        var store = new DesktopServerSettingsStore(_directory);
        var vault = new DesktopCredentialVault(_directory);
        vault.SaveRefreshToken("device", "", "ivan", "device-key-123456", "refresh-token");

        store.SaveVerifiedEndpoint(new Uri("https://task.local/"), vault);
        Assert.Null(vault.GetRefreshToken());
        Assert.Equal("https://task.local/", store.Load()!.AbsoluteUri);
        Assert.Empty(Directory.GetFiles(_directory, "*.tmp"));

        vault.SaveRefreshToken("device", "", "ivan", "device-key-123456", "refresh-token-2");
        store.SaveVerifiedEndpoint(new Uri("https://task.local"), vault);
        Assert.NotNull(vault.GetRefreshToken());
    }

    [Fact]
    public void Settings_ChangingEndpoint_ClearsPersistedAndMemoryCredentials()
    {
        var store = new DesktopServerSettingsStore(_directory);
        var vault = new DesktopCredentialVault(_directory);
        store.SaveVerifiedEndpoint(new Uri("https://one.task.local"), vault);
        vault.SaveRefreshToken("device", "", "ivan", "device-key-123456", "refresh-token");
        vault.SetAccessToken("access-token");

        store.SaveVerifiedEndpoint(new Uri("https://two.task.local"), vault);

        Assert.Null(vault.GetRefreshToken());
        Assert.Null(vault.GetAccessToken());
        Assert.Equal("https://two.task.local/", store.Load()!.AbsoluteUri);
    }

    [Fact]
    public void Settings_CorruptFile_DoesNotThrow_AndIsIsolated()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Path.Combine(_directory, "server-settings.json"), "{broken", Encoding.UTF8);
        var store = new DesktopServerSettingsStore(_directory);

        Assert.Null(store.Load());
        Assert.False(File.Exists(Path.Combine(_directory, "server-settings.json")));
        Assert.Single(Directory.GetFiles(_directory, "server-settings.json.corrupt-*"));
    }

    [Fact]
    public async global::System.Threading.Tasks.Task Probe_LiveAndReady_ReturnsNormalizedEndpoint()
    {
        var paths = new List<string>();
        var client = CreateProbe(request =>
        {
            paths.Add(request.RequestUri!.AbsolutePath);
            var status = paths.Count == 1 ? "Alive" : "Ready";
            return Json(HttpStatusCode.OK, $$"""{"status":"{{status}}"}""");
        });

        var result = await client.ProbeAsync("https://task.local/", CancellationToken.None);

        var success = Assert.IsType<ServerProbeResult.Succeeded>(result);
        Assert.Equal("https://task.local/", success.Endpoint.AbsoluteUri);
        Assert.Equal(["/health/live", "/health/ready"], paths);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task Probe_Ready503_ReturnsNotReady()
    {
        var client = CreateProbe(request => request.RequestUri!.AbsolutePath.EndsWith("live")
            ? Json(HttpStatusCode.OK, """{"status":"Alive"}""")
            : Json(HttpStatusCode.ServiceUnavailable, """{"status":"NotReady"}"""));

        Assert.IsType<ServerProbeResult.NotReady>(
            await client.ProbeAsync("https://task.local", CancellationToken.None));
    }

    [Fact]
    public async global::System.Threading.Tasks.Task Probe_UnexpectedPayload_ReturnsUnexpectedResponse()
    {
        var client = CreateProbe(_ => Json(HttpStatusCode.OK, "{}"));

        Assert.IsType<ServerProbeResult.UnexpectedResponse>(
            await client.ProbeAsync("https://task.local", CancellationToken.None));
    }

    [Fact]
    public async global::System.Threading.Tasks.Task Probe_NetworkFailure_ReturnsUnreachable()
    {
        var client = new DesktopServerProbeClient(new HttpClient(new FakeHandler(
            _ => throw new HttpRequestException("offline"))));

        Assert.IsType<ServerProbeResult.Unreachable>(
            await client.ProbeAsync("https://task.local", CancellationToken.None));
    }

    [Fact]
    public async global::System.Threading.Tasks.Task Probe_CertificateFailure_ReturnsTlsFailure()
    {
        var exception = new HttpRequestException(
            "TLS failed",
            new AuthenticationException("certificate rejected"));
        var client = new DesktopServerProbeClient(new HttpClient(new FakeHandler(_ => throw exception)));

        Assert.IsType<ServerProbeResult.TlsFailure>(
            await client.ProbeAsync("https://task.local", CancellationToken.None));
    }

    [Fact]
    public async global::System.Threading.Tasks.Task Probe_CallerCancellation_IsPropagated()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var client = new DesktopServerProbeClient(new HttpClient(new CancelingHandler()));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.ProbeAsync("https://task.local", cts.Token));
    }

    private static DesktopServerProbeClient CreateProbe(Func<HttpRequestMessage, HttpResponseMessage> response) =>
        new(new HttpClient(new FakeHandler(response)));

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        protected override global::System.Threading.Tasks.Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => global::System.Threading.Tasks.Task.FromResult(response(request));
    }

    private sealed class CancelingHandler : HttpMessageHandler
    {
        protected override global::System.Threading.Tasks.Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            global::System.Threading.Tasks.Task.FromCanceled<HttpResponseMessage>(cancellationToken);
    }
}
