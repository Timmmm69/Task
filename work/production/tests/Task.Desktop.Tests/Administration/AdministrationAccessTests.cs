using Task.Desktop.Administration;
using Task.Desktop.ViewModels;
using Task.Desktop.Work;
using AsyncTask = System.Threading.Tasks.Task;

namespace Task.Desktop.Tests.Administration;

public sealed class AdministrationAccessTests
{
    [Theory]
    [InlineData("User.ManageRoles")]
    [InlineData("User.Read")]
    [InlineData("Role.Read")]
    [InlineData("User.Create")]
    [InlineData("User.Update")]
    [InlineData("User.Block")]
    [InlineData("organization.manage")]
    [InlineData("Task.Read")]
    public async AsyncTask AccessTabNeverReadsUsersOrEnablesAccountCommands(string capability)
    {
        var client = new Client();
        using var vm = new AdministrationViewModel(client, [capability]);
        vm.SelectedTab = vm.Tabs.Single(t => t.Area == AdministrationArea.Access);
        vm.Activate();
        Assert.True(vm.IsAccessManagement);
        Assert.False(vm.IsUsers);
        Assert.Equal(capability == "User.ManageRoles", vm.CanReadCurrentArea);
        Assert.Contains("OQ-ADMIN-ROLES-READ", vm.RoleAssignmentLimitation);
        Assert.False(await vm.RefreshCommand.ExecuteAsync());
        Assert.False(await vm.CreateUserCommand.ExecuteAsync());
        Assert.False(await vm.EditUserCommand.ExecuteAsync());
        Assert.False(await vm.SearchUsersCommand.ExecuteAsync());
        Assert.Equal(0, client.Reads);
        Assert.Empty(vm.Users);
    }

    [Theory]
    [InlineData(AdministrationArea.Roles)]
    [InlineData(AdministrationArea.Resources)]
    public async AsyncTask PermissionLossClearsProjectionAndRejectsLateReceipt(AdministrationArea area)
    {
        var client = new Client(); using var vm = Vm(client, area);
        Assert.Equal(1, ProjectionCount(vm, area));
        client.Wait = new(); var refresh = vm.RefreshCommand.ExecuteAsync();
        vm.UpdateCapabilities([]);
        Assert.Equal(0, ProjectionCount(vm, area));
        client.Wait.SetResult(); await refresh;
        Assert.Equal(0, ProjectionCount(vm, area));
        Assert.Null(vm.SelectedRole); Assert.Null(vm.SelectedResource);
        Assert.True(vm.IsLimitedRole);
    }

    [Theory]
    [InlineData("session")]
    [InlineData("deactivate")]
    [InlineData("dispose")]
    public async AsyncTask InvalidatedViewNeverPublishesLateRoleData(string reason)
    {
        var client = new Client { Wait = new() };
        using var vm = new AdministrationViewModel(client, ["Role.Read"]);
        vm.SelectedTab = vm.Tabs.Single(t => t.Area == AdministrationArea.Roles);
        vm.Activate();
        if (reason == "session") vm.UpdateSessionState(false);
        else if (reason == "deactivate") vm.Deactivate();
        else vm.Dispose();
        client.Wait.SetResult();
        await Finished(vm);
        Assert.Empty(vm.Roles);
        Assert.Null(vm.SelectedRole);
    }

    [Fact]
    public async AsyncTask TabSwitchDuringReadLoadsOnlyLatestTabAndNeverPublishesOldReceipt()
    {
        var client = new Client(); using var vm = Vm(client, AdministrationArea.Roles);
        client.Wait = new(); var refresh = vm.RefreshCommand.ExecuteAsync();
        vm.SelectedTab = vm.Tabs.Single(t => t.Area == AdministrationArea.Resources);
        client.Wait.SetResult(); await refresh;
        Assert.Single(vm.Resources);
        Assert.Equal(3, client.Reads);
        Assert.False(vm.IsLoading);
        // The initial confirmed role remains cached, but the discarded receipt cannot replace it.
        Assert.Equal(1, vm.SelectedRole!.Id.ToByteArray()[0]);
    }

    [Theory]
    [InlineData(AdministrationArea.Roles, false)]
    [InlineData(AdministrationArea.Resources, false)]
    [InlineData(AdministrationArea.Roles, true)]
    [InlineData(AdministrationArea.Resources, true)]
    public async AsyncTask ForbiddenAndExpiredSessionRemovePreviouslyConfirmedData(AdministrationArea area, bool expired)
    {
        var client = new Client(); using var vm = Vm(client, area);
        client.Expired = expired; client.Forbidden = !expired;
        await vm.RefreshCommand.ExecuteAsync();
        Assert.Equal(0, ProjectionCount(vm, area));
        if (expired) { Assert.True(vm.IsLimitedRole); Assert.False(vm.RefreshCommand.CanExecute(null)); }
    }

    [Fact]
    public async AsyncTask SearchAndPaginationUseTheAppliedFilterAndResetCursor()
    {
        var client = new AdministrationUserEditorTests.Client { NextCursor = "cursor-1" };
        using var vm = AdministrationUserEditorTests.Vm(client);
        vm.UserSearchText = "  Анна  "; await vm.SearchUsersCommand.ExecuteAsync();
        Assert.Equal(new DesktopUserQuery(Filter: "Анна"), client.LastQuery);
        vm.UserSearchText = "unsubmitted"; await vm.MoreUsersCommand.ExecuteAsync();
        Assert.Equal(new DesktopUserQuery(Filter: "Анна", Cursor: "cursor-1"), client.LastQuery);
        vm.UserSearchText = " "; await vm.SearchUsersCommand.ExecuteAsync();
        Assert.Equal(new DesktopUserQuery(), client.LastQuery);
        var reads = client.PageReads;
        vm.UpdateCapabilities(["User.ManageRoles"]);
        Assert.False(await vm.SearchUsersCommand.ExecuteAsync());
        Assert.Equal(reads, client.PageReads); Assert.Empty(vm.Users);
    }

    [Fact]
    public async AsyncTask ForbiddenFreshUserReadRevokesCachedReadAccessBeforeAnyMutation()
    {
        var editorClient = new AdministrationUserEditorTests.Client { DetailFailure = new DesktopUserResult<DesktopManagedUser>.Forbidden() };
        using var editor = AdministrationUserEditorTests.Vm(editorClient);
        await editor.EditUserCommand.ExecuteAsync();
        Assert.Empty(editor.Users); Assert.Null(editor.SelectedUser);
        Assert.False(await editor.CreateUserCommand.ExecuteAsync());
        var transitionClient = new AdministrationUserTransitionTests.Client { DetailFailure = new DesktopUserResult<DesktopManagedUser>.Forbidden() };
        using var lifecycle = AdministrationUserTransitionTests.Vm(transitionClient);
        await lifecycle.BeginUserTransitionCommand.ExecuteAsync(DesktopUserTransition.Block);
        Assert.Empty(lifecycle.Users); Assert.Null(lifecycle.SelectedUser);
        Assert.False(lifecycle.ConfirmUserTransitionCommand.CanExecute(null));
        Assert.Empty(transitionClient.Calls);
    }

    private static AdministrationViewModel Vm(Client client, AdministrationArea area)
    {
        var vm = new AdministrationViewModel(client, ["Role.Read", "FileCatalog.Read"]);
        vm.SelectedTab = vm.Tabs.Single(t => t.Area == area); vm.Activate(); return vm;
    }
    private static int ProjectionCount(AdministrationViewModel vm, AdministrationArea area) =>
        area == AdministrationArea.Roles ? vm.Roles.Count : vm.Resources.Count;
    private static async AsyncTask Finished(AdministrationViewModel vm)
    {
        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (vm.RefreshCommand.IsExecuting && DateTime.UtcNow < deadline) await AsyncTask.Delay(10);
        Assert.False(vm.RefreshCommand.IsExecuting);
    }

    private sealed class Client : IDesktopAdministrationApiClient
    {
        public int Reads;
        public TaskCompletionSource? Wait;
        public bool Forbidden, Expired;
        private async System.Threading.Tasks.Task<DesktopWorkResult<IReadOnlyList<T>>> Read<T>(T item)
        {
            Reads++;
            if (Wait is not null) await Wait.Task; // Deliberately ignores cancellation.
            return Expired ? new DesktopWorkResult<IReadOnlyList<T>>.AuthenticationFailure()
                : Forbidden ? new DesktopWorkResult<IReadOnlyList<T>>.Forbidden()
                : new DesktopWorkResult<IReadOnlyList<T>>.Succeeded([item]);
        }
        public System.Threading.Tasks.Task<DesktopWorkResult<IReadOnlyList<DesktopAdminRole>>> GetRolesAsync(CancellationToken cancellationToken = default) =>
            Read(new DesktopAdminRole(new Guid(Reads + 1, 0, 0, new byte[8]), "role", "Роль", true, ["User.Read"]));
        public System.Threading.Tasks.Task<DesktopWorkResult<IReadOnlyList<DesktopNetworkResource>>> GetResourcesAsync(CancellationToken cancellationToken = default) =>
            Read(new DesktopNetworkResource(Guid.NewGuid(), 1, "Ресурс", null, null, "active"));
        public System.Threading.Tasks.Task<DesktopUserResult<DesktopUserPage>> GetUserPageAsync(DesktopUserQuery query, CancellationToken cancellationToken = default)
        { Reads++; throw new InvalidOperationException("User.Read is required."); }
        public System.Threading.Tasks.Task<DesktopWorkResult<IReadOnlyList<DesktopAdminUser>>> GetUsersAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public System.Threading.Tasks.Task<DesktopUserResult<DesktopManagedUser>> GetUserDetailsAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public System.Threading.Tasks.Task<DesktopUserResult<DesktopManagedUser>> CreateUserAsync(DesktopUserCreate draft, string idempotencyKey, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public System.Threading.Tasks.Task<DesktopUserResult<DesktopManagedUser>> UpdateUserAsync(Guid id, long version, DesktopUserPatch patch, string idempotencyKey, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public System.Threading.Tasks.Task<DesktopUserResult<DesktopManagedUser>> TransitionUserAsync(Guid id, long version, DesktopUserTransition transition, string? idempotencyKey = null, string? reason = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
