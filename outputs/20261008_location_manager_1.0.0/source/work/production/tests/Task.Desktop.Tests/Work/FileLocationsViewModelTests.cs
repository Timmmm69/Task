using Task.Desktop.Administration;
using Task.Desktop.ViewModels;
using Task.Desktop.Work;

namespace Task.Desktop.Tests.Work;

public sealed class FileLocationsViewModelTests
{
    private static DesktopCatalogItem Item(Guid? id = null) => new(id ?? Guid.NewGuid(), 1, "Документ", "file_reference", null, null, "active");
    private static DesktopFileLocation Location(string? path = @"C:\Work\a.txt", bool primary = true) => new(Guid.NewGuid(), 1, "local_path", path, path is not null, primary);
    private static FileLocationsViewModel Model(Client client, Func<bool>? read = null, Func<bool>? write = null) => new(client, read ?? (() => true), write ?? (() => true));

    [Fact]
    public async System.Threading.Tasks.Task SelectionLoadsLocationsAndEmptyList()
    {
        var client = new Client(); var item = Item(); client.Rows[item.Id] = [Location(), Location(primary: false)];
        using var vm = Model(client); vm.SetItem(item); await vm.SelectionLoad;
        Assert.Equal(2, vm.Items.Count); Assert.Equal(item.Id, client.LastRead); Assert.False(vm.IsEmpty);
        vm.SetItem(Item()); await vm.SelectionLoad; Assert.Empty(vm.Items); Assert.True(vm.IsEmpty);
    }

    [Fact]
    public async System.Threading.Tasks.Task SelectionClearsOldRowsAndIgnoresLateResponse()
    {
        var client = new Client(); var first = Item(); var second = Item();
        var pending = new TaskCompletionSource<DesktopWorkResult<IReadOnlyList<DesktopFileLocation>>>(TaskCreationOptions.RunContinuationsAsynchronously);
        client.Read = (id, _) => id == first.Id ? pending.Task : System.Threading.Tasks.Task.FromResult<DesktopWorkResult<IReadOnlyList<DesktopFileLocation>>>(new DesktopWorkResult<IReadOnlyList<DesktopFileLocation>>.Succeeded([Location(@"C:\New.txt")], 3));
        using var vm = Model(client); vm.SetItem(first); var firstLoad = vm.SelectionLoad;
        Assert.True(vm.IsLoading); Assert.False(vm.AddCommand.CanExecute(null)); Assert.Empty(vm.Items);
        vm.SetItem(second); await vm.SelectionLoad;
        pending.SetResult(new DesktopWorkResult<IReadOnlyList<DesktopFileLocation>>.Succeeded([Location(@"C:\Old.txt")], 1));
        await firstLoad;
        Assert.Equal(@"C:\New.txt", Assert.Single(vm.Items).RawPath); Assert.False(vm.IsLoading);
        vm.SetItem(null); Assert.Empty(vm.Items); Assert.False(vm.IsLoading); Assert.False(vm.IsVisible);
    }

    [Fact]
    public async System.Threading.Tasks.Task AddLocalThenEditRefreshesAggregateVersionBeforeSecondMutation()
    {
        var client = new Client(); var item = Item(); using var vm = Model(client);
        var parentVersion = 0L; vm.ParentVersionChanged += (_, version) => parentVersion = version;
        vm.SetItem(item); await vm.SelectionLoad; await vm.AddCommand.ExecuteAsync();
        vm.RawPath = @"C:\Work\first.txt"; Assert.True(vm.SaveCommand.CanExecute(null)); await vm.SaveCommand.ExecuteAsync();
        Assert.Equal(2, parentVersion); Assert.Equal(@"C:\Work\first.txt", Assert.Single(vm.Items).RawPath);
        await vm.EditCommand.ExecuteAsync(vm.Items[0]); vm.RawPath = @"C:\Work\updated.txt"; vm.IsEnabled = false; vm.Priority = "5";
        await vm.SaveCommand.ExecuteAsync();
        Assert.Equal(new long[] { 1, 2 }, client.ExpectedVersions); Assert.Equal(3, parentVersion);
        Assert.Equal(@"C:\Work\updated.txt", Assert.Single(vm.Items).RawPath); Assert.False(vm.Items[0].IsEnabled); Assert.Equal(5, vm.Items[0].Priority);
    }

    [Fact]
    public async System.Threading.Tasks.Task UncRequiresSelectionOfExistingNetworkResource()
    {
        var client = new Client(); using var vm = Model(client); vm.SetItem(Item()); await vm.SelectionLoad;
        await vm.AddCommand.ExecuteAsync(); vm.LocationType = "unc_path"; vm.RawPath = @"\\server\docs\contract.txt";
        Assert.False(vm.SaveCommand.CanExecute(null));
        vm.NetworkResource = Assert.Single(vm.NetworkResources); await vm.SaveCommand.ExecuteAsync();
        Assert.Equal(client.Resource.Id, client.LastDraft!.NetworkResourceId); Assert.Equal("unc_path", client.LastDraft.LocationType);
        Assert.Equal(vm.RawPath, Assert.Single(vm.Items).RawPath);
    }

    [Fact]
    public async System.Threading.Tasks.Task MakePrimaryReloadsSiblingsAndDeletionOfLastLocationIsEmpty()
    {
        var client = new Client(); var item = Item(); client.Rows[item.Id] = [Location(), Location(primary: false)];
        using var vm = Model(client); vm.SetItem(item); await vm.SelectionLoad;
        var second = vm.Items[1]; await vm.MakePrimaryCommand.ExecuteAsync(second);
        Assert.Equal(second.Id, Assert.Single(vm.Items, i => i.IsPrimary).Id);
        Assert.Null(client.LastDraft!.RawPath); Assert.False(vm.MakePrimaryCommand.CanExecute(vm.Items.Single(i => i.IsPrimary)));
        while (vm.Items.Count > 0)
        {
            var row = vm.Items[0]; await vm.DeleteCommand.ExecuteAsync(row);
            Assert.True(vm.ShowDeleteConfirmation); Assert.Contains(row, vm.Items);
            await vm.ConfirmDeleteCommand.ExecuteAsync(); Assert.DoesNotContain(row, vm.Items);
        }
        Assert.True(vm.IsEmpty); Assert.Contains("Файл на диске не изменён", vm.Message);
    }

    [Fact]
    public async System.Threading.Tasks.Task NoReadPermissionDoesNotCallApiAndWritePermissionDisablesActions()
    {
        var client = new Client(); using var denied = Model(client, () => false); denied.SetItem(Item()); await denied.SelectionLoad;
        Assert.Null(client.LastRead); Assert.False(denied.RefreshCommand.CanExecute(null)); Assert.Empty(denied.Items);
        using var readOnly = Model(client, write: () => false); readOnly.SetItem(Item()); await readOnly.SelectionLoad;
        Assert.False(readOnly.AddCommand.CanExecute(null)); Assert.False(readOnly.SaveCommand.CanExecute(null));
    }

    [Theory]
    [InlineData("forbidden")]
    [InlineData("not_found")]
    [InlineData("session")]
    public async System.Threading.Tasks.Task ChangedAccessClearsSensitiveRows(string failure)
    {
        var client = new Client(); var item = Item(); client.Rows[item.Id] = [Location()]; using var vm = Model(client);
        vm.SetItem(item); await vm.SelectionLoad;
        client.Read = (_, _) => System.Threading.Tasks.Task.FromResult<DesktopWorkResult<IReadOnlyList<DesktopFileLocation>>>(failure switch
        {
            "forbidden" => new DesktopWorkResult<IReadOnlyList<DesktopFileLocation>>.Forbidden(),
            "not_found" => new DesktopWorkResult<IReadOnlyList<DesktopFileLocation>>.NotFound(),
            _ => new DesktopWorkResult<IReadOnlyList<DesktopFileLocation>>.AuthenticationFailure(),
        });
        await vm.RefreshCommand.ExecuteAsync(); Assert.Empty(vm.Items); Assert.False(vm.AddCommand.CanExecute(null)); Assert.NotEmpty(vm.Message);
    }

    [Fact]
    public async System.Threading.Tasks.Task RedactedPathIsNeverSentBackInPatch()
    {
        var client = new Client(); var item = Item(); client.Rows[item.Id] = [Location(null)]; using var vm = Model(client);
        vm.SetItem(item); await vm.SelectionLoad; Assert.True(vm.HasPartialAccess); Assert.Contains("скрыт", vm.Items[0].PathLabel);
        await vm.EditCommand.ExecuteAsync(vm.Items[0]); Assert.False(vm.PathEditable); Assert.Equal("", vm.RawPath);
        vm.Priority = "4"; await vm.SaveCommand.ExecuteAsync(); Assert.Null(client.LastDraft!.RawPath); Assert.Null(vm.Items[0].RawPath);
    }

    [Fact]
    public async System.Threading.Tasks.Task NetworkFailureKeepsConfirmedRowsAndDraftAndRequiresRefresh()
    {
        var client = new Client(); var item = Item(); client.Rows[item.Id] = [Location()]; using var vm = Model(client);
        vm.SetItem(item); await vm.SelectionLoad; var old = vm.Items[0]; await vm.AddCommand.ExecuteAsync(); vm.RawPath = @"C:\Draft.txt";
        client.MutationFailure = new DesktopWorkResult<bool>.ServerUnavailable(); await vm.SaveCommand.ExecuteAsync();
        Assert.Equal(old, Assert.Single(vm.Items)); Assert.True(vm.EditorVisible); Assert.Equal(@"C:\Draft.txt", vm.RawPath); Assert.False(vm.SaveCommand.CanExecute(null));
        client.MutationFailure = null; await vm.RefreshCommand.ExecuteAsync(); await vm.SaveCommand.ExecuteAsync(); Assert.Equal(2, vm.Items.Count);
        client.Read = (_, _) => System.Threading.Tasks.Task.FromResult<DesktopWorkResult<IReadOnlyList<DesktopFileLocation>>>(new DesktopWorkResult<IReadOnlyList<DesktopFileLocation>>.ServerUnavailable());
        await vm.RefreshCommand.ExecuteAsync(); Assert.Equal(2, vm.Items.Count); Assert.Contains("недоступен", vm.Message);
    }

    [Fact]
    public async System.Threading.Tasks.Task ConflictReloadsAndPreservesDraftWithoutAutomaticOverwrite()
    {
        var client = new Client(); var item = Item(); client.Rows[item.Id] = [Location()]; using var vm = Model(client);
        vm.SetItem(item); await vm.SelectionLoad; await vm.EditCommand.ExecuteAsync(vm.Items[0]); vm.RawPath = @"C:\Draft.txt";
        client.Version = 7; client.Rows[item.Id][0] = client.Rows[item.Id][0] with { RawPath = @"C:\Other.txt" };
        await vm.SaveCommand.ExecuteAsync();
        Assert.Equal(@"C:\Other.txt", Assert.Single(vm.Items).RawPath); Assert.Equal(@"C:\Draft.txt", vm.RawPath); Assert.Contains("другим пользователем", vm.Message);
        Assert.Equal(new long[] { 1 }, client.ExpectedVersions); await vm.SaveCommand.ExecuteAsync();
        Assert.Equal(new long[] { 1, 7 }, client.ExpectedVersions); Assert.Equal(@"C:\Draft.txt", vm.Items[0].RawPath);
    }

    [Fact]
    public async System.Threading.Tasks.Task MissingEtagUsesParentReadAndRevokedCapabilityClearsCachedPaths()
    {
        var client = new Client(); var item = Item(); client.Rows[item.Id] = [Location()];
        client.Read = (id, _) => System.Threading.Tasks.Task.FromResult<DesktopWorkResult<IReadOnlyList<DesktopFileLocation>>>(new DesktopWorkResult<IReadOnlyList<DesktopFileLocation>>.Succeeded(client.Rows[id]));
        var canRead = true; using var vm = Model(client, () => canRead); vm.SetItem(item); await vm.SelectionLoad;
        Assert.Equal(1, client.ParentReads); Assert.Single(vm.Items); canRead = false; vm.UpdateAccess(); Assert.Empty(vm.Items);
        canRead = true; vm.UpdateAccess(); await vm.SelectionLoad; Assert.Single(vm.Items);
    }

    [Fact]
    public async System.Threading.Tasks.Task AccessRevokedDuringResourceReadCanRecoverWithoutItemSwitch()
    {
        var client = new Client(); var item = Item(); var allowed = true; using var vm = Model(client, () => allowed);
        vm.SetItem(item); await vm.SelectionLoad;
        var pending = new TaskCompletionSource<DesktopWorkResult<IReadOnlyList<DesktopNetworkResource>>>(TaskCreationOptions.RunContinuationsAsynchronously);
        client.ReadResources = () => pending.Task;
        var add = vm.AddCommand.ExecuteAsync(); Assert.True(vm.ResourcesLoading);
        allowed = false; vm.UpdateAccess(); Assert.False(vm.ResourcesLoading); Assert.False(vm.EditorVisible);
        allowed = true; vm.UpdateAccess(); await vm.SelectionLoad;
        pending.SetResult(new DesktopWorkResult<IReadOnlyList<DesktopNetworkResource>>.Succeeded([client.Resource])); await add;
        Assert.Empty(vm.NetworkResources); Assert.True(vm.AddCommand.CanExecute(null));
        client.ReadResources = null; await vm.AddCommand.ExecuteAsync(); Assert.Single(vm.NetworkResources);
    }

    [Fact]
    public async System.Threading.Tasks.Task SameItemWithNewParentVersionAutomaticallyReloadsLocations()
    {
        var client = new Client(); var item = Item(); client.Rows[item.Id] = [Location()]; using var vm = Model(client);
        vm.SetItem(item); await vm.SelectionLoad;
        client.Version = 5; client.Rows[item.Id].Add(Location(primary: false));
        vm.SetItem(item with { Version = 5 }); await vm.SelectionLoad;
        Assert.Equal(2, vm.Items.Count); Assert.True(vm.AddCommand.CanExecute(null));
    }

    [Fact]
    public void AccessibilityNamesDoNotExposeDtoIdentifiersOrHiddenPaths()
    {
        var location = Location(null);
        Assert.DoesNotContain(location.Id.ToString(), location.ToString()); Assert.DoesNotContain("Version", location.ToString());
        Assert.Contains("скрыт", location.ToString());
        var resource = new DesktopNetworkResource(Guid.NewGuid(), 1, "Документы", null, null, "active"); Assert.Equal("Документы", resource.ToString());
        Assert.Equal("Сетевое расположение", new CatalogItemTypeChoice("unc_path", "Сетевое расположение").ToString());
    }

    private sealed class Client : IDesktopFileLocationsClient
    {
        public readonly Dictionary<Guid, List<DesktopFileLocation>> Rows = [];
        public readonly List<long> ExpectedVersions = [];
        public long Version = 1;
        public Guid? LastRead;
        public int ParentReads;
        public DesktopLocationDraft? LastDraft;
        public DesktopWorkResult<bool>? MutationFailure;
        public Func<Guid, CancellationToken, System.Threading.Tasks.Task<DesktopWorkResult<IReadOnlyList<DesktopFileLocation>>>>? Read;
        public Func<System.Threading.Tasks.Task<DesktopWorkResult<IReadOnlyList<DesktopNetworkResource>>>>? ReadResources;
        public DesktopNetworkResource Resource { get; } = new(Guid.NewGuid(), 1, "Документы", @"\\server\docs", null, "active");
        public System.Threading.Tasks.Task<DesktopWorkResult<IReadOnlyList<DesktopFileLocation>>> GetLocationsAsync(Guid id, CancellationToken ct = default)
        { LastRead = id; return Read?.Invoke(id, ct) ?? Ok<IReadOnlyList<DesktopFileLocation>>(Rows.GetValueOrDefault(id, []).ToArray(), Version); }
        public System.Threading.Tasks.Task<DesktopWorkResult<DesktopCatalogItem>> GetCatalogItemAsync(Guid id, CancellationToken ct = default)
        { ParentReads++; return Ok(Item(id) with { Version = Version }); }
        public System.Threading.Tasks.Task<DesktopWorkResult<IReadOnlyList<DesktopNetworkResource>>> GetNetworkResourcesAsync(CancellationToken ct = default) => ReadResources?.Invoke() ?? Ok<IReadOnlyList<DesktopNetworkResource>>([Resource]);
        public System.Threading.Tasks.Task<DesktopWorkResult<bool>> CreateLocationAsync(Guid id, long version, DesktopLocationDraft draft, CancellationToken ct = default) => Change(id, version, null, draft);
        public System.Threading.Tasks.Task<DesktopWorkResult<bool>> PatchLocationAsync(Guid id, long version, Guid locationId, DesktopLocationDraft draft, CancellationToken ct = default) => Change(id, version, locationId, draft);
        public System.Threading.Tasks.Task<DesktopWorkResult<bool>> DeleteLocationAsync(Guid id, long version, Guid locationId, CancellationToken ct = default) => Change(id, version, locationId, null);
        private System.Threading.Tasks.Task<DesktopWorkResult<bool>> Change(Guid id, long version, Guid? locationId, DesktopLocationDraft? draft)
        {
            ExpectedVersions.Add(version); LastDraft = draft;
            if (MutationFailure is not null) return System.Threading.Tasks.Task.FromResult(MutationFailure);
            if (version != Version) return System.Threading.Tasks.Task.FromResult<DesktopWorkResult<bool>>(new DesktopWorkResult<bool>.Conflict());
            if (!Rows.TryGetValue(id, out var rows)) Rows[id] = rows = [];
            if (draft is null) rows.RemoveAll(i => i.Id == locationId);
            else
            {
                if (draft.IsPrimary) for (var i = 0; i < rows.Count; i++) rows[i] = rows[i] with { IsPrimary = false };
                var old = rows.FirstOrDefault(i => i.Id == locationId);
                var updated = new DesktopFileLocation(locationId ?? Guid.NewGuid(), (old?.Version ?? 0) + 1, draft.LocationType, draft.RawPath ?? old?.RawPath, true, draft.IsPrimary, draft.IsEnabled, draft.Priority, draft.NetworkResourceId);
                if (old is null) rows.Add(updated); else rows[rows.IndexOf(old)] = updated;
            }
            return Ok(true, ++Version);
        }
        private static System.Threading.Tasks.Task<DesktopWorkResult<T>> Ok<T>(T value, long? version = null) => System.Threading.Tasks.Task.FromResult<DesktopWorkResult<T>>(new DesktopWorkResult<T>.Succeeded(value, version));
    }
}
