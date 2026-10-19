using System.IO;
using Task.Desktop.Projects;
using Task.Desktop.ViewModels;
using Task.Desktop.Tests.TaskScreen;
using AsyncTask = System.Threading.Tasks.Task;
using MembersResult = Task.Desktop.Projects.DesktopProjectResult<System.Collections.Generic.IReadOnlyList<Task.Desktop.Projects.DesktopProjectMemberDto>>;

namespace Task.Desktop.Tests.Projects;

public sealed partial class ProjectsViewModelTests
{
    private static FakeProjectsClient SectionClient() => new()
    {
        Projects = SuccessPage(Project()),
        ProjectHandler = (id, _) => AsyncTask.FromResult<DesktopProjectResult<DesktopProjectDto>>(
            new DesktopProjectResult<DesktopProjectDto>.Succeeded(Project() with { Id = id })),
        Roles = new DesktopProjectResult<IReadOnlyList<DesktopProjectRoleDto>>.Succeeded([Role()]),
        Members = new MembersResult.Succeeded([new(ProjectId, User, RoleId, "active", 1, null, null)])
    };

    [Fact]
    public async AsyncTask Sections_MembersLoadingNeverBlocksOverviewTasksOrExistingCommands()
    {
        var gate = new System.Threading.Tasks.TaskCompletionSource<MembersResult>();
        var client = SectionClient(); client.MembersHandler = (_, _) => gate.Task;
        var tasks = new TasksViewModelTests.FakeTasksApiClient();
        using var vm = new ProjectsViewModel(client, User, Capabilities, tasks);
        vm.Activate();
        Assert.Equal(ProjectSectionState.Loaded, vm.OverviewSection.State);
        Assert.Equal(ProjectSectionState.Loading, vm.MembersSection.State);
        Assert.False(vm.IsDetailLoading);
        Assert.True(vm.EditProjectCommand.CanExecute(null));
        Assert.True(vm.NewProjectCommand.CanExecute(null));
        Assert.True(vm.LoadRelatedTasksCommand.CanExecute(null));
        Assert.Equal(0, tasks.PageCallCount);
        gate.SetResult(new MembersResult.Forbidden()); await vm.CurrentDetailsOperation;
        Assert.Equal(ProjectSectionState.Forbidden, vm.MembersSection.State);
        Assert.Equal(ProjectSectionState.Loaded, vm.OverviewSection.State);
        Assert.Equal(ProjectsScreenState.Loaded, vm.State);
        Assert.True(vm.CanUpdate);
        Assert.True(vm.LoadRelatedTasksCommand.CanExecute(null));
    }

    [Theory]
    [InlineData("forbidden", ProjectSectionState.Forbidden)]
    [InlineData("notfound", ProjectSectionState.Forbidden)]
    [InlineData("unavailable", ProjectSectionState.ServerUnavailable)]
    [InlineData("malformed", ProjectSectionState.Error)]
    [InlineData("exception", ProjectSectionState.Error)]
    public async AsyncTask Sections_MemberFailuresAreLocalAndNeverEmpty(string failure, ProjectSectionState expected)
    {
        var client = SectionClient();
        client.MembersHandler = (_, _) => failure == "exception" ? AsyncTask.FromException<MembersResult>(new IOException("sensitive"))
            : AsyncTask.FromResult<MembersResult>(failure switch
            {
                "forbidden" => new MembersResult.Forbidden(), "notfound" => new MembersResult.NotFound(),
                "unavailable" => new MembersResult.ServerUnavailable(), _ => new MembersResult.MalformedResponse()
            });
        using var vm = new ProjectsViewModel(client, User, Capabilities);
        vm.Activate(); await vm.CurrentDetailsOperation;
        Assert.Equal(expected, vm.MembersSection.State);
        Assert.DoesNotContain("sensitive", vm.MembersSection.StatusText);
        Assert.Equal(ProjectSectionState.Loaded, vm.OverviewSection.State);
        Assert.Equal(ProjectsScreenState.Loaded, vm.State);
        Assert.NotNull(vm.SelectedItem);
        Assert.True(vm.ReloadMembersCommand.CanExecute(null));
    }

    [Theory]
    [InlineData("switch")]
    [InlineData("return")]
    [InlineData("permission")]
    [InlineData("session")]
    [InlineData("deactivate")]
    [InlineData("close")]
    [InlineData("collapse")]
    [InlineData("offline")]
    public async AsyncTask Sections_CancelAndRejectLateMembersEvenWhenTransportIgnoresCancellation(string transition)
    {
        var client = SectionClient();
        var second = Project() with { Id = Guid.NewGuid(), Name = "Второй" };
        client.Projects = SuccessPage(Project(), second);
        client.ProjectHandler = (id, _) => AsyncTask.FromResult<DesktopProjectResult<DesktopProjectDto>>(
            new DesktopProjectResult<DesktopProjectDto>.Succeeded(id == second.Id ? second : Project()));
        var gate = new System.Threading.Tasks.TaskCompletionSource<MembersResult>();
        CancellationToken token = default;
        client.MembersHandler = (_, ct) => { token = ct; return gate.Task; };
        using var vm = new ProjectsViewModel(client, User, Capabilities);
        vm.Activate(); var old = vm.CurrentDetailsOperation;
        client.MembersHandler = (id, _) => AsyncTask.FromResult<MembersResult>(new MembersResult.Succeeded([]));
        switch (transition)
        {
            case "switch": vm.SelectedItem = vm.Items[1]; break;
            case "return": var first = vm.Items[0]; vm.SelectedItem = vm.Items[1]; vm.SelectedItem = first; break;
            case "permission": vm.UpdateCapabilities(["Project.Read", "Task.Read"]); break;
            case "session": vm.UpdateSessionState(false); break;
            case "deactivate": vm.Deactivate(); break;
            case "close": vm.SelectedItem = null; break;
            case "collapse": vm.CancelMembersSection(); break;
            case "offline": vm.UpdateConnectivity(false); break;
        }
        Assert.True(token.IsCancellationRequested);
        gate.SetResult(client.Members); await old;
        Assert.Empty(vm.Members);
        Assert.NotEqual(ProjectSectionState.Loading, vm.MembersSection.State);
        Assert.All(vm.DetailSections, section => Assert.Equal(vm.SelectedItem?.Id, section.ProjectId));
        Assert.All(vm.DetailSections, section => Assert.Equal(vm.SelectedItem?.Source.OrganizationId, section.OrganizationId));
        if (transition == "switch") Assert.Equal(second.Name, vm.SelectedItem?.Name);
        if (transition == "permission") Assert.Equal(ProjectSectionState.Forbidden, vm.MembersSection.State);
    }

    [Fact]
    public async AsyncTask Sections_ReadOnlyAndUnconnectedSlotsNeverRequestForbiddenData()
    {
        var client = SectionClient();
        using var vm = new ProjectsViewModel(client, User, ["Project.Read", "Task.Read"]);
        vm.Activate(); await vm.CurrentDetailsOperation;
        Assert.Equal(0, client.MemberCalls); Assert.Equal(0, client.RoleCalls);
        Assert.Equal(ProjectSectionState.Forbidden, vm.MembersSection.State);
        Assert.False(vm.EditProjectCommand.CanExecute(null));
        Assert.False(vm.AddMemberCommand.CanExecute(null));
        Assert.False(await vm.ReloadMembersCommand.ExecuteAsync());
        foreach (var section in new[] { vm.FilesSection, vm.ContactsSection })
        {
            Assert.False(section.IsConnected); Assert.False(section.CanRead);
            Assert.Equal(ProjectSectionState.NotLoaded, section.State);
            Assert.False(section.HasConfirmedData);
        }
        vm.UpdateCapabilities([]);
        Assert.Null(vm.SelectedItem); Assert.Empty(vm.Members);
    }

    [Fact]
    public async AsyncTask Sections_OfflineAndUnavailableRetainConfirmedMembersButRevocationClearsThem()
    {
        var client = SectionClient();
        using var vm = new ProjectsViewModel(client, User, Capabilities);
        vm.Activate(); await vm.CurrentDetailsOperation;
        Assert.Single(vm.Members);
        client.Members = new MembersResult.ServerUnavailable();
        await vm.ReloadMembersCommand.ExecuteAsync();
        Assert.Equal(ProjectSectionState.ServerUnavailable, vm.MembersSection.State);
        Assert.True(vm.MembersSection.HasConfirmedData); Assert.Single(vm.Members);
        vm.UpdateConnectivity(false);
        Assert.Equal(ProjectSectionState.Offline, vm.MembersSection.State);
        Assert.Single(vm.Members); Assert.False(vm.CanUpdate);
        Assert.False(await vm.ReloadMembersCommand.ExecuteAsync());
        vm.UpdateConnectivity(true);
        Assert.Single(vm.Members);
        vm.UpdateCapabilities(["Project.Read"]);
        Assert.Empty(vm.Members); Assert.False(vm.MembersSection.HasConfirmedData);
        Assert.Equal(ProjectSectionState.Forbidden, vm.MembersSection.State);
        Assert.Equal(ProjectSectionState.Loaded, vm.OverviewSection.State);
    }

    [Fact]
    public async AsyncTask Sections_RoleFailureGivesPartialAccessAndExplicitRetryRecovers()
    {
        var client = SectionClient();
        client.Roles = new DesktopProjectResult<IReadOnlyList<DesktopProjectRoleDto>>.ServerUnavailable();
        using var vm = new ProjectsViewModel(client, User, Capabilities);
        vm.Activate(); await vm.CurrentDetailsOperation;
        Assert.Equal(ProjectSectionState.PartialAccess, vm.MembersSection.State);
        Assert.Single(vm.Members);
        client.Roles = new DesktopProjectResult<IReadOnlyList<DesktopProjectRoleDto>>.Succeeded([Role()]);
        await vm.ReloadMembersCommand.ExecuteAsync();
        Assert.Equal(ProjectSectionState.Loaded, vm.MembersSection.State);
        Assert.Equal("Редактор", vm.Members.Single().RoleText);
        vm.UpdateSessionState(false);
        Assert.Empty(vm.Members); Assert.False(vm.MembersSection.HasConfirmedData);
    }

    [Fact]
    public async AsyncTask Sections_OrganizationChangeClearsAllContextsAndRejectsLateSameIdResponse()
    {
        var client = SectionClient();
        var gate = new System.Threading.Tasks.TaskCompletionSource<MembersResult>();
        client.MembersHandler = (_, _) => gate.Task;
        using var vm = new ProjectsViewModel(client, User, Capabilities);
        vm.Activate(); var old = vm.CurrentDetailsOperation;
        var replacement = Project() with { OrganizationId = Guid.NewGuid(), Name = "Другой контекст" };
        client.ProjectHandler = (_, _) => AsyncTask.FromResult<DesktopProjectResult<DesktopProjectDto>>(
            new DesktopProjectResult<DesktopProjectDto>.Succeeded(replacement));
        client.MembersHandler = (_, _) => AsyncTask.FromResult<MembersResult>(new MembersResult.Succeeded([]));
        vm.SelectedItem = new ProjectItemViewModel(replacement);
        await vm.CurrentDetailsOperation;
        gate.SetResult(client.Members); await old;
        Assert.Empty(vm.Members);
        Assert.Equal(replacement.Name, vm.SelectedItem.Name);
        Assert.All(vm.DetailSections, section => Assert.Equal(replacement.OrganizationId, section.OrganizationId));
    }

    [Fact]
    public async AsyncTask Sections_RevocationDuringRoleLoadNeverRequestsMembersAndListDenialClearsCard()
    {
        var client = SectionClient();
        var gate = new System.Threading.Tasks.TaskCompletionSource<DesktopProjectResult<IReadOnlyList<DesktopProjectRoleDto>>>();
        CancellationToken token = default;
        client.RolesHandler = ct => { token = ct; return gate.Task; };
        using var vm = new ProjectsViewModel(client, User, Capabilities);
        vm.Activate(); var pending = vm.CurrentDetailsOperation;
        vm.UpdateCapabilities(["Project.Read"]);
        Assert.True(token.IsCancellationRequested);
        gate.SetResult(client.Roles); await pending;
        Assert.Equal(0, client.MemberCalls);
        Assert.Equal(ProjectSectionState.Forbidden, vm.MembersSection.State);
        client.Projects = new DesktopProjectResult<DesktopProjectPage>.Forbidden();
        await vm.RefreshCommand.ExecuteAsync();
        Assert.Empty(vm.Items); Assert.Null(vm.SelectedItem); Assert.Empty(vm.Members);
    }

    [Fact]
    public async AsyncTask Sections_ExistingArchiveActionStillRequiresCompletedProjectAndUsesServerResponse()
    {
        var client = SectionClient();
        using var vm = new ProjectsViewModel(client, User, Capabilities);
        vm.Activate(); await vm.CurrentDetailsOperation;
        Assert.False(vm.ArchiveProjectCommand.CanExecute(null));
        var completed = Project() with { Status = DesktopProjectStatus.Completed, ActualEndAt = DateTimeOffset.UtcNow };
        client.ProjectHandler = (_, _) => AsyncTask.FromResult<DesktopProjectResult<DesktopProjectDto>>(
            new DesktopProjectResult<DesktopProjectDto>.Succeeded(completed));
        client.Archive = new DesktopProjectResult<DesktopProjectDto>.Succeeded(completed with { LifecycleState = "archived" });
        await vm.RefreshCommand.ExecuteAsync(); await vm.CurrentDetailsOperation;
        Assert.True(vm.ArchiveProjectCommand.CanExecute(null));
        Assert.True(await vm.ArchiveProjectCommand.ExecuteAsync());
        Assert.Empty(vm.Items); Assert.Null(vm.SelectedItem);
        Assert.Equal(ProjectsScreenState.Empty, vm.State);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async AsyncTask Sections_ServerRevocationClearsConfirmedMembersAndAuthEndsEntireSession(bool authentication)
    {
        var client = SectionClient();
        using var vm = new ProjectsViewModel(client, User, Capabilities);
        vm.Activate(); await vm.CurrentDetailsOperation;
        Assert.Single(vm.Members);
        client.Members = authentication ? new MembersResult.AuthenticationFailure() : new MembersResult.Forbidden();
        await vm.ReloadMembersCommand.ExecuteAsync();
        Assert.Empty(vm.Members); Assert.False(vm.MembersSection.HasConfirmedData);
        if (authentication)
        { Assert.Null(vm.SelectedItem); Assert.Empty(vm.Items); Assert.Equal(ProjectsScreenState.SessionEnded, vm.State); }
        else
        { Assert.NotNull(vm.SelectedItem); Assert.Equal(ProjectSectionState.Loaded, vm.OverviewSection.State); Assert.True(vm.CanUpdate); }
    }
}
