using System.Text.Json.Serialization;
using Task.Application.Security;

namespace Task.Desktop.Administration;

/// <summary>The permitted User projection; contains no credentials or session material.</summary>
public sealed record DesktopManagedUser(
    [property: JsonRequired] Guid Id,
    [property: JsonRequired] Guid OrganizationId,
    [property: JsonRequired] long Version,
    [property: JsonRequired] string DisplayName,
    [property: JsonRequired] string FirstName,
    [property: JsonRequired] string LastName,
    [property: JsonRequired] string Login,
    string? WorkEmail,
    Guid? DepartmentId,
    string? JobTitle,
    [property: JsonRequired] string AccountStatus,
    DateTimeOffset? CreatedAt = null,
    DateTimeOffset? UpdatedAt = null);

/// <summary>Only the currently supported server query. Cursor takes precedence over Page.</summary>
public sealed record DesktopUserQuery(string? Filter = null, string? Cursor = null, int Page = 1);
public sealed record DesktopUserPage(IReadOnlyList<DesktopManagedUser> Items, string? NextCursor, long Total);
public sealed record DesktopUserCreate(string FirstName, string LastName, string Login,
    string? DisplayName = null, string? WorkEmail = null, Guid? DepartmentId = null, string? JobTitle = null);

/// <summary>Unspecified fields are omitted. Specified null clears only nullable fields.</summary>
public sealed record DesktopUserPatch
{
    public OptionalUserField<string> DisplayName { get; init; } = new(false, null);
    public OptionalUserField<string> FirstName { get; init; } = new(false, null);
    public OptionalUserField<string> LastName { get; init; } = new(false, null);
    public OptionalUserField<string> Login { get; init; } = new(false, null);
    public OptionalUserField<string?> WorkEmail { get; init; } = new(false, null);
    public OptionalUserField<Guid?> DepartmentId { get; init; } = new(false, null);
    public OptionalUserField<string?> JobTitle { get; init; } = new(false, null);
}

public enum DesktopUserTransition { Activate, Block, Deactivate, Reactivate, Unblock }

public abstract record DesktopUserResult<T>
{
    private DesktopUserResult() { }
    public string? ErrorCode { get; init; }
    public string? CorrelationId { get; init; }
    public bool? Retryable { get; init; }
    public int? RetryAfterSeconds { get; init; }
    public sealed record Succeeded(T Value, long? EntityVersion = null, bool Replayed = false) : DesktopUserResult<T>;
    public sealed record AuthenticationFailure : DesktopUserResult<T>;
    public sealed record Forbidden : DesktopUserResult<T>;
    public sealed record NotFound : DesktopUserResult<T>;
    public sealed record ValidationFailure(string Message) : DesktopUserResult<T>;
    public sealed record Conflict : DesktopUserResult<T>;
    public sealed record VersionConflict : DesktopUserResult<T>;
    public sealed record PreconditionRequired : DesktopUserResult<T>;
    public sealed record ServerUnavailable : DesktopUserResult<T>;
    public sealed record MalformedResponse : DesktopUserResult<T>;
}

public partial interface IDesktopAdministrationApiClient
{
    System.Threading.Tasks.Task<DesktopUserResult<DesktopUserPage>> GetUserPageAsync(
        DesktopUserQuery query, CancellationToken cancellationToken = default);
    System.Threading.Tasks.Task<DesktopUserResult<DesktopManagedUser>> GetUserDetailsAsync(
        Guid id, CancellationToken cancellationToken = default);
    System.Threading.Tasks.Task<DesktopUserResult<DesktopManagedUser>> CreateUserAsync(
        DesktopUserCreate draft, string idempotencyKey, CancellationToken cancellationToken = default);
    System.Threading.Tasks.Task<DesktopUserResult<DesktopManagedUser>> UpdateUserAsync(
        Guid id, long version, DesktopUserPatch patch, string idempotencyKey, CancellationToken cancellationToken = default);
    System.Threading.Tasks.Task<DesktopUserResult<DesktopManagedUser>> TransitionUserAsync(
        Guid id, long version, DesktopUserTransition transition, string? idempotencyKey = null,
        string? reason = null, CancellationToken cancellationToken = default);
}
