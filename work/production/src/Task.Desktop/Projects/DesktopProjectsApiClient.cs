using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using Task.Desktop.Security;

namespace Task.Desktop.Projects;

public enum DesktopProjectStatus { Planning, Active, Paused, Completed }

public sealed record DesktopProjectDto(
    Guid Id, Guid OrganizationId, long Version, string Name, string? Description,
    Guid OwnerUserId, Guid? ManagerUserId, DesktopProjectStatus Status,
    DateOnly? StartDate, DateOnly? PlannedEndDate, DateTimeOffset? ActualEndAt,
    string? DefaultTimeZone, string? ColorCode, string LifecycleState);

public sealed record DesktopProjectPage(
    IReadOnlyList<DesktopProjectDto> Items, string? NextCursor, bool HasMore);

public sealed record DesktopProjectMemberDto(
    Guid ProjectId, Guid UserAccountId, Guid ProjectRoleId, string Status,
    long Version, DateTimeOffset? JoinedAt, DateTimeOffset? RemovedAt);

public sealed record DesktopProjectRoleDto(
    Guid Id, string Code, string Name, bool IsSystem, string Status,
    IReadOnlyList<string> PermissionCodes);

public sealed record DesktopProjectDraft(
    string Name, string? Description, Guid OwnerUserId, Guid? ManagerUserId,
    DesktopProjectStatus Status, DateOnly? StartDate, DateOnly? PlannedEndDate,
    DateTimeOffset? ActualEndAt = null, string? DefaultTimeZone = null,
    string? ColorCode = null);

public abstract record DesktopProjectResult<T> where T : class
{
    private DesktopProjectResult() { }
    public sealed record Succeeded(T Value, long? EntityVersion = null) : DesktopProjectResult<T>;
    public sealed record AuthenticationFailure : DesktopProjectResult<T>;
    public sealed record Forbidden : DesktopProjectResult<T>;
    public sealed record NotFound : DesktopProjectResult<T>;
    public sealed record VersionConflict : DesktopProjectResult<T>;
    public sealed record ValidationFailure(string Message) : DesktopProjectResult<T>;
    public sealed record InvalidState : DesktopProjectResult<T>;
    public sealed record ServerUnavailable : DesktopProjectResult<T>;
    public sealed record MalformedResponse : DesktopProjectResult<T>;
}

public interface IDesktopProjectsApiClient
{
    System.Threading.Tasks.Task<DesktopProjectResult<DesktopProjectPage>> GetProjectsAsync(
        string? cursor = null, CancellationToken cancellationToken = default);
    System.Threading.Tasks.Task<DesktopProjectResult<DesktopProjectDto>> GetProjectAsync(
        Guid id, CancellationToken cancellationToken = default);
    System.Threading.Tasks.Task<DesktopProjectResult<IReadOnlyList<DesktopProjectRoleDto>>> GetRolesAsync(
        CancellationToken cancellationToken = default);
    System.Threading.Tasks.Task<DesktopProjectResult<IReadOnlyList<DesktopProjectMemberDto>>> GetMembersAsync(
        Guid projectId, CancellationToken cancellationToken = default);
    System.Threading.Tasks.Task<DesktopProjectResult<DesktopProjectDto>> CreateProjectAsync(
        DesktopProjectDraft draft, string idempotencyKey, CancellationToken cancellationToken = default);
    System.Threading.Tasks.Task<DesktopProjectResult<DesktopProjectDto>> UpdateProjectAsync(
        Guid id, long version, DesktopProjectDraft draft, CancellationToken cancellationToken = default);
    System.Threading.Tasks.Task<DesktopProjectResult<DesktopProjectDto>> ArchiveProjectAsync(
        Guid id, long version, string idempotencyKey, CancellationToken cancellationToken = default);
    System.Threading.Tasks.Task<DesktopProjectResult<DesktopProjectMemberDto>> AddMemberAsync(
        Guid projectId, long projectVersion, Guid userId, Guid roleId,
        string idempotencyKey, CancellationToken cancellationToken = default);
    System.Threading.Tasks.Task<DesktopProjectResult<DesktopProjectMemberDto>> ChangeMemberRoleAsync(
        Guid projectId, long projectVersion, Guid userId, Guid roleId,
        string idempotencyKey, CancellationToken cancellationToken = default);
    System.Threading.Tasks.Task<DesktopProjectResult<DesktopProjectDto>> RemoveMemberAsync(
        Guid projectId, long projectVersion, Guid userId, CancellationToken cancellationToken = default);
}

public sealed class DesktopProjectsApiClient : IDesktopProjectsApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly DesktopAuthenticatedGetExecutor _executor;
    private readonly Uri _projectsUri;
    private readonly Uri _rolesUri;

    public DesktopProjectsApiClient(HttpClient httpClient, Uri serverEndpoint, SessionService sessionService)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(serverEndpoint);
        ArgumentNullException.ThrowIfNull(sessionService);
        if (!serverEndpoint.IsAbsoluteUri) throw new ArgumentException("The server endpoint must be absolute.", nameof(serverEndpoint));
        _executor = new(httpClient, sessionService);
        var root = serverEndpoint.AbsoluteUri.TrimEnd('/');
        _projectsUri = new($"{root}/api/v1/projects", UriKind.Absolute);
        _rolesUri = new($"{root}/api/v1/project-roles", UriKind.Absolute);
    }

    public async System.Threading.Tasks.Task<DesktopProjectResult<DesktopProjectPage>> GetProjectsAsync(
        string? cursor = null, CancellationToken cancellationToken = default)
    {
        var uri = string.IsNullOrWhiteSpace(cursor) ? _projectsUri
            : new Uri($"{_projectsUri.AbsoluteUri}?cursor={Uri.EscapeDataString(cursor.Trim())}");
        var result = await _executor.GetAsync(uri, NewCorrelation(), cancellationToken).ConfigureAwait(false);
        return result is AuthenticatedGetResult.Response { StatusCode: HttpStatusCode.OK } response
            ? TryReadPage(response.Body, out var page) ? new DesktopProjectResult<DesktopProjectPage>.Succeeded(page) : new DesktopProjectResult<DesktopProjectPage>.MalformedResponse()
            : Failure<DesktopProjectPage>(result);
    }

    public async System.Threading.Tasks.Task<DesktopProjectResult<DesktopProjectDto>> GetProjectAsync(
        Guid id, CancellationToken cancellationToken = default)
    {
        RequireId(id, nameof(id));
        var result = await _executor.GetAsync(new Uri($"{_projectsUri.AbsoluteUri}/{id:D}"), NewCorrelation(), cancellationToken).ConfigureAwait(false);
        return ReadProjectResult(result);
    }

    public async System.Threading.Tasks.Task<DesktopProjectResult<IReadOnlyList<DesktopProjectRoleDto>>> GetRolesAsync(
        CancellationToken cancellationToken = default)
    {
        var result = await _executor.GetAsync(_rolesUri, NewCorrelation(), cancellationToken).ConfigureAwait(false);
        if (result is not AuthenticatedGetResult.Response { StatusCode: HttpStatusCode.OK } response)
            return Failure<IReadOnlyList<DesktopProjectRoleDto>>(result);
        try
        {
            var payload = JsonSerializer.Deserialize<List<RolePayload>>(response.Body, JsonOptions);
            if (payload is null || payload.Count > 200 || payload.Any(item => !item.IsValid))
                return new DesktopProjectResult<IReadOnlyList<DesktopProjectRoleDto>>.MalformedResponse();
            return new DesktopProjectResult<IReadOnlyList<DesktopProjectRoleDto>>.Succeeded(
                payload.Select(item => new DesktopProjectRoleDto(item.Id, item.Code!, item.Name!, item.IsSystem,
                    item.Status!, Array.AsReadOnly(item.PermissionCodes!))).ToArray());
        }
        catch (JsonException) { return new DesktopProjectResult<IReadOnlyList<DesktopProjectRoleDto>>.MalformedResponse(); }
    }

    public async System.Threading.Tasks.Task<DesktopProjectResult<IReadOnlyList<DesktopProjectMemberDto>>> GetMembersAsync(
        Guid projectId, CancellationToken cancellationToken = default)
    {
        RequireId(projectId, nameof(projectId));
        var members = new List<DesktopProjectMemberDto>();
        long? projectVersion = null;
        for (var page = 1; page <= 51; page++)
        {
            var result = await _executor.GetAsync(new Uri($"{_projectsUri.AbsoluteUri}/{projectId:D}/members?limit=200&page={page}"), NewCorrelation(), cancellationToken).ConfigureAwait(false);
            if (result is not AuthenticatedGetResult.Response { StatusCode: HttpStatusCode.OK } response)
                return Failure<IReadOnlyList<DesktopProjectMemberDto>>(result);
            try
            {
                var payload = JsonSerializer.Deserialize<MemberPagePayload>(response.Body, JsonOptions);
                if (payload?.Items is null || payload.Items.Count > 200 || payload.Items.Any(item => !TryMapMember(item, out _)))
                    return new DesktopProjectResult<IReadOnlyList<DesktopProjectMemberDto>>.MalformedResponse();
                members.AddRange(payload.Items.Select(item => { TryMapMember(item, out var member); return member; }));
                var pageVersion = ReadEntityTag(response.EntityTag);
                if (pageVersion is null)
                    return new DesktopProjectResult<IReadOnlyList<DesktopProjectMemberDto>>.MalformedResponse();
                if (projectVersion is not null && pageVersion != projectVersion)
                    return new DesktopProjectResult<IReadOnlyList<DesktopProjectMemberDto>>.VersionConflict();
                projectVersion = pageVersion;
                if (payload.Items.Count < 200)
                    return new DesktopProjectResult<IReadOnlyList<DesktopProjectMemberDto>>.Succeeded(members.ToArray(), projectVersion);
            }
            catch (JsonException) { return new DesktopProjectResult<IReadOnlyList<DesktopProjectMemberDto>>.MalformedResponse(); }
        }
        return new DesktopProjectResult<IReadOnlyList<DesktopProjectMemberDto>>.MalformedResponse();
    }

    public System.Threading.Tasks.Task<DesktopProjectResult<DesktopProjectDto>> CreateProjectAsync(
        DesktopProjectDraft draft, string idempotencyKey, CancellationToken cancellationToken = default) =>
        SendProjectAsync(HttpMethod.Post, _projectsUri, ValidateDraft(draft, true), null, RequireKey(idempotencyKey), cancellationToken);

    public System.Threading.Tasks.Task<DesktopProjectResult<DesktopProjectDto>> UpdateProjectAsync(
        Guid id, long version, DesktopProjectDraft draft, CancellationToken cancellationToken = default)
    {
        RequireVersioned(id, version);
        var body = ValidateDraft(draft, false);
        body.Remove("ownerUserId");
        return SendProjectAsync(HttpMethod.Patch, new Uri($"{_projectsUri.AbsoluteUri}/{id:D}"), body, version, null, cancellationToken);
    }

    public System.Threading.Tasks.Task<DesktopProjectResult<DesktopProjectDto>> ArchiveProjectAsync(
        Guid id, long version, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        RequireVersioned(id, version);
        return SendProjectAsync(HttpMethod.Post, new Uri($"{_projectsUri.AbsoluteUri}/{id:D}/archive"),
            new JsonObject { ["expectedVersion"] = version }, version, RequireKey(idempotencyKey), cancellationToken);
    }

    public async System.Threading.Tasks.Task<DesktopProjectResult<DesktopProjectMemberDto>> AddMemberAsync(
        Guid projectId, long projectVersion, Guid userId, Guid roleId, string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        RequireMemberCommand(projectId, projectVersion, userId, roleId);
        var body = new JsonObject { ["projectId"] = projectId, ["userAccountId"] = userId, ["projectRoleId"] = roleId, ["status"] = "active" };
        var result = await SendAsync(HttpMethod.Post, new Uri($"{_projectsUri.AbsoluteUri}/{projectId:D}/members"), body,
            projectVersion, RequireKey(idempotencyKey), cancellationToken).ConfigureAwait(false);
        return ReadMemberResult(result);
    }

    public async System.Threading.Tasks.Task<DesktopProjectResult<DesktopProjectMemberDto>> ChangeMemberRoleAsync(
        Guid projectId, long projectVersion, Guid userId, Guid roleId, string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        RequireMemberCommand(projectId, projectVersion, userId, roleId);
        var result = await SendAsync(HttpMethod.Patch, new Uri($"{_projectsUri.AbsoluteUri}/{projectId:D}/members/{userId:D}"),
            new JsonObject { ["projectRoleId"] = roleId }, projectVersion, RequireKey(idempotencyKey), cancellationToken).ConfigureAwait(false);
        return ReadMemberResult(result);
    }

    public async System.Threading.Tasks.Task<DesktopProjectResult<DesktopProjectDto>> RemoveMemberAsync(
        Guid projectId, long projectVersion, Guid userId, CancellationToken cancellationToken = default)
    {
        RequireVersioned(projectId, projectVersion); RequireId(userId, nameof(userId));
        var result = await SendAsync(HttpMethod.Delete, new Uri($"{_projectsUri.AbsoluteUri}/{projectId:D}/members/{userId:D}"),
            new JsonObject(), projectVersion, null, cancellationToken).ConfigureAwait(false);
        if (result is AuthenticatedGetResult.Response { StatusCode: HttpStatusCode.NoContent } response)
            return ReadEntityTag(response.EntityTag) is long version
                ? new DesktopProjectResult<DesktopProjectDto>.Succeeded(null!, version)
                : new DesktopProjectResult<DesktopProjectDto>.MalformedResponse();
        return Failure<DesktopProjectDto>(result);
    }

    private async System.Threading.Tasks.Task<DesktopProjectResult<DesktopProjectDto>> SendProjectAsync(
        HttpMethod method, Uri uri, JsonObject body, long? version, string? key, CancellationToken cancellationToken) =>
        ReadProjectResult(await SendAsync(method, uri, body, version, key, cancellationToken).ConfigureAwait(false));

    private System.Threading.Tasks.Task<AuthenticatedGetResult> SendAsync(
        HttpMethod method, Uri uri, JsonObject body, long? version, string? key, CancellationToken cancellationToken) =>
        _executor.SendAsync(method, uri, JsonSerializer.SerializeToUtf8Bytes(body, JsonOptions), NewCorrelation(),
            version is null ? null : $"\"v{version.Value}\"", key, cancellationToken);

    private static DesktopProjectResult<DesktopProjectDto> ReadProjectResult(AuthenticatedGetResult result)
    {
        if (result is AuthenticatedGetResult.Response { StatusCode: HttpStatusCode.OK or HttpStatusCode.Created } response)
            return TryReadProject(response.Body, out var project) && ReadEntityTag(response.EntityTag) is long tag && tag == project.Version
                ? new DesktopProjectResult<DesktopProjectDto>.Succeeded(project, project.Version)
                : new DesktopProjectResult<DesktopProjectDto>.MalformedResponse();
        return Failure<DesktopProjectDto>(result);
    }

    private static DesktopProjectResult<DesktopProjectMemberDto> ReadMemberResult(AuthenticatedGetResult result)
    {
        if (result is AuthenticatedGetResult.Response { StatusCode: HttpStatusCode.OK or HttpStatusCode.Created } response)
        {
            try
            {
                var payload = JsonSerializer.Deserialize<MemberPayload>(response.Body, JsonOptions);
                return TryMapMember(payload, out var member) && ReadEntityTag(response.EntityTag) is long version
                    ? new DesktopProjectResult<DesktopProjectMemberDto>.Succeeded(member, version)
                    : new DesktopProjectResult<DesktopProjectMemberDto>.MalformedResponse();
            }
            catch (JsonException) { return new DesktopProjectResult<DesktopProjectMemberDto>.MalformedResponse(); }
        }
        return Failure<DesktopProjectMemberDto>(result);
    }

    private static DesktopProjectResult<T> Failure<T>(AuthenticatedGetResult result) where T : class
    {
        if (result is AuthenticatedGetResult.AuthenticationFailure) return new DesktopProjectResult<T>.AuthenticationFailure();
        if (result is AuthenticatedGetResult.ServerUnavailable || result is AuthenticatedGetResult.Response { StatusCode: >= HttpStatusCode.InternalServerError }) return new DesktopProjectResult<T>.ServerUnavailable();
        if (result is AuthenticatedGetResult.Response { StatusCode: HttpStatusCode.Forbidden }) return new DesktopProjectResult<T>.Forbidden();
        if (result is AuthenticatedGetResult.Response { StatusCode: HttpStatusCode.NotFound }) return new DesktopProjectResult<T>.NotFound();
        if (result is AuthenticatedGetResult.Response { StatusCode: HttpStatusCode.PreconditionFailed }) return new DesktopProjectResult<T>.VersionConflict();
        if (result is AuthenticatedGetResult.Response { StatusCode: HttpStatusCode.UnprocessableEntity or HttpStatusCode.BadRequest } response)
            return new DesktopProjectResult<T>.ValidationFailure(ReadProblem(response.Body));
        if (result is AuthenticatedGetResult.Response { StatusCode: HttpStatusCode.Conflict }) return new DesktopProjectResult<T>.InvalidState();
        return new DesktopProjectResult<T>.MalformedResponse();
    }

    private static string ReadProblem(string body)
    {
        try { return JsonNode.Parse(body)?["title"]?.GetValue<string>() ?? "Проверьте введённые данные."; }
        catch (JsonException) { return "Проверьте введённые данные."; }
    }

    private static JsonObject ValidateDraft(DesktopProjectDraft draft, bool create)
    {
        ArgumentNullException.ThrowIfNull(draft);
        var name = draft.Name?.Trim();
        if (name is not { Length: >= 1 and <= 300 }) throw new ArgumentException("Project name must contain 1-300 characters.");
        if (draft.Description?.Length > 10000) throw new ArgumentException("Project description is too long.");
        RequireId(draft.OwnerUserId, nameof(draft.OwnerUserId));
        if (draft.ManagerUserId == Guid.Empty) throw new ArgumentException("Manager id must not be empty.");
        if (draft.StartDate.HasValue && draft.PlannedEndDate < draft.StartDate) throw new ArgumentException("Planned end cannot precede start.");
        if (draft.Status == DesktopProjectStatus.Completed && draft.ActualEndAt is null) throw new ArgumentException("Completed project requires actual end time.");
        if (draft.ActualEndAt is { Offset: not { Ticks: 0 } }) throw new ArgumentException("Actual end time must use UTC.");
        if (draft.ColorCode is not null && !System.Text.RegularExpressions.Regex.IsMatch(draft.ColorCode, "^#[0-9A-Fa-f]{6}([0-9A-Fa-f]{2})?$")) throw new ArgumentException("Invalid project color.");
        var body = new JsonObject
        {
            ["name"] = name, ["description"] = draft.Description?.Trim(), ["ownerUserId"] = draft.OwnerUserId,
            ["managerUserId"] = draft.ManagerUserId, ["status"] = StatusValue(draft.Status),
            ["startDate"] = draft.StartDate?.ToString("yyyy-MM-dd"), ["plannedEndDate"] = draft.PlannedEndDate?.ToString("yyyy-MM-dd"),
            ["actualEndAt"] = draft.ActualEndAt?.ToUniversalTime().ToString("O"), ["defaultTimeZone"] = draft.DefaultTimeZone,
            ["colorCode"] = draft.ColorCode
        };
        if (!create) body.Remove("ownerUserId");
        return body;
    }

    private static bool TryReadPage(string body, out DesktopProjectPage page)
    {
        page = null!;
        try
        {
            var payload = JsonSerializer.Deserialize<ProjectPagePayload>(body, JsonOptions);
            if (payload?.Items is null || payload.Items.Count > 200 || payload.NextCursor?.Length > 2048) return false;
            var items = new List<DesktopProjectDto>(payload.Items.Count);
            foreach (var item in payload.Items) { if (!TryMapProject(item, out var project)) return false; items.Add(project); }
            page = new(items, payload.NextCursor, payload.HasMore);
            return !payload.HasMore || !string.IsNullOrWhiteSpace(payload.NextCursor);
        }
        catch (JsonException) { return false; }
    }

    private static bool TryReadProject(string body, out DesktopProjectDto project)
    {
        project = null!;
        try { return TryMapProject(JsonSerializer.Deserialize<ProjectPayload>(body, JsonOptions), out project); }
        catch (JsonException) { return false; }
    }

    private static bool TryMapProject(ProjectPayload? p, out DesktopProjectDto project)
    {
        project = null!;
        if (p is null || p.Id == Guid.Empty || p.OrganizationId == Guid.Empty || p.OwnerUserId == Guid.Empty || p.Version < 1
            || string.IsNullOrWhiteSpace(p.Name) || p.Name.Length > 300 || p.LifecycleState is not ("active" or "archived" or "trashed")
            || !TryStatus(p.Status, out var status)) return false;
        project = new(p.Id, p.OrganizationId, p.Version, p.Name.Trim(), p.Description, p.OwnerUserId,
            p.ManagerUserId, status, p.StartDate, p.PlannedEndDate, p.ActualEndAt, p.DefaultTimeZone, p.ColorCode, p.LifecycleState);
        return true;
    }

    private static bool TryMapMember(MemberPayload? p, out DesktopProjectMemberDto member)
    {
        member = null!;
        if (p is null || p.ProjectId == Guid.Empty || p.UserAccountId == Guid.Empty || p.ProjectRoleId == Guid.Empty
            || p.Version < 1 || p.Status is not ("invited" or "active" or "removed")) return false;
        member = new(p.ProjectId, p.UserAccountId, p.ProjectRoleId, p.Status, p.Version, p.JoinedAt, p.RemovedAt);
        return true;
    }

    private static bool TryStatus(string? value, out DesktopProjectStatus status) => Enum.TryParse(value switch
    { "planning" => "Planning", "active" => "Active", "paused" => "Paused", "completed" => "Completed", _ => null }, out status);
    private static string StatusValue(DesktopProjectStatus value) => value switch
    { DesktopProjectStatus.Planning => "planning", DesktopProjectStatus.Active => "active", DesktopProjectStatus.Paused => "paused", DesktopProjectStatus.Completed => "completed", _ => throw new ArgumentOutOfRangeException(nameof(value)) };
    private static void RequireId(Guid id, string name) { if (id == Guid.Empty) throw new ArgumentException("Identifier must not be empty.", name); }
    private static void RequireVersioned(Guid id, long version) { RequireId(id, nameof(id)); if (version < 1) throw new ArgumentOutOfRangeException(nameof(version)); }
    private static void RequireMemberCommand(Guid project, long version, Guid user, Guid role) { RequireVersioned(project, version); RequireId(user, nameof(user)); RequireId(role, nameof(role)); }
    private static string RequireKey(string key) => !string.IsNullOrWhiteSpace(key) && key.Length is >= 8 and <= 200 ? key : throw new ArgumentException("Invalid idempotency key.", nameof(key));
    private static string NewCorrelation() => Guid.NewGuid().ToString("D");
    private static long? ReadEntityTag(string? value) => value is { Length: >= 4 } && value.StartsWith("\"v", StringComparison.Ordinal) && value.EndsWith('"') && long.TryParse(value.AsSpan(2, value.Length - 3), out var version) && version > 0 ? version : null;

    private sealed record ProjectPagePayload(List<ProjectPayload>? Items, string? NextCursor, bool HasMore);
    private sealed record ProjectPayload(Guid Id, Guid OrganizationId, long Version, string? Name, string? Description,
        Guid OwnerUserId, Guid? ManagerUserId, string? Status, DateOnly? StartDate, DateOnly? PlannedEndDate,
        DateTimeOffset? ActualEndAt, string? DefaultTimeZone, string? ColorCode, string? LifecycleState);
    private sealed record MemberPagePayload(List<MemberPayload>? Items);
    private sealed record MemberPayload(Guid ProjectId, Guid UserAccountId, Guid ProjectRoleId, string? Status,
        long Version, DateTimeOffset? JoinedAt, DateTimeOffset? RemovedAt);
    private sealed record RolePayload(Guid Id, string? Code, string? Name, bool IsSystem, string? Status, string[]? PermissionCodes)
    {
        public bool IsValid => Id != Guid.Empty && Code is { Length: > 0 and <= 128 } && Name is { Length: > 0 and <= 300 }
            && Status == "active" && PermissionCodes is { Length: <= 200 } && PermissionCodes.All(code => code is { Length: > 0 and <= 128 });
    }
}
