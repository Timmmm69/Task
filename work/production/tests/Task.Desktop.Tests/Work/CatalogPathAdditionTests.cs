using System.IO;
using System.Reflection;
using Task.Desktop.Administration;
using Task.Desktop.ViewModels;
using Task.Desktop.Work;

namespace Task.Desktop.Tests.Work;

public interface IPathTestClient : IDesktopWorkApiClient, IDesktopFileLocationsClient { }

public class PathClientProxy : DispatchProxy
{
    public readonly List<DesktopCatalogItem> Items = [];
    public readonly List<(Guid Item, long Version, DesktopLocationDraft Draft)> Locations = [];
    public IReadOnlyList<DesktopNetworkResource> Resources = [new(Guid.NewGuid(), 1, "Share", @"\\server\share", null, "active")];
    public DesktopWorkResult<DesktopCatalogItem>? CreateFailure;
    public DesktopWorkResult<bool>? LocationFailure;
    public int Creates, Active, Peak;
    public Guid? Parent;
    public static (IPathTestClient Client, PathClientProxy State) Make()
    {
        var client = Create<IPathTestClient, PathClientProxy>(); return (client, (PathClientProxy)client);
    }
    protected override object? Invoke(MethodInfo? method, object?[]? args)
    {
        args ??= [];
        switch (method!.Name)
        {
            case "GetCatalogAsync": return Ready<IReadOnlyList<DesktopCatalogItem>>(Items.ToArray());
            case "GetNetworkResourcesAsync": return Ready(Resources);
            case "GetCatalogItemAsync": return Ready(Items.Single(i => i.Id == (Guid)args[0]!));
            case "GetLocationsAsync":
                return Ready<IReadOnlyList<DesktopFileLocation>>(Locations.Where(l => l.Item == (Guid)args[0]!).Select(l =>
                    new DesktopFileLocation(Guid.NewGuid(), 1, l.Draft.LocationType, l.Draft.RawPath, true)).ToArray());
            case "CreateCatalogItemAsync":
                Creates++; Parent = (Guid?)args[3];
                if (CreateFailure is not null) return System.Threading.Tasks.Task.FromResult(CreateFailure);
                var item = new DesktopCatalogItem(Guid.NewGuid(), 1, (string)args[0]!, (string)args[1]!, null, null, "active", Parent);
                Items.Add(item); return Ready(item);
            case "CreateLocationAsync": return Save((Guid)args[0]!, (long)args[1]!, (DesktopLocationDraft)args[2]!);
            default: throw new InvalidOperationException("Unexpected call: " + method.Name);
        }
    }
    private async System.Threading.Tasks.Task<DesktopWorkResult<bool>> Save(Guid id, long version, DesktopLocationDraft draft)
    {
        Active++; Peak = Math.Max(Peak, Active);
        try { await System.Threading.Tasks.Task.Delay(1); Locations.Add((id, version, draft)); return LocationFailure ?? new DesktopWorkResult<bool>.Succeeded(true, version + 1); }
        finally { Active--; }
    }
    private static System.Threading.Tasks.Task<DesktopWorkResult<T>> Ready<T>(T value) =>
        System.Threading.Tasks.Task.FromResult<DesktopWorkResult<T>>(new DesktopWorkResult<T>.Succeeded(value));
}

public sealed class CatalogPathAdditionTests
{
    [Fact]
    public async System.Threading.Tasks.Task RevokedCatalogReadDuringMetadataProbeStopsWrite()
    {
        var (client, state) = PathClientProxy.Make();
        using var vm = new WorkHubViewModel(client, ["FileCatalog.Read", "FileCatalog.Create", "FileLocation.Update"]);
        vm.Activate(WorkHubArea.Catalog); await vm.RefreshCommand.ExecuteAsync();
        var service = new CatalogPathAddition(client, _ =>
        {
            vm.UpdateCapabilities(["FileCatalog.Create", "FileLocation.Update"]);
            return FileAttributes.Normal;
        });
        typeof(WorkHubViewModel).GetField("_pathAddition", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(vm, service);
        await vm.AddCatalogPathsAsync([@"C:\data\a.txt"], null);
        Assert.Equal(0, state.Creates); Assert.Empty(state.Locations);
    }

    [Fact]
    public async System.Threading.Tasks.Task RealFileAndFolderRemainUnchangedAndFolderChildrenAreNotImported()
    {
        var directory = Path.Combine(Path.GetTempPath(), "TaskDrop", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "a.txt"); await File.WriteAllTextAsync(path, "untouched");
            var original = await File.ReadAllBytesAsync(path);
            var (client, state) = PathClientProxy.Make();
            var service = new CatalogPathAddition(client);
            var result = await service.AddAsync([path, directory], null, () => true, []);
            Assert.All(result, r => Assert.True(r.Added)); Assert.Equal(2, state.Items.Count);
            Assert.Equal(original, await File.ReadAllBytesAsync(path)); Assert.Single(Directory.GetFiles(directory));
            Assert.Single(state.Items, i => i.ItemType == "folder_reference");
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async System.Threading.Tasks.Task ConnectionLostStopsRemainingCommandsAndWindowsDenialCreatesNothing()
    {
        var (client, state) = PathClientProxy.Make(); state.CreateFailure = new DesktopWorkResult<DesktopCatalogItem>.ServerUnavailable();
        var service = new CatalogPathAddition(client, _ => FileAttributes.Normal);
        Assert.Equal(3, (await service.AddAsync([@"C:\a.txt", @"C:\b.txt", @"C:\c.txt"], null, () => true, [])).Count);
        Assert.Equal(1, state.Creates);
        var denied = new CatalogPathAddition(client, _ => throw new UnauthorizedAccessException());
        Assert.False(Assert.Single(await denied.AddAsync([@"C:\denied.txt"], null, () => true, [])).Added);
        Assert.Equal(1, state.Creates);
    }

    [Theory]
    [InlineData(@"C:\data\file.txt", @"C:\data\file.txt")]
    [InlineData(@"\\server\share\file.txt", @"\\server\share\file.txt")]
    [InlineData(@"C:\data\folder\", @"C:\data\folder")]
    public void SupportedPathsNormalize(string input, string expected) => Assert.Equal(expected, CatalogPathAddition.Normalize(input));

    [Theory]
    [InlineData("https://example.org/a")]
    [InlineData("file:///C:/data/a.txt")]
    [InlineData("shell:Downloads")]
    [InlineData(@"\\user:password@server\share\a")]
    [InlineData(@"\\server\c$\a.txt")]
    [InlineData(@"C:\data\..\a.txt")]
    [InlineData(@"C:\a.txt:stream")]
    [InlineData(@"\\?\C:\a.txt")]
    [InlineData(@"C:\run.cmd")]
    [InlineData("relative.txt")]
    public void RejectsUnsupportedPaths(string path) => Assert.Null(CatalogPathAddition.Normalize(path));

    [Fact]
    public async System.Threading.Tasks.Task SingleLocalFileUsesExistingCommandsAndVersion()
    {
        var (client, state) = PathClientProxy.Make();
        var service = new CatalogPathAddition(client, _ => FileAttributes.Normal);
        var result = Assert.Single(await service.AddAsync([@"C:\data\a.txt"], null, () => true, []));
        Assert.True(result.Added); Assert.Equal("file_reference", Assert.Single(state.Items).ItemType);
        var location = Assert.Single(state.Locations); Assert.Equal(1, location.Version);
        Assert.Equal("local_path", location.Draft.LocationType); Assert.Null(location.Draft.NetworkResourceId);
    }

    [Fact]
    public async System.Threading.Tasks.Task FolderIsOneReferenceWithoutRecursiveEnumeration()
    {
        var (client, state) = PathClientProxy.Make(); var probes = 0;
        var service = new CatalogPathAddition(client, _ => { probes++; return FileAttributes.Directory; });
        var parent = Guid.NewGuid();
        Assert.True(Assert.Single(await service.AddAsync([@"C:\data\folder"], parent, () => true, [])).Added);
        Assert.Equal(1, probes); Assert.Equal("folder_reference", Assert.Single(state.Items).ItemType); Assert.Equal(parent, state.Parent);
    }

    [Theory]
    [InlineData(@"\\server\share\a.txt", true)]
    [InlineData(@"\\server\share2\a.txt", false)]
    public async System.Threading.Tasks.Task UncRequiresAllowlistedRoot(string path, bool accepted)
    {
        var (client, state) = PathClientProxy.Make(); var service = new CatalogPathAddition(client, _ => FileAttributes.Normal);
        Assert.Equal(accepted, Assert.Single(await service.AddAsync([path], null, () => true, [])).Added);
        Assert.Equal(accepted ? 1 : 0, state.Creates);
        if (accepted) { Assert.Equal("unc_path", state.Locations[0].Draft.LocationType); Assert.Equal(state.Resources[0].Id, state.Locations[0].Draft.NetworkResourceId); }
    }

    [Fact]
    public async System.Threading.Tasks.Task PartialFailureRetainsCreatedIdAndRepeatDoesNotCreateAgain()
    {
        var (client, state) = PathClientProxy.Make(); state.LocationFailure = new DesktopWorkResult<bool>.Forbidden();
        var service = new CatalogPathAddition(client, _ => FileAttributes.Normal);
        var first = Assert.Single(await service.AddAsync([@"C:\data\a.txt"], null, () => true, []));
        Assert.False(first.Added); Assert.NotNull(first.ItemId); Assert.Contains("Недостаточно прав", first.Message);
        var retry = Assert.Single(await service.AddAsync([@"c:\DATA\a.txt"], null, () => true, []));
        Assert.Equal(first.ItemId, retry.ItemId); Assert.Equal(1, state.Creates); Assert.Single(state.Locations);
    }

    [Fact]
    public async System.Threading.Tasks.Task MultiFileReportsPartialSuccessAndSkipsDuplicates()
    {
        var (client, state) = PathClientProxy.Make(); var service = new CatalogPathAddition(client, _ => FileAttributes.Normal);
        var paths = new[] { @"C:\data\a.txt", "https://example.org/a", @"C:\data\b.txt", @"C:\data\a.txt" };
        var results = await service.AddAsync(paths, null, () => true, []);
        Assert.Equal(4, results.Count); Assert.Equal(2, results.Count(r => r.Added)); Assert.Equal(2, state.Creates);
        Assert.DoesNotContain(await service.AddAsync(paths, null, () => true, state.Items), r => r.Added); Assert.Equal(2, state.Creates);
        var newService = new CatalogPathAddition(client, _ => FileAttributes.Normal);
        Assert.False(Assert.Single(await newService.AddAsync([paths[0]], null, () => true, state.Items)).Added); Assert.Equal(2, state.Creates);
    }

    [Fact]
    public async System.Threading.Tasks.Task UncertainCreateIsNeverReplayed()
    {
        var (client, state) = PathClientProxy.Make(); state.CreateFailure = new DesktopWorkResult<DesktopCatalogItem>.ServerUnavailable();
        var service = new CatalogPathAddition(client, _ => FileAttributes.Normal);
        await service.AddAsync([@"C:\data\a.txt"], null, () => true, []);
        await service.AddAsync([@"C:\data\a.txt"], null, () => true, []);
        Assert.Equal(1, state.Creates); Assert.Empty(state.Locations);
    }

    [Fact]
    public async System.Threading.Tasks.Task PermissionDeniedIsMappedAndDoesNotSendLocation()
    {
        var (client, state) = PathClientProxy.Make(); state.CreateFailure = new DesktopWorkResult<DesktopCatalogItem>.Forbidden();
        var result = Assert.Single(await new CatalogPathAddition(client, _ => FileAttributes.Normal).AddAsync([@"C:\data\a.txt"], null, () => true, []));
        Assert.Contains("Недостаточно прав", result.Message); Assert.Empty(state.Locations);
    }

    [Fact]
    public async System.Threading.Tasks.Task OfflineOrCanceledSelectionPerformsNoIoOrWrites()
    {
        var (client, state) = PathClientProxy.Make(); var probes = 0;
        var service = new CatalogPathAddition(client, _ => { probes++; return FileAttributes.Normal; });
        Assert.False(Assert.Single(await service.AddAsync([@"C:\data\a.txt"], null, () => false, [])).Added);
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.AddAsync([@"C:\data\a.txt"], null, () => true, [], cancellation.Token));
        Assert.Equal(0, probes); Assert.Equal(0, state.Creates);
    }

    [Fact]
    public async System.Threading.Tasks.Task LargeDropIsSequentialAndReturnsBeforeMetadataProbeCompletes()
    {
        var (client, state) = PathClientProxy.Make(); using var release = new ManualResetEventSlim();
        var entered = new System.Threading.Tasks.TaskCompletionSource<bool>(System.Threading.Tasks.TaskCreationOptions.RunContinuationsAsynchronously);
        var service = new CatalogPathAddition(client, _ => { entered.TrySetResult(true); release.Wait(TimeSpan.FromSeconds(5)); return FileAttributes.Normal; });
        var paths = Enumerable.Range(0, 300).Select(i => $@"C:\data\{i}.txt").ToArray();
        var pending = service.AddAsync(paths, null, () => true, []);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5)); Assert.False(pending.IsCompleted); release.Set();
        Assert.Equal(300, (await pending).Count(r => r.Added)); Assert.Equal(1, state.Peak);
        Assert.False(Assert.Single(await service.AddAsync(Enumerable.Repeat(paths[0], 1001).ToArray(), null, () => true, [])).Added);
    }

    [Fact]
    public async System.Threading.Tasks.Task InvalidTargetPermissionsAndOfflineDisableDropInViewModel()
    {
        var (client, state) = PathClientProxy.Make();
        var folder = new DesktopCatalogItem(Guid.NewGuid(), 1, "Folder", "virtual_folder", null, null, "active");
        var file = folder with { Id = Guid.NewGuid(), ItemType = "file_reference" }; state.Items.AddRange([folder, file]);
        using var vm = new WorkHubViewModel(client, ["FileCatalog.Read", "FileCatalog.Create", "FileLocation.Update"]);
        vm.Activate(WorkHubArea.Catalog); await vm.RefreshCommand.ExecuteAsync();
        Assert.True(vm.CanDropCatalogPaths(folder.Id)); Assert.False(vm.CanDropCatalogPaths(file.Id)); Assert.False(vm.CanDropCatalogPaths(Guid.NewGuid()));
        await vm.SelectCatalogItemAsync(folder.Id); Assert.Equal(folder.Id, vm.CurrentCatalogFolder);
        vm.UpdateConnectivity(false); Assert.False(vm.CanDropCatalogPaths(folder.Id));
        await vm.AddCatalogPathsAsync([@"C:\data\a.txt"], folder.Id); Assert.Equal(0, state.Creates);
        using var denied = new WorkHubViewModel(client, ["FileCatalog.Read", "FileCatalog.Create"]);
        denied.Activate(WorkHubArea.Catalog); await denied.RefreshCommand.ExecuteAsync(); Assert.False(denied.CanDropCatalogPaths(null));
    }
}
