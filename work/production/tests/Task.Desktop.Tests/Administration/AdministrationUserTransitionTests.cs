using Task.Desktop.Administration;
using Task.Desktop.ViewModels;
using Task.Desktop.Work;
using AsyncTask = System.Threading.Tasks.Task;

namespace Task.Desktop.Tests.Administration;

public sealed class AdministrationUserTransitionTests
{
    public static IEnumerable<object[]> Matrix =>
        from status in new[] { "pending_activation", "active", "blocked", "deactivated" }
        from action in Enum.GetValues<DesktopUserTransition>()
        select new object[] { status, action, action switch
        {
            DesktopUserTransition.Activate => status == "pending_activation",
            DesktopUserTransition.Block => status == "active",
            DesktopUserTransition.Unblock => status == "blocked",
            DesktopUserTransition.Deactivate => status != "deactivated",
            DesktopUserTransition.Reactivate => status == "deactivated", _ => false,
        }};

    [Theory, MemberData(nameof(Matrix))]
    public async AsyncTask EffectiveActionsUseFreshVersionAndOnlyConfirmedReceipt(string status, DesktopUserTransition action, bool allowed)
    {
        var client = new Client(status); using var vm = Vm(client);
        Assert.Equal(allowed, vm.BeginUserTransitionCommand.CanExecute(action));
        Assert.Equal(allowed, await vm.BeginUserTransitionCommand.ExecuteAsync(action));
        if (!allowed) { Assert.Equal(0, client.DetailReads); return; }
        Assert.True(vm.IsTransitionOpen); Assert.Empty(client.Calls);
        Assert.Contains(client.User.DisplayName, vm.TransitionTitle); Assert.Contains(client.User.Login, vm.TransitionTitle);
        vm.TransitionReason = " Проверка "; client.Wait = new();
        var sending = vm.ConfirmUserTransitionCommand.ExecuteAsync();
        Assert.Equal(status, vm.SelectedUser!.AccountStatus); Assert.Equal(19, vm.SelectedUser.Version);
        Assert.False(await vm.ConfirmUserTransitionCommand.ExecuteAsync());
        Assert.False(await vm.BeginUserTransitionCommand.ExecuteAsync(action));
        Assert.False(await vm.CancelUserTransitionCommand.ExecuteAsync());
        Assert.False(await vm.CreateUserCommand.ExecuteAsync()); Assert.False(await vm.EditUserCommand.ExecuteAsync());
        Assert.False(await vm.RefreshCommand.ExecuteAsync()); Assert.False(vm.CanSelectUser);
        var call = Assert.Single(client.Calls); Assert.Equal(19, call.Version);
        Assert.Equal(client.User.Id, call.Id); Assert.Equal(action, call.Action);
        Assert.Equal(action is DesktopUserTransition.Block or DesktopUserTransition.Deactivate ? "Проверка" : null, call.Reason);
        var target = action == DesktopUserTransition.Block ? "blocked" : action == DesktopUserTransition.Deactivate ? "deactivated" : "active";
        client.Wait.SetResult(new DesktopUserResult<DesktopManagedUser>.Succeeded(client.User with { Version = 20, AccountStatus = target }));
        await sending;
        Assert.Equal(target, vm.SelectedUser.AccountStatus); Assert.Equal(20, vm.SelectedUser.Version);
        Assert.False(vm.IsTransitionOpen); Assert.Contains("подтверждено сервером", vm.Message);
        Assert.Equal(1, client.PageReads);
    }

    [Theory]
    [InlineData(DesktopUserTransition.Activate, "pending_activation", "User.Create")]
    [InlineData(DesktopUserTransition.Block, "active", "User.Block")]
    [InlineData(DesktopUserTransition.Unblock, "blocked", "User.Block")]
    [InlineData(DesktopUserTransition.Deactivate, "active", "User.Block")]
    [InlineData(DesktopUserTransition.Reactivate, "deactivated", "User.Block")]
    public async AsyncTask PermissionsCheckedBeforeReadAndAgainBeforeSend(DesktopUserTransition action, string status, string permission)
    {
        var client = new Client(status); using var vm = Vm(client, ["User.Read"]);
        Assert.False(await vm.BeginUserTransitionCommand.ExecuteAsync(action)); Assert.Equal(0, client.DetailReads);
        Assert.False(vm.ShowActivate || vm.ShowBlock || vm.ShowUnblock || vm.ShowDeactivate || vm.ShowReactivate);
        vm.UpdateCapabilities(["User.Read", permission]); await vm.BeginUserTransitionCommand.ExecuteAsync(action);
        vm.UpdateCapabilities(["User.Read"]);
        Assert.False(await vm.ConfirmUserTransitionCommand.ExecuteAsync()); Assert.Empty(client.Calls);
    }

    [Theory]
    [InlineData(DesktopUserTransition.Block, null)] [InlineData(DesktopUserTransition.Block, " ")]
    [InlineData(DesktopUserTransition.Deactivate, null)] [InlineData(DesktopUserTransition.Deactivate, "\t")]
    public async AsyncTask RequiredReasonNeverSendsInvalidRequest(DesktopUserTransition action, string? reason)
    {
        var client = new Client(); using var vm = Vm(client); await vm.BeginUserTransitionCommand.ExecuteAsync(action);
        vm.TransitionReason = reason; await vm.ConfirmUserTransitionCommand.ExecuteAsync();
        Assert.Empty(client.Calls); Assert.Contains("Укажите причину", vm.Message);
        vm.TransitionReason = new('x', 2001); await vm.ConfirmUserTransitionCommand.ExecuteAsync(); Assert.Empty(client.Calls);
    }

    [Theory]
    [InlineData("version", "Версия")]
    [InlineData("last-admin", "последнего активного администратора")]
    [InlineData("forbidden", "Недостаточно прав")]
    [InlineData("not-found", "недоступна")]
    [InlineData("invalid-key", "Ключ")]
    public async AsyncTask RejectionsKeepStateAndRequireNewConfirmation(string kind, string text)
    {
        var client = new Client { Failure = kind switch
        {
            "version" => new DesktopUserResult<DesktopManagedUser>.VersionConflict { ErrorCode = "VERSION_CONFLICT" },
            "last-admin" => new DesktopUserResult<DesktopManagedUser>.Conflict { ErrorCode = "INVALID_STATE_TRANSITION" },
            "forbidden" => new DesktopUserResult<DesktopManagedUser>.Forbidden(),
            "not-found" => new DesktopUserResult<DesktopManagedUser>.NotFound(),
            _ => new DesktopUserResult<DesktopManagedUser>.Conflict { ErrorCode = "IDEMPOTENCY_KEY_REUSED" },
        }};
        using var vm = Vm(client); await OpenBlock(vm); await vm.ConfirmUserTransitionCommand.ExecuteAsync();
        Assert.Equal("active", vm.SelectedUser!.AccountStatus); Assert.Equal(19, vm.SelectedUser.Version);
        Assert.Contains(text, vm.Message); Assert.False(await vm.ConfirmUserTransitionCommand.ExecuteAsync());
        Assert.Single(client.Calls); Assert.Equal(1, client.DetailReads);
        await vm.CancelUserTransitionCommand.ExecuteAsync(); client.Failure = null;
        await OpenBlock(vm); await vm.ConfirmUserTransitionCommand.ExecuteAsync();
        Assert.Equal(2, client.Calls.Count); Assert.NotEqual(client.Calls[0].Key, client.Calls[1].Key);
    }

    [Theory]
    [InlineData("offline")] [InlineData("processing")] [InlineData("malformed")] [InlineData("cancelled")]
    public async AsyncTask ExplicitRetryKeepsOriginalVersionReasonAndKey(string kind)
    {
        var client = new Client { Failure = kind switch
        {
            "offline" => new DesktopUserResult<DesktopManagedUser>.ServerUnavailable(),
            "processing" => new DesktopUserResult<DesktopManagedUser>.Conflict { ErrorCode = "REQUEST_IN_PROGRESS" },
            _ => new DesktopUserResult<DesktopManagedUser>.MalformedResponse(),
        }};
        using var vm = Vm(client); await OpenBlock(vm);
        if (kind == "cancelled") client.ThrowCancellation = true;
        await vm.ConfirmUserTransitionCommand.ExecuteAsync();
        Assert.Equal("active", vm.SelectedUser!.AccountStatus); Assert.False(vm.CanEditTransitionReason);
        Assert.Equal("Проверить исходный запрос", vm.TransitionConfirmLabel);
        vm.TransitionReason = "Другая причина не должна попасть в повтор";
        if (kind == "offline") Assert.False(await vm.ConfirmUserTransitionCommand.ExecuteAsync());
        client.ThrowCancellation = false; client.Failure = null; vm.UpdateConnectivity(true);
        await vm.ConfirmUserTransitionCommand.ExecuteAsync();
        Assert.Equal(2, client.Calls.Count); Assert.Equal(client.Calls[0], client.Calls[1]);
        Assert.Equal("blocked", vm.SelectedUser.AccountStatus);
    }

    [Fact]
    public async AsyncTask OfflineSessionCancelAndFreshStatePreventMutations()
    {
        var client = new Client(); using var vm = Vm(client);
        vm.UpdateConnectivity(false); Assert.False(await vm.BeginUserTransitionCommand.ExecuteAsync(DesktopUserTransition.Block));
        Assert.Equal(0, client.DetailReads); vm.UpdateConnectivity(true);
        await OpenBlock(vm); await vm.CancelUserTransitionCommand.ExecuteAsync();
        Assert.False(await vm.ConfirmUserTransitionCommand.ExecuteAsync()); Assert.Empty(client.Calls);
        client.User = client.User with { AccountStatus = "deactivated" };
        await vm.BeginUserTransitionCommand.ExecuteAsync(DesktopUserTransition.Block);
        Assert.False(vm.IsTransitionOpen); Assert.Equal("deactivated", vm.SelectedUser!.AccountStatus);
        client.User = client.User with { AccountStatus = "active" }; await vm.RefreshCommand.ExecuteAsync();
        await OpenBlock(vm); vm.UpdateConnectivity(false);
        Assert.False(await vm.ConfirmUserTransitionCommand.ExecuteAsync()); vm.UpdateConnectivity(true);
        vm.UpdateSessionState(false); Assert.Empty(vm.Users); Assert.Null(vm.SelectedUser);
        Assert.False(await vm.ConfirmUserTransitionCommand.ExecuteAsync()); Assert.Empty(client.Calls);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async AsyncTask LateResponsesAfterAccessLossDoNotRestoreData(bool mutation)
    {
        var client = new Client(); using var vm = Vm(client);
        if (mutation) await OpenBlock(vm); else client.DetailWait = new();
        client.Wait = new();
        var pending = mutation ? vm.ConfirmUserTransitionCommand.ExecuteAsync()
            : vm.BeginUserTransitionCommand.ExecuteAsync(DesktopUserTransition.Block);
        vm.UpdateSessionState(false);
        var receipt = new DesktopUserResult<DesktopManagedUser>.Succeeded(client.User with { AccountStatus = "blocked" });
        if (mutation) client.Wait.SetResult(receipt); else client.DetailWait!.SetResult(receipt);
        await pending; Assert.Empty(vm.Users); Assert.Null(vm.SelectedUser); Assert.False(vm.IsTransitionOpen);
    }

    [Fact]
    public async AsyncTask ReplayedReceiptMayKeepVersionAndStillConfirmsState()
    {
        var client = new Client(); using var vm = Vm(client); await OpenBlock(vm);
        client.Failure = new DesktopUserResult<DesktopManagedUser>.Succeeded(client.User with { AccountStatus = "blocked" }, Replayed: true);
        await vm.ConfirmUserTransitionCommand.ExecuteAsync();
        Assert.Equal(19, vm.SelectedUser!.Version); Assert.Equal("blocked", vm.SelectedUser.AccountStatus);
        Assert.Contains("ранее выполненного", vm.Message);
    }

    [Theory]
    [InlineData("forbidden")] [InlineData("not-found")] [InlineData("offline")] [InlineData("session")]
    public async AsyncTask DetailFailuresCannotOpenConfirmationOrSendMutation(string failure)
    {
        var client = new Client { DetailFailure = failure switch
        {
            "forbidden" => new DesktopUserResult<DesktopManagedUser>.Forbidden(),
            "not-found" => new DesktopUserResult<DesktopManagedUser>.NotFound(),
            "offline" => new DesktopUserResult<DesktopManagedUser>.ServerUnavailable(),
            _ => new DesktopUserResult<DesktopManagedUser>.AuthenticationFailure(),
        }};
        using var vm = Vm(client); await vm.BeginUserTransitionCommand.ExecuteAsync(DesktopUserTransition.Block);
        Assert.False(vm.IsTransitionOpen); Assert.False(await vm.ConfirmUserTransitionCommand.ExecuteAsync());
        Assert.Empty(client.Calls); Assert.NotNull(vm.Message);
        if (failure == "session") { Assert.Empty(vm.Users); Assert.Null(vm.SelectedUser); }
    }

    internal static AdministrationViewModel Vm(Client client, string[]? caps = null)
    { var vm = new AdministrationViewModel(client, caps ?? ["User.Read", "User.Create", "User.Update", "User.Block"]); vm.Activate(); return vm; }
    internal static async AsyncTask OpenBlock(AdministrationViewModel vm)
    { await vm.BeginUserTransitionCommand.ExecuteAsync(DesktopUserTransition.Block); vm.TransitionReason = "Проверка"; }

    internal sealed class Client(string status = "active") : IDesktopAdministrationApiClient
    {
        public DesktopManagedUser User = AdministrationUserEditorTests.Client.User with { AccountStatus = status };
        public DesktopUserResult<DesktopManagedUser>? Failure, DetailFailure;
        public TaskCompletionSource<DesktopUserResult<DesktopManagedUser>>? Wait, DetailWait;
        public bool ThrowCancellation;
        public int DetailReads, PageReads;
        public List<(Guid Id, long Version, DesktopUserTransition Action, string? Key, string? Reason)> Calls = [];
        public System.Threading.Tasks.Task<DesktopUserResult<DesktopUserPage>> GetUserPageAsync(DesktopUserQuery query, CancellationToken cancellationToken = default)
        { PageReads++; return System.Threading.Tasks.Task.FromResult<DesktopUserResult<DesktopUserPage>>(new DesktopUserResult<DesktopUserPage>.Succeeded(new([User with { Version = 1 }], null, 1))); }
        public System.Threading.Tasks.Task<DesktopUserResult<DesktopManagedUser>> GetUserDetailsAsync(Guid id, CancellationToken cancellationToken = default)
        { DetailReads++; return DetailWait?.Task ?? System.Threading.Tasks.Task.FromResult(DetailFailure ?? new DesktopUserResult<DesktopManagedUser>.Succeeded(User)); }
        public System.Threading.Tasks.Task<DesktopUserResult<DesktopManagedUser>> TransitionUserAsync(Guid id, long version, DesktopUserTransition transition, string? idempotencyKey = null, string? reason = null, CancellationToken cancellationToken = default)
        {
            Calls.Add((id, version, transition, idempotencyKey, reason));
            if (ThrowCancellation) throw new OperationCanceledException();
            return Wait?.Task ?? System.Threading.Tasks.Task.FromResult(Failure ?? new DesktopUserResult<DesktopManagedUser>.Succeeded(User with
            { Version = version + 1, AccountStatus = transition == DesktopUserTransition.Block ? "blocked" : transition == DesktopUserTransition.Deactivate ? "deactivated" : "active" }));
        }
        public System.Threading.Tasks.Task<DesktopUserResult<DesktopManagedUser>> CreateUserAsync(DesktopUserCreate draft, string idempotencyKey, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public System.Threading.Tasks.Task<DesktopUserResult<DesktopManagedUser>> UpdateUserAsync(Guid id, long version, DesktopUserPatch patch, string idempotencyKey, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public System.Threading.Tasks.Task<DesktopWorkResult<IReadOnlyList<DesktopAdminUser>>> GetUsersAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public System.Threading.Tasks.Task<DesktopWorkResult<IReadOnlyList<DesktopAdminRole>>> GetRolesAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public System.Threading.Tasks.Task<DesktopWorkResult<IReadOnlyList<DesktopNetworkResource>>> GetResourcesAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
