using Task.Desktop.Projects;
using Task.Desktop.ViewModels;
using AsyncTask = System.Threading.Tasks.Task;

namespace Task.Desktop.Tests.Projects;

public sealed partial class ProjectsViewModelTests
{
    private static readonly Guid Participant = Guid.NewGuid();

    private static FakeProjectsClient ConfirmationClient()
    {
        var project = Project() with { Status = DesktopProjectStatus.Completed, Version = 7 };
        return new FakeProjectsClient
        {
            Projects = SuccessPage(project),
            Project = new DesktopProjectResult<DesktopProjectDto>.Succeeded(project),
            Roles = new DesktopProjectResult<IReadOnlyList<DesktopProjectRoleDto>>.Succeeded([Role()]),
            Members = new DesktopProjectResult<IReadOnlyList<DesktopProjectMemberDto>>.Succeeded(
                [new(ProjectId, Participant, RoleId, "active", 1, null, null)])
        };
    }

    private static async AsyncTask Ready(ProjectsViewModel vm)
    {
        vm.Activate();
        await WaitUntil(() => vm.State == ProjectsScreenState.Loaded && !vm.IsDetailLoading && !vm.MembersSection.IsLoading);
        await vm.CurrentDetailsOperation;
    }

    private static AsyncCommand ActionCommand(ProjectsViewModel vm, bool archive) => archive ? vm.ArchiveProjectCommand : vm.RemoveMemberCommand;

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async AsyncTask MissingConfirmationHandlerSafelyCancels(bool archive)
    {
        var client = ConfirmationClient();
        using var vm = new ProjectsViewModel(client, User, Capabilities);
        await Ready(vm);
        await ActionCommand(vm, archive).ExecuteAsync(vm.Members.Single());
        Assert.Equal(0, client.ArchiveCalls + client.RemoveCalls);
    }

    [Fact]
    public async AsyncTask RemovalConfirmationIncludesAvailableNameAndAccountId()
    {
        var client = ConfirmationClient(); var prompts = 0;
        using var vm = new ProjectsViewModel(client, User, Capabilities.Append("User.Read"), confirmAction: (_, message, _) =>
        {
            prompts++; Assert.Contains("Иван Петров", message); Assert.Contains(Participant.ToString("D"), message);
            return false;
        });
        await Ready(vm); vm.Members.Single().SetName("Иван Петров");
        await vm.RemoveMemberCommand.ExecuteAsync(vm.Members.Single());
        Assert.Equal(1, prompts); Assert.Equal(0, client.RemoveCalls);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async AsyncTask CancelledWhileConfirmingNeverCallsApi(bool archive)
    {
        var client = ConfirmationClient(); using var cancellation = new CancellationTokenSource();
        using var vm = new ProjectsViewModel(client, User, Capabilities, confirmAction: (_, _, _) => { cancellation.Cancel(); return true; });
        await Ready(vm);
        await ActionCommand(vm, archive).ExecuteAsync(vm.Members.Single(), cancellation.Token);
        Assert.Equal(0, client.ArchiveCalls + client.RemoveCalls);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async AsyncTask ConfirmationCancelPreservesStateAndNeverCallsApi(bool archive)
    {
        var client = ConfirmationClient(); var prompts = 0;
        using var vm = new ProjectsViewModel(client, User, Capabilities, confirmAction: (title, message, action) =>
        {
            prompts++;
            Assert.Contains("Проект Альфа", message);
            Assert.Equal(archive ? "Архивировать" : "Исключить", action);
            Assert.Equal(archive ? "Архивировать проект?" : "Исключить участника из проекта?", title);
            if (!archive) { Assert.Contains(Participant.ToString("D"), message); Assert.Contains("не удаляется", message); }
            Assert.Equal(0, client.ArchiveCalls + client.RemoveCalls);
            return false;
        });
        await Ready(vm);
        var selected = vm.SelectedItem; var items = vm.Items; var members = vm.Members;
        await ActionCommand(vm, archive).ExecuteAsync(vm.Members.Single());
        Assert.Equal(1, prompts); Assert.Equal(0, client.ArchiveCalls + client.RemoveCalls);
        Assert.Same(items, vm.Items); Assert.Same(selected, vm.SelectedItem); Assert.Same(members, vm.Members);
        Assert.Equal(7, selected!.Source.Version);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async AsyncTask ConfirmationSendsCapturedTargetOnceAndAppliesSuccess(bool archive)
    {
        var client = ConfirmationClient(); var prompts = 0; ProjectsViewModel vm = null!;
        client.Archive = new DesktopProjectResult<DesktopProjectDto>.Succeeded(Project() with { Version = 8 }, 8);
        client.Update = client.Archive;
        vm = new ProjectsViewModel(client, User, Capabilities, confirmAction: (_, _, _) =>
        {
            prompts++;
            Assert.False(ActionCommand(vm, archive).ExecuteAsync(vm.Members.Single()).GetAwaiter().GetResult());
            if (!archive) client.Members = new DesktopProjectResult<IReadOnlyList<DesktopProjectMemberDto>>.Succeeded([]);
            return true;
        });
        using (vm)
        {
            await Ready(vm); var selected = vm.SelectedItem!; var loads = client.MemberCalls;
            await ActionCommand(vm, archive).ExecuteAsync(vm.Members.Single());
            Assert.Equal(1, prompts); Assert.Equal(1, client.ArchiveCalls + client.RemoveCalls);
            Assert.Equal(ProjectId, client.LastAction!.Value.ProjectId); Assert.Equal(7, client.LastAction.Value.Version);
            Assert.Equal(archive ? (Guid?)null : Participant, client.LastAction.Value.UserId);
            if (archive) { Assert.Empty(vm.Items); Assert.Null(vm.SelectedItem); Assert.Contains("архив", vm.Message); }
            else { Assert.Empty(vm.Members); Assert.Equal(8, selected.Source.Version); Assert.Equal(loads + 1, client.MemberCalls); Assert.Contains("исключён", vm.Message); }
        }
    }

    [Theory]
    [InlineData(true, "permission")]
    [InlineData(false, "permission")]
    [InlineData(true, "offline")]
    [InlineData(false, "offline")]
    [InlineData(true, "session")]
    [InlineData(false, "session")]
    [InlineData(true, "status")]
    [InlineData(false, "owner")]
    [InlineData(false, "section")]
    public async AsyncTask UnavailableActionDoesNotEvenAskForConfirmation(bool archive, string reason)
    {
        var client = ConfirmationClient(); var prompts = 0;
        using var vm = new ProjectsViewModel(client, User, Capabilities, confirmAction: (_, _, _) => { prompts++; return true; });
        await Ready(vm); var member = vm.Members.Single();
        ChangeConfirmationContext(vm, reason);
        Assert.False(ActionCommand(vm, archive).CanExecute(member));
        await ActionCommand(vm, archive).ExecuteAsync(member);
        Assert.Equal(0, prompts); Assert.Equal(0, client.ArchiveCalls + client.RemoveCalls);
    }

    [Theory]
    [InlineData(true, "permission")]
    [InlineData(false, "permission")]
    [InlineData(true, "offline")]
    [InlineData(false, "offline")]
    [InlineData(true, "session")]
    [InlineData(false, "session")]
    [InlineData(true, "status")]
    [InlineData(false, "owner")]
    [InlineData(false, "section")]
    [InlineData(true, "selection")]
    [InlineData(false, "selection")]
    [InlineData(true, "version")]
    [InlineData(false, "version")]
    [InlineData(true, "id")]
    [InlineData(false, "id")]
    [InlineData(true, "deactivate")]
    [InlineData(false, "deactivate")]
    [InlineData(true, "context")]
    [InlineData(false, "context")]
    [InlineData(true, "accessRestored")]
    [InlineData(false, "accessRestored")]
    [InlineData(true, "permissionsRestored")]
    [InlineData(false, "permissionsRestored")]
    [InlineData(false, "membersReloaded")]
    public async AsyncTask ChangedContextDuringConfirmationNeverSendsStaleRequest(bool archive, string reason)
    {
        var client = ConfirmationClient(); ProjectsViewModel vm = null!;
        vm = new ProjectsViewModel(client, User, Capabilities, confirmAction: (_, _, _) =>
        {
            if (reason == "membersReloaded") vm.ReloadMembersCommand.ExecuteAsync().GetAwaiter().GetResult();
            else ChangeConfirmationContext(vm, reason);
            return true;
        });
        using (vm)
        {
            await Ready(vm);
            await ActionCommand(vm, archive).ExecuteAsync(vm.Members.Single());
            Assert.Equal(0, client.ArchiveCalls + client.RemoveCalls);
        }
    }

    private static void ChangeConfirmationContext(ProjectsViewModel vm, string reason)
    {
        var item = vm.SelectedItem!;
        switch (reason)
        {
            case "permission": vm.UpdateCapabilities(["Project.Read"]); break;
            case "offline": vm.UpdateConnectivity(false); break;
            case "session": vm.UpdateSessionState(false); break;
            case "status": item.Replace(item.Source with { Status = DesktopProjectStatus.Active }); break;
            case "owner": item.Replace(item.Source with { OwnerUserId = Participant }); break;
            case "section": vm.MembersSection.SetState(ProjectSectionState.ServerUnavailable); break;
            case "selection": vm.SelectedItem = null; break;
            case "version": item.Replace(item.Source with { Version = 8 }); break;
            case "id": item.Replace(item.Source with { Id = Guid.NewGuid() }); break;
            case "deactivate": vm.Deactivate(); break;
            case "context": vm.CancelDetailSections(); break;
            case "accessRestored": vm.UpdateConnectivity(false); vm.UpdateConnectivity(true); break;
            case "permissionsRestored": vm.UpdateCapabilities(["Project.Read"]); vm.UpdateCapabilities(Capabilities); break;
            default: throw new ArgumentException(reason);
        }
    }

    [Theory]
    [InlineData(true, "conflict")]
    [InlineData(false, "conflict")]
    [InlineData(true, "forbidden")]
    [InlineData(false, "forbidden")]
    [InlineData(true, "invalid")]
    [InlineData(false, "notFound")]
    [InlineData(true, "unavailable")]
    [InlineData(false, "unavailable")]
    public async AsyncTask ServerFailurePreservesDataAndDoesNotReportSuccessOrRetry(bool archive, string error)
    {
        var client = ConfirmationClient();
        DesktopProjectResult<DesktopProjectDto> result = error switch
        {
            "conflict" => new DesktopProjectResult<DesktopProjectDto>.VersionConflict(),
            "forbidden" => new DesktopProjectResult<DesktopProjectDto>.Forbidden(),
            "invalid" => new DesktopProjectResult<DesktopProjectDto>.InvalidState(),
            "notFound" => new DesktopProjectResult<DesktopProjectDto>.NotFound(),
            _ => new DesktopProjectResult<DesktopProjectDto>.ServerUnavailable()
        };
        client.Archive = result; client.Update = result;
        using var vm = new ProjectsViewModel(client, User, Capabilities, confirmAction: (_, _, _) => true);
        await Ready(vm); var selected = vm.SelectedItem; var members = vm.Members;
        await ActionCommand(vm, archive).ExecuteAsync(vm.Members.Single());
        Assert.Equal(1, client.ArchiveCalls + client.RemoveCalls);
        Assert.Same(selected, vm.SelectedItem); Assert.Single(vm.Items); Assert.Same(members, vm.Members);
        Assert.Equal(7, selected!.Source.Version);
        Assert.DoesNotContain("перемещён", vm.Message ?? ""); Assert.DoesNotContain("исключён", vm.Message ?? "");
    }
}
