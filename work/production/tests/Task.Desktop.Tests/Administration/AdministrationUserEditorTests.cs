using Task.Desktop.Administration;
using Task.Desktop.ViewModels;
using Task.Desktop.Work;
using AsyncTask = System.Threading.Tasks.Task;

namespace Task.Desktop.Tests.Administration;

public sealed class AdministrationUserEditorTests
{
    [Theory]
    [InlineData(false, false)] [InlineData(true, false)] [InlineData(false, true)] [InlineData(true, true)]
    public void IndependentWritePermissions(bool create, bool update)
    {
        using var vm = Vm(new(), create, update);
        Assert.Equal(create, vm.CreateUserCommand.CanExecute(null));
        Assert.Equal(update, vm.EditUserCommand.CanExecute(null));
    }

    [Fact]
    public async AsyncTask NoReadPermissionOrInactiveSessionCannotReadOrWrite()
    {
        var client = new Client(); using var vm = new AdministrationViewModel(client, ["User.Create", "User.Update"]);
        vm.Activate();
        Assert.Equal(0, client.PageReads);
        Assert.False(await vm.CreateUserCommand.ExecuteAsync());
        Assert.False(await vm.EditUserCommand.ExecuteAsync());
        vm.UpdateCapabilities(["User.Read", "User.Create", "User.Update"]);
        await vm.RefreshCommand.ExecuteAsync();
        Assert.Single(vm.Users);
        vm.UpdateSessionState(false);
        Assert.Empty(vm.Users); Assert.False(vm.CreateUserCommand.CanExecute(null));
    }

    [Theory]
    [InlineData("", "Крылова", "anna", "Имя")]
    [InlineData("Анна", "", "anna", "Фамилия")]
    [InlineData("Анна", "Крылова", "ab", "Логин")]
    public async AsyncTask RequiredValidationNeverCallsServer(string first, string last, string login, string error)
    {
        var client = new Client(); using var vm = Vm(client);
        await vm.CreateUserCommand.ExecuteAsync();
        vm.UserEditor!.FirstName = first; vm.UserEditor.LastName = last; vm.UserEditor.Login = login;
        await vm.SaveUserCommand.ExecuteAsync();
        Assert.Contains(error, vm.UserEditorMessage); Assert.Empty(client.Creates);
    }

    [Fact]
    public void OptionalFieldsAndContractLengths()
    {
        var editor = Draft(); Assert.Empty(editor.Validate());
        Assert.Null(editor.Create().DepartmentId); Assert.Null(editor.Create().WorkEmail);
        Assert.Null(editor.Create().DisplayName); Assert.Null(editor.Create().JobTitle);
        editor.FirstName = new('a', 101); editor.LastName = new('b', 101); editor.Login = new('c', 101);
        editor.DisplayName = new('d', 301); editor.WorkEmail = "invalid"; editor.JobTitle = new('e', 201);
        Assert.Equal(6, editor.Validate().Count);
        editor = new(Client.User); editor.DisplayName = "";
        Assert.Contains(editor.Validate(), error => error.Contains("Отображаемое имя"));
    }

    [Fact]
    public async AsyncTask CreateSelectsConfirmedPendingAccountEvenOutsideFirstPage()
    {
        var client = new Client(); using var vm = Vm(client);
        await vm.CreateUserCommand.ExecuteAsync(); Fill(vm.UserEditor!);
        await vm.SaveUserCommand.ExecuteAsync();
        var request = Assert.Single(client.Creates);
        Assert.Null(request.Draft.DepartmentId); Assert.Null(request.Draft.DisplayName);
        Assert.Null(vm.UserEditor); Assert.Equal(client.Created.Id, vm.SelectedUser!.Id);
        Assert.Equal("pending_activation", vm.SelectedUser.AccountStatus);
        Assert.Contains("отдельный процесс", vm.Message); Assert.Equal(2, client.PageReads);
    }

    [Fact]
    public async AsyncTask EditReadsFreshVersionAndSendsOnlyJobTitle()
    {
        var client = new Client(); using var vm = Vm(client);
        await vm.EditUserCommand.ExecuteAsync();
        Assert.Equal(19, vm.UserEditor!.Original!.Version); Assert.Equal("Анна", vm.UserEditor.FirstName);
        Assert.Equal(Client.User.WorkEmail, vm.UserEditor.WorkEmail);
        vm.UserEditor.JobTitle = "Инженер";
        await vm.SaveUserCommand.ExecuteAsync();
        var patch = Assert.Single(client.Updates);
        Assert.Equal(19, patch.Version); Assert.Equal(Client.User.Id, patch.Id);
        Assert.True(patch.Patch.JobTitle.IsSpecified); Assert.Equal("Инженер", patch.Patch.JobTitle.Value);
        Assert.False(patch.Patch.FirstName.IsSpecified); Assert.False(patch.Patch.LastName.IsSpecified);
        Assert.False(patch.Patch.DisplayName.IsSpecified); Assert.False(patch.Patch.Login.IsSpecified);
        Assert.False(patch.Patch.WorkEmail.IsSpecified); Assert.False(patch.Patch.DepartmentId.IsSpecified);
        Assert.Equal("Инженер", vm.SelectedUser!.JobTitle); Assert.Equal(20, vm.SelectedUser.Version);
    }

    [Fact]
    public async AsyncTask CancelAndUnchangedFormNeverMutate()
    {
        var client = new Client(); using var vm = Vm(client);
        await vm.EditUserCommand.ExecuteAsync(); await vm.SaveUserCommand.ExecuteAsync();
        Assert.Contains("Нет изменений", vm.UserEditorMessage);
        vm.UserEditor!.JobTitle = "Черновик";
        await vm.CancelUserCommand.ExecuteAsync();
        Assert.Null(vm.UserEditor); Assert.Empty(client.Updates); Assert.Equal("Руководитель", vm.SelectedUser!.JobTitle);
        await vm.EditUserCommand.ExecuteAsync(); Assert.Equal("Руководитель", vm.UserEditor!.JobTitle);
    }

    [Fact]
    public void NullableClearingIsDistinctFromOmissionAndPreservesDepartment()
    {
        var editor = new AdminUserEditor(Client.User);
        Assert.False(editor.Patch().WorkEmail.IsSpecified); Assert.False(editor.Patch().JobTitle.IsSpecified);
        editor.WorkEmail = ""; editor.JobTitle = " ";
        var patch = editor.Patch();
        Assert.True(patch.WorkEmail.IsSpecified); Assert.Null(patch.WorkEmail.Value);
        Assert.True(patch.JobTitle.IsSpecified); Assert.Null(patch.JobTitle.Value);
        Assert.False(patch.DepartmentId.IsSpecified);
        var empty = new AdminUserEditor(Client.User with { WorkEmail = null, JobTitle = null });
        Assert.False(empty.HasChanges);
    }

    [Theory]
    [InlineData("login")] [InlineData("email")]
    public async AsyncTask DuplicateLoginAndEmailRetainDraftAndRecover(string field)
    {
        var client = new Client { Failure = new DesktopUserResult<DesktopManagedUser>.Conflict { ErrorCode = "DUPLICATE_RESOURCE" } };
        using var vm = Vm(client);
        await vm.CreateUserCommand.ExecuteAsync(); Fill(vm.UserEditor!);
        vm.UserEditor!.WorkEmail = "used@example.test";
        var editor = vm.UserEditor;
        await vm.SaveUserCommand.ExecuteAsync();
        Assert.Same(editor, vm.UserEditor); Assert.Single(vm.Users);
        Assert.Contains("Логин", vm.UserEditorMessage); Assert.Contains("email", vm.UserEditorMessage);
        Assert.Contains("сервер не уточняет", vm.UserEditorMessage);
        client.Failure = null;
        if (field == "login") editor.Login = "free.login"; else editor.WorkEmail = "free@example.test";
        await vm.SaveUserCommand.ExecuteAsync();
        Assert.Null(vm.UserEditor); Assert.NotEqual(client.Creates[0].Key, client.Creates[1].Key);
    }

    [Fact]
    public async AsyncTask VersionConflictKeepsInputAndCannotBlindlyResubmit()
    {
        var client = new Client { Failure = new DesktopUserResult<DesktopManagedUser>.VersionConflict() };
        using var vm = Vm(client); await vm.EditUserCommand.ExecuteAsync();
        vm.UserEditor!.JobTitle = "Черновик"; await vm.SaveUserCommand.ExecuteAsync();
        Assert.Equal("Черновик", vm.UserEditor.JobTitle); Assert.Contains("другим администратором", vm.UserEditorMessage);
        Assert.False(await vm.SaveUserCommand.ExecuteAsync()); Assert.Single(client.Updates);
        Assert.Equal("Руководитель", vm.SelectedUser!.JobTitle);
    }

    [Fact]
    public async AsyncTask OfflineAndCapabilityRevocationKeepDraftButDisableWrites()
    {
        var client = new Client(); using var vm = Vm(client); await vm.EditUserCommand.ExecuteAsync();
        vm.UserEditor!.JobTitle = "Черновик";
        vm.UpdateConnectivity(false); Assert.False(await vm.SaveUserCommand.ExecuteAsync());
        Assert.False(vm.CanEditUserFields); Assert.Single(vm.Users);
        vm.UpdateConnectivity(true); Assert.True(vm.SaveUserCommand.CanExecute(null));
        vm.UpdateCapabilities(["User.Read"]); Assert.False(await vm.SaveUserCommand.ExecuteAsync());
        Assert.Equal("Черновик", vm.UserEditor.JobTitle); Assert.Empty(client.Updates);
        vm.UpdateCapabilities([]); Assert.Null(vm.UserEditor); Assert.Empty(vm.Users);
    }

    [Theory]
    [InlineData(403)] [InlineData(404)] [InlineData(422)] [InlineData(503)] [InlineData(500)]
    public async AsyncTask FailedDetailReadDoesNotOpenOrLocallyConfirmEditor(int status)
    {
        var client = new Client { DetailFailure = Failure(status) }; using var vm = Vm(client);
        await vm.EditUserCommand.ExecuteAsync(); Assert.Null(vm.UserEditor);
        Assert.NotNull(vm.Message); Assert.Empty(client.Updates);
        client.DetailFailure = null; vm.UpdateConnectivity(true);
        if (status == 403)
        {
            // Connectivity recovery cannot restore a server-denied User.Read grant.
            Assert.Empty(vm.Users); Assert.False(await vm.EditUserCommand.ExecuteAsync());
            vm.UpdateCapabilities(["User.Read", "User.Create", "User.Update"]);
            await vm.RefreshCommand.ExecuteAsync();
        }
        await vm.EditUserCommand.ExecuteAsync(); Assert.NotNull(vm.UserEditor);
    }

    [Fact]
    public async AsyncTask SessionExpiryClearsPermittedDataAndBlocksOperations()
    {
        var client = new Client { DetailFailure = new DesktopUserResult<DesktopManagedUser>.AuthenticationFailure() };
        using var vm = Vm(client); await vm.EditUserCommand.ExecuteAsync();
        Assert.Empty(vm.Users); Assert.Null(vm.UserEditor); Assert.False(vm.CreateUserCommand.CanExecute(null));
        Assert.Contains("Сессия завершена", vm.Message);
    }

    [Fact]
    public async AsyncTask LateDetailAfterSelectionChangeOrPermissionRevocationIsIgnored()
    {
        var client = new Client(); using var vm = Vm(client);
        client.DetailWait = new(); var opening = vm.EditUserCommand.ExecuteAsync();
        Assert.True(vm.IsUserEditorLoading);
        vm.SelectedUser = new(Guid.NewGuid(), 3, "Другой", "other", null, "active");
        client.DetailWait.SetResult(new DesktopUserResult<DesktopManagedUser>.Succeeded(Client.User));
        await opening; Assert.Null(vm.UserEditor); Assert.False(vm.IsUserEditorLoading);
        client.DetailWait = new(); opening = vm.EditUserCommand.ExecuteAsync();
        vm.UpdateCapabilities(["User.Read"]);
        client.DetailWait.SetResult(new DesktopUserResult<DesktopManagedUser>.Succeeded(Client.User));
        await opening; Assert.Null(vm.UserEditor);
    }

    [Fact]
    public async AsyncTask DoubleSaveCannotSendSecondMutationOrCancelInFlight()
    {
        var client = new Client { SaveWait = new() }; using var vm = Vm(client);
        await vm.CreateUserCommand.ExecuteAsync(); Fill(vm.UserEditor!);
        var saving = vm.SaveUserCommand.ExecuteAsync();
        Assert.False(await vm.SaveUserCommand.ExecuteAsync()); Assert.False(await vm.CancelUserCommand.ExecuteAsync());
        Assert.False(vm.CanEditUserFields); Assert.Single(client.Creates);
        client.SaveWait.SetResult(new DesktopUserResult<DesktopManagedUser>.Succeeded(client.Created));
        await saving; Assert.Null(vm.UserEditor);
    }

    [Fact]
    public async AsyncTask UncertainCreateRetryRetainsOriginalKeyAndPayload()
    {
        var client = new Client { Failure = new DesktopUserResult<DesktopManagedUser>.ServerUnavailable() };
        using var vm = Vm(client); await vm.CreateUserCommand.ExecuteAsync(); Fill(vm.UserEditor!);
        await vm.SaveUserCommand.ExecuteAsync(); Assert.NotNull(vm.UserEditor); Assert.Single(vm.Users);
        Assert.False(vm.CanEditUserFields); Assert.False(vm.SaveUserCommand.CanExecute(null));
        client.Failure = null; vm.UpdateConnectivity(true); await vm.SaveUserCommand.ExecuteAsync();
        Assert.Equal(2, client.Creates.Count); Assert.Equal(client.Creates[0], client.Creates[1]);
        Assert.Null(vm.UserEditor);
    }

    [Fact]
    public async AsyncTask EmptyPaginationAndLateListAfterPermissionLoss()
    {
        var client = new Client(); using var vm = Vm(client);
        client.NextCursor = "next-page"; await vm.RefreshCommand.ExecuteAsync(); Assert.True(vm.HasMoreUsers);
        client.PageUsers = [Client.User with { Id = Guid.NewGuid(), DisplayName = "Вторая" }];
        client.NextCursor = null; await vm.MoreUsersCommand.ExecuteAsync();
        Assert.Equal(2, vm.Users.Count); Assert.False(vm.HasMoreUsers); Assert.Equal("next-page", client.LastQuery!.Cursor);
        client.PageUsers = []; await vm.RefreshCommand.ExecuteAsync(); Assert.True(vm.IsUserListEmpty);
        client.PageWait = new(); var refresh = vm.RefreshCommand.ExecuteAsync();
        vm.UpdateCapabilities([]);
        client.PageWait.SetResult(new DesktopUserResult<DesktopUserPage>.Succeeded(new([Client.User], null, 1)));
        await refresh; Assert.Empty(vm.Users);
    }

    [Fact]
    public async AsyncTask ConfirmedSaveSurvivesFailedListRefresh()
    {
        var client = new Client { PageWait = new() };
        using var vm = new AdministrationViewModel(client, ["User.Read", "User.Create"]);
        client.PageWait = null; vm.Activate();
        await vm.CreateUserCommand.ExecuteAsync(); Fill(vm.UserEditor!);
        client.PageWait = new(); var save = vm.SaveUserCommand.ExecuteAsync();
        client.PageWait.SetException(new InvalidOperationException("read failed after receipt"));
        await save;
        Assert.Null(vm.UserEditor); Assert.Equal(client.Created.Id, vm.SelectedUser!.Id);
        Assert.Contains("подтверждена ответом сервера", vm.Message); Assert.Single(client.Creates);
    }

    [Fact]
    public async AsyncTask SameCapabilitiesDuringSaveDoNotDiscardConfirmedReceipt()
    {
        var client = new Client { SaveWait = new() }; using var vm = Vm(client);
        await vm.CreateUserCommand.ExecuteAsync(); Fill(vm.UserEditor!);
        var save = vm.SaveUserCommand.ExecuteAsync();
        vm.UpdateCapabilities(["User.Read", "User.Create", "User.Update"]);
        client.SaveWait.SetResult(new DesktopUserResult<DesktopManagedUser>.Succeeded(client.Created));
        await save; Assert.Null(vm.UserEditor); Assert.Equal(client.Created.Id, vm.SelectedUser!.Id);
    }

    [Fact]
    public async AsyncTask CapabilityChangeDuringSaveKeepsOriginalRequestForExplicitRetry()
    {
        var client = new Client { SaveWait = new() }; using var vm = Vm(client);
        await vm.CreateUserCommand.ExecuteAsync(); Fill(vm.UserEditor!);
        var save = vm.SaveUserCommand.ExecuteAsync();
        vm.UpdateCapabilities(["User.Read"]);
        client.SaveWait.SetResult(new DesktopUserResult<DesktopManagedUser>.Succeeded(client.Created));
        await save; Assert.NotNull(vm.UserEditor); Assert.False(vm.CanEditUserFields);
        vm.UpdateCapabilities(["User.Read", "User.Create"]);
        Assert.False(vm.CanEditUserFields); client.SaveWait = null;
        await vm.SaveUserCommand.ExecuteAsync(); Assert.Null(vm.UserEditor);
        Assert.Equal(client.Creates[0], client.Creates[1]);
    }

    internal static AdministrationViewModel Vm(Client client, bool create = true, bool update = true)
    {
        var caps = new List<string> { "User.Read" }; if (create) caps.Add("User.Create"); if (update) caps.Add("User.Update");
        var vm = new AdministrationViewModel(client, caps); vm.Activate(); return vm;
    }
    private static AdminUserEditor Draft() { var editor = new AdminUserEditor(); Fill(editor); return editor; }
    internal static void Fill(AdminUserEditor editor) { editor.FirstName = "Анна"; editor.LastName = "Крылова"; editor.Login = "anna.k"; }
    private static DesktopUserResult<DesktopManagedUser> Failure(int code) => code switch
    {
        403 => new DesktopUserResult<DesktopManagedUser>.Forbidden(), 404 => new DesktopUserResult<DesktopManagedUser>.NotFound(),
        422 => new DesktopUserResult<DesktopManagedUser>.ValidationFailure("ignored"),
        503 => new DesktopUserResult<DesktopManagedUser>.ServerUnavailable(), _ => new DesktopUserResult<DesktopManagedUser>.MalformedResponse(),
    };

    internal sealed class Client : IDesktopAdministrationApiClient
    {
        internal static readonly DesktopManagedUser User = new(Guid.NewGuid(), Guid.NewGuid(), 19,
            "Анна Крылова", "Анна", "Крылова", "anna.k", "anna@example.test", Guid.NewGuid(), "Руководитель", "active");
        public DesktopManagedUser Created { get; } = User with { Id = Guid.NewGuid(), Version = 1, AccountStatus = "pending_activation" };
        public DesktopUserResult<DesktopManagedUser>? Failure, DetailFailure;
        public TaskCompletionSource<DesktopUserResult<DesktopManagedUser>>? DetailWait, SaveWait;
        public TaskCompletionSource<DesktopUserResult<DesktopUserPage>>? PageWait;
        public List<(DesktopUserCreate Draft, string Key)> Creates { get; } = [];
        public List<(Guid Id, long Version, DesktopUserPatch Patch, string Key)> Updates { get; } = [];
        public int PageReads;
        public DesktopUserQuery? LastQuery;
        public string? NextCursor;
        public IReadOnlyList<DesktopManagedUser> PageUsers = [User];
        public System.Threading.Tasks.Task<DesktopUserResult<DesktopUserPage>> GetUserPageAsync(DesktopUserQuery query, CancellationToken cancellationToken = default)
        {
            PageReads++; LastQuery = query;
            return PageWait?.Task ?? System.Threading.Tasks.Task.FromResult<DesktopUserResult<DesktopUserPage>>(
                new DesktopUserResult<DesktopUserPage>.Succeeded(new(PageUsers, NextCursor, PageUsers.Count)));
        }
        public System.Threading.Tasks.Task<DesktopUserResult<DesktopManagedUser>> GetUserDetailsAsync(Guid id, CancellationToken cancellationToken = default)
            => DetailWait?.Task ?? Result(DetailFailure ?? new DesktopUserResult<DesktopManagedUser>.Succeeded(User));
        public System.Threading.Tasks.Task<DesktopUserResult<DesktopManagedUser>> CreateUserAsync(DesktopUserCreate draft, string idempotencyKey, CancellationToken cancellationToken = default)
        { Creates.Add((draft, idempotencyKey)); return SaveWait?.Task ?? Result(Failure ?? new DesktopUserResult<DesktopManagedUser>.Succeeded(Created)); }
        public System.Threading.Tasks.Task<DesktopUserResult<DesktopManagedUser>> UpdateUserAsync(Guid id, long version, DesktopUserPatch patch, string idempotencyKey, CancellationToken cancellationToken = default)
        { Updates.Add((id, version, patch, idempotencyKey)); return SaveWait?.Task ?? Result(Failure ?? new DesktopUserResult<DesktopManagedUser>.Succeeded(User with { Version = version + 1, JobTitle = patch.JobTitle.Value })); }
        private static System.Threading.Tasks.Task<DesktopUserResult<DesktopManagedUser>> Result(DesktopUserResult<DesktopManagedUser> result) => System.Threading.Tasks.Task.FromResult(result);
        public System.Threading.Tasks.Task<DesktopWorkResult<IReadOnlyList<DesktopAdminUser>>> GetUsersAsync(CancellationToken cancellationToken = default) => throw new InvalidOperationException("Legacy user list must not be used.");
        public System.Threading.Tasks.Task<DesktopWorkResult<IReadOnlyList<DesktopAdminRole>>> GetRolesAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public System.Threading.Tasks.Task<DesktopWorkResult<IReadOnlyList<DesktopNetworkResource>>> GetResourcesAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public System.Threading.Tasks.Task<DesktopUserResult<DesktopManagedUser>> TransitionUserAsync(Guid id, long version, DesktopUserTransition transition, string? idempotencyKey = null, string? reason = null, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Profile editor must never call transitions.");
    }
}
