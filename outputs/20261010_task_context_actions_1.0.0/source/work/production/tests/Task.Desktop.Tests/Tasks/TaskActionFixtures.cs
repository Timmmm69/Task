using Task.Desktop.Projects;
namespace Task.Desktop.Tests.TaskScreen;
internal sealed class ActionProjectsClient : IDesktopProjectsApiClient
{
    public DesktopProjectDto Project { get; set; } = new(Guid.NewGuid(), Guid.NewGuid(), 1, "Проект команды", null, Guid.NewGuid(), null, DesktopProjectStatus.Active, null, null, null, null, null, "active");
    private static System.Threading.Tasks.Task<DesktopProjectResult<T>> Ok<T>(T value) where T : class => System.Threading.Tasks.Task.FromResult<DesktopProjectResult<T>>(new DesktopProjectResult<T>.Succeeded(value));
    public System.Threading.Tasks.Task<DesktopProjectResult<DesktopProjectPage>> GetProjectsAsync(string? cursor = null, CancellationToken cancellationToken = default) => Ok(new DesktopProjectPage([Project], null, false));
    public System.Threading.Tasks.Task<DesktopProjectResult<DesktopProjectDto>> GetProjectAsync(Guid id, CancellationToken cancellationToken = default) => Ok(Project);
    public System.Threading.Tasks.Task<DesktopProjectResult<IReadOnlyList<DesktopProjectRoleDto>>> GetRolesAsync(CancellationToken cancellationToken = default) => Ok<IReadOnlyList<DesktopProjectRoleDto>>([]);
    public System.Threading.Tasks.Task<DesktopProjectResult<IReadOnlyList<DesktopProjectMemberDto>>> GetMembersAsync(Guid projectId, CancellationToken cancellationToken = default) => Ok<IReadOnlyList<DesktopProjectMemberDto>>([]);
    public System.Threading.Tasks.Task<DesktopProjectResult<DesktopProjectDto>> CreateProjectAsync(DesktopProjectDraft draft, string idempotencyKey, CancellationToken cancellationToken = default) => throw new InvalidOperationException();
    public System.Threading.Tasks.Task<DesktopProjectResult<DesktopProjectDto>> UpdateProjectAsync(Guid id, long version, DesktopProjectDraft draft, CancellationToken cancellationToken = default) => throw new InvalidOperationException();
    public System.Threading.Tasks.Task<DesktopProjectResult<DesktopProjectDto>> ArchiveProjectAsync(Guid id, long version, string idempotencyKey, CancellationToken cancellationToken = default) => throw new InvalidOperationException();
    public System.Threading.Tasks.Task<DesktopProjectResult<DesktopProjectMemberDto>> AddMemberAsync(Guid projectId, long projectVersion, Guid userId, Guid roleId, string idempotencyKey, CancellationToken cancellationToken = default) => throw new InvalidOperationException();
    public System.Threading.Tasks.Task<DesktopProjectResult<DesktopProjectMemberDto>> ChangeMemberRoleAsync(Guid projectId, long projectVersion, Guid userId, Guid roleId, string idempotencyKey, CancellationToken cancellationToken = default) => throw new InvalidOperationException();
    public System.Threading.Tasks.Task<DesktopProjectResult<DesktopProjectDto>> RemoveMemberAsync(Guid projectId, long projectVersion, Guid userId, CancellationToken cancellationToken = default) => throw new InvalidOperationException();
}
