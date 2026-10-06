using System.IO;
using Microsoft.Data.Sqlite;
using Task.Desktop.Calendar;
using Task.Desktop.Modes;
using Task.Desktop.Personal;
using Task.Desktop.Projects;
using Task.Desktop.TaskApi;
using Task.Desktop.ViewModels;
using Task.Desktop.Work;
using Task.Domain.Reminders;

namespace Task.Desktop.Tests.Personal;

public sealed class PersonalWorkspaceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "Task-workspace-tests", Guid.NewGuid().ToString("N"));
    private PersonalDataPaths Paths => new(_root);
    private PersonalTaskStore Open() => new(Paths);
    private static long Version(PersonalTaskStore s, Guid id) => s.WorkspaceObjects().Single(o => o.Id == id).Version;
    internal const string RemoveWorkspaceSchema = """
        DROP VIEW personal_objects; DROP INDEX personal_tasks_active; DROP INDEX personal_projects_active;
        DROP TABLE object_links; DROP TABLE personal_file_locations; DROP TABLE personal_catalog;
        DROP TABLE personal_contacts; DROP TABLE personal_lifecycle; DROP TABLE personal_workspace_settings;
        """;
    private void Sql(string sql)
    {
        using var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Paths.DatabasePath, Pooling = false }.ToString());
        db.Open(); using var c = db.CreateCommand(); c.CommandText = sql; c.ExecuteNonQuery();
    }
    [Fact]
    public void CatalogFoldersLocationsAndContacts_PersistWithoutCopyingFileBytes()
    {
        Directory.CreateDirectory(_root); var file = Path.Combine(_root, "document.txt"); var content = "BYTES_ONLY_NEVER_COPIED_7359"; File.WriteAllText(file, content);
        Guid id;
        using (var s = Open())
        {
            var folder = s.CreateCatalog("Folder", "virtual_folder", null);
            var item = s.CreateCatalog("Document", "file_reference", "Description", folder.Id); id = item.Id;
            var directory = s.CreateCatalog("Share", "folder_reference", null);
            s.AddPersonalLocation(item.Id, 1, file); s.AddPersonalLocation(directory.Id, 1, @"\\server\share\folder");
            Assert.Throws<PersonalVersionConflictException>(() => s.AddPersonalLocation(item.Id, 1, file));
            Assert.Throws<ArgumentException>(() => s.CreateCatalog("Web", "web_link", null));
            Assert.Throws<PersonalTransitionException>(() => s.AddPersonalLocation(folder.Id, 1, file));
            var contact = s.CreateContact("Мария", "Иванова", "Мария Иванова");
            Assert.Equal(contact.Id, Assert.Single(s.SearchPersonal("иванова")).ObjectId);
            Assert.Throws<ArgumentException>(() => s.MoveCatalog(folder.Id, 1, folder.Id));
        }
        using (var reopened = Open())
        {
            Assert.Equal(file, reopened.PersonalLocation(id)!.RawPath);
            Assert.Equal(3, reopened.Catalog().Count); Assert.Single(reopened.Contacts());
            Assert.Equal(2, Version(reopened, id));
        }
        Assert.Equal(content, File.ReadAllText(file));
        Assert.DoesNotContain(content, System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(Paths.DatabasePath)));
    }
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void LegacyTrashedProjectNeverInventsItsPreviousState(bool completed)
    {
        Guid id;
        using (var s = Open())
        {
            var project = s.SaveProject(new(Guid.Empty, 0, "Legacy", null, completed ? DesktopProjectStatus.Completed : DesktopProjectStatus.Active, null, null, ActualEndAt: completed ? DateTimeOffset.UtcNow : null));
            id = project.Id; s.ChangeWorkspaceLifecycle(id, 1, "trash");
        }
        Sql(RemoveWorkspaceSchema + "PRAGMA user_version=3;");
        using (var s = Open())
        {
            Assert.True(s.NeedsLegacyProjectRecovery(id)); Assert.Throws<ArgumentException>(() => s.ChangeWorkspaceLifecycle(id, 2, "restore"));
            Assert.Equal(2, Version(s, id));
            if (!completed) Assert.Throws<ArgumentException>(() => s.ChangeWorkspaceLifecycle(id, 2, "restore", "archived"));
            s.ChangeWorkspaceLifecycle(id, 2, "restore", completed ? "archived" : "active");
        }
        using var reopened = Open(); Assert.Equal(completed ? "archived" : "active", reopened.WorkspaceObjects().Single().Lifecycle); Assert.False(reopened.NeedsLegacyProjectRecovery(id));
    }
    [Theory]
    [InlineData("task", "task_file")]
    [InlineData("project", "project_file")]
    [InlineData("contact", "contact_file")]
    [InlineData("task", "task_contact")]
    public void ApplicableLinks_CreateRemoveRestart(string sourceType, string type)
    {
        Guid source; Guid target; Guid link;
        using (var s = Open())
        {
            source = sourceType switch
            {
                "task" => s.Create(new("Task", DesktopTaskPriority.Normal)).Id,
                "project" => s.SaveProject(new(Guid.Empty, 0, "Project", null, DesktopProjectStatus.Active, null, null)).Id,
                _ => s.CreateContact("Contact", null, "Contact").Id,
            };
            target = type == "task_contact" ? s.CreateContact("Person", null, "Person").Id : s.CreateCatalog("File", "file_reference", null).Id;
            s.AddPersonalLink(source, 1, target, type); link = Assert.Single(s.PersonalLinks(source).Items).Id;
            Assert.Single(s.PersonalLinks(target).Items); Assert.Equal(2, Version(s, source));
            Assert.Throws<PersonalVersionConflictException>(() => s.AddPersonalLink(source, 1, target, type));
            Assert.Throws<ArgumentException>(() => s.AddPersonalLink(target, 1, source, type));
        }
        using (var s = Open())
        {
            Assert.Equal(link, Assert.Single(s.PersonalLinks(source).Items).Id);
            Assert.Throws<PersonalTaskNotFoundException>(() => s.RemovePersonalLink(target, 1, link));
            s.RemovePersonalLink(source, 2, link);
        }
        using var reopened = Open(); Assert.Empty(reopened.PersonalLinks(source).Items); Assert.Equal(3, Version(reopened, source));
    }
    [Fact]
    public void TrashTaskWithLinkedFile_RestoresPreviousArchiveStateAndPreservesFile()
    {
        Directory.CreateDirectory(_root); var file = Path.Combine(_root, "kept.txt"); File.WriteAllText(file, "kept"); Guid taskId; Guid catalogId;
        using (var s = Open())
        {
            var task = s.Create(new("Archived task", DesktopTaskPriority.Normal)); taskId = task.Id;
            var catalog = s.CreateCatalog("File", "file_reference", null); catalogId = catalog.Id;
            s.AddPersonalLocation(catalog.Id, 1, file); s.AddPersonalLink(task.Id, 1, catalog.Id, "task_file");
            s.ChangeWorkspaceLifecycle(task.Id, 2, "archive"); Assert.Empty(s.List());
            s.ChangeWorkspaceLifecycle(task.Id, 3, "trash"); Assert.Empty(s.PersonalLinks(catalog.Id).Items);
            Assert.Single(s.Catalog()); Assert.True(File.Exists(file));
        }
        using (var s = Open())
        {
            s.ChangeWorkspaceLifecycle(taskId, 4, "restore"); Assert.Empty(s.List()); Assert.Single(s.WorkspaceLifecycle("archived")); Assert.Single(s.PersonalLinks(taskId).Items);
            s.ChangeWorkspaceLifecycle(taskId, 5, "unarchive"); Assert.Single(s.List());
            s.ChangeWorkspaceLifecycle(catalogId, 2, "trash"); Assert.True(File.Exists(file)); Assert.Null(s.PersonalLocation(catalogId));
            s.PurgeWorkspaceObject(catalogId, 3); Assert.True(File.Exists(file)); Assert.Empty(s.PersonalLinks(taskId).Items);
            s.ChangeWorkspaceLifecycle(taskId, 6, "trash"); s.PurgeWorkspaceObject(taskId, 7); Assert.Empty(s.WorkspaceObjects());
        }
        using var reopened = Open(); Assert.Empty(reopened.Catalog()); Assert.True(File.Exists(file));
    }
    [Fact]
    public void ArchivedProjectKeepsActiveAssignedTask_TrashContactHidesAndRestoresRelation()
    {
        using var s = Open(); var project = s.SaveProject(new(Guid.Empty, 0, "Project", null, DesktopProjectStatus.Active, null, null));
        var task = s.Create(new("Assigned task", DesktopTaskPriority.Normal, card: new() { ProjectId = project.Id }));
        Assert.Throws<ArgumentException>(() => s.ChangeWorkspaceLifecycle(project.Id, 1, "archive"));
        var completed = s.SaveProject(project with { Status = DesktopProjectStatus.Completed, ActualEndAt = DateTimeOffset.UtcNow });
        s.ChangeWorkspaceLifecycle(project.Id, completed.Version, "archive"); Assert.Empty(s.Projects()); Assert.Equal(project.Id, Assert.Single(s.List()).Card!.ProjectId);
        var edited = s.Patch(new(task.Id, task.Version, title: DesktopTaskField<string>.From("Still editable"), priority: DesktopTaskField<DesktopTaskPriority>.From(DesktopTaskPriority.High))); Assert.Equal(2, edited.Version);
        var file = s.CreateCatalog("File", "file_reference", null);
        Assert.Throws<PersonalTransitionException>(() => s.AddPersonalLink(project.Id, 3, file.Id, "project_file"));
        var contact = s.CreateContact("Person", null, "Person"); s.AddPersonalLink(task.Id, 2, contact.Id, "task_contact");
        s.ChangeWorkspaceLifecycle(contact.Id, 1, "trash"); Assert.Empty(s.Contacts()); Assert.Empty(s.PersonalLinks(task.Id).Items); Assert.Single(s.List());
        s.ChangeWorkspaceLifecycle(contact.Id, 2, "restore"); Assert.Single(s.PersonalLinks(task.Id).Items);
        s.ChangeWorkspaceLifecycle(contact.Id, 3, "trash"); s.PurgeWorkspaceObject(contact.Id, 4); Assert.Empty(s.PersonalLinks(task.Id).Items);
        s.ChangeWorkspaceLifecycle(project.Id, 3, "trash"); Assert.Throws<ArgumentException>(() => s.PurgeWorkspaceObject(project.Id, 4));
        Assert.Equal("trashed", s.WorkspaceObjects().Single(o => o.Id == project.Id).Lifecycle);
    }
    [Fact]
    public void SearchIsLiteralUnicodeActiveOnlyAcrossAllFourTypes()
    {
        using var s = Open(); s.Create(new("PERSONAL_ONLY_MARKER ЗАДАЧА", DesktopTaskPriority.Normal));
        s.SaveProject(new(Guid.Empty, 0, "PERSONAL_ONLY_MARKER Проект", null, DesktopProjectStatus.Planning, null, null));
        s.CreateContact("Имя", null, "PERSONAL_ONLY_MARKER Контакт"); var file = s.CreateCatalog("PERSONAL_ONLY_MARKER Файл", "file_reference", null);
        Assert.Equal(new[] { "catalog_item", "contact", "project", "task" }, s.SearchPersonal("personal_only_marker").Select(r => r.ObjectType).Order().ToArray());
        Assert.Single(s.SearchPersonal("задача")); Assert.Empty(s.SearchPersonal("CORPORATE_ONLY_MARKER")); Assert.Empty(s.SearchPersonal("%' OR 1=1 --"));
        s.ChangeWorkspaceLifecycle(file.Id, 1, "archive"); Assert.Equal(3, s.SearchPersonal("PERSONAL_ONLY_MARKER").Count);
    }
    [Fact]
    public async System.Threading.Tasks.Task PersonalContextOfflineWorkflow_ReopensAndNeverTouchesCorporateSettings()
    {
        Directory.CreateDirectory(_root); var file = Path.Combine(_root, "offline.txt"); File.WriteAllText(file, "offline");
        var corporatePath = Path.Combine(_root, "server-settings.json"); const string corporate = "{\"serverEndpoint\":\"https://127.0.0.1:1/\"}"; File.WriteAllText(corporatePath, corporate);
        Guid catalogId;
        using (var model = new PersonalApplicationModel(_root))
        {
            var workspace = model.Workspace!; var hub = workspace.Hub; hub.Activate(WorkHubArea.Catalog);
            hub.NewItemName = "Offline folder"; hub.NewItemType = "virtual_folder"; await hub.CreateCatalogItemCommand.ExecuteAsync(); Assert.Single(hub.Catalog);
            hub.NewItemName = "Offline file"; hub.NewItemType = "file_reference"; await hub.CreateCatalogItemCommand.ExecuteAsync(); catalogId = hub.SelectedCatalogItem!.Id;
            hub.NewItemPath = file; await hub.AddLocationCommand.ExecuteAsync(); Assert.Equal(2, hub.SelectedCatalogItem!.Version);
            model.Tasks!.CaptureText = "Offline task"; await model.Tasks.CaptureCommand.ExecuteAsync();
            hub.Activate(WorkHubArea.Contacts); hub.NewContactFirstName = "Offline"; hub.NewContactDisplayName = "Offline person"; await hub.CreateContactCommand.ExecuteAsync();
            workspace.Refresh(); workspace.Selected = workspace.Objects.Single(o => o.ObjectType == "task"); workspace.Target = workspace.ActiveObjects.Single(o => o.Id == catalogId);
            await workspace.Links.AddCommand.ExecuteAsync(); Assert.Single(workspace.Links.Items);
            await workspace.ArchiveCommand.ExecuteAsync(); Assert.Equal("archived", workspace.Selected!.Lifecycle);
            await workspace.RestoreCommand.ExecuteAsync(); Assert.Equal("active", workspace.Selected!.Lifecycle);
            hub.Activate(WorkHubArea.Search); hub.SearchQuery = "Offline"; await hub.SearchCommand.ExecuteAsync(); Assert.Equal(4, hub.SearchResults.Count);
            workspace.Selected = workspace.Objects.Single(o => o.Id == catalogId); await workspace.TrashCommand.ExecuteAsync(); Assert.True(File.Exists(file));
            hub.Activate(WorkHubArea.Settings); hub.AllowLocalPaths = false; await hub.SaveUserSettingsCommand.ExecuteAsync();
            Assert.DoesNotContain("Подключение и сервер", hub.SettingsSections); Assert.False(hub.CanReadOrganization);
        }
        using (var model = new PersonalApplicationModel(_root))
        {
            var workspace = model.Workspace!; workspace.Selected = workspace.Objects.Single(o => o.Id == catalogId);
            Assert.Equal("trashed", workspace.Selected.Lifecycle); await workspace.RestoreCommand.ExecuteAsync();
            workspace.Hub.Activate(WorkHubArea.Settings); Assert.False(workspace.Hub.AllowLocalPaths);
            Assert.True(File.Exists(file)); Assert.Equal(corporate, File.ReadAllText(corporatePath)); Assert.False(File.Exists(Path.Combine(_root, "credentials.bin")));
        }
    }
    [Fact]
    public async System.Threading.Tasks.Task SettingsPersistAndEnforceLocalFilePolicyAndNotificationSuppression()
    {
        Guid id;
        using (var s = Open())
        {
            var item = s.CreateCatalog("Local", "file_reference", null); id = item.Id; s.AddPersonalLocation(id, 1, @"C:\Work\file.txt");
            var settings = s.WorkspaceSettings(); s.SaveWorkspaceSettings(settings with { AllowLocalPaths = false, FirstDayOfWeek = 7, WorkdayStart = "10:00:00" });
            Assert.Throws<PersonalVersionConflictException>(() => s.SaveWorkspaceSettings(settings));
            s.SaveWorkspaceNotifications(s.WorkspaceNotificationPreferences() with { DesktopEnabled = false, SoundEnabled = false });
            var task = s.Create(new("Notify", DesktopTaskPriority.Normal)); s.AddReminder(task.Id, DesktopScheduleItemType.Task, ReminderTriggerType.Absolute, absolute: DateTimeOffset.UtcNow.AddSeconds(-1));
            s.ReconcileReminders(); Assert.Empty(s.ClaimPresentations()); Assert.Equal("suppressed", Assert.Single(s.Notifications()).PresentationState);
        }
        using var reopened = Open(); Assert.False(reopened.WorkspaceSettings().AllowLocalPaths); Assert.False(reopened.WorkspaceNotificationPreferences().SoundEnabled);
        var location = Assert.IsType<DesktopWorkResult<DesktopFileLocation?>.Succeeded>(await new PersonalWorkClient(reopened).ResolveLocationAsync(id)).Value; Assert.False(location!.CanOpenOnDevice);
        Assert.Equal(new DateOnly(2026, 10, 4), CalendarViewModel.StartOfWeek(new(2026, 10, 6), DayOfWeek.Sunday));
        Assert.Empty(reopened.ClaimPresentations()); Assert.Single(reopened.Notifications());
    }
    [Fact]
    public void MigrationFromStage3AndFailure_AreAdditiveAndRollback()
    {
        Guid id; using (var s = Open()) id = s.Create(new("Retained", DesktopTaskPriority.Normal)).Id;
        Sql(RemoveWorkspaceSchema + "PRAGMA user_version=3;");
        using (var upgraded = Open()) Assert.Equal(id, Assert.Single(upgraded.List()).Id);
        Sql(RemoveWorkspaceSchema + "PRAGMA user_version=3; CREATE TABLE personal_contacts(id TEXT);");
        Assert.Throws<InvalidDataException>(() => Open());
        using var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Paths.DatabasePath, Pooling = false }.ToString()); db.Open();
        using var c = db.CreateCommand(); c.CommandText = "PRAGMA user_version;"; Assert.Equal(3L, c.ExecuteScalar());
        c.CommandText = "SELECT COUNT(*) FROM tasks;"; Assert.Equal(1L, c.ExecuteScalar()); c.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE name='personal_catalog';"; Assert.Equal(0L, c.ExecuteScalar());
    }
    [Theory]
    [InlineData(@"C:\Work\run.exe")]
    [InlineData(@"C:\Work\run.msc")]
    [InlineData(@"C:\Work\run.chm")]
    [InlineData(@"C:\Work\run.jar")]
    [InlineData(@"C:\Work\run.py")]
    [InlineData(@"C:\Work\run.ps1")]
    [InlineData(@"C:\Work\run.lnk")]
    [InlineData(@"C:\Work\run.vbs")]
    [InlineData(@"C:\Work\run.url")]
    [InlineData(@"C:\Work\file.txt:payload.exe")]
    [InlineData(@"\\.\C:\Work\file.txt")]
    [InlineData(@"\\?\C:\Work\file.txt")]
    [InlineData(@"C:\Work\NUL.txt")]
    [InlineData(@"C:relative.txt")]
    [InlineData("https://server/file.txt")]
    public void UnsafePathsNeverReachShellOrDatabase(string path)
    {
        var calls = 0; var adapter = new WindowsFileAccessAdapter(_ => calls++);
        Assert.Equal(FileOpenStatus.Rejected, adapter.Open(path).Status); Assert.Equal(0, calls);
        using var s = Open(); var item = s.CreateCatalog("Safe", "file_reference", null);
        Assert.Throws<ArgumentException>(() => s.AddPersonalLocation(item.Id, 1, path)); Assert.Equal(1, Version(s, item.Id));
    }
    [Fact]
    public void OpeningRequiresExplicitActionAndExistingAccessibleFile()
    {
        Directory.CreateDirectory(_root); var path = Path.Combine(_root, "file.txt"); var calls = new List<string>(); var adapter = new WindowsFileAccessAdapter(calls.Add);
        using var s = Open(); var item = s.CreateCatalog("File", "file_reference", null); s.AddPersonalLocation(item.Id, 1, path); Assert.Empty(calls);
        Assert.Equal(FileOpenStatus.NotFound, adapter.Open(path).Status); Assert.Empty(calls);
        File.WriteAllText(path, "content"); Assert.Equal(FileOpenStatus.Opened, adapter.Open(path).Status); Assert.Equal([path], calls);
        s.ChangeWorkspaceLifecycle(item.Id, 2, "trash"); s.PurgeWorkspaceObject(item.Id, 3); Assert.True(File.Exists(path)); Assert.Equal([path], calls);
    }
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
