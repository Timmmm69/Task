using System.Security.Cryptography;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Task.Application.Security;

/// <summary>
/// Data needed to issue one ES256 access token. Claim names and types mirror the claims read by
/// AccessTokenValidator (Task.Api.Security): sub, sid and org are "D"-formatted GUIDs, cver and
/// sver are strictly positive integers. IssuedAtUtc defaults to the current UTC time and Lifetime
/// to 5 minutes, which is the maximum lifetime the validator accepts.
/// </summary>
public sealed record JwtIssuanceRequest(
    Guid SubjectId,
    Guid SessionId,
    Guid OrgId,
    long CredentialVersion,
    long SessionVersion,
    DateTime? IssuedAtUtc = null,
    TimeSpan? Lifetime = null);

/// <summary>
/// Issues ES256 access tokens that AccessTokenValidator (Task.Api.Security) accepts: the same
/// claim set, the configured issuer/audience and the kid of the loaded signing key. The key is
/// loaded once at construction and must be an ECDSA P-256 private key in PEM form (PKCS#8
/// "BEGIN PRIVATE KEY" or SEC1 "BEGIN EC PRIVATE KEY"); otherwise the constructor throws and no
/// token can ever be issued. Key material is never exposed in exception messages or logs.
/// </summary>
public sealed class JwtAccessTokenIssuer
{
    private static readonly TimeSpan DefaultLifetime = TimeSpan.FromMinutes(5);
    private static readonly byte[] SigningProbe = [0x01, 0x02, 0x03];

    private readonly string _issuer;
    private readonly string _audience;
    private readonly ECDsaSecurityKey _signingKey;

    public JwtAccessTokenIssuer(string issuer, string audience, string signingKeyReference)
    {
        if (string.IsNullOrWhiteSpace(issuer))
        {
            throw new InvalidOperationException("The JWT issuer is not configured.");
        }

        if (string.IsNullOrWhiteSpace(audience))
        {
            throw new InvalidOperationException("The JWT audience is not configured.");
        }

        if (string.IsNullOrWhiteSpace(signingKeyReference)
            || !signingKeyReference.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Task:Identity:SigningKeyReference must be a file: reference, not a secret value.");
        }

        var keyPath = signingKeyReference.Substring("file:".Length);
        if (!File.Exists(keyPath))
        {
            throw new InvalidOperationException(
                $"The signing key file '{keyPath}' does not exist or is not readable.");
        }

        _issuer = issuer;
        _audience = audience;
        _signingKey = LoadSigningKey(keyPath, Path.GetFileNameWithoutExtension(keyPath));
    }

    /// <summary>
    /// Issues a signed access token. Throws ArgumentException for requests the validator would
    /// reject (empty identities, non-positive versions or lifetime) and OperationCanceledException
    /// when cancellation is requested. Never writes key material anywhere.
    /// </summary>
    public Task<string> IssueAsync(JwtIssuanceRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (request.SubjectId == Guid.Empty
            || request.SessionId == Guid.Empty
            || request.OrgId == Guid.Empty)
        {
            throw new ArgumentException("Subject, session and organization must be non-empty GUIDs.", nameof(request));
        }

        if (request.CredentialVersion <= 0 || request.SessionVersion <= 0)
        {
            throw new ArgumentException("Credential and session versions must be positive.", nameof(request));
        }

        var lifetime = request.Lifetime ?? DefaultLifetime;
        if (lifetime <= TimeSpan.Zero)
        {
            throw new ArgumentException("Token lifetime must be positive.", nameof(request));
        }

        var issuedAt = (request.IssuedAtUtc ?? DateTime.UtcNow).ToUniversalTime();
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = _issuer,
            Audience = _audience,
            Claims = new Dictionary<string, object>
            {
                ["sub"] = request.SubjectId.ToString("D"),
                ["sid"] = request.SessionId.ToString("D"),
                ["org"] = request.OrgId.ToString("D"),
                ["cver"] = request.CredentialVersion,
                ["sver"] = request.SessionVersion,
            },
            IssuedAt = issuedAt,
            NotBefore = issuedAt,
            Expires = issuedAt + lifetime,
            SigningCredentials = new SigningCredentials(_signingKey, SecurityAlgorithms.EcdsaSha256),
        };

        return System.Threading.Tasks.Task.FromResult(new JsonWebTokenHandler().CreateToken(descriptor));
    }

    private static ECDsaSecurityKey LoadSigningKey(string keyPath, string keyId)
    {
        string pem;
        try
        {
            pem = File.ReadAllText(keyPath);
        }
        catch (Exception)
        {
            throw new InvalidOperationException($"The signing key file '{keyPath}' is not readable.");
        }

        ECDsa? ecdsa = null;
        try
        {
            ecdsa = ECDsa.Create();
            ecdsa.ImportFromPem(pem);
            if (ecdsa.KeySize != 256)
            {
                throw new InvalidOperationException(
                    $"The signing key file '{keyPath}' is not an ECDSA P-256 private key (256-bit key expected).");
            }

            // ImportFromPem also accepts public-only keys; a real signature proves the key can
            // actually sign and fails closed when the private part is missing or unusable.
            ecdsa.SignData(
                SigningProbe,
                HashAlgorithmName.SHA256,
                DSASignatureFormat.IeeeP1363FixedFieldConcatenation);

            return new ECDsaSecurityKey(ecdsa) { KeyId = keyId };
        }
        catch (InvalidOperationException)
        {
            ecdsa?.Dispose();
            throw;
        }
        catch (Exception ex)
        {
            ecdsa?.Dispose();
            throw new InvalidOperationException(
                $"The signing key file '{keyPath}' does not contain a readable ECDSA private key in PEM format.",
                ex);
        }
    }
}

/// <summary>
/// Options read by AddTaskApiTokenIssuer from the same "Task:Identity" section that the Task.Api
/// foundation options use; property names match TaskIdentityFoundationOptions so no new
/// configuration format is introduced.
/// </summary>
internal sealed class JwtIssuerOptions
{
    public const string SectionName = "Task:Identity";

    public string? Issuer { get; init; }

    public string? Audience { get; init; }

    public string? SigningKeyReference { get; init; }
}

/// <summary>
/// Registers JwtAccessTokenIssuer from the "Task:Identity" configuration section. The signing
/// key is loaded eagerly, so registration fails closed with an exception when the key file is
/// missing, unreadable or not an ECDSA P-256 private key; the service can never issue a token
/// without a usable key.
/// </summary>
public static class TaskApiTokenIssuerServiceCollectionExtensions
{
    public static IServiceCollection AddTaskApiTokenIssuer(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var options = new JwtIssuerOptions();
        configuration.GetSection(JwtIssuerOptions.SectionName).Bind(options);

        services.AddSingleton(
            new JwtAccessTokenIssuer(options.Issuer!, options.Audience!, options.SigningKeyReference!));
        return services;
    }
}
