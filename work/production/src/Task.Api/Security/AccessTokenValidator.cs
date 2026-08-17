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
    long Sver);

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
        catch (SecurityTokenException)
        {
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

        claims = new AccessTokenClaims(sub, sid, org, cver, sver);
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
    private readonly IReadOnlyDictionary<string, ECDsaSecurityKey> _keys;

    private JwtVerificationKeys(IReadOnlyDictionary<string, ECDsaSecurityKey> keys)
    {
        _keys = keys;
    }

    public bool HasKeys => _keys.Count > 0;

    public static JwtVerificationKeys Load(TaskIdentityFoundationOptions identityOptions)
    {
        var keys = new Dictionary<string, ECDsaSecurityKey>(StringComparer.Ordinal);

        var reference = identityOptions.VerificationKeysDirectory;
        if (!string.IsNullOrWhiteSpace(reference)
            && reference.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
        {
            var directory = reference.Substring("file:".Length);
            if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
            {
                foreach (var path in Directory.EnumerateFiles(directory, "*.pem"))
                {
                    TryLoadKey(path, keys);
                }
            }
        }

        return new JwtVerificationKeys(keys);
    }

    public ECDsaSecurityKey? Find(string? keyId) =>
        keyId is not null && _keys.TryGetValue(keyId, out var key) ? key : null;

    private static void TryLoadKey(string path, IDictionary<string, ECDsaSecurityKey> keys)
    {
        var keyId = Path.GetFileNameWithoutExtension(path);
        if (string.IsNullOrWhiteSpace(keyId) || keys.ContainsKey(keyId))
        {
            return;
        }

        try
        {
            var pem = File.ReadAllText(path);
            if (!pem.Contains("PUBLIC KEY", StringComparison.Ordinal))
            {
                return;
            }

            var ecdsa = ECDsa.Create();
            ecdsa.ImportFromPem(pem);
            if (ecdsa.KeySize != 256)
            {
                ecdsa.Dispose();
                return;
            }

            keys[keyId] = new ECDsaSecurityKey(ecdsa) { KeyId = keyId };
        }
        catch (Exception)
        {
            // Fail closed: unreadable or malformed key material is skipped.
        }
    }
}