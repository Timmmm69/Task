using System.Security.Cryptography;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Task.Api.Security;

/// <summary>
/// Failure category reported by the access token validator. The HTTP handler maps these to the
/// stable problem codes SESSION_EXPIRED (lifetime failures) and AUTHENTICATION_REQUIRED.
/// </summary>
internal enum AccessTokenFailureCode
{
    Invalid,
    Expired,
}

internal sealed record AccessTokenClaims(
    Guid Sub,
    Guid Sid,
    Guid Org,
    long Cver,
    long Sver,
    DateTime AccessExpiresAtUtc = default);

internal sealed class AccessTokenValidationResult
{
    private AccessTokenValidationResult(AccessTokenClaims? claims, AccessTokenFailureCode? failure)
    {
        Claims = claims;
        Failure = failure;
    }

    public static AccessTokenValidationResult Success(AccessTokenClaims claims) => new(claims, null);

    public static AccessTokenValidationResult Failed(AccessTokenFailureCode failure) => new(null, failure);

    public AccessTokenClaims? Claims { get; }

    public AccessTokenFailureCode? Failure { get; }

    public bool IsSuccess => Claims is not null;

    public bool IsExpired => Failure == AccessTokenFailureCode.Expired;
}

/// <summary>
/// Validates ES256 access tokens against the loaded verification keys and identity options.
/// Never throws and never logs token material or claim values.
/// </summary>
internal sealed class AccessTokenValidator
{
    private static readonly TimeSpan ClockSkew = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan MaxTokenLifetime = TimeSpan.FromMinutes(5);

    private readonly JwtVerificationKeys _verificationKeys;
    private readonly TaskIdentityFoundationOptions _identityOptions;

    public AccessTokenValidator(
        JwtVerificationKeys verificationKeys,
        TaskIdentityFoundationOptions identityOptions)
    {
        _verificationKeys = verificationKeys;
        _identityOptions = identityOptions;
    }

    public async global::System.Threading.Tasks.Task<AccessTokenValidationResult> ValidateAsync(string accessToken)
    {
        if (string.IsNullOrWhiteSpace(accessToken)
            || !_verificationKeys.HasKeys
            || string.IsNullOrWhiteSpace(_identityOptions.Issuer)
            || string.IsNullOrWhiteSpace(_identityOptions.Audience))
        {
            return AccessTokenValidationResult.Failed(AccessTokenFailureCode.Invalid);
        }

        try
        {
            var validationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = _identityOptions.Issuer,
                ValidateAudience = true,
                ValidAudience = _identityOptions.Audience,
                AudienceValidator = ValidateSingleAudience,
                ValidateLifetime = true,
                ClockSkew = ClockSkew,
                RequireExpirationTime = true,
                RequireSignedTokens = true,
                ValidateIssuerSigningKey = true,
                ValidAlgorithms = [SecurityAlgorithms.EcdsaSha256],
                IssuerSigningKeyResolver = ResolveVerificationKey,
            };

            var validationResult = await new JsonWebTokenHandler().ValidateTokenAsync(
                accessToken,
                validationParameters);
            if (!validationResult.IsValid)
            {
                return ClassifyFailure(validationResult.Exception);
            }

            if (validationResult.SecurityToken is not JsonWebToken jsonToken
                || jsonToken.IssuedAt == DateTime.MinValue
                || jsonToken.ValidTo == DateTime.MinValue
                || jsonToken.ValidTo - jsonToken.IssuedAt > MaxTokenLifetime)
            {
                return AccessTokenValidationResult.Failed(AccessTokenFailureCode.Expired);
            }

            return TryReadClaims(jsonToken, out var claims)
                ? AccessTokenValidationResult.Success(claims)
                : AccessTokenValidationResult.Failed(AccessTokenFailureCode.Invalid);
        }
        catch (Exception)
        {
            // Fail closed and never leak validation internals: any unexpected failure while
            // parsing or validating the token is treated as an invalid token.
            return AccessTokenValidationResult.Failed(AccessTokenFailureCode.Invalid);
        }
    }

    private IEnumerable<SecurityKey> ResolveVerificationKey(
        string _,
        SecurityToken securityToken,
        string? keyId,
        TokenValidationParameters validationParameters)
    {
        var key = _verificationKeys.Find(keyId);
        return key is null ? [] : [key];
    }

    private static bool ValidateSingleAudience(
        IEnumerable<string> audiences,
        SecurityToken _,
        TokenValidationParameters validationParameters)
    {
        using var enumerator = audiences.GetEnumerator();
        if (!enumerator.MoveNext())
        {
            return false;
        }

        var audience = enumerator.Current;
        return !enumerator.MoveNext()
            && string.Equals(audience, validationParameters.ValidAudience, StringComparison.Ordinal);
    }

    private static AccessTokenValidationResult ClassifyFailure(Exception validationException) =>
        validationException switch
        {
            SecurityTokenExpiredException
                or SecurityTokenNotYetValidException
                or SecurityTokenInvalidLifetimeException
                or SecurityTokenNoExpirationException => AccessTokenValidationResult.Failed(
                    AccessTokenFailureCode.Expired),
            _ => AccessTokenValidationResult.Failed(AccessTokenFailureCode.Invalid),
        };

    private static bool TryReadClaims(JsonWebToken token, out AccessTokenClaims claims)
    {
        claims = default!;
        if (!TryReadGuid(token, "sub", out var sub)
            || !TryReadGuid(token, "sid", out var sid)
            || !TryReadGuid(token, "org", out var org)
            || !token.TryGetPayloadValue<long>("cver", out var cver)
            || cver <= 0
            || !token.TryGetPayloadValue<long>("sver", out var sver)
            || sver <= 0)
        {
            return false;
        }

        claims = new AccessTokenClaims(sub, sid, org, cver, sver, token.ValidTo);
        return true;
    }

    private static bool TryReadGuid(JsonWebToken token, string name, out Guid value)
    {
        value = Guid.Empty;
        return token.TryGetPayloadValue<string>(name, out var raw)
            && !string.IsNullOrWhiteSpace(raw)
            && Guid.TryParseExact(raw, "D", out value)
            && value != Guid.Empty;
    }
}

/// <summary>
/// ECDSA P-256 verification keys loaded once from SPKI PEM files named &lt;kid&gt;.pem inside the
/// configured file: directory. An unreadable or empty directory yields an empty set and the
/// authentication handler fails closed.
/// </summary>
internal sealed class JwtVerificationKeys
{
    private const int MaximumVerificationKeyCount = 2;

    private readonly IReadOnlyDictionary<string, ECDsaSecurityKey> _keys;

    private JwtVerificationKeys(IReadOnlyDictionary<string, ECDsaSecurityKey> keys)
    {
        _keys = keys;
    }

    public bool HasKeys => _keys.Count > 0;

    public static JwtVerificationKeys Load(TaskIdentityFoundationOptions identityOptions)
    {
        ArgumentNullException.ThrowIfNull(identityOptions);

        var keys = new Dictionary<string, ECDsaSecurityKey>(StringComparer.Ordinal);

        if (identityOptions.IsUnconfigured)
        {
            return new JwtVerificationKeys(keys);
        }

        try
        {
            var reference = identityOptions.VerificationKeysDirectory;
            if (string.IsNullOrWhiteSpace(reference)
                || !reference.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "Task:Identity:VerificationKeysDirectory must be a file: reference.");
            }

            var directory = reference.Substring("file:".Length);
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            {
                throw new InvalidOperationException(
                    "The configured verification key directory does not exist or is not readable.");
            }

            var paths = Directory.EnumerateFiles(directory, "*.pem")
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();
            if (paths.Length is < 1 or > MaximumVerificationKeyCount)
            {
                throw new InvalidOperationException(
                    "The verification key directory must contain the active public key and at most one previous public key.");
            }

            foreach (var path in paths)
            {
                LoadKey(path, keys);
            }

        }
        catch (InvalidOperationException)
        {
            DisposeKeys(keys.Values);
            throw;
        }
        catch (Exception)
        {
            DisposeKeys(keys.Values);
            throw new InvalidOperationException(
                "The verification key directory is not readable or contains invalid key material.");
        }

        return new JwtVerificationKeys(keys);
    }

    public ECDsaSecurityKey? Find(string? keyId) =>
        keyId is not null && _keys.TryGetValue(keyId, out var key) ? key : null;

    private static void LoadKey(string path, IDictionary<string, ECDsaSecurityKey> keys)
    {
        var keyId = Path.GetFileNameWithoutExtension(path);
        if (string.IsNullOrWhiteSpace(keyId) || keys.ContainsKey(keyId))
        {
            throw new InvalidOperationException("Verification key identifiers must be non-empty and unique.");
        }

        ECDsa? ecdsa = null;
        try
        {
            var pem = File.ReadAllText(path);
            if (!pem.Contains("PUBLIC KEY", StringComparison.Ordinal)
                || pem.Contains("PRIVATE KEY", StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "A verification key file must contain a public key only.");
            }

            ecdsa = ECDsa.Create();
            ecdsa.ImportFromPem(pem);
            if (ecdsa.KeySize != 256)
            {
                throw new InvalidOperationException(
                    "A verification key file is not an ECDSA P-256 public key.");
            }

            keys[keyId] = new ECDsaSecurityKey(ecdsa) { KeyId = keyId };
            ecdsa = null;
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception)
        {
            throw new InvalidOperationException(
                "A verification key file is not readable or contains invalid key material.");
        }
        finally
        {
            ecdsa?.Dispose();
        }
    }

    internal static void ValidateActiveKeyPair(
        string? signingKeyReference,
        JwtVerificationKeys verificationKeys)
    {
        if (string.IsNullOrWhiteSpace(signingKeyReference)
            || !signingKeyReference.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Task:Identity:SigningKeyReference must be a file: reference.");
        }

        var signingKeyPath = signingKeyReference.Substring("file:".Length);
        var activeKeyId = Path.GetFileNameWithoutExtension(signingKeyPath);
        if (string.IsNullOrWhiteSpace(activeKeyId)
            || verificationKeys.Find(activeKeyId) is not { } verificationKey)
        {
            throw new InvalidOperationException(
                "The verification key directory does not contain the public key for the active signing key id.");
        }

        try
        {
            using var signingKey = ECDsa.Create();
            signingKey.ImportFromPem(File.ReadAllText(signingKeyPath));
            var expected = signingKey.ExportSubjectPublicKeyInfo();
            var actual = verificationKey.ECDsa.ExportSubjectPublicKeyInfo();
            if (!CryptographicOperations.FixedTimeEquals(expected, actual))
            {
                throw new InvalidOperationException(
                    "The active signing key does not match its verification public key.");
            }
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception)
        {
            throw new InvalidOperationException(
                "The active signing key file is not readable or contains invalid key material.");
        }
    }

    private static void DisposeKeys(IEnumerable<ECDsaSecurityKey> keys)
    {
        foreach (var key in keys)
        {
            key.ECDsa.Dispose();
        }
    }
}
