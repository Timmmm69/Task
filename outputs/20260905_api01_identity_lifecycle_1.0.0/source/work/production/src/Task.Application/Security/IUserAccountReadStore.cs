namespace Task.Application.Security;

/// <summary>
/// Canonical Stage 2.2 User read projection.
/// Credential, password and session material must never be exposed through this model.
/// </summary>
public sealed record UserAccountReadProjection(
    Guid Id,
    Guid OrganizationId,
    long Version,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    string DisplayName,
    string FirstName,
    string LastName,
    string Login,
    string? WorkEmail,
    Guid? DepartmentId,
    string? JobTitle,
    UserAccountStatus AccountStatus);

public enum UserAccountStatus
{
    PendingActivation = 0,
    Active = 1,
    Blocked = 2,
    Deactivated = 3,
}

public interface IUserAccountReadStore
{
    global::System.Threading.Tasks.Task<UserAccountReadProjection?> GetByIdAsync(
        Guid organizationId,
        Guid userId,
        CancellationToken cancellationToken = default);

    global::System.Threading.Tasks.Task<UserAccountReadPage> GetPageAsync(
        UserAccountReadPageRequest request,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("User account paging is not configured by this store.");
}

public sealed record UserAccountReadPageRequest(
    Guid OrganizationId,
    string? Filter,
    int Page,
    Guid? Cursor,
    int PageSize = 100);

public sealed record UserAccountReadPage(
    IReadOnlyList<UserAccountReadProjection> Items,
    Guid? NextCursor,
    long Total);
