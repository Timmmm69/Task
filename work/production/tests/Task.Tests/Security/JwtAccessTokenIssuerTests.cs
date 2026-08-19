using System.Security.Cryptography;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Task.Application.Security;

namespace Task.Tests.Security;

public sealed class JwtAccessTokenIssuerTests : IDisposable
{
    private const string Issuer = "https://task.example.internal";
    private const string Audience = "task-desktop";

    private static readonly Guid SubjectId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid SessionId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid OrgId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private static readonly DateTime FixedIssuedAt =
        new(2026, 6, 1, 10, 0, 0, DateTimeKind.Utc);

    private readonly string _tempRoot =
        Path.Combine(Path.GetTempPath(), $"task-issuer-tests-{Guid.NewGuid():N}");

    public void Dispose()
    {
        try
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
        catch (IOException)
        {
            // Best-effort cleanup; a locked file must not fail the test run.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    [Fact]
    public async global::System.Threading.Tasks.Task IssueAsync_DefaultLifetime_IssuesEs256TokenWithExpectedClaims()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var issuer = CreateIssuer(ecdsa.ExportECPrivateKeyPem(), alias: "task-signing");

        var token = await issuer.IssueAsync(DefaultRequest(), CancellationToken.None);
        var jwt = new JsonWebTokenHandler().ReadJsonWebToken(token);

        Assert.Equal("ES256", jwt.Alg);
        Assert.Equal("JWT", jwt.Typ);
        Assert.Equal("task-signing", jwt.GetHeaderValue<string>("kid"));
        Assert.Equal(Issuer, jwt.Issuer);
        Assert.Equal(Audience, jwt.GetPayloadValue<string>("aud"));
        Assert.Equal(SubjectId.ToString("D"), jwt.GetPayloadValue<string>("sub"));
        Assert.Equal(SessionId.ToString("D"), jwt.GetPayloadValue<string>("sid"));
        Assert.Equal(OrgId.ToString("D"), jwt.GetPayloadValue<string>("org"));
        Assert.Equal(3L, jwt.GetPayloadValue<long>("cver"));
        Assert.Equal(7L, jwt.GetPayloadValue<long>("sver"));
        Assert.Equal(FixedIssuedAt, jwt.IssuedAt);
        Assert.Equal(FixedIssuedAt, jwt.ValidFrom);
        Assert.Equal(FixedIssuedAt.AddMinutes(5), jwt.ValidTo);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task IssueAsync_ExplicitIssuedAtAndLifetime_AreHonored()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var issuer = CreateIssuer(ecdsa.ExportPkcs8PrivateKeyPem());

        var token = await issuer.IssueAsync(
            DefaultRequest() with { IssuedAtUtc = FixedIssuedAt, Lifetime = TimeSpan.FromMinutes(2) },
            CancellationToken.None);
        var jwt = new JsonWebTokenHandler().ReadJsonWebToken(token);

        Assert.Equal(FixedIssuedAt, jwt.IssuedAt);
        Assert.Equal(FixedIssuedAt.AddMinutes(2), jwt.ValidTo);
    }

    [Theory]
    [InlineData(DateTimeKind.Unspecified)]
    [InlineData(DateTimeKind.Utc)]
    [InlineData(DateTimeKind.Local)]
    public async global::System.Threading.Tasks.Task IssueAsync_IssuedAtKind_ProducesExpectedUtcTimestamp(DateTimeKind kind)
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var issuer = CreateIssuer(ecdsa.ExportECPrivateKeyPem());
        var issuedAt = new DateTime(2026, 6, 1, 10, 0, 0, kind);
        var expectedUtc = kind == DateTimeKind.Unspecified
            ? issuedAt
            : issuedAt.ToUniversalTime();

        var token = await issuer.IssueAsync(
            DefaultRequest() with { IssuedAtUtc = issuedAt },
            CancellationToken.None);
        var jwt = new JsonWebTokenHandler().ReadJsonWebToken(token);

        Assert.Equal(expectedUtc, jwt.IssuedAt);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task IssueAsync_Pkcs8PemKey_Succeeds()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var issuer = CreateIssuer(ecdsa.ExportPkcs8PrivateKeyPem(), alias: "rotation-7.pem");

        var token = await issuer.IssueAsync(DefaultRequest(), CancellationToken.None);
        var jwt = new JsonWebTokenHandler().ReadJsonWebToken(token);

        Assert.Equal("ES256", jwt.Alg);
        Assert.Equal("rotation-7", jwt.GetHeaderValue<string>("kid"));
    }

    [Fact]
    public async global::System.Threading.Tasks.Task IssueAsync_WithCancellationRequested_Throws()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var issuer = CreateIssuer(ecdsa.ExportECPrivateKeyPem());
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => issuer.IssueAsync(DefaultRequest(), cts.Token));
    }

    [Theory]
    [InlineData("empty-sub")]
    [InlineData("empty-sid")]
    [InlineData("empty-org")]
    [InlineData("zero-cver")]
    [InlineData("zero-sver")]
    [InlineData("zero-lifetime")]
    public async global::System.Threading.Tasks.Task IssueAsync_InvalidRequest_Throws(string caseName)
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var issuer = CreateIssuer(ecdsa.ExportECPrivateKeyPem());
        var request = caseName switch
        {
            "empty-sub" => DefaultRequest() with { SubjectId = Guid.Empty },
            "empty-sid" => DefaultRequest() with { SessionId = Guid.Empty },
            "empty-org" => DefaultRequest() with { OrgId = Guid.Empty },
            "zero-cver" => DefaultRequest() with { CredentialVersion = 0 },
            "zero-sver" => DefaultRequest() with { SessionVersion = -1 },
            "zero-lifetime" => DefaultRequest() with { Lifetime = TimeSpan.Zero },
            _ => throw new InvalidOperationException($"Unknown case {caseName}"),
        };

        await Assert.ThrowsAsync<ArgumentException>(
            () => issuer.IssueAsync(request, CancellationToken.None));
    }

    [Fact]
    public void Constructor_MissingKeyFile_Throws()
    {
        var missing = Path.Combine(Path.GetTempPath(), $"task-missing-key-{Guid.NewGuid():N}.pem");

        var ex = Assert.Throws<InvalidOperationException>(
            () => new JwtAccessTokenIssuer(Issuer, Audience, $"file:{missing}"));

        Assert.Contains(missing, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Constructor_PublicKeyOnly_Throws()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        var ex = Assert.Throws<InvalidOperationException>(
            () => CreateIssuer(ecdsa.ExportSubjectPublicKeyInfoPem()));

        Assert.DoesNotContain("PRIVATE KEY", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Constructor_NonP256Curve_Throws()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP384);

        Assert.Throws<InvalidOperationException>(
            () => CreateIssuer(ecdsa.ExportPkcs8PrivateKeyPem()));
    }

    [Fact]
    public void Constructor_MalformedPem_Throws()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => CreateIssuer("this is not a PEM file"));

        Assert.DoesNotContain("this is not a PEM file", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("env:MY_KEY")]
    public void Constructor_InvalidSigningKeyReference_Throws(string? reference)
    {
        Assert.Throws<InvalidOperationException>(
            () => new JwtAccessTokenIssuer(Issuer, Audience, reference!));
    }

    [Fact]
    public void Constructor_MissingIssuerOrAudience_Throws()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var keyPath = WriteKey(ecdsa.ExportECPrivateKeyPem());

        Assert.Throws<InvalidOperationException>(
            () => new JwtAccessTokenIssuer(" ", Audience, $"file:{keyPath}"));
        Assert.Throws<InvalidOperationException>(
            () => new JwtAccessTokenIssuer(Issuer, " ", $"file:{keyPath}"));
    }

    [Fact]
    public void Failures_NeverExposeKeyMaterial()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var pem = ecdsa.ExportPkcs8PrivateKeyPem();

        var exceptions = new List<Exception>
        {
            Assert.Throws<InvalidOperationException>(() => CreateIssuer(pem.Replace("PRIVATE KEY", "PUBLIC KEY", StringComparison.Ordinal))),
            Assert.Throws<InvalidOperationException>(() => CreateIssuer("garbage")),
        };

        foreach (var ex in exceptions)
        {
            Assert.DoesNotContain("BEGIN", ex.Message, StringComparison.Ordinal);
            Assert.DoesNotContain(pem, ex.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async global::System.Threading.Tasks.Task AddTaskApiTokenIssuer_ReadsIdentitySection_AndIssues()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var keyPath = WriteKey(ecdsa.ExportPkcs8PrivateKeyPem());
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Task:Identity:Issuer"] = Issuer,
                ["Task:Identity:Audience"] = Audience,
                ["Task:Identity:SigningKeyReference"] = $"file:{keyPath}",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddTaskApiTokenIssuer(configuration);
        using var provider = services.BuildServiceProvider();
        var issuer = provider.GetRequiredService<JwtAccessTokenIssuer>();

        var token = await issuer.IssueAsync(DefaultRequest(), CancellationToken.None);
        var jwt = new JsonWebTokenHandler().ReadJsonWebToken(token);

        Assert.Equal(Issuer, jwt.Issuer);
        Assert.Equal(Audience, jwt.GetPayloadValue<string>("aud"));
    }

    [Fact]
    public void AddTaskApiTokenIssuer_WithoutSigningKeyReference_FailsClosedOnRegistration()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Task:Identity:Issuer"] = Issuer,
                ["Task:Identity:Audience"] = Audience,
            })
            .Build();

        var services = new ServiceCollection();

        Assert.Throws<InvalidOperationException>(
            () => services.AddTaskApiTokenIssuer(configuration));
    }

    private static JwtIssuanceRequest DefaultRequest() =>
        new(SubjectId, SessionId, OrgId, CredentialVersion: 3, SessionVersion: 7)
        {
            IssuedAtUtc = FixedIssuedAt,
        };

    private JwtAccessTokenIssuer CreateIssuer(string pem, string alias = "task-signing")
    {
        var keyPath = WriteKey(pem, alias);
        return new JwtAccessTokenIssuer(Issuer, Audience, $"file:{keyPath}");
    }

    private string WriteKey(string pem, string alias = "task-signing")
    {
        Directory.CreateDirectory(TempRoot);
        var path = Path.Combine(TempRoot, alias);
        File.WriteAllText(path, pem);
        return path;
    }

    private string TempRoot => _tempRoot;
}
