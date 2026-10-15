using Task.Desktop.Administration;
using Task.Desktop.ViewModels;
using Task.Desktop.Work;
using AsyncTask = System.Threading.Tasks.Task;

namespace Task.Desktop.Tests.Employees;

public sealed class EmployeePickerTests
{
    internal static DesktopAdminUser User(string login = "ivan", string status = "active") =>
        new(Guid.NewGuid(), 1, "Иван Иванов", login, "Инженер", status);
    private static DesktopWorkResult<IReadOnlyList<DesktopAdminUser>> Page(params DesktopAdminUser[] users) =>
        new DesktopWorkResult<IReadOnlyList<DesktopAdminUser>>.Succeeded(users);

    [Fact]
    public async AsyncTask SearchSelectClearAndNamesakesUseAccountIdentity()
    {
        var first = User(); var second = User("ivan2");
        var directory = new Directory { Search = (q, _, _) => { Assert.Equal("Иван", q); return AsyncTask.FromResult(Page(first, second)); } };
        using var picker = new EmployeePickerViewModel(directory, ["User.Read"], debounce: TimeSpan.Zero);
        picker.SearchText = "Иван"; await picker.CurrentOperation;
        Assert.Equal(2, picker.Results.Count); Assert.Equal(first.DisplayName, second.DisplayName);
        Assert.NotEqual(first.Login, second.Login); Assert.Equal("Инженер", picker.Results[1].JobTitle);
        picker.Candidate = second;
        Assert.Null(picker.SelectedUserAccountId);
        Assert.True(picker.ConfirmSelection()); Assert.Equal(second.Id, picker.SelectedUserAccountId);
        picker.ClearSelection(); Assert.Null(picker.SelectedUserAccountId); Assert.Null(picker.Candidate);
        picker.Candidate = first with { Id = Guid.NewGuid() };
        Assert.False(picker.ConfirmSelection());
    }

    [Fact]
    public async AsyncTask PreselectionUsesSingleAccountAndRequiredFieldCannotClear()
    {
        var user = User(); var directory = new Directory { Get = (id, _) => { Assert.Equal(user.Id, id); return AsyncTask.FromResult<DesktopWorkResult<DesktopAdminUser>>(new DesktopWorkResult<DesktopAdminUser>.Succeeded(user)); } };
        using var picker = new EmployeePickerViewModel(directory, ["User.Read"], allowClear: false);
        await picker.PreselectAsync(user.Id);
        Assert.Equal(user.Id, picker.SelectedUserAccountId); Assert.Equal(0, directory.SearchCalls);
        Assert.False(picker.ClearCommand.CanExecute(null)); picker.ClearSelection();
        Assert.Equal(user.Id, picker.SelectedUserAccountId);
    }

    [Fact]
    public async AsyncTask PreselectionFailureRetriesSameIdAndStaleReadCannotRestoreRevokedData()
    {
        var user = User(); var failure = true;
        var pending = new System.Threading.Tasks.TaskCompletionSource<DesktopWorkResult<DesktopAdminUser>>();
        var directory = new Directory { Get = (id, _) =>
        {
            Assert.Equal(user.Id, id);
            return AsyncTask.FromResult<DesktopWorkResult<DesktopAdminUser>>(failure
                ? new DesktopWorkResult<DesktopAdminUser>.NotFound() : new DesktopWorkResult<DesktopAdminUser>.Succeeded(user));
        } };
        using var picker = new EmployeePickerViewModel(directory, ["User.Read"]);
        await picker.PreselectAsync(user.Id); Assert.Equal(EmployeePickerState.NotFound, picker.State); Assert.Null(picker.SelectedUserAccountId);
        failure = false; await picker.RetryCommand.ExecuteAsync(); Assert.Equal(user.Id, picker.SelectedUserAccountId);
        directory.Get = (_, _) => pending.Task;
        var operation = picker.PreselectAsync(user.Id); picker.UpdateAccess([]);
        pending.SetResult(new DesktopWorkResult<DesktopAdminUser>.Succeeded(user)); await operation;
        Assert.Null(picker.SelectedUserAccountId); Assert.Equal(EmployeePickerState.Forbidden, picker.State);
    }

    [Fact]
    public async AsyncTask EmptyAndPaginationWithDuplicatesAndInactiveOnlyPage()
    {
        var first = User(); var second = User("second"); var step = 0;
        var directory = new Directory { Search = (_, cursor, _) => AsyncTask.FromResult<DesktopWorkResult<IReadOnlyList<DesktopAdminUser>>>(++step switch
        {
            1 => new DesktopWorkResult<IReadOnlyList<DesktopAdminUser>>.Succeeded([User(status: "blocked")]) { NextCursor = "one" },
            2 when cursor == "one" => new DesktopWorkResult<IReadOnlyList<DesktopAdminUser>>.Succeeded([first]) { NextCursor = "two" },
            3 when cursor == "two" => new DesktopWorkResult<IReadOnlyList<DesktopAdminUser>>.Succeeded([first, second]),
            _ => throw new InvalidOperationException(),
        }) };
        using var picker = new EmployeePickerViewModel(directory, ["User.Read"]);
        await picker.SearchAsync(); Assert.Equal(EmployeePickerState.Empty, picker.State); Assert.True(picker.HasMore);
        await picker.LoadMoreAsync(); await picker.LoadMoreAsync(); await picker.LoadMoreAsync();
        Assert.Equal(new[] { first.Id, second.Id }, picker.Results.Select(u => u.Id));
        Assert.False(picker.HasMore); Assert.Equal(3, directory.SearchCalls);
    }

    [Fact]
    public async AsyncTask NewInputCancelsOldRequestAndIgnoresNonCooperativeResponse()
    {
        var old = new System.Threading.Tasks.TaskCompletionSource<DesktopWorkResult<IReadOnlyList<DesktopAdminUser>>>();
        CancellationToken oldToken = default; var current = User("current");
        var directory = new Directory { Search = (q, _, token) =>
        { if (q == "old") { oldToken = token; return old.Task; } return AsyncTask.FromResult(Page(current)); } };
        using var picker = new EmployeePickerViewModel(directory, ["User.Read"], debounce: TimeSpan.Zero);
        picker.SearchText = "old"; var pending = picker.CurrentOperation;
        picker.SearchText = "new"; await picker.CurrentOperation;
        Assert.True(oldToken.IsCancellationRequested);
        old.SetResult(Page(User("stale"))); await pending;
        Assert.Same(current, Assert.Single(picker.Results)); Assert.Equal(EmployeePickerState.Ready, picker.State);
    }

    [Fact]
    public async AsyncTask DebounceDoesNotQueryEveryKeystroke()
    {
        var directory = new Directory();
        using var picker = new EmployeePickerViewModel(directory, ["User.Read"], debounce: TimeSpan.FromMilliseconds(40));
        picker.SearchText = "И"; var first = picker.CurrentOperation;
        picker.SearchText = "Ив"; var second = picker.CurrentOperation;
        picker.SearchText = "Иван";
        await AsyncTask.WhenAll(first, second, picker.CurrentOperation);
        Assert.Equal(1, directory.SearchCalls);
    }

    [Fact]
    public async AsyncTask CancelAndDisposeInvalidateLateResponses()
    {
        var pending = new System.Threading.Tasks.TaskCompletionSource<DesktopWorkResult<IReadOnlyList<DesktopAdminUser>>>();
        var directory = new Directory { Search = (_, _, _) => pending.Task };
        using var picker = new EmployeePickerViewModel(directory, ["User.Read"]);
        var operation = picker.SearchAsync(); picker.Cancel();
        pending.SetResult(Page(User())); await operation;
        Assert.Equal(EmployeePickerState.Idle, picker.State); Assert.Empty(picker.Results);
        pending = new(); operation = picker.SearchAsync(); picker.Dispose();
        pending.SetResult(Page(User())); await operation;
        Assert.Empty(picker.Results); Assert.False(picker.CanQuery);
    }

    [Fact]
    public async AsyncTask ExternalCancellationWithNonCooperativeTransportExitsLoading()
    {
        using var cancel = new CancellationTokenSource();
        var pending = new System.Threading.Tasks.TaskCompletionSource<DesktopWorkResult<IReadOnlyList<DesktopAdminUser>>>();
        using var picker = new EmployeePickerViewModel(new Directory { Search = (_, _, _) => pending.Task }, ["User.Read"]);
        var operation = picker.SearchAsync(cancel.Token); cancel.Cancel();
        pending.SetResult(Page(User())); await operation;
        Assert.False(picker.IsLoading); Assert.Empty(picker.Results);
    }

    [Fact]
    public async AsyncTask ForbiddenDoesNotLoadOrDiscloseAndRevocationClearsSelection()
    {
        var user = User(); var directory = new Directory { Search = (_, _, _) => AsyncTask.FromResult(Page(user)) };
        using var picker = new EmployeePickerViewModel(directory, ["Project.ManageMembers", "Employee.Read"]);
        await picker.SearchAsync(); await picker.PreselectAsync(user.Id);
        Assert.Equal(EmployeePickerState.Forbidden, picker.State); Assert.Equal(0, directory.SearchCalls); Assert.Equal(0, directory.GetCalls);
        picker.UpdateAccess(["User.Read"]); await picker.SearchAsync(); picker.Candidate = user; Assert.True(picker.ConfirmSelection());
        picker.UpdateAccess([]); Assert.Empty(picker.Results); Assert.Null(picker.SelectedUserAccountId); Assert.False(picker.ConfirmSelection());
    }

    [Theory]
    [InlineData("forbidden", EmployeePickerState.Forbidden)]
    [InlineData("auth", EmployeePickerState.AuthenticationRequired)]
    [InlineData("unavailable", EmployeePickerState.Unavailable)]
    [InlineData("malformed", EmployeePickerState.Error)]
    [InlineData("missing", EmployeePickerState.NotFound)]
    public async AsyncTask FailureIsSafeAndRetryRecovers(string kind, EmployeePickerState state)
    {
        var user = User(); var failure = true;
        var directory = new Directory { Search = (_, _, _) => AsyncTask.FromResult(failure ? kind switch
        {
            "forbidden" => new DesktopWorkResult<IReadOnlyList<DesktopAdminUser>>.Forbidden(),
            "auth" => new DesktopWorkResult<IReadOnlyList<DesktopAdminUser>>.AuthenticationFailure(),
            "unavailable" => new DesktopWorkResult<IReadOnlyList<DesktopAdminUser>>.ServerUnavailable(),
            "missing" => new DesktopWorkResult<IReadOnlyList<DesktopAdminUser>>.NotFound(),
            _ => (DesktopWorkResult<IReadOnlyList<DesktopAdminUser>>)new DesktopWorkResult<IReadOnlyList<DesktopAdminUser>>.MalformedResponse(),
        } : Page(user)) };
        using var picker = new EmployeePickerViewModel(directory, ["User.Read"]);
        await picker.SearchAsync(); Assert.Equal(state, picker.State); Assert.Empty(picker.Results);
        failure = false; await picker.RetryCommand.ExecuteAsync(); Assert.Same(user, Assert.Single(picker.Results));
        picker.UpdateAccess(["User.Read"], online: false); await picker.SearchAsync();
        Assert.Equal(EmployeePickerState.Offline, picker.State); Assert.Equal(2, directory.SearchCalls);
        picker.UpdateAccess(["User.Read"], sessionReady: false);
        Assert.Equal(EmployeePickerState.AuthenticationRequired, picker.State);
    }

    [Theory]
    [InlineData("blocked")][InlineData("deactivated")][InlineData("pending_activation")][InlineData("unknown")]
    public async AsyncTask ActiveOnlyRejectsInvalidAccountsIncludingPreselection(string status)
    {
        var user = User(status: status);
        var directory = new Directory { Search = (_, _, _) => AsyncTask.FromResult(Page(user)),
            Get = (_, _) => AsyncTask.FromResult<DesktopWorkResult<DesktopAdminUser>>(new DesktopWorkResult<DesktopAdminUser>.Succeeded(user)) };
        using var picker = new EmployeePickerViewModel(directory, ["User.Read"]);
        await picker.SearchAsync(); Assert.Empty(picker.Results);
        picker.Candidate = user; Assert.False(picker.ConfirmSelection());
        await picker.PreselectAsync(user.Id); Assert.Null(picker.SelectedUserAccountId); Assert.Equal(EmployeePickerState.InvalidSelection, picker.State);
        using var unrestricted = new EmployeePickerViewModel(directory, ["User.Read"], activeOnly: false);
        await unrestricted.SearchAsync(); Assert.Equal(status == "unknown" ? 0 : 1, unrestricted.Results.Count);
    }

    [Fact]
    public async AsyncTask RepeatedCursorStopsAndFailedNextPageRetriesSameCursor()
    {
        var first = User(); var count = 0; var cursors = new List<string?>();
        var directory = new Directory { Search = (_, cursor, _) =>
        {
            cursors.Add(cursor);
            return AsyncTask.FromResult<DesktopWorkResult<IReadOnlyList<DesktopAdminUser>>>(++count switch
            {
                1 => new DesktopWorkResult<IReadOnlyList<DesktopAdminUser>>.Succeeded([first]) { NextCursor = "next" },
                2 => new DesktopWorkResult<IReadOnlyList<DesktopAdminUser>>.ServerUnavailable(),
                _ => new DesktopWorkResult<IReadOnlyList<DesktopAdminUser>>.Succeeded([User()]) { NextCursor = "next" },
            });
        } };
        using var picker = new EmployeePickerViewModel(directory, ["User.Read"]);
        await picker.SearchAsync(); await picker.LoadMoreAsync();
        picker.Candidate = first; Assert.False(picker.CanConfirm);
        await picker.RetryCommand.ExecuteAsync();
        Assert.Equal(new string?[] { null, "next", "next" }, cursors); Assert.Equal(EmployeePickerState.Error, picker.State);
        Assert.Same(first, Assert.Single(picker.Results));
    }

    internal sealed class Directory : IDesktopEmployeeDirectory
    {
        public int SearchCalls, GetCalls;
        public Func<string, string?, CancellationToken, System.Threading.Tasks.Task<DesktopWorkResult<IReadOnlyList<DesktopAdminUser>>>> Search = (_, _, _) => AsyncTask.FromResult(Page());
        public Func<Guid, CancellationToken, System.Threading.Tasks.Task<DesktopWorkResult<DesktopAdminUser>>> Get = (_, _) => AsyncTask.FromResult<DesktopWorkResult<DesktopAdminUser>>(new DesktopWorkResult<DesktopAdminUser>.NotFound());
        public System.Threading.Tasks.Task<DesktopWorkResult<IReadOnlyList<DesktopAdminUser>>> SearchUsersAsync(string filter, string? cursor = null, CancellationToken cancellationToken = default)
        { SearchCalls++; return Search(filter, cursor, cancellationToken); }
        public System.Threading.Tasks.Task<DesktopWorkResult<DesktopAdminUser>> GetUserAsync(Guid userAccountId, CancellationToken cancellationToken = default)
        { GetCalls++; return Get(userAccountId, cancellationToken); }
    }
}
