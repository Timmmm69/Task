using Task.Desktop.Projects;
using Task.Desktop.TaskApi;
using Task.Desktop.ViewModels;
using Task.Domain;
using Task.Desktop.Tests.TaskScreen;

namespace Task.Desktop.Tests.Projects;

public sealed class ProjectsViewModelTests
{
    private static readonly Guid Organization = Guid.NewGuid();
    private static readonly Guid User = Guid.NewGuid();
    private static readonly Guid ProjectId = Guid.NewGuid();
    private static readonly Guid RoleId = Guid.NewGuid();
    private static readonly string[] Capabilities =
        ["Project.Read", "Project.Create", "Project.Update", "Project.Archive", "Project.ManageMembers", "Task.Read"];

    [Fact]
    public async System.Threading.Tasks.Task ActivationLoadsRolesMembersAndOnlyTasksFromSelectedProject()
    {
        var client = new FakeProjectsClient();
        client.Projects = SuccessPage(Project());
        client.Project = new DesktopProjectResult<DesktopProjectDto>.Succeeded(Project());
        client.Roles = new DesktopProjectResult<IReadOnlyList<DesktopProjectRoleDto>>.Succeeded([Role()]);
        client.Members = new DesktopProjectResult<IReadOnlyList<DesktopProjectMemberDto>>.Succeeded(
            [new(ProjectId, User, RoleId, "active", 1, DateTimeOffset.UtcNow, null)]);
        var tasks = new TasksViewModelTests.FakeTasksApiClient();
        tasks.EnqueuePage(new DesktopTasksApiResult<DesktopTaskPage>.Succeeded(new([
            Task(Guid.NewGuid(), ProjectId, "Связанная"),
            Task(Guid.NewGuid(), Guid.NewGuid(), "Чужая")], null, 2)));

        using var vm = new ProjectsViewModel(client, User, Capabilities, tasks);
        vm.Activate();
        await WaitUntil(() => vm.State == ProjectsScreenState.Loaded && vm.Members.Count == 1 && vm.RelatedTasks.Count == 1);

        Assert.Equal("Проект Альфа", vm.SelectedItem?.Name);
        Assert.Equal("Редактор", vm.Members[0].RoleText);
        Assert.Equal("Связанная", vm.RelatedTasks[0].Title);
        Assert.Equal(1, client.RoleCalls);
        Assert.Equal(1, client.MemberCalls);
    }

    [Fact]
    public async System.Threading.Tasks.Task CreateUsesCurrentUserAndAppliesAuthoritativeResponse()
    {
        var client = new FakeProjectsClient
        {
            Projects = SuccessPage(),
            Roles = new DesktopProjectResult<IReadOnlyList<DesktopProjectRoleDto>>.Succeeded([Role()]),
            Create = new DesktopProjectResult<DesktopProjectDto>.Succeeded(Project() with { Name = "Новый проект" })
        };
        using var vm = new ProjectsViewModel(client, User, Capabilities);
        vm.Activate();
        await WaitUntil(() => vm.State == ProjectsScreenState.Empty);
        await vm.NewProjectCommand.ExecuteAsync();
        vm.Editor!.Name = " Новый проект ";
        vm.Editor.Status = vm.Editor.Statuses.Single(option => option.Value == DesktopProjectStatus.Active);

        await vm.SaveProjectCommand.ExecuteAsync();

        Assert.Null(vm.Editor);
        Assert.Equal("Новый проект", vm.SelectedItem?.Name);
        Assert.Equal(User, client.LastDraft?.OwnerUserId);
        Assert.Equal(1, client.CreateCalls);
    }

    [Fact]
    public async System.Threading.Tasks.Task MemberMutationAdvancesProjectVersionAndReloadsMembership()
    {
        var client = new FakeProjectsClient
        {
            Projects = SuccessPage(Project()), Project = new DesktopProjectResult<DesktopProjectDto>.Succeeded(Project()),
            Roles = new DesktopProjectResult<IReadOnlyList<DesktopProjectRoleDto>>.Succeeded([Role()]),
            Members = new DesktopProjectResult<IReadOnlyList<DesktopProjectMemberDto>>.Succeeded([]),
            Add = new DesktopProjectResult<DesktopProjectMemberDto>.Succeeded(
                new(ProjectId, Guid.NewGuid(), RoleId, "active", 1, DateTimeOffset.UtcNow, null), 2)
        };
        using var vm = new ProjectsViewModel(client, User, Capabilities);
        vm.Activate();
        await WaitUntil(() => vm.State == ProjectsScreenState.Loaded && !vm.IsDetailLoading);
        var addedUser = Guid.NewGuid();
        vm.NewMemberUserId = addedUser.ToString("D"); vm.NewMemberRole = vm.Roles.Single();

        await vm.AddMemberCommand.ExecuteAsync();

        Assert.Equal(ProjectId, client.LastMemberProject);
        Assert.Equal(1, client.LastMemberProjectVersion);
        Assert.Equal(addedUser, client.LastMemberUser);
        Assert.Equal(2, vm.SelectedItem?.Source.Version);
        Assert.Equal(string.Empty, vm.NewMemberUserId);
    }

    [Fact]
    public void MissingReadPermissionKeepsProjectDataClosed()
    {
        var client = new FakeProjectsClient();
        using var vm = new ProjectsViewModel(client, User, ["Project.Create"]);
        vm.Activate();
        Assert.Equal(ProjectsScreenState.Forbidden, vm.State);
        Assert.Empty(vm.Items);
        Assert.Equal(0, client.ProjectPageCalls);
        Assert.False(vm.NewProjectCommand.CanExecute(null));
    }

    [Fact]
    public void EditorRejectsInvalidDatesAndCompletesWithServerTimestamp()
    {
        var editor = new ProjectEditorViewModel(User) { Name = "Альфа", StartDate = "10.09.2026", PlannedEndDate = "09.09.2026" };
        Assert.False(editor.TryBuild(out _));
        Assert.Contains("раньше", editor.Error);
        editor.PlannedEndDate = "11.09.2026";
        editor.Status = editor.Statuses.Single(option => option.Value == DesktopProjectStatus.Completed);
        Assert.True(editor.TryBuild(out var draft));
        Assert.NotNull(draft.ActualEndAt);
        Assert.Equal(TimeSpan.Zero, draft.ActualEndAt?.Offset);
    }

    private static DesktopProjectDto Project() => new(ProjectId, Organization, 1, "Проект Альфа", "Описание", User,
        null, DesktopProjectStatus.Active, new DateOnly(2026, 9, 1), new DateOnly(2026, 10, 1), null, "Europe/Minsk", "#336699", "active");
    private static DesktopProjectRoleDto Role() => new(RoleId, "project_editor", "Редактор", false, "active", ["project.read", "task.update"]);
    private static DesktopProjectResult<DesktopProjectPage> SuccessPage(params DesktopProjectDto[] projects) =>
        new DesktopProjectResult<DesktopProjectPage>.Succeeded(new(projects, null, false));
    private static DesktopTaskDto Task(Guid id, Guid projectId, string title) => new(id, Organization, 1,
        DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, title, User, DesktopTaskStatus.New, DesktopTaskPriority.Normal,
        null, null, [], [], null, new TaskCardContent { ProjectId = projectId });

    private static async System.Threading.Tasks.Task WaitUntil(Func<bool> predicate)
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        while (!predicate()) await System.Threading.Tasks.Task.Delay(10, cancellation.Token);
    }

    private sealed class FakeProjectsClient : IDesktopProjectsApiClient
    {
        public DesktopProjectResult<DesktopProjectPage> Projects { get; set; } = SuccessPage();
        public DesktopProjectResult<DesktopProjectDto> Project { get; set; } = new DesktopProjectResult<DesktopProjectDto>.NotFound();
        public DesktopProjectResult<IReadOnlyList<DesktopProjectRoleDto>> Roles { get; set; } = new DesktopProjectResult<IReadOnlyList<DesktopProjectRoleDto>>.Succeeded([]);
        public DesktopProjectResult<IReadOnlyList<DesktopProjectMemberDto>> Members { get; set; } = new DesktopProjectResult<IReadOnlyList<DesktopProjectMemberDto>>.Succeeded([]);
        public DesktopProjectResult<DesktopProjectDto> Create { get; set; } = new DesktopProjectResult<DesktopProjectDto>.ServerUnavailable();
        public DesktopProjectResult<DesktopProjectDto> Update { get; set; } = new DesktopProjectResult<DesktopProjectDto>.ServerUnavailable();
        public DesktopProjectResult<DesktopProjectDto> Archive { get; set; } = new DesktopProjectResult<DesktopProjectDto>.ServerUnavailable();
        public DesktopProjectResult<DesktopProjectMemberDto> Add { get; set; } = new DesktopProjectResult<DesktopProjectMemberDto>.ServerUnavailable();
        public int ProjectPageCalls { get; private set; }
        public int RoleCalls { get; private set; }
        public int MemberCalls { get; private set; }
        public int CreateCalls { get; private set; }
        public DesktopProjectDraft? LastDraft { get; private set; }
        public Guid LastMemberProject { get; private set; }
        public long LastMemberProjectVersion { get; private set; }
        public Guid LastMemberUser { get; private set; }

        public System.Threading.Tasks.Task<DesktopProjectResult<DesktopProjectPage>> GetProjectsAsync(string? cursor = null, CancellationToken cancellationToken = default)
        { ProjectPageCalls++; return System.Threading.Tasks.Task.FromResult(Projects); }
        public System.Threading.Tasks.Task<DesktopProjectResult<DesktopProjectDto>> GetProjectAsync(Guid id, CancellationToken cancellationToken = default) => System.Threading.Tasks.Task.FromResult(Project);
        public System.Threading.Tasks.Task<DesktopProjectResult<IReadOnlyList<DesktopProjectRoleDto>>> GetRolesAsync(CancellationToken cancellationToken = default)
        { RoleCalls++; return System.Threading.Tasks.Task.FromResult(Roles); }
        public System.Threading.Tasks.Task<DesktopProjectResult<IReadOnlyList<DesktopProjectMemberDto>>> GetMembersAsync(Guid projectId, CancellationToken cancellationToken = default)
        { MemberCalls++; return System.Threading.Tasks.Task.FromResult(Members); }
        public System.Threading.Tasks.Task<DesktopProjectResult<DesktopProjectDto>> CreateProjectAsync(DesktopProjectDraft draft, string idempotencyKey, CancellationToken cancellationToken = default)
        { CreateCalls++; LastDraft = draft; return System.Threading.Tasks.Task.FromResult(Create); }
        public System.Threading.Tasks.Task<DesktopProjectResult<DesktopProjectDto>> UpdateProjectAsync(Guid id, long version, DesktopProjectDraft draft, CancellationToken cancellationToken = default)
        { LastDraft = draft; return System.Threading.Tasks.Task.FromResult(Update); }
        public System.Threading.Tasks.Task<DesktopProjectResult<DesktopProjectDto>> ArchiveProjectAsync(Guid id, long version, string idempotencyKey, CancellationToken cancellationToken = default) => System.Threading.Tasks.Task.FromResult(Archive);
        public System.Threading.Tasks.Task<DesktopProjectResult<DesktopProjectMemberDto>> AddMemberAsync(Guid projectId, long projectVersion, Guid userId, Guid roleId, string idempotencyKey, CancellationToken cancellationToken = default)
        { LastMemberProject = projectId; LastMemberProjectVersion = projectVersion; LastMemberUser = userId; return System.Threading.Tasks.Task.FromResult(Add); }
        public System.Threading.Tasks.Task<DesktopProjectResult<DesktopProjectMemberDto>> ChangeMemberRoleAsync(Guid projectId, long projectVersion, Guid userId, Guid roleId, string idempotencyKey, CancellationToken cancellationToken = default) => System.Threading.Tasks.Task.FromResult(Add);
        public System.Threading.Tasks.Task<DesktopProjectResult<DesktopProjectDto>> RemoveMemberAsync(Guid projectId, long projectVersion, Guid userId, CancellationToken cancellationToken = default) => System.Threading.Tasks.Task.FromResult(Update);
    }
}
