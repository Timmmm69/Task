using Task.Desktop.ViewModels;
using Task.Desktop.Work;

namespace Task.Desktop.Tests.Work;

public sealed class WorkHubViewModelTests
{
    [Fact]
    public async System.Threading.Tasks.Task CatalogScenarioCreatesLocationAndOpensOnlyResolvedPath()
    {
        var client = new FakeClient(); var files = new RecordingFiles();
        using var vm = new WorkHubViewModel(client,
            ["FileCatalog.Read", "FileCatalog.Create", "FileLocation.Update", "FileReference.Open"], files);
        vm.Activate(WorkHubArea.Catalog);
        await Eventually(() => vm.Catalog.Count == 1);
        vm.NewItemName = "Contract";
        await vm.CreateCatalogItemCommand.ExecuteAsync();
        vm.NewItemPath = @"C:\Work\contract.docx";
        await vm.AddLocationCommand.ExecuteAsync();
        await vm.OpenFileCommand.ExecuteAsync();

        Assert.Equal(@"C:\Work\contract.docx", files.OpenedPath);
        Assert.Equal(2, vm.Catalog.Count);
        Assert.Equal("Расположение файла сохранено.", client.LocationMessage);
    }

    [Fact]
    public async System.Threading.Tasks.Task SearchResultRaisesTypedNavigationRequest()
    {
        var client = new FakeClient(); using var vm = new WorkHubViewModel(client, ["Search.Use"]);
        vm.Activate(WorkHubArea.Search); vm.SearchQuery = "alpha";
        await vm.SearchCommand.ExecuteAsync();
        var opened = (Type: "", Id: Guid.Empty);
        vm.OpenObjectRequested += (type, id) => opened = (type, id);
        await vm.OpenSearchResultCommand.ExecuteAsync(Assert.Single(vm.SearchResults));
        Assert.Equal(("task", FakeClient.TaskId), opened);
    }

    [Fact]
    public async System.Threading.Tasks.Task NotificationCommandsMaintainUnreadCount()
    {
        var client = new FakeClient(); using var vm = new WorkHubViewModel(client, ["Notification.ReadOwn"]);
        vm.Activate(WorkHubArea.Notifications);
        await Eventually(() => vm.Notifications.Count == 2);
        Assert.Equal(2, vm.UnreadCount);
        var opened = (Type: "", Id: Guid.Empty);
        vm.OpenObjectRequested += (type, id) => opened = (type, id);
        await vm.OpenNotificationSourceCommand.ExecuteAsync(vm.Notifications[0]);
        Assert.Equal(("task", FakeClient.TaskId), opened);
        await vm.MarkReadCommand.ExecuteAsync();
        Assert.Equal(1, vm.UnreadCount);
        await vm.MarkAllReadCommand.ExecuteAsync();
        Assert.Equal(0, vm.UnreadCount);
    }

    [Fact]
    public async System.Threading.Tasks.Task ArchiveAndTrashUseDedicatedServerLifecycleAndRestore()
    {
        var client = new FakeClient(); using var vm = new WorkHubViewModel(client,
            ["History.Read", "Archive.Restore", "Trash.Read", "Trash.Restore"]);
        vm.Activate(WorkHubArea.Archive);
        await Eventually(() => vm.LifecycleItems.Count == 1);
        Assert.Equal("Архив", vm.AreaTitle);
        await vm.RestoreLifecycleItemCommand.ExecuteAsync(vm.LifecycleItems[0]);
        Assert.Empty(vm.LifecycleItems);
        Assert.Equal(FakeClient.ArchivedId, client.RestoredArchiveId);

        vm.Activate(WorkHubArea.Trash);
        await Eventually(() => vm.LifecycleItems.Count == 1);
        await vm.RestoreLifecycleItemCommand.ExecuteAsync(vm.LifecycleItems[0]);
        Assert.Equal(FakeClient.TrashedId, client.RestoredTrashId);
    }

    [Fact]
    public async System.Threading.Tasks.Task SettingsLoadAndSaveOnlyThroughCapabilityGuardedApi()
    {
        var client = new FakeClient(); using var vm = new WorkHubViewModel(client,
            ["Settings.ReadOwn", "Settings.UpdateOwn", "Organization.Read", "Organization.Update"]);
        vm.Activate(WorkHubArea.Settings);
        await Eventually(() => vm.SaveUserSettingsCommand.CanExecute(null) && vm.SaveOrganizationSettingsCommand.CanExecute(null));
        Assert.Equal("ru-RU", vm.UserLanguage);
        vm.DefaultTaskDurationMinutes = 90;
        vm.DesktopNotificationsEnabled = false;
        vm.TrashRetentionDays = 45;

        await vm.SaveUserSettingsCommand.ExecuteAsync();
        await vm.SaveNotificationPreferencesCommand.ExecuteAsync();
        await vm.SaveOrganizationSettingsCommand.ExecuteAsync();

        Assert.Equal(90, client.SavedUserSettings?.DefaultTaskDurationMinutes);
        Assert.False(client.SavedNotificationPreferences?.DesktopEnabled);
        Assert.Equal(45, client.SavedOrganizationSettings?.TrashRetentionDays);
    }

    [Theory]
    [InlineData(@"C:\Temp\setup.exe")]
    [InlineData("relative.docx")]
    [InlineData("https://example.test/document")]
    public void FileAdapterRejectsExecutableAndNonFileLocations(string path)
    {
        var result = new WindowsFileAccessAdapter().Open(path);
        Assert.Equal(FileOpenStatus.Rejected, result.Status);
    }

    [Fact]
    public void CapabilityRefreshRevokesPresentationActionsImmediately()
    {
        using var vm = new WorkHubViewModel(new FakeClient(), ["FileCatalog.Read", "FileCatalog.Create"]);
        vm.Activate(WorkHubArea.Catalog); vm.NewItemName = "Draft";
        Assert.True(vm.CreateCatalogItemCommand.CanExecute(null));
        vm.UpdateCapabilities(["FileCatalog.Read"]);
        Assert.False(vm.CreateCatalogItemCommand.CanExecute(null));
    }

    private static async System.Threading.Tasks.Task Eventually(Func<bool> condition)
    {
        for (var i = 0; i < 50 && !condition(); i++) await System.Threading.Tasks.Task.Delay(10);
        Assert.True(condition());
    }

    private sealed class RecordingFiles : IFileAccessAdapter
    {
        public string? OpenedPath { get; private set; }
        public FileOpenResult Open(string path) { OpenedPath = path; return new(FileOpenStatus.Opened, "Открыто"); }
    }

    private sealed class FakeClient : IDesktopWorkApiClient
    {
        public static readonly Guid ItemId = Guid.NewGuid();
        public static readonly Guid TaskId = Guid.NewGuid();
        public static readonly Guid ArchivedId = Guid.NewGuid();
        public static readonly Guid TrashedId = Guid.NewGuid();
        public string? LocationMessage { get; private set; }
        public Guid? RestoredArchiveId { get; private set; }
        public Guid? RestoredTrashId { get; private set; }
        public DesktopUserSettings? SavedUserSettings { get; private set; }
        public DesktopNotificationPreferences? SavedNotificationPreferences { get; private set; }
        public DesktopOrganizationSettings? SavedOrganizationSettings { get; private set; }
        public System.Threading.Tasks.Task<DesktopWorkResult<IReadOnlyList<DesktopCatalogItem>>> GetCatalogAsync(CancellationToken cancellationToken = default) => Ok<IReadOnlyList<DesktopCatalogItem>>([new(ItemId, 1, "Plan", "file_reference", null, ".docx", "active")]);
        public System.Threading.Tasks.Task<DesktopWorkResult<IReadOnlyList<DesktopContact>>> GetContactsAsync(CancellationToken cancellationToken = default) => Ok<IReadOnlyList<DesktopContact>>([]);
        public System.Threading.Tasks.Task<DesktopWorkResult<IReadOnlyList<DesktopNotification>>> GetNotificationsAsync(CancellationToken cancellationToken = default) => Ok<IReadOnlyList<DesktopNotification>>([new(Guid.NewGuid(), 1, "task.assigned", "One", "Body", "info", "delivered", TaskId, DateTimeOffset.UtcNow), new(Guid.NewGuid(), 1, "system.info", "Two", "Body", "warning", "delivered", null, DateTimeOffset.UtcNow)]);
        public System.Threading.Tasks.Task<DesktopWorkResult<IReadOnlyList<DesktopSearchResult>>> SearchAsync(string query, CancellationToken cancellationToken = default) => Ok<IReadOnlyList<DesktopSearchResult>>([new(TaskId, "task", "Alpha", null, 1, DateTimeOffset.UtcNow)]);
        public System.Threading.Tasks.Task<DesktopWorkResult<DesktopCatalogItem>> CreateCatalogItemAsync(string name, string itemType, string? description, CancellationToken cancellationToken = default) => Ok(new DesktopCatalogItem(Guid.NewGuid(), 1, name, itemType, description, null, "active"));
        public System.Threading.Tasks.Task<DesktopWorkResult<DesktopContact>> CreateContactAsync(string firstName, string? lastName, string displayName, CancellationToken cancellationToken = default) => Ok(new DesktopContact(Guid.NewGuid(), 1, displayName, firstName, lastName, null, "active", "active"));
        public System.Threading.Tasks.Task<DesktopWorkResult<bool>> AddLocationAsync(Guid itemId, long version, string rawPath, CancellationToken cancellationToken = default) { LocationMessage = "Расположение файла сохранено."; return Ok(true); }
        public System.Threading.Tasks.Task<DesktopWorkResult<DesktopFileLocation?>> ResolveLocationAsync(Guid itemId, CancellationToken cancellationToken = default) => Ok<DesktopFileLocation?>(new(Guid.NewGuid(), 1, "local_path", @"C:\Work\contract.docx", true));
        public System.Threading.Tasks.Task<DesktopWorkResult<bool>> MarkNotificationReadAsync(Guid notificationId, CancellationToken cancellationToken = default) => Ok(true);
        public System.Threading.Tasks.Task<DesktopWorkResult<bool>> MarkAllNotificationsReadAsync(IReadOnlyCollection<Guid> notificationIds, CancellationToken cancellationToken = default) => Ok(true);
        public System.Threading.Tasks.Task<DesktopWorkResult<IReadOnlyList<DesktopLifecycleItem>>> GetArchiveAsync(CancellationToken cancellationToken = default) => Ok<IReadOnlyList<DesktopLifecycleItem>>([new(ArchivedId, "project", "Archive project", 3, "archived", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null, null)]);
        public System.Threading.Tasks.Task<DesktopWorkResult<IReadOnlyList<DesktopLifecycleItem>>> GetTrashAsync(CancellationToken cancellationToken = default) => Ok<IReadOnlyList<DesktopLifecycleItem>>([new(TrashedId, "task", "Deleted task", 4, "trashed", DateTimeOffset.UtcNow, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(30))]);
        public System.Threading.Tasks.Task<DesktopWorkResult<bool>> RestoreArchiveAsync(Guid objectId, long version, CancellationToken cancellationToken = default) { RestoredArchiveId = objectId; return Ok(true); }
        public System.Threading.Tasks.Task<DesktopWorkResult<bool>> RestoreTrashAsync(Guid objectId, long version, CancellationToken cancellationToken = default) { RestoredTrashId = objectId; return Ok(true); }
        public System.Threading.Tasks.Task<DesktopWorkResult<DesktopUserSettings>> GetUserSettingsAsync(CancellationToken cancellationToken = default) => Ok(UserSettings());
        public System.Threading.Tasks.Task<DesktopWorkResult<DesktopUserSettings>> UpdateUserSettingsAsync(DesktopUserSettings settings, CancellationToken cancellationToken = default) { SavedUserSettings = settings; return Ok(settings with { Version = settings.Version + 1 }); }
        public System.Threading.Tasks.Task<DesktopWorkResult<DesktopNotificationPreferences>> GetNotificationPreferencesAsync(CancellationToken cancellationToken = default) => Ok(new DesktopNotificationPreferences(1, true, true, true, 15, null, null, null));
        public System.Threading.Tasks.Task<DesktopWorkResult<DesktopNotificationPreferences>> UpdateNotificationPreferencesAsync(DesktopNotificationPreferences preferences, CancellationToken cancellationToken = default) { SavedNotificationPreferences = preferences; return Ok(preferences with { Version = preferences.Version + 1 }); }
        public System.Threading.Tasks.Task<DesktopWorkResult<DesktopOrganizationSettings>> GetOrganizationSettingsAsync(CancellationToken cancellationToken = default) => Ok(OrganizationSettings());
        public System.Threading.Tasks.Task<DesktopWorkResult<DesktopOrganizationSettings>> UpdateOrganizationSettingsAsync(DesktopOrganizationSettings settings, CancellationToken cancellationToken = default) { SavedOrganizationSettings = settings; return Ok(settings with { Version = settings.Version + 1 }); }
        private static DesktopUserSettings UserSettings() => new(1, "ru-RU", "24h", 1, "09:00:00", "18:00:00", [6, 7], 60, 15, true, true, true, "show_actions");
        private static DesktopOrganizationSettings OrganizationSettings() => new(1, 30, 1095, 90, 90, 20, "09:00:00", "18:00:00", 1, 1048576);
        private static System.Threading.Tasks.Task<DesktopWorkResult<T>> Ok<T>(T value) => System.Threading.Tasks.Task.FromResult<DesktopWorkResult<T>>(new DesktopWorkResult<T>.Succeeded(value));
    }
}
