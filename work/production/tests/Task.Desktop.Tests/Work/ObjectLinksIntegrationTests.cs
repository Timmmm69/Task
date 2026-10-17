using Task.Desktop.ViewModels;
using Task.Desktop.Work;
using AsyncTask = System.Threading.Tasks.Task;
using LinkResult = Task.Desktop.Work.DesktopWorkResult<Task.Desktop.Work.DesktopObjectLinks>;
using PageResult = Task.Desktop.Work.DesktopWorkResult<System.Collections.Generic.IReadOnlyList<Task.Desktop.Work.DesktopSearchResult>>;

namespace Task.Desktop.Tests.Work;

public sealed class ObjectLinksIntegrationTests
{
    internal static readonly string[] Access = [.. ObjectLinkTargetPickerTests.Capabilities, "ObjectLink.Read", "ObjectLink.Delete", "FileCatalog.Update"];
    internal sealed class Client : IDesktopObjectLinksClient, IDesktopObjectLinkTargetSearchClient
    {
        public DesktopSearchResult Hit = ObjectLinkTargetPickerTests.Hit();
        public (Guid Source, long Version, Guid Target, string Type)? Added;
        public int Adds, Removes;
        public long Version = 7;
        public IReadOnlyList<DesktopObjectLink> Rows = [];
        public Func<CancellationToken, System.Threading.Tasks.Task<LinkResult>>? Write, Read;
        public Func<CancellationToken, System.Threading.Tasks.Task<PageResult>>? Search;
        public System.Threading.Tasks.Task<LinkResult> GetLinksAsync(Guid source, CancellationToken ct = default) => Read?.Invoke(ct) ?? AsyncTask.FromResult<LinkResult>(new LinkResult.Succeeded(new(Rows, Version)));
        public System.Threading.Tasks.Task<LinkResult> AddLinkAsync(Guid source, long version, Guid target, string type, CancellationToken ct = default)
        {
            Added = (source, version, target, type); Adds++;
            if (Write is not null) return Write(ct);
            Rows = [new(Guid.NewGuid(), source, target, type)]; Version++;
            return GetLinksAsync(source, ct);
        }
        public System.Threading.Tasks.Task<LinkResult> RemoveLinkAsync(Guid source, long version, Guid link, CancellationToken ct = default)
        { Removes++; Rows = []; Version++; return GetLinksAsync(source, ct); }
        public System.Threading.Tasks.Task<PageResult> SearchTargetsAsync(string q, IReadOnlyCollection<string> types, string? cursor, CancellationToken ct = default)
            => Search?.Invoke(ct) ?? AsyncTask.FromResult<PageResult>(new PageResult.Succeeded([Hit]));
    }
    internal static ObjectLinksViewModel Model(Client client, Func<bool>? create = null, Func<bool>? delete = null)
        => new(client, create, delete, targetPicker: new(client, Access, TimeSpan.Zero), corporate: true) { ShowTargetId = false };
    internal static async AsyncTask Select(ObjectLinksViewModel vm)
    {
        if (vm.TargetPicker!.SearchText == "План") await vm.TargetPicker.SearchAsync();
        else { vm.TargetPicker.SearchText = "План"; await vm.TargetPicker.CurrentOperation; }
        vm.TargetPicker.Candidate = Assert.Single(vm.TargetPicker.Results);
        Assert.True(vm.TargetPicker.ConfirmSelection());
    }

    [Fact]
    public async AsyncTask PickerAddsRealIdOnlyAfterConfirmationAndServerResponse()
    {
        var client = new Client(); using var vm = Model(client); var source = Guid.NewGuid();
        vm.SetSource(source, 7, "task"); vm.TargetId = Guid.NewGuid().ToString();
        Assert.False(vm.AddCommand.CanExecute(null));
        await Select(vm);
        var pending = new System.Threading.Tasks.TaskCompletionSource<LinkResult>();
        client.Write = _ => pending.Task;
        var operation = vm.AddCommand.ExecuteAsync();
        Assert.Equal((source, 7L, client.Hit.ObjectId, "task_file"), client.Added);
        Assert.Empty(vm.Items); Assert.True(vm.IsBusy);
        Assert.False(vm.RefreshCommand.CanExecute(null)); Assert.False(await vm.AddCommand.ExecuteAsync());
        var row = new DesktopObjectLink(Guid.NewGuid(), source, client.Hit.ObjectId, "task_file");
        pending.SetResult(new LinkResult.Succeeded(new([row], 8))); await operation;
        Assert.Same(row, Assert.Single(vm.Items)); Assert.Null(vm.TargetPicker!.SelectedObjectId); Assert.False(vm.IsBusy);
        client.Hit = ObjectLinkTargetPickerTests.Hit(); await Select(vm); client.Write = null;
        await vm.AddCommand.ExecuteAsync(); Assert.Equal(8, client.Added!.Value.Version);
    }
    [Theory]
    [InlineData("task", "task_project", "project")]
    [InlineData("project", "project_contact", "contact")]
    [InlineData("project", "project_contact", "company")]
    [InlineData("catalog_item", "related", "task")]
    [InlineData("calendar_event", "related", "employee_profile")]
    [InlineData("interaction", "related", "catalog_item")]
    public async AsyncTask AdditionalServerTypesCreateUsingTheConfirmedTarget(string sourceType, string type, string targetType)
    {
        var client = new Client { Hit = ObjectLinkTargetPickerTests.Hit(targetType) };
        using var vm = Model(client); var source = Guid.NewGuid();
        vm.SetSource(source, 7, sourceType); vm.LinkType = type;
        vm.UpdateAccess([.. Access, "Calendar.Read", "CalendarEvent.Update", "Interaction.Update", "Employee.Read"]);
        await Select(vm); Assert.True(vm.AddCommand.CanExecute(null));
        await vm.AddCommand.ExecuteAsync();
        Assert.Equal((source, 7L, client.Hit.ObjectId, type), client.Added);
        Assert.Equal(client.Hit.ObjectId, Assert.Single(vm.Items).TargetObjectId);
    }
    [Theory]
    [InlineData("task", "task_file,task_contact,task_project,related")][InlineData("project", "project_file,project_contact,related")]
    [InlineData("contact", "contact_file,related")][InlineData("company", "contact_file,related")]
    [InlineData("catalog_item", "related")][InlineData("calendar_event", "related")][InlineData("interaction", "related")][InlineData("unknown", "")]
    public void SourceMatrixHasNoImpossibleCreation(string source, string types)
    {
        using var vm = Model(new()); vm.SetSource(Guid.NewGuid(), 1, source);
        Assert.Equal(types, string.Join(',', vm.LinkTypes.Select(t => t.Value)));
        Assert.Equal(types.Length > 0, vm.HasCreationTypes);
        vm.LinkType = "task_file"; Assert.False(vm.AddCommand.CanExecute(null));
    }
    [Fact]
    public async AsyncTask TypeAndSourceChangesCancelSearchAndClearTargetEvenOnAba()
    {
        var client = new Client(); using var vm = Model(client); var first = Guid.NewGuid();
        vm.SetSource(first, 7, "task"); await Select(vm);
        vm.LinkType = "task_contact"; Assert.Null(vm.TargetPicker!.SelectedTarget); Assert.Empty(vm.TargetPicker.Results);
        Assert.Equal(new[] { "contact", "company" }, vm.TargetPicker.AllowedTargetTypes);
        var pending = new System.Threading.Tasks.TaskCompletionSource<PageResult>(); CancellationToken requestToken = default;
        client.Search = ct => { requestToken = ct; return pending.Task; };
        vm.TargetPicker.SearchText = "Другой"; var search = vm.TargetPicker.CurrentOperation;
        vm.SetSource(Guid.NewGuid(), 3, "project"); vm.SetSource(first, 7, "task");
        Assert.True(requestToken.IsCancellationRequested);
        pending.SetResult(new PageResult.Succeeded([ObjectLinkTargetPickerTests.Hit("contact")])); await search;
        Assert.Empty(vm.TargetPicker.Results); Assert.False(vm.AddCommand.CanExecute(null));
    }
    [Fact]
    public async AsyncTask VersionChangeOnSameSourceInvalidatesConfirmedSelection()
    {
        using var vm = Model(new()); var id = Guid.NewGuid(); vm.SetSource(id, 7, "task"); await Select(vm);
        vm.SetSource(id, 8, "task"); Assert.Null(vm.TargetPicker!.SelectedTarget); Assert.False(vm.AddCommand.CanExecute(null));
    }
    [Fact]
    public async AsyncTask CreationAndDeletionHaveIndependentPermissions()
    {
        var create = true; var remove = false; var client = new Client();
        using var vm = Model(client, () => create, () => remove); vm.SetSource(Guid.NewGuid(), 7, "task"); await Select(vm);
        Assert.True(vm.AddCommand.CanExecute(null));
        await vm.AddCommand.ExecuteAsync(); Assert.False(vm.RemoveCommand.CanExecute(null));
        create = false; remove = true; client.Hit = ObjectLinkTargetPickerTests.Hit(); await Select(vm);
        Assert.False(vm.AddCommand.CanExecute(null)); Assert.True(vm.RemoveCommand.CanExecute(null));
        await vm.RemoveCommand.ExecuteAsync(); Assert.Empty(vm.Items);
    }
    [Fact]
    public async AsyncTask KnownDuplicateAndSelfLinkAreBlocked()
    {
        var client = new Client(); using var vm = Model(client); var source = Guid.NewGuid(); vm.SetSource(source, 7, "task");
        client.Rows = [new(Guid.NewGuid(), source, client.Hit.ObjectId, "task_file")];
        await vm.RefreshCommand.ExecuteAsync(); await Select(vm); Assert.False(vm.AddCommand.CanExecute(null));
        vm.SetSource(client.Hit.ObjectId, 7, "task");
        vm.TargetPicker!.SearchText = "План"; await vm.TargetPicker.CurrentOperation;
        Assert.Empty(vm.TargetPicker.Results); Assert.False(vm.AddCommand.CanExecute(null));
    }
    [Theory]
    [InlineData("version")][InlineData("forbidden")][InlineData("deleted")][InlineData("archived")]
    [InlineData("offline")][InlineData("auth")][InlineData("unknown")][InlineData("duplicate")]
    public async AsyncTask FailedWriteKeepsConfirmedRowsAndRequiresReconciliation(string error)
    {
        var client = new Client(); using var vm = Model(client); var source = Guid.NewGuid(); vm.SetSource(source, 7, "task");
        var old = new DesktopObjectLink(Guid.NewGuid(), source, Guid.NewGuid(), "task_file"); client.Rows = [old];
        await vm.RefreshCommand.ExecuteAsync(); await Select(vm);
        LinkResult failure = error switch
        {
            "version" => new LinkResult.Conflict(), "forbidden" => new LinkResult.Forbidden(), "deleted" => new LinkResult.NotFound(),
            "offline" => new LinkResult.ServerUnavailable(), "auth" => new LinkResult.AuthenticationFailure(),
            "archived" => new LinkResult.ValidationFailure("INTERNAL_PRIVATE_DETAILS"),
            "duplicate" => new LinkResult.ValidationFailure("Такая связь уже существует. Обновите связи."), _ => new LinkResult.MalformedResponse(),
        };
        client.Write = _ => AsyncTask.FromResult(failure); await vm.AddCommand.ExecuteAsync();
        if (error == "auth") Assert.Empty(vm.Items); else Assert.Same(old, Assert.Single(vm.Items));
        Assert.DoesNotContain("обновлены", vm.Message); Assert.DoesNotContain("INTERNAL", vm.Message);
        Assert.False(vm.AddCommand.CanExecute(null)); Assert.False(vm.RemoveCommand.CanExecute(null));
        Assert.Null(vm.TargetPicker!.SelectedTarget);
        if (error == "auth") vm.UpdateAccess(Access);
        client.Write = null; await vm.RefreshCommand.ExecuteAsync(); await Select(vm);
        Assert.True(vm.AddCommand.CanExecute(null));
    }
    [Fact]
    public async AsyncTask SourceChangeIgnoresLateMutationAndLateRefreshIncludingAba()
    {
        var client = new Client(); using var vm = Model(client); var source = Guid.NewGuid(); vm.SetSource(source, 7, "task"); await Select(vm);
        var pending = new System.Threading.Tasks.TaskCompletionSource<LinkResult>(); client.Write = _ => pending.Task;
        var changed = 0; vm.Changed += _ => changed++;
        var add = vm.AddCommand.ExecuteAsync(); vm.SetSource(Guid.NewGuid(), 1, "contact"); vm.SetSource(source, 7, "task");
        pending.SetResult(new LinkResult.Succeeded(new([new(Guid.NewGuid(), source, client.Hit.ObjectId, "task_file")], 8)));
        await add; Assert.Empty(vm.Items); Assert.Equal(0, changed); Assert.Empty(vm.Message);
    }
    [Theory]
    [InlineData(false)][InlineData(true)]
    public async AsyncTask UnexpectedExceptionIsSafeAndLateFailureDoesNotAffectNewSource(bool changeSource)
    {
        var client = new Client(); using var vm = Model(client); vm.SetSource(Guid.NewGuid(), 7, "task"); await Select(vm);
        var pending = new System.Threading.Tasks.TaskCompletionSource<LinkResult>(); client.Write = _ => pending.Task;
        var operation = vm.AddCommand.ExecuteAsync();
        if (changeSource) vm.SetSource(Guid.NewGuid(), 2, "contact");
        pending.SetException(new InvalidOperationException("PRIVATE_SERVER_DETAILS")); await operation;
        Assert.DoesNotContain("PRIVATE", vm.Message); Assert.Empty(vm.Items);
        if (changeSource) Assert.Empty(vm.Message); else { Assert.Contains("подтвердить", vm.Message); Assert.False(vm.AddCommand.CanExecute(null)); }
    }

    [Fact]
    public async AsyncTask LateReadCannotReplaceNewSourceStateAndDisposeCancelsSearch()
    {
        var client = new Client(); using var vm = Model(client); var source = Guid.NewGuid(); vm.SetSource(source, 7, "task");
        var read = new System.Threading.Tasks.TaskCompletionSource<LinkResult>(); client.Read = _ => read.Task;
        var operation = vm.RefreshCommand.ExecuteAsync(); vm.SetSource(Guid.NewGuid(), 2, "contact"); vm.SetSource(source, 7, "task");
        read.SetResult(new LinkResult.Succeeded(new([new(Guid.NewGuid(), source, client.Hit.ObjectId, "task_file")], 9)));
        await operation; Assert.Empty(vm.Items); Assert.Empty(vm.Message);
        var search = new System.Threading.Tasks.TaskCompletionSource<PageResult>(); CancellationToken token = default;
        client.Search = ct => { token = ct; return search.Task; }; vm.TargetPicker!.SearchText = "План";
        var pendingSearch = vm.TargetPicker.CurrentOperation; vm.Dispose();
        search.SetResult(new PageResult.Succeeded([client.Hit])); await pendingSearch;
        Assert.True(token.IsCancellationRequested); Assert.Empty(vm.TargetPicker.Results); Assert.False(vm.RefreshCommand.CanExecute(null));
    }

    [Theory]
    [InlineData("ObjectLink.Create")][InlineData("Task.Read")][InlineData("FileCatalog.Read")]
    public async AsyncTask RevokedAccessClearsSelectionAndStopsCreation(string permission)
    {
        using var vm = Model(new()); vm.SetSource(Guid.NewGuid(), 7, "task"); await Select(vm);
        vm.UpdateAccess(Access.Where(p => p != permission));
        Assert.Null(vm.TargetPicker!.SelectedTarget); Assert.False(vm.AddCommand.CanExecute(null)); Assert.Empty(vm.TargetPicker.Results);
    }
    [Theory]
    [InlineData(true, false)][InlineData(false, true)]
    public async AsyncTask OfflineAndEndedSessionInvalidatePicker(bool session, bool online)
    {
        using var vm = Model(new()); vm.SetSource(Guid.NewGuid(), 7, "task"); await Select(vm);
        vm.UpdateAccess(Access, session, online); Assert.False(vm.TargetPicker!.CanQuery); Assert.False(vm.AddCommand.CanExecute(null));
    }
}
