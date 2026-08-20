namespace Task.Application.Security;

/// <summary>
/// Credential state of a user account as loaded by the password change flow.
/// </summary>
public sealed record AccountCredential(
    string PasswordHash,
    string PasswordParameters,
    long CredentialVersion,
    string AccountStatus);

/// <summary>
/// Persistence port for credential rotation: the current hash and credential version in
/// iam.user_accounts and the disabled-password audit trail in iam.password_history.
/// </summary>
public interface IAccountCredentialStore
{
    /// <summary>
    /// Loads the current credential hash, parameters, credential version and account status.
    /// Returns null when the account does not exist in the organization (a distinct outcome
    /// for the caller).
    /// </summary>
    global::System.Threading.Tasks.Task<AccountCredential?> GetCredentialAsync(
        Guid organizationId,
        Guid userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists the new credential hash, parameters and the incremented credential version for
    /// the account. Returns true when a row was updated (the account exists), false otherwise.
    /// </summary>
    global::System.Threading.Tasks.Task<bool> UpdateCredentialAsync(
        Guid organizationId,
        Guid userId,
        PasswordHashRecord hash,
        int newCredentialVersion,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Appends a hash to the user's password history as the disabled current password.
    /// No-op when the account does not exist in the organization. iam.password_history is
    /// keyed by user_account_id; the organization parameter still scopes the insert so that
    /// history is only written for accounts of the given organization.
    /// </summary>
    global::System.Threading.Tasks.Task AddPasswordToHistoryAsync(
        Guid organizationId,
        Guid userId,
        PasswordHashRecord hash,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns up to limit most recent password history hashes of the user, newest first.
    /// </summary>
    global::System.Threading.Tasks.Task<IReadOnlyList<PasswordHashRecord>> GetRecentPasswordHistoryAsync(
        Guid organizationId,
        Guid userId,
        int limit,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Clears must_change_password on the account after a successful password rotation.
    /// Returns true when a row was updated, false when the account does not exist.
    /// </summary>
    global::System.Threading.Tasks.Task<bool> ResetMustChangePasswordAsync(
        Guid organizationId,
        Guid userId,
        CancellationToken cancellationToken = default) =>
        global::System.Threading.Tasks.Task.FromResult(false);
}