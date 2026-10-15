using Task.Desktop.Administration;
using Task.Desktop.Projects;
using Task.Desktop.ViewModels;
using Task.Desktop.Work;
using Task.Desktop.Tests.Employees;
using AsyncTask = System.Threading.Tasks.Task;

namespace Task.Desktop.Tests.Projects;

public sealed partial class ProjectsViewModelTests
{
    private static DesktopAdminUser Employee(Guid? id = null, string login = "ivan") => new(id ?? Guid.NewGuid(), 1, "Иван Иванов", login, "Инженер", "active");
    private static FakeProjectsClient PickerClient(DesktopProjectDto? project = null) => new()
    {
        Projects = SuccessPage(project ?? Project()),
        Project = new DesktopProjectResult<DesktopProjectDto>.Succeeded(project ?? Project()),
        Roles = new DesktopProjectResult<IReadOnlyList<DesktopProjectRoleDto>>.Succeeded([Role()]),
    };
    private static EmployeePickerTests.Directory Directory(params DesktopAdminUser[] users) => new()
    {
        Search = (_, _, _) => AsyncTask.FromResult<DesktopWorkResult<IReadOnlyList<DesktopAdminUser>>>(new DesktopWorkResult<IReadOnlyList<DesktopAdminUser>>.Succeeded(users)),
        Get = (id, _) => AsyncTask.FromResult<DesktopWorkResult<DesktopAdminUser>>(users.FirstOrDefault(u => u.Id == id) is { } user
            ? new DesktopWorkResult<DesktopAdminUser>.Succeeded(user) : new DesktopWorkResult<DesktopAdminUser>.NotFound()),
    };
    private static ProjectsViewModel PickerModel(FakeProjectsClient client, EmployeePickerTests.Directory directory, IEnumerable<string>? capabilities = null)
        => new(client, User, capabilities ?? Capabilities.Append("User.Read"), directory: directory);
    private static async AsyncTask Choose(EmployeePickerViewModel picker, DesktopAdminUser user)
    {
        picker.SearchText = "Иван"; await picker.CurrentOperation;
        picker.Candidate = picker.Results.Single(u => u.Id == user.Id);
        Assert.True(picker.ConfirmSelection());
    }

    [Fact]
    public async AsyncTask PickerAddsExactNamesakeAndRole_ReloadsServerMembersWithoutLosingProject()
    {
        var first = Employee(); var second = Employee(login: "ivan2");
        var client = PickerClient(); var directory = Directory(Employee(User), first, second);
        using var vm = PickerModel(client, directory); vm.Activate();
        await WaitUntil(() => !vm.IsDetailLoading && vm.SelectedItem is not null);
        var selectedProject = vm.SelectedItem;
        await Choose(vm.MemberPicker!, second);
        Assert.Equal(second.Id, vm.NewMemberAccountId);
        var member = new DesktopProjectMemberDto(ProjectId, second.Id, RoleId, "active", 1, null, null);
        client.Add = new DesktopProjectResult<DesktopProjectMemberDto>.Succeeded(member, 2);
        client.Members = new DesktopProjectResult<IReadOnlyList<DesktopProjectMemberDto>>.Succeeded([member]);
        await vm.AddMemberCommand.ExecuteAsync();
        Assert.Equal(second.Id, client.LastMemberUser); Assert.Equal(RoleId, client.LastMemberRole);
        Assert.True(Guid.TryParse(client.LastMemberKey, out _)); Assert.Equal(1, client.LastMemberProjectVersion);
        Assert.Same(selectedProject, vm.SelectedItem); Assert.Equal(2, vm.SelectedItem!.Source.Version);
        Assert.Contains("ivan2", Assert.Single(vm.Members).UserText);
        Assert.Contains("Иван Иванов", vm.SelectedItem.OwnerText);
        await Choose(vm.MemberPicker!, second);
        Assert.False(vm.AddMemberCommand.CanExecute(null)); Assert.Equal(1, client.AddCalls);
    }

    [Theory]
    [InlineData("deactivated")]
    [InlineData("blocked")]
    public async AsyncTask DeactivatedAfterSelectionIsNotSubmitted(string status)
    {
        var employee = Employee(); var directory = Directory(employee); var client = PickerClient();
        using var vm = PickerModel(client, directory); vm.Activate();
        await Choose(vm.MemberPicker!, employee);
        directory.Get = (_, _) => AsyncTask.FromResult<DesktopWorkResult<DesktopAdminUser>>(new DesktopWorkResult<DesktopAdminUser>.Succeeded(employee with { AccountStatus = status }));
        await vm.AddMemberCommand.ExecuteAsync();
        Assert.Equal(0, client.AddCalls); Assert.Null(vm.NewMemberAccountId);
        Assert.Contains("активный", vm.Message);
    }

    [Theory]
    [InlineData("forbidden")]
    [InlineData("notfound")]
    [InlineData("offline")]
    [InlineData("auth")]
    public async AsyncTask UnavailableSelectionStopsBeforeMembershipWrite(string failure)
    {
        var employee = Employee(); var directory = Directory(employee); var client = PickerClient();
        using var vm = PickerModel(client, directory); vm.Activate(); await Choose(vm.MemberPicker!, employee);
        directory.Get = (_, _) => AsyncTask.FromResult<DesktopWorkResult<DesktopAdminUser>>(failure switch
        {
            "forbidden" => new DesktopWorkResult<DesktopAdminUser>.Forbidden(),
            "notfound" => new DesktopWorkResult<DesktopAdminUser>.NotFound(),
            "auth" => new DesktopWorkResult<DesktopAdminUser>.AuthenticationFailure(),
            _ => new DesktopWorkResult<DesktopAdminUser>.ServerUnavailable(),
        });
        await vm.AddMemberCommand.ExecuteAsync();
        Assert.Equal(0, client.AddCalls); Assert.Null(vm.NewMemberAccountId); Assert.NotNull(vm.Message);
    }

    [Fact]
    public async AsyncTask NoUserReadRetainsUuidWorkflow_WithoutDirectoryRequestsOrNames()
    {
        var directory = Directory(Employee(User)); var client = PickerClient();
        using var vm = PickerModel(client, directory, Capabilities); vm.Activate();
        Assert.False(vm.UsesEmployeePicker); Assert.Null(vm.MemberPicker);
        vm.NewMemberUserId = Guid.NewGuid().ToString("D");
        Assert.True(vm.AddMemberCommand.CanExecute(null)); await vm.AddMemberCommand.ExecuteAsync();
        Assert.Equal(1, client.AddCalls); Assert.Equal(0, directory.GetCalls + directory.SearchCalls);
        Assert.Contains("имя недоступно", vm.SelectedItem!.OwnerText);
        await vm.EditProjectCommand.ExecuteAsync(); Assert.False(vm.Editor!.UsesManagerPicker);
    }

    [Fact]
    public async AsyncTask MissingManageMembersAndReadOnlyStatesDisableCommandsAndClearIdentities()
    {
        var employee = Employee(); var directory = Directory(Employee(User), employee); var client = PickerClient();
        using var vm = PickerModel(client, directory); vm.Activate(); await Choose(vm.MemberPicker!, employee);
        vm.UpdateConnectivity(false); Assert.False(vm.AddMemberCommand.CanExecute(null)); Assert.Null(vm.MemberPicker);
        Assert.Contains("имя недоступно", vm.SelectedItem!.OwnerText);
        vm.UpdateConnectivity(true); await Choose(vm.MemberPicker!, employee);
        vm.UpdateCapabilities(["Project.Read", "User.Read"]);
        Assert.False(vm.AddMemberCommand.CanExecute(null)); Assert.Null(vm.MemberPicker);
        vm.UpdateSessionState(false); Assert.False(vm.NewProjectCommand.CanExecute(null));
        Assert.Equal(0, client.AddCalls);
    }

    [Fact]
    public async AsyncTask ManagerPreselectionChangeClearAndCreateWithoutManagerPreserveOwnerAndVersion()
    {
        var first = Employee(); var second = Employee(login: "ivan2");
        var project = Project() with { ManagerUserId = first.Id, Version = 7 };
        var directory = Directory(Employee(User), first, second); var client = PickerClient(project);
        client.Update = new DesktopProjectResult<DesktopProjectDto>.Succeeded(project with { ManagerUserId = second.Id, Version = 8 });
        using var vm = PickerModel(client, directory); vm.Activate(); await vm.EditProjectCommand.ExecuteAsync();
        Assert.Equal(first.Id, vm.Editor!.ManagerPicker!.SelectedUserAccountId);
        Assert.Contains(first.Login, vm.SelectedItem!.ManagerText);
        await Choose(vm.Editor.ManagerPicker, second); await vm.SaveProjectCommand.ExecuteAsync();
        Assert.Equal(second.Id, client.LastDraft!.ManagerUserId); Assert.Equal(User, client.LastDraft.OwnerUserId); Assert.Equal(7, client.LastUpdateVersion);
        await vm.NewProjectCommand.ExecuteAsync(); vm.Editor!.Name = "Без руководителя";
        Assert.True(vm.Editor.TryBuild(out var empty)); Assert.Null(empty.ManagerUserId);
        await Choose(vm.Editor.ManagerPicker!, first); await vm.Editor.ClearManagerCommand.ExecuteAsync();
        Assert.True(vm.Editor.TryBuild(out empty)); Assert.Null(empty.ManagerUserId);
        client.Create = new DesktopProjectResult<DesktopProjectDto>.Succeeded(Project());
        await vm.SaveProjectCommand.ExecuteAsync(); Assert.Null(client.LastDraft!.ManagerUserId);
    }

    [Fact]
    public async AsyncTask FailedManagerPreselectionDoesNotSilentlyRemoveManager_ExplicitClearWorks()
    {
        var project = Project() with { ManagerUserId = Guid.NewGuid() }; var client = PickerClient(project);
        using var vm = PickerModel(client, Directory()); vm.Activate(); await vm.EditProjectCommand.ExecuteAsync();
        Assert.False(vm.Editor!.TryBuild(out _)); Assert.Equal(project.ManagerUserId.ToString(), vm.Editor.ManagerId);
        await vm.Editor.ClearManagerCommand.ExecuteAsync(); Assert.True(vm.Editor.TryBuild(out var draft)); Assert.Null(draft.ManagerUserId);
    }

    [Fact]
    public async AsyncTask ManagerDeactivationAtSubmitKeepsDraftAndDoesNotWrite()
    {
        var user = Employee(); var client = PickerClient(); var directory = Directory(user);
        using var vm = PickerModel(client, directory); vm.Activate(); await vm.EditProjectCommand.ExecuteAsync();
        await Choose(vm.Editor!.ManagerPicker!, user);
        directory.Get = (_, _) => AsyncTask.FromResult<DesktopWorkResult<DesktopAdminUser>>(new DesktopWorkResult<DesktopAdminUser>.Succeeded(user with { AccountStatus = "deactivated" }));
        await vm.SaveProjectCommand.ExecuteAsync();
        Assert.Null(client.LastDraft); Assert.NotNull(vm.Editor); Assert.Equal(user.Id.ToString("D"), vm.Editor.ManagerId);
        Assert.Contains("активный", vm.Editor.Error);
    }

    [Fact]
    public async AsyncTask PickerManagerVersionConflictPreservesChoiceAndRequiresExplicitRetry()
    {
        var user = Employee(); var client = PickerClient();
        using var vm = PickerModel(client, Directory(user)); vm.Activate(); await vm.EditProjectCommand.ExecuteAsync();
        await Choose(vm.Editor!.ManagerPicker!, user);
        client.Update = new DesktopProjectResult<DesktopProjectDto>.VersionConflict();
        client.Project = new DesktopProjectResult<DesktopProjectDto>.Succeeded(Project() with { Version = 4 });
        await vm.SaveProjectCommand.ExecuteAsync();
        Assert.Equal(user.Id, vm.Editor!.ManagerPicker!.SelectedUserAccountId); Assert.Equal(4, vm.Editor.Source!.Version);
        Assert.Contains("черновик сохранён", vm.Editor.Error); Assert.Equal(User, client.LastDraft!.OwnerUserId);
        client.Update = new DesktopProjectResult<DesktopProjectDto>.Succeeded(Project() with { Version = 5, ManagerUserId = user.Id });
        await vm.SaveProjectCommand.ExecuteAsync(); Assert.Equal(4, client.LastUpdateVersion); Assert.Null(vm.Editor);
        Assert.Contains(user.Login, vm.SelectedItem!.ManagerText);
    }

    [Fact]
    public async AsyncTask LostDirectoryPermissionClearsNamesAndKeepsProjectRoleCommands()
    {
        var user = Employee(); var client = PickerClient(); var directory = Directory(Employee(User), user);
        using var vm = PickerModel(client, directory); vm.Activate();
        Assert.Contains("Иван", vm.SelectedItem!.OwnerText); await Choose(vm.MemberPicker!, user);
        directory.Get = (_, _) => AsyncTask.FromResult<DesktopWorkResult<DesktopAdminUser>>(new DesktopWorkResult<DesktopAdminUser>.Forbidden());
        await vm.AddMemberCommand.ExecuteAsync();
        Assert.Equal(0, client.AddCalls); Assert.Contains("имя недоступно", vm.SelectedItem.OwnerText);
        Assert.True(vm.CanManageMembers); Assert.False(vm.AddMemberCommand.CanExecute(null));
        var calls = directory.GetCalls + directory.SearchCalls;
        await vm.MemberPicker!.SearchAsync(); Assert.Equal(calls, directory.GetCalls + directory.SearchCalls);
        Assert.Contains("User.Read", vm.MemberEntryHint);
    }

    [Theory]
    [InlineData("version")]
    [InlineData("duplicate")]
    [InlineData("forbidden")]
    [InlineData("notfound")]
    [InlineData("auth")]
    [InlineData("offline")]
    public async AsyncTask RejectedMembershipNeverAppliesOptimisticRowsOrVersion(string failure)
    {
        var user = Employee(); var client = PickerClient();
        client.Add = failure switch
        {
            "version" => new DesktopProjectResult<DesktopProjectMemberDto>.VersionConflict(),
            "duplicate" => new DesktopProjectResult<DesktopProjectMemberDto>.InvalidState(),
            "forbidden" => new DesktopProjectResult<DesktopProjectMemberDto>.Forbidden(),
            "notfound" => new DesktopProjectResult<DesktopProjectMemberDto>.NotFound(),
            "auth" => new DesktopProjectResult<DesktopProjectMemberDto>.AuthenticationFailure(),
            _ => new DesktopProjectResult<DesktopProjectMemberDto>.ServerUnavailable(),
        };
        using var vm = PickerModel(client, Directory(user)); vm.Activate(); await Choose(vm.MemberPicker!, user);
        await vm.AddMemberCommand.ExecuteAsync();
        Assert.Empty(vm.Members); Assert.Equal(1, vm.SelectedItem!.Source.Version); Assert.NotNull(vm.Message);
        if (failure == "auth") Assert.False(vm.AddMemberCommand.CanExecute(null));
    }

    [Fact]
    public async AsyncTask ProjectSwitchCancelsOldSearchAndLateRevalidationCannotAddToEitherProject()
    {
        var user = Employee(); var directory = Directory(user); var client = PickerClient();
        var second = Project() with { Id = Guid.NewGuid() }; client.Projects = SuccessPage(Project(), second);
        using var vm = PickerModel(client, directory); vm.Activate();
        var old = vm.MemberPicker!;
        var search = new System.Threading.Tasks.TaskCompletionSource<DesktopWorkResult<IReadOnlyList<DesktopAdminUser>>>();
        directory.Search = (_, _, _) => search.Task;
        var searching = old.SearchAsync(); vm.SelectedItem = vm.Items[1];
        search.SetResult(new DesktopWorkResult<IReadOnlyList<DesktopAdminUser>>.Succeeded([user])); await searching;
        Assert.Empty(old.Results); Assert.Null(vm.NewMemberAccountId);
        directory.Search = Directory(user).Search; await Choose(vm.MemberPicker!, user);
        var get = new System.Threading.Tasks.TaskCompletionSource<DesktopWorkResult<DesktopAdminUser>>();
        directory.Get = (_, _) => get.Task;
        var adding = vm.AddMemberCommand.ExecuteAsync(); vm.SelectedItem = vm.Items[0];
        get.SetResult(new DesktopWorkResult<DesktopAdminUser>.Succeeded(user)); await adding;
        Assert.Equal(0, client.AddCalls); Assert.Null(vm.NewMemberAccountId);
    }
}
