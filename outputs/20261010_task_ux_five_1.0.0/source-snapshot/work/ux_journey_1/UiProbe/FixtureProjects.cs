using Task.Desktop.Projects;
using T = System.Threading.Tasks.Task;

internal sealed class FixtureProjects : IDesktopProjectsApiClient
{
    public bool Fail { get; set; } = true;
    public List<DesktopProjectDto> Items { get; } = [];
    public System.Threading.Tasks.Task<DesktopProjectResult<DesktopProjectPage>> GetProjectsAsync(string? cursor = null, CancellationToken cancellationToken = default) =>
        T.FromResult<DesktopProjectResult<DesktopProjectPage>>(new DesktopProjectResult<DesktopProjectPage>.Succeeded(new(Items.ToArray(), null, false)));
    public System.Threading.Tasks.Task<DesktopProjectResult<DesktopProjectDto>> GetProjectAsync(Guid id, CancellationToken cancellationToken = default) =>
        T.FromResult<DesktopProjectResult<DesktopProjectDto>>(new DesktopProjectResult<DesktopProjectDto>.Succeeded(Items.Single(p => p.Id == id)));
    public System.Threading.Tasks.Task<DesktopProjectResult<IReadOnlyList<DesktopProjectRoleDto>>> GetRolesAsync(CancellationToken cancellationToken = default) =>
        T.FromResult<DesktopProjectResult<IReadOnlyList<DesktopProjectRoleDto>>>(new DesktopProjectResult<IReadOnlyList<DesktopProjectRoleDto>>.Succeeded([]));
    public System.Threading.Tasks.Task<DesktopProjectResult<IReadOnlyList<DesktopProjectMemberDto>>> GetMembersAsync(Guid projectId, CancellationToken cancellationToken = default) =>
        T.FromResult<DesktopProjectResult<IReadOnlyList<DesktopProjectMemberDto>>>(new DesktopProjectResult<IReadOnlyList<DesktopProjectMemberDto>>.Succeeded([]));
    public System.Threading.Tasks.Task<DesktopProjectResult<DesktopProjectDto>> CreateProjectAsync(DesktopProjectDraft draft, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        if (Fail) return T.FromResult<DesktopProjectResult<DesktopProjectDto>>(new DesktopProjectResult<DesktopProjectDto>.ServerUnavailable());
        var project = new DesktopProjectDto(Guid.NewGuid(), Guid.NewGuid(), 1, draft.Name, draft.Description, draft.OwnerUserId,
            draft.ManagerUserId, draft.Status, draft.StartDate, draft.PlannedEndDate, null, null, null, "active");
        Items.Add(project);
        return T.FromResult<DesktopProjectResult<DesktopProjectDto>>(new DesktopProjectResult<DesktopProjectDto>.Succeeded(project));
    }
    public System.Threading.Tasks.Task<DesktopProjectResult<DesktopProjectDto>> UpdateProjectAsync(Guid id, long version, DesktopProjectDraft draft, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public System.Threading.Tasks.Task<DesktopProjectResult<DesktopProjectDto>> ArchiveProjectAsync(Guid id, long version, string idempotencyKey, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public System.Threading.Tasks.Task<DesktopProjectResult<DesktopProjectMemberDto>> AddMemberAsync(Guid projectId, long projectVersion, Guid userId, Guid roleId, string idempotencyKey, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public System.Threading.Tasks.Task<DesktopProjectResult<DesktopProjectMemberDto>> ChangeMemberRoleAsync(Guid projectId, long projectVersion, Guid userId, Guid roleId, string idempotencyKey, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public System.Threading.Tasks.Task<DesktopProjectResult<DesktopProjectDto>> RemoveMemberAsync(Guid projectId, long projectVersion, Guid userId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
}
