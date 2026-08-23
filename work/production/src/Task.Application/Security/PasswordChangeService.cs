namespace Task.Application.Security;

/// <summary>
/// Outcome of a password change attempt. UnknownAccount and InvalidCurrentPassword are
/// intentionally distinct at this layer; collapsing them into one caller-facing message is
/// the responsibility of the API layer.
/// </summary>
public enum PasswordChangeOutcome
{
    /// <summary>The password was rotated, the old hash archived and other sessions revoked.</summary>
    Success,

    /// <summary>No account with the given identity exists in the organization.</summary>
    UnknownAccount,

    /// <summary>The account is permanently blocked; credentials are not changed.</summary>
    AccountBlocked,

    /// <summary>The supplied current password does not verify against the stored hash.</summary>
    InvalidCurrentPassword,

    /// <summary>The new password equals the current one or one of the recent history hashes.</summary>
    PasswordReuseDetected,

    /// <summary>The new password does not meet the complexity policy.</summary>
    WeakPassword,
}

/// <summary>
/// Result of a password change attempt. RevokedSessionCount is meaningful only for Success:
/// the number of the user's sessions that were invalidated (the session identified by
/// currentSessionId, when supplied, is never revoked).
/// </summary>
public sealed record PasswordChangeResult(PasswordChangeOutcome Outcome, int RevokedSessionCount);

/// <summary>
/// Orchestrates user-initiated password rotation: verifies the current password, enforces the
/// reuse and complexity policies, persists the new hash with an incremented credential version,
/// archives the old hash and revokes all other sessions of the user. The service is fail-closed:
/// blocked and unknown accounts never reach the write path, and any verification failure leaves
/// the stored credentials untouched.
/// </summary>
public sealed class PasswordChangeService
{
    /// <summary>Default number of recent password history hashes checked against reuse.</summary>
    public const int DefaultHistoryLimit = 5;

    /// <summary>Minimum accepted password length.</summary>
    public const int MinimumPasswordLength = 10;

    private readonly IAccountCredentialStore _credentialStore;
    private readonly IPasswordHasher _passwordHasher;
    private readonly int _historyLimit;

    public PasswordChangeService(
        IAccountCredentialStore credentialStore,
        IPasswordHasher passwordHasher,
        int historyLimit = DefaultHistoryLimit)
    {
        _credentialStore = credentialStore ?? throw new ArgumentNullException(nameof(credentialStore));
        _passwordHasher = passwordHasher ?? throw new ArgumentNullException(nameof(passwordHasher));
        if (historyLimit < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(historyLimit),
                "History limit must be at least 1.");
        }

        _historyLimit = historyLimit;
    }

    /// <summary>
    /// Rotates the password of a user account. Checks run in a fixed order: unknown account,
    /// blocked account, current password verification, password reuse against the current hash
    /// and the recent history, complexity policy. On success the credential version is
    /// incremented, the old hash is archived and all sessions except currentSessionId
    /// (when supplied) are revoked.
    /// </summary>
    public async global::System.Threading.Tasks.Task<PasswordChangeResult> ChangePasswordAsync(
        Guid organizationId,
        Guid userId,
        string currentPassword,
        string newPassword,
        Guid? currentSessionId,
        CancellationToken cancellationToken = default)
    {
        EnsureIdentifier(organizationId, nameof(organizationId));
        EnsureIdentifier(userId, nameof(userId));
        ArgumentException.ThrowIfNullOrWhiteSpace(currentPassword);
        ArgumentException.ThrowIfNullOrWhiteSpace(newPassword);

        var credential = await _credentialStore.GetCredentialAsync(organizationId, userId, cancellationToken);
        if (credential is null)
        {
            return UnknownAccountResult;
        }

        if (string.Equals(credential.AccountStatus, BlockedAccountStatus, StringComparison.Ordinal))
        {
            return AccountBlockedResult;
        }

        var currentRecord = new PasswordHashRecord(credential.PasswordHash, credential.PasswordParameters);
        if (!_passwordHasher.VerifyPassword(currentPassword, currentRecord))
        {
            return InvalidCurrentPasswordResult;
        }

        if (_passwordHasher.VerifyPassword(newPassword, currentRecord) ||
            await IsReusedFromHistoryAsync(organizationId, userId, newPassword, cancellationToken))
        {
            return PasswordReuseDetectedResult;
        }

        if (!MeetsComplexityPolicy(newPassword))
        {
            return WeakPasswordResult;
        }

        var newHash = _passwordHasher.HashPassword(newPassword);
        var commit = await _credentialStore.CommitPasswordChangeAsync(
            organizationId,
            userId,
            currentRecord,
            newHash,
            credential.CredentialVersion,
            currentSessionId,
            cancellationToken);
        if (!commit.Succeeded)
        {
            // The account credential or authenticated session changed after validation. Fail
            // closed and expose the same caller-facing result as invalid current credentials.
            return InvalidCurrentPasswordResult;
        }

        return new PasswordChangeResult(PasswordChangeOutcome.Success, commit.RevokedSessionCount);
    }

    private async global::System.Threading.Tasks.Task<bool> IsReusedFromHistoryAsync(
        Guid organizationId,
        Guid userId,
        string newPassword,
        CancellationToken cancellationToken)
    {
        var history = await _credentialStore.GetRecentPasswordHistoryAsync(
            organizationId, userId, _historyLimit, cancellationToken);
        foreach (var historicalHash in history)
        {
            if (_passwordHasher.VerifyPassword(newPassword, historicalHash))
            {
                return true;
            }
        }

        return false;
    }

    private static bool MeetsComplexityPolicy(string password)
    {
        if (password.Length < MinimumPasswordLength)
        {
            return false;
        }

        if (!password.Any(char.IsUpper) || !password.Any(char.IsDigit))
        {
            return false;
        }

        return password.Any(character => !char.IsLetterOrDigit(character));
    }

    private static void EnsureIdentifier(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("Identifier must not be empty.", parameterName);
        }
    }

    private const string BlockedAccountStatus = "blocked";

    private static readonly PasswordChangeResult UnknownAccountResult =
        new(PasswordChangeOutcome.UnknownAccount, 0);

    private static readonly PasswordChangeResult AccountBlockedResult =
        new(PasswordChangeOutcome.AccountBlocked, 0);

    private static readonly PasswordChangeResult InvalidCurrentPasswordResult =
        new(PasswordChangeOutcome.InvalidCurrentPassword, 0);

    private static readonly PasswordChangeResult PasswordReuseDetectedResult =
        new(PasswordChangeOutcome.PasswordReuseDetected, 0);

    private static readonly PasswordChangeResult WeakPasswordResult =
        new(PasswordChangeOutcome.WeakPassword, 0);
}
