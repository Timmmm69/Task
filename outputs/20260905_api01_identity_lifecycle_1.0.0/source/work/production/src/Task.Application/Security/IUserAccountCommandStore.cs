namespace Task.Application.Security;

public sealed record IdentityCommandContext(
    Guid OrganizationId,
    Guid ActorUserId,
    Guid? ActorSessionId,
    Guid CorrelationId,
    string OperationId,
    string IdempotencyKey,
    byte[] RequestHash,
    bool CanManageAllDevices = false);

public sealed record UserAccountCreateCommand(
    string DisplayName,
    string FirstName,
    string LastName,
    string Login,
    string? WorkEmail,
    Guid? DepartmentId,
    string? JobTitle,
    PasswordHashRecord InitialCredential);

public sealed record OptionalUserField<T>(bool IsSpecified, T? Value);

public sealed record UserAccountPatchCommand(
    OptionalUserField<string> DisplayName,
    OptionalUserField<string> FirstName,
    OptionalUserField<string> LastName,
    OptionalUserField<string> Login,
    OptionalUserField<string?> WorkEmail,
    OptionalUserField<Guid?> DepartmentId,
    OptionalUserField<string?> JobTitle);

public enum UserAccountTransition
{
    Activate = 0,
    Block = 1,
    Deactivate = 2,
    Reactivate = 3,
    Unblock = 4,
}

public enum IdentityCommandDisposition
{
    Executed = 0,
    Replayed = 1,
    RequestInProgress = 2,
    IdempotencyKeyReused = 3,
    NotFound = 4,
    VersionConflict = 5,
    DuplicateResource = 6,
    InvalidStateTransition = 7,
}

public sealed record UserAccountCommandResult(
    IdentityCommandDisposition Disposition,
    UserAccountReadProjection? User = null,
    int? RetryAfterSeconds = null);

public sealed record PasswordResetCommandResult(
    IdentityCommandDisposition Disposition,
    long? Version = null,
    int? RetryAfterSeconds = null,
    DateTimeOffset? ExpiresAtUtc = null);

public interface IUserAccountCommandStore
{
    global::System.Threading.Tasks.Task<UserAccountCommandResult> CreateAsync(
        IdentityCommandContext context,
        UserAccountCreateCommand command,
        CancellationToken cancellationToken = default);

    global::System.Threading.Tasks.Task<UserAccountCommandResult> UpdateAsync(
        IdentityCommandContext context,
        Guid userId,
        long expectedVersion,
        UserAccountPatchCommand command,
        CancellationToken cancellationToken = default);

    global::System.Threading.Tasks.Task<UserAccountCommandResult> TransitionAsync(
        IdentityCommandContext context,
        Guid userId,
        long expectedVersion,
        UserAccountTransition transition,
        string? reason,
        CancellationToken cancellationToken = default);

    global::System.Threading.Tasks.Task<PasswordResetCommandResult> ResetPasswordAsync(
        IdentityCommandContext context,
        Guid userId,
        long expectedVersion,
        PasswordHashRecord temporaryCredential,
        CancellationToken cancellationToken = default);
}
