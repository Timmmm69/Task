using Task.Desktop.ViewModels;
using Task.Desktop.Work;
using AsyncTask = System.Threading.Tasks.Task;
using PageResult = Task.Desktop.Work.DesktopWorkResult<System.Collections.Generic.IReadOnlyList<Task.Desktop.Work.DesktopSearchResult>>;

namespace Task.Desktop.Tests.Work;

public sealed class ObjectLinkTargetPickerTests
{
    internal static readonly string[] Capabilities = ["Search.Use", "ObjectLink.Create", "Task.Read", "Project.Read", "Project.Update", "Contact.Read", "Contact.Update", "FileCatalog.Read"];
    internal static DesktopSearchResult Hit(string type = "catalog_item", string title = "План") => new(Guid.NewGuid(), type, title, null, 1, DateTimeOffset.UtcNow);
    private static PageResult Page(params DesktopSearchResult[] hits) => new PageResult.Succeeded(hits);
    internal sealed class SearchClient : IDesktopObjectLinkTargetSearchClient
    {
        public int Calls;
        public Func<string, IReadOnlyCollection<string>, string?, CancellationToken, System.Threading.Tasks.Task<PageResult>> Search = (_, _, _, _) => AsyncTask.FromResult(Page());
        public System.Threading.Tasks.Task<PageResult> SearchTargetsAsync(string q, IReadOnlyCollection<string> types, string? cursor, CancellationToken ct = default)
        { Calls++; return Search(q, types, cursor, ct); }
    }
    internal static ObjectLinkTargetPickerViewModel Picker(SearchClient client, string sourceType = "task", string linkType = "task_file", Guid? source = null, TimeSpan? debounce = null)
    {
        var picker = new ObjectLinkTargetPickerViewModel(client, Capabilities, debounce ?? TimeSpan.Zero);
        picker.SetContext(source ?? Guid.NewGuid(), sourceType, linkType); return picker;
    }

    [Theory]
    [InlineData("task", "task_file", "catalog_item")]
    [InlineData("project", "project_file", "catalog_item")]
    [InlineData("contact", "contact_file", "catalog_item")]
    [InlineData("company", "contact_file", "catalog_item")]
    [InlineData("task", "task_contact", "contact,company")]
    public async AsyncTask SearchUsesNameAndServerTypes(string source, string link, string expected)
    {
        var hit = Hit(expected.Split(',')[0]);
        var client = new SearchClient { Search = (q, types, cursor, _) =>
        {
            Assert.Equal("План", q); Assert.Equal(expected.Split(','), types); Assert.Null(cursor);
            return AsyncTask.FromResult(Page(hit));
        } };
        using var picker = Picker(client, source, link);
        Assert.Equal(ObjectLinkTargetPickerState.Initial, picker.State);
        picker.SearchText = "  План  "; await picker.CurrentOperation;
        Assert.Same(hit, Assert.Single(picker.Results)); Assert.Equal(ObjectLinkTargetPickerState.Ready, picker.State);
        Assert.Null(picker.SelectedObjectId);
    }

    [Fact]
    public async AsyncTask SameNamesSelectRealIdsAndArbitraryObjectsCannotBeConfirmed()
    {
        var first = Hit(); var second = Hit();
        var client = new SearchClient { Search = (_, _, _, _) => AsyncTask.FromResult(Page(first, second)) };
        using var picker = Picker(client);
        picker.SearchText = "План"; await picker.CurrentOperation;
        picker.Candidate = second with { }; Assert.False(picker.ConfirmSelection());
        picker.Candidate = second; Assert.True(picker.ConfirmSelection()); Assert.Equal(second.ObjectId, picker.SelectedObjectId);
        Assert.Equal(ObjectLinkTargetPickerState.Selected, picker.State);
        picker.SearchText = "произвольная строка"; Assert.Null(picker.SelectedObjectId);
        picker.Candidate = first; Assert.True(picker.ConfirmSelection());
        picker.ClearSelection(); Assert.Null(picker.SelectedObjectId); Assert.Empty(picker.Results);
    }

    [Fact]
    public async AsyncTask IncompatibleContextAndSourceChangesClearSelection()
    {
        var file = Hit(); var source = Guid.NewGuid();
        using var picker = Picker(new() { Search = (_, _, _, _) => AsyncTask.FromResult(Page(file)) }, source: source);
        picker.SearchText = "План"; await picker.CurrentOperation; picker.Candidate = file; Assert.True(picker.ConfirmSelection());
        picker.SetContext(source, "task", "task_contact"); Assert.Null(picker.SelectedObjectId); Assert.Empty(picker.Results);
        picker.SetContext(source, "task", "task_file"); await picker.SearchAsync(); picker.Candidate = file; Assert.True(picker.ConfirmSelection());
        picker.SetContext(Guid.NewGuid(), "task", "task_file"); Assert.Null(picker.SelectedObjectId); Assert.Empty(picker.Results);
        picker.SetContext(source, "task", "related"); Assert.False(picker.CanQuery);
        picker.SetContext(source, "project", "task_file"); Assert.False(picker.CanQuery);
    }

    [Fact]
    public async AsyncTask SelfCannotBeSelectedAndFileLocationCannotBecomeCatalogItem()
    {
        var source = Guid.NewGuid(); var own = Hit() with { ObjectId = source };
        var client = new SearchClient { Search = (_, _, _, _) => AsyncTask.FromResult(Page(own)) };
        using var picker = Picker(client, source: source);
        picker.SearchText = "План"; await picker.CurrentOperation;
        Assert.Empty(picker.Results); picker.Candidate = own; Assert.False(picker.ConfirmSelection());
        client.Search = (_, _, _, _) => AsyncTask.FromResult(Page(Hit("file_location")));
        await picker.SearchAsync(); Assert.Equal(ObjectLinkTargetPickerState.RequestFailed, picker.State); Assert.Empty(picker.Results);
    }

    [Fact]
    public async AsyncTask PaginationDeduplicatesAndRepeatedCursorFailsClosed()
    {
        var first = Hit(); var second = Hit();
        var client = new SearchClient { Search = (_, _, cursor, _) => AsyncTask.FromResult<PageResult>(cursor switch
        {
            null => new PageResult.Succeeded([first]) { NextCursor = "a" },
            "a" => new PageResult.Succeeded([first, second]) { NextCursor = "b" },
            _ => new PageResult.Succeeded([Hit()]) { NextCursor = "a" },
        }) };
        using var picker = Picker(client); picker.SearchText = "План"; await picker.CurrentOperation;
        await picker.LoadMoreAsync(); Assert.Equal(new[] { first, second }, picker.Results);
        await picker.LoadMoreAsync(); Assert.False(picker.HasMore); Assert.Empty(picker.Results);
        Assert.Equal(ObjectLinkTargetPickerState.RequestFailed, picker.State);
        await picker.LoadMoreAsync(); Assert.Equal(3, client.Calls);
        await picker.RetryCommand.ExecuteAsync(); Assert.Single(picker.Results); Assert.True(picker.HasMore);
    }

    [Fact]
    public async AsyncTask EmptyPageCanContinueAndWholeSearchIsBounded()
    {
        var client = new SearchClient { Search = (_, _, _, _) => AsyncTask.FromResult<PageResult>(new PageResult.Succeeded([]) { NextCursor = "first" }) };
        using var picker = Picker(client); picker.SearchText = "План"; await picker.CurrentOperation;
        Assert.Equal(ObjectLinkTargetPickerState.Empty, picker.State); Assert.True(picker.HasMore);
        client.Search = (_, _, _, _) => AsyncTask.FromResult<PageResult>(new PageResult.Succeeded(Enumerable.Range(0, 50).Select(_ => Hit()).ToArray()) { NextCursor = Guid.NewGuid().ToString() });
        for (var i = 0; i < 25; i++) await picker.LoadMoreAsync();
        Assert.Equal(500, picker.Results.Count); Assert.False(picker.HasMore); Assert.True(picker.LimitReached); Assert.Equal(11, client.Calls);
        picker.ClearSelection();
        client.Search = (_, _, _, _) => AsyncTask.FromResult<PageResult>(new PageResult.Succeeded([]) { NextCursor = Guid.NewGuid().ToString() });
        await picker.SearchAsync(); for (var i = 0; i < 25; i++) await picker.LoadMoreAsync();
        Assert.False(picker.HasMore); Assert.True(picker.LimitReached); Assert.Equal(31, client.Calls);
    }

    [Fact]
    public async AsyncTask ChangedInputCancelsAndLateResponseCannotReplaceNewResults()
    {
        var pending = new System.Threading.Tasks.TaskCompletionSource<PageResult>(); CancellationToken oldToken = default;
        var current = Hit();
        var client = new SearchClient { Search = (q, _, _, ct) =>
        { if (q == "старый") { oldToken = ct; return pending.Task; } return AsyncTask.FromResult(Page(current)); } };
        using var picker = Picker(client);
        picker.SearchText = "старый"; var old = picker.CurrentOperation;
        picker.SearchText = "новый"; await picker.CurrentOperation; Assert.True(oldToken.IsCancellationRequested);
        pending.SetResult(Page(Hit())); await old; Assert.Same(current, Assert.Single(picker.Results));
    }

    [Fact]
    public async AsyncTask LengthAndDebounceBoundRequests()
    {
        var client = new SearchClient(); using var picker = Picker(client, debounce: TimeSpan.FromMilliseconds(40));
        picker.SearchText = "П"; await picker.CurrentOperation; Assert.Equal(0, client.Calls);
        picker.SearchText = new string('a', 201); await picker.CurrentOperation; Assert.Equal(0, client.Calls);
        picker.SearchText = "Пл"; var first = picker.CurrentOperation;
        picker.SearchText = "Пла"; var second = picker.CurrentOperation;
        picker.SearchText = "План"; await AsyncTask.WhenAll(first, second, picker.CurrentOperation); Assert.Equal(1, client.Calls);
    }

    [Theory]
    [InlineData("Search.Use")]
    [InlineData("ObjectLink.Create")]
    [InlineData("FileCatalog.Read")]
    [InlineData("Task.Read")]
    public async AsyncTask MissingPermissionsNeverQuery(string missing)
    {
        var client = new SearchClient(); using var picker = Picker(client);
        picker.UpdateAccess(Capabilities.Where(p => p != missing).Append("ObjectLink.Read"));
        picker.SearchText = "План"; await picker.CurrentOperation; await picker.SearchAsync();
        Assert.Equal(0, client.Calls); Assert.Equal(ObjectLinkTargetPickerState.Forbidden, picker.State);
    }

    [Fact]
    public async AsyncTask ScopeChangeEvenWithSameCapabilitiesAndLogoutClearAllData()
    {
        var hit = Hit(); var client = new SearchClient { Search = (_, _, _, _) => AsyncTask.FromResult(Page(hit)) };
        using var picker = Picker(client); picker.SearchText = "План"; await picker.CurrentOperation;
        picker.Candidate = hit; Assert.True(picker.ConfirmSelection());
        var late = new System.Threading.Tasks.TaskCompletionSource<PageResult>(); client.Search = (_, _, _, _) => late.Task;
        var search = picker.SearchAsync(); picker.UpdateAccess(Capabilities);
        late.SetResult(Page(hit)); await search; Assert.Empty(picker.Results); Assert.Null(picker.SelectedObjectId);
        picker.UpdateAccess(Capabilities, online: false); Assert.Equal(ObjectLinkTargetPickerState.Offline, picker.State); Assert.False(picker.CanQuery);
        picker.UpdateAccess(Capabilities, sessionReady: false); Assert.Equal(ObjectLinkTargetPickerState.SessionExpired, picker.State); Assert.False(picker.CanQuery);
    }

    [Theory]
    [InlineData("forbidden", ObjectLinkTargetPickerState.Forbidden)]
    [InlineData("auth", ObjectLinkTargetPickerState.SessionExpired)]
    [InlineData("unavailable", ObjectLinkTargetPickerState.ServerUnavailable)]
    [InlineData("malformed", ObjectLinkTargetPickerState.RequestFailed)]
    [InlineData("expiredCursor", ObjectLinkTargetPickerState.RequestFailed)]
    [InlineData("throw", ObjectLinkTargetPickerState.RequestFailed)]
    public async AsyncTask FailuresClearPreviousPagesAndRetryRestarts(string kind, ObjectLinkTargetPickerState state)
    {
        var hit = Hit(); var client = new SearchClient { Search = (_, _, _, _) => AsyncTask.FromResult<PageResult>(new PageResult.Succeeded([hit]) { NextCursor = "a" }) };
        using var picker = Picker(client); picker.SearchText = "План"; await picker.CurrentOperation;
        picker.Candidate = hit; Assert.True(picker.ConfirmSelection());
        client.Search = (_, _, _, _) => kind == "throw" ? throw new InvalidOperationException("SENSITIVE") : AsyncTask.FromResult<PageResult>(kind switch
        {
            "forbidden" => new PageResult.Forbidden(), "auth" => new PageResult.AuthenticationFailure(),
            "unavailable" => new PageResult.ServerUnavailable(), "expiredCursor" => new PageResult.Conflict(), _ => new PageResult.MalformedResponse(),
        });
        await picker.LoadMoreAsync(); Assert.Equal(state, picker.State); Assert.Empty(picker.Results); Assert.Null(picker.SelectedObjectId); Assert.False(picker.HasMore);
        Assert.DoesNotContain("SENSITIVE", picker.StatusText);
        client.Search = (_, _, cursor, _) => { Assert.Null(cursor); return AsyncTask.FromResult(Page(hit)); };
        if (kind == "auth") { Assert.False(picker.RetryCommand.CanExecute(null)); picker.UpdateAccess(Capabilities); }
        await picker.RetryCommand.ExecuteAsync(); Assert.Equal(ObjectLinkTargetPickerState.Ready, picker.State);
    }

    [Theory]
    [InlineData("null")][InlineData("nullHit")][InlineData("emptyId")][InlineData("emptyTitle")]
    [InlineData("wrongType")][InlineData("version")][InlineData("timestamp")][InlineData("tooMany")][InlineData("cursor")]
    public async AsyncTask MalformedTypedPageNeverExposesPartialResults(string kind)
    {
        var hit = Hit();
        IReadOnlyList<DesktopSearchResult> rows = kind switch
        {
            "null" => null!, "nullHit" => [hit, null!], "emptyId" => [hit with { ObjectId = Guid.Empty }],
            "emptyTitle" => [hit with { Title = " " }], "wrongType" => [hit, Hit("task")],
            "version" => [hit with { Version = 0 }], "timestamp" => [hit with { UpdatedAt = default }],
            "tooMany" => Enumerable.Repeat(hit, 51).ToArray(), _ => [hit],
        };
        using var picker = Picker(new() { Search = (_, _, _, _) => AsyncTask.FromResult<PageResult>(new PageResult.Succeeded(rows) { NextCursor = kind == "cursor" ? " " : null }) });
        picker.SearchText = "План"; await picker.CurrentOperation;
        Assert.Equal(ObjectLinkTargetPickerState.RequestFailed, picker.State); Assert.Empty(picker.Results);
    }

    [Fact]
    public async AsyncTask CancelDisposeAndExternalCancellationInvalidateNonCooperativeRequests()
    {
        var pending = new System.Threading.Tasks.TaskCompletionSource<PageResult>();
        using var picker = Picker(new() { Search = (_, _, _, _) => pending.Task });
        picker.SearchText = "План"; var operation = picker.CurrentOperation; picker.Cancel();
        pending.SetResult(Page(Hit())); await operation; Assert.Empty(picker.Results);
        using var cancel = new CancellationTokenSource(); pending = new(); operation = picker.SearchAsync(cancel.Token); cancel.Cancel();
        pending.SetResult(Page(Hit())); await operation; Assert.False(picker.IsLoading); Assert.Empty(picker.Results);
        pending = new(); operation = picker.SearchAsync(); picker.Dispose();
        pending.SetResult(Page(Hit())); await operation; Assert.False(picker.CanQuery); Assert.Empty(picker.Results);
    }
}
