using System.Security.Cryptography;
using Microsoft.IdentityModel.JsonWebTokens;
using Task.Api.Security;
using Task.Application.Security;

namespace Task.ServiceHosts.Tests;

public sealed class JwtAccessTokenIssuerTests
{
    private const string Issuer = "https://task.example.internal";
    private const string Audience = "task-desktop";
    private const string OtherIssuer = "https://other.example.internal";
    private const string OtherAudience = "other-app";
    private const string KeyId = "roundtrip-key";

    private static readonly Guid SubjectId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid SessionId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid OrgId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    [Fact]
    public async global::System.Threading.Tasks.Task IssueAsync_TokenAcceptedByAccessTokenValidator_WithMatchingClaims()
    {
        var roundTrip = CreateRoundTrip();

        var token = await roundTrip.Issuer.IssueAsync(DefaultRequest(), CancellationToken.None);
        var result = await roundTrip.Validator.ValidateAsync(token);

        Assert.True(result.IsSuccess, result.Failure?.ToString());
        Assert.Equal(SubjectId, result.Claims!.Sub);
        Assert.Equal(SessionId, result.Claims.Sid);
        Assert.Equal(OrgId, result.Claims.Org);
        Assert.Equal(3, result.Claims.Cver);
        Assert.Equal(7, result.Claims.Sver);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task IssueAsync_TokenHeaderKid_MatchesVerificationKey()
    {
        var roundTrip = CreateRoundTrip();

        var token = await roundTrip.Issuer.IssueAsync(DefaultRequest(), CancellationToken.None);
        var jwt = new JsonWebTokenHandler().ReadJsonWebToken(token);

        Assert.Equal(KeyId, jwt.GetHeaderValue<string>("kid"));
        Assert.NotNull(roundTrip.VerificationKeys.Find(KeyId));
    }

    [Fact]
    public async global::System.Threading.Tasks.Task IssueAsync_ExplicitTwoMinuteLifetime_AcceptedByValidator()
    {
        var roundTrip = CreateRoundTrip();

        var token = await roundTrip.Issuer.IssueAsync(
            DefaultRequest() with { Lifetime = TimeSpan.FromMinutes(2) },
            CancellationToken.None);
        var result = await roundTrip.Validator.ValidateAsync(token);

        Assert.True(result.IsSuccess, result.Failure?.ToString());
    }

    [Fact]
    public async global::System.Threading.Tasks.Task IssueAsync_WrongIssuer_RejectedByValidator()
    {
        var roundTrip = CreateRoundTrip(issuer: OtherIssuer);

        var token = await roundTrip.Issuer.IssueAsync(DefaultRequest(), CancellationToken.None);
        var result = await roundTrip.Validator.ValidateAsync(token);

        Assert.False(result.IsSuccess);
        Assert.False(result.IsExpired);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task IssueAsync_WrongAudience_RejectedByValidator()
    {
        var roundTrip = CreateRoundTrip(audience: OtherAudience);

        var token = await roundTrip.Issuer.IssueAsync(DefaultRequest(), CancellationToken.None);
        var result = await roundTrip.Validator.ValidateAsync(token);

        Assert.False(result.IsSuccess);
        Assert.False(result.IsExpired);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task IssueAsync_KeyUnknownToValidator_Rejected()
    {
        var roundTrip = CreateRoundTrip();

        using var otherKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var foreignIssuer = new JwtAccessTokenIssuer(Issuer, Audience, $"file:{WriteKey(otherKey.ExportPkcs8PrivateKeyPem())}");

        var token = await foreignIssuer.IssueAsync(DefaultRequest(), CancellationToken.None);
        var result = await roundTrip.Validator.ValidateAsync(token);

        Assert.False(result.IsSuccess);
        Assert.False(result.IsExpired);
    }

    private static (JwtAccessTokenIssuer Issuer, AccessTokenValidator Validator, JwtVerificationKeys VerificationKeys) CreateRoundTrip(
        string? issuer = null,
        string? audience = null)
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var directory = Path.Combine(Path.GetTempPath(), $"task-issuer-roundtrip-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var keyFile = Path.Combine(directory, KeyId);
        File.WriteAllText(keyFile, ecdsa.ExportPkcs8PrivateKeyPem());
        File.WriteAllText(
            Path.Combine(directory, $"{KeyId}.pem"),
            ecdsa.ExportSubjectPublicKeyInfoPem());

        var options = new TaskIdentityFoundationOptions
        {
            Issuer = Issuer,
            Audience = Audience,
            SigningKeyReference = $"file:{keyFile}",
            PepperReference = "file:/run/secrets/task-pepper",
            VerificationKeysDirectory = $"file:{directory}",
        };

        var issuerService = new JwtAccessTokenIssuer(
            issuer ?? Issuer,
            audience ?? Audience,
            $"file:{keyFile}");
        var verificationKeys = JwtVerificationKeys.Load(options);
        return (issuerService, new AccessTokenValidator(verificationKeys, options), verificationKeys);
    }

    private static JwtIssuanceRequest DefaultRequest() =>
        new(SubjectId, SessionId, OrgId, CredentialVersion: 3, SessionVersion: 7);

    private static string WriteKey(string pem)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"task-issuer-foreign-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "foreign-key");
        File.WriteAllText(path, pem);
        return path;
    }
}
