using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Task.Desktop.Modes;
using Task.Desktop.Personal;
using Task.Desktop.Security;
using Task.Desktop.TaskApi;
using Task.Desktop.ViewModels;

namespace Task.Desktop.Tests.Personal;

public sealed class PersonalSafetyTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "Task-safety-tests", Guid.NewGuid().ToString("N"));
    private PersonalDataPaths Paths => new(_root);
    private string Package => Path.Combine(_root, "manual.taskbackup");
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private PersonalTaskStore Open(PersonalStoreOwnership owner, bool restoring = false) => new(Paths, null, owner, restoring: restoring);
    private void Sql(string sql)
    {
        using var connection = PersonalDatabaseValidation.Open(Paths.DatabasePath, SqliteOpenMode.ReadWrite);
        using var command = connection.CreateCommand(); command.CommandText = sql; command.ExecuteNonQuery();
    }
    private byte[] Bytes
    {
        get
        {
            using var stream = new FileStream(Paths.DatabasePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var memory = new MemoryStream(); stream.CopyTo(memory); return memory.ToArray();
        }
    }
    private void Rewrite(Func<PersonalBackupMetadata, PersonalBackupMetadata> change, bool damage = false)
    {
        byte[] db; PersonalBackupMetadata metadata;
        using (var zip = ZipFile.OpenRead(Package))
        {
            using var m = zip.GetEntry("metadata.json")!.Open(); metadata = JsonSerializer.Deserialize<PersonalBackupMetadata>(m, JsonOptions)!;
            using var entry = zip.GetEntry("personal.db")!.Open(); using var memory = new MemoryStream(); entry.CopyTo(memory); db = memory.ToArray();
        }
        if (damage) db[80] ^= 1;
        File.Delete(Package);
        using var output = ZipFile.Open(Package, ZipArchiveMode.Create);
        using (var m = output.CreateEntry("metadata.json").Open()) JsonSerializer.Serialize(m, change(metadata), JsonOptions);
        using var d = output.CreateEntry("personal.db").Open(); d.Write(db);
    }
    [Fact]
    public void RoundTrip_CompletePackageAllowlistAndSafetyCopy_CorporateAndPhysicalFilesUntouched()
    {
        using var owner = PersonalStoreOwnership.Acquire(Paths);
        PersonalTaskStore? store = Open(owner);
        try
        {
            var task = store.Create(new("Snapshot", DesktopTaskPriority.High));
            store.WriteChecklist(task.Id, 1, null, "Checklist", null, false);
            var file = Path.Combine(_root, "external.txt"); File.WriteAllText(file, "physical file excluded");
            var catalog = store.CreateCatalog("Reference", "file_reference", null); store.AddPersonalLocation(catalog.Id, catalog.Version, file);
            var corporateFiles = new[] { "credentials.bin", "server-settings.json", "corporate-cache.json", "device.key" };
            foreach (var name in corporateFiles) File.WriteAllText(Path.Combine(_root, name), "CORPORATE_SECRET_" + name);
            var service = new PersonalBackupService(Paths, owner); var metadata = service.Backup(store, Package);
            using (var zip = ZipFile.OpenRead(Package))
            {
                Assert.Equal(new[] { "metadata.json", "personal.db" }, zip.Entries.Select(e => e.FullName).Order().ToArray());
                using var entry = zip.GetEntry("personal.db")!.Open(); Assert.Equal(metadata.Sha256, Convert.ToHexString(SHA256.HashData(entry)));
                Assert.Equal(4, metadata.SchemaVersion); Assert.Equal(1, metadata.FormatVersion); Assert.Equal(TimeSpan.Zero, metadata.CreatedAtUtc.Offset);
                Assert.NotEmpty(metadata.AppVersion);
            }
            store.Patch(new(task.Id, 2, title: DesktopTaskField<string>.From("Changed")));
            var safety = service.Restore(Package, () => { store?.Dispose(); store = null; }, () => store = Open(owner, true));
            Assert.Equal("Snapshot", store!.Get(task.Id)!.Title); Assert.Single(store.Checklist(task.Id)); Assert.Single(store.Catalog());
            Assert.True(File.Exists(safety));
            using (var old = PersonalDatabaseValidation.Open(safety, SqliteOpenMode.ReadOnly))
            { using var command = old.CreateCommand(); command.CommandText = "SELECT title FROM tasks;"; Assert.Equal("Changed", command.ExecuteScalar()); }
            foreach (var name in corporateFiles) Assert.Equal("CORPORATE_SECRET_" + name, File.ReadAllText(Path.Combine(_root, name)));
            Assert.Equal("physical file excluded", File.ReadAllText(file)); Assert.False(service.HasPendingRestore);
        }
        finally { store?.Dispose(); }
    }
    [Theory]
    [InlineData("sha")]
    [InlineData("truncated")]
    [InlineData("format")]
    [InlineData("schema")]
    [InlineData("metadata")]
    [InlineData("extra-entry")]
    [InlineData("integrity")]
    [InlineData("schema-mismatch")]
    public void InvalidPackage_NeverClosesOrChangesCurrentDatabase(string failure)
    {
        using var owner = PersonalStoreOwnership.Acquire(Paths); using var store = Open(owner);
        store.Create(new("Keep", DesktopTaskPriority.Normal)); var service = new PersonalBackupService(Paths, owner); service.Backup(store, Package);
        switch (failure)
        {
            case "sha": Rewrite(m => m, true); break;
            case "format": Rewrite(m => m with { FormatVersion = 99 }); break;
            case "schema": Rewrite(m => m with { SchemaVersion = 99 }); break;
            case "metadata": Rewrite(m => m with { AppVersion = "" }); break;
            case "schema-mismatch": Rewrite(m => m with { SchemaVersion = 3 }); break;
            case "truncated": var bytes = File.ReadAllBytes(Package); File.WriteAllBytes(Package, bytes[..(bytes.Length / 2)]); break;
            case "extra-entry": using (var zip = ZipFile.Open(Package, ZipArchiveMode.Update)) zip.CreateEntry("credentials.bin"); break;
            case "integrity":
                byte[] invalid = new byte[512];
                using (var zip = ZipFile.Open(Package, ZipArchiveMode.Update))
                {
                    zip.GetEntry("personal.db")!.Delete(); using (var entry = zip.CreateEntry("personal.db").Open()) entry.Write(invalid);
                    zip.GetEntry("metadata.json")!.Delete(); using var entryM = zip.CreateEntry("metadata.json").Open();
                    JsonSerializer.Serialize(entryM, new PersonalBackupMetadata("Task.Personal", 1, 4, "1.0", DateTimeOffset.UtcNow, Convert.ToHexString(SHA256.HashData(invalid))), JsonOptions);
                }
                break;
        }
        var before = Bytes; var closed = false;
        Assert.NotNull(Record.Exception(() => service.Restore(Package, () => closed = true, () => throw new Exception("Must not reopen"))));
        Assert.False(closed); Assert.Equal(before, Bytes); Assert.Equal("Keep", Assert.Single(store.List()).Title);
        Assert.False(service.HasPendingRestore); Assert.Empty(Directory.GetFiles(Paths.DirectoryPath, "restore-*.tmp"));
    }
    [Theory]
    [InlineData("snapshot")]
    [InlineData("package-write")]
    [InlineData("package-complete")]
    public void BackupIOFailure_SourceAndExistingValidBackupUnchanged_NoTemporarySuccess(string stage)
    {
        using var owner = PersonalStoreOwnership.Acquire(Paths); using var store = Open(owner); store.Create(new("Keep", DesktopTaskPriority.Normal));
        new PersonalBackupService(Paths, owner).Backup(store, Package); var previous = File.ReadAllBytes(Package); var before = Bytes;
        var service = new PersonalBackupService(Paths, owner, point => { if (point == stage) throw new IOException("disk full injection"); });
        Assert.Throws<IOException>(() => service.Backup(store, Package)); Assert.Equal(previous, File.ReadAllBytes(Package)); Assert.Equal(before, Bytes);
        Assert.Empty(Directory.GetFiles(_root, "*.tmp", SearchOption.AllDirectories));
    }
    [Fact]
    public void FailedTempCreationAndActualPackageOpenFailure_KeepSource()
    {
        using var owner = PersonalStoreOwnership.Acquire(Paths); using var store = Open(owner); store.Create(new("Keep", DesktopTaskPriority.Normal));
        var service = new PersonalBackupService(Paths, owner); service.Backup(store, Package); var before = Bytes;
        var failing = new PersonalBackupService(Paths, owner, point => { if (point == "restore-temp") throw new IOException("no space for temp"); });
        Assert.Throws<IOException>(() => failing.Restore(Package, () => throw new Exception("Never close"), () => { }));
        Assert.Throws<DirectoryNotFoundException>(() => service.Backup(store, Path.Combine(_root, "absent", "backup.taskbackup")));
        Assert.Equal(before, Bytes); Assert.Empty(Directory.GetFiles(Paths.DirectoryPath, "*.tmp"));
    }
    [Theory]
    [InlineData("before-activation")]
    [InlineData("after-activation")]
    [InlineData("reopen")]
    [InlineData("startup-verified")]
    public void RestoreFailure_RollsBackWithSafetyCopyAndReopensCurrentData(string stage)
    {
        using var owner = PersonalStoreOwnership.Acquire(Paths); PersonalTaskStore? store = Open(owner);
        try
        {
            var task = store.Create(new("Backup", DesktopTaskPriority.Normal)); new PersonalBackupService(Paths, owner).Backup(store, Package);
            store.Patch(new(task.Id, 1, title: DesktopTaskField<string>.From("Current"))); var before = Bytes; var firstReopen = true;
            var service = new PersonalBackupService(Paths, owner, point => { if (point == stage) throw new IOException(stage); });
            Assert.Throws<IOException>(() => service.Restore(Package, () => { store?.Dispose(); store = null; }, () =>
            {
                if (stage == "reopen" && firstReopen) { firstReopen = false; throw new IOException("reopen failed"); }
                store = Open(owner, true);
            }));
            Assert.Equal("Current", store!.Get(task.Id)!.Title); Assert.Equal(before, Bytes);
            Assert.Single(Directory.GetFiles(Path.Combine(Paths.DirectoryPath, "recovery"), "tasks.db", SearchOption.AllDirectories));
            Assert.False(service.HasPendingRestore);
        }
        finally { store?.Dispose(); }
    }
    [Fact]
    public void BothReopensFail_SafetyAndFailedDatabaseRemainAvailable()
    {
        using var owner = PersonalStoreOwnership.Acquire(Paths); PersonalTaskStore? store = Open(owner);
        var task = store.Create(new("Backup", DesktopTaskPriority.Normal)); var service = new PersonalBackupService(Paths, owner); service.Backup(store, Package);
        store.Patch(new(task.Id, 1, title: DesktopTaskField<string>.From("Current")));
        Assert.Throws<AggregateException>(() => service.Restore(Package, () => { store?.Dispose(); store = null; }, () => throw new IOException("cannot reopen")));
        using var reopened = Open(owner); Assert.Equal("Current", reopened.Get(task.Id)!.Title);
        Assert.NotEmpty(Directory.GetFiles(Path.Combine(Paths.DirectoryPath, "recovery"), "failed-*.db", SearchOption.AllDirectories));
    }
    [Theory]
    [InlineData("corrupt")]
    [InlineData("empty")]
    [InlineData("actor")]
    [InlineData("future")]
    [InlineData("table")]
    public void BadActiveDatabase_FailsClosedByteForByte_AndValidBackupCanRecover(string kind)
    {
        using var owner = PersonalStoreOwnership.Acquire(Paths); using (var store = Open(owner))
        { store.Create(new("Restore me", DesktopTaskPriority.Normal)); new PersonalBackupService(Paths, owner).Backup(store, Package); }
        switch (kind)
        {
            case "corrupt": File.WriteAllBytes(Paths.DatabasePath, new byte[512]); break;
            case "empty": File.WriteAllBytes(Paths.DatabasePath, []); break;
            case "actor": Sql("UPDATE personal_metadata SET value='invalid';"); break;
            case "future": Sql("PRAGMA user_version=99;"); break;
            case "table": Sql("DROP VIEW personal_objects; DROP TABLE personal_contacts;"); break;
        }
        var before = Bytes; Assert.NotNull(Record.Exception(() => Open(owner))); Assert.Equal(before, Bytes);
        PersonalTaskStore? restored = null;
        try
        {
            var safety = new PersonalBackupService(Paths, owner).Restore(Package, () => restored?.Dispose(), () => restored = Open(owner, true));
            Assert.Equal("Restore me", Assert.Single(restored!.List()).Title); Assert.Equal(before, File.ReadAllBytes(safety));
        }
        finally { restored?.Dispose(); }
    }
    [Fact]
    public void InvalidCrashMarkerOrUnavailableSafety_DoesNotPreventExplicitValidatedBackupRecovery()
    {
        using var owner = PersonalStoreOwnership.Acquire(Paths);
        using (var store = Open(owner)) { store.Create(new("Backup recovery", DesktopTaskPriority.Normal)); new PersonalBackupService(Paths, owner).Backup(store, Package); }
        var marker = Path.Combine(Paths.DirectoryPath, "restore-pending.json"); File.WriteAllText(marker, "invalid interrupted marker");
        var before = Bytes; Assert.Throws<InvalidDataException>(() => Open(owner)); Assert.Equal(before, Bytes);
        PersonalTaskStore? restored = null;
        try
        {
            var safety = new PersonalBackupService(Paths, owner).Restore(Package, () => { restored?.Dispose(); restored = null; }, () => restored = Open(owner, true));
            Assert.Equal("Backup recovery", Assert.Single(restored!.List()).Title); Assert.False(File.Exists(marker));
            Assert.Equal("invalid interrupted marker", File.ReadAllText(Path.Combine(Path.GetDirectoryName(safety)!, "previous-restore-pending.json")));
        }
        finally { restored?.Dispose(); }
    }
    [Theory]
    [InlineData("before-activation")]
    [InlineData("after-activation")]
    public void NestedRestoreFailure_RetainsPreviousRecoveryMarkerAndOriginalData(string stage)
    {
        using var owner = PersonalStoreOwnership.Acquire(Paths);
        using (var store = Open(owner))
        {
            var task = store.Create(new("Backup", DesktopTaskPriority.Normal)); new PersonalBackupService(Paths, owner).Backup(store, Package);
            store.Patch(new(task.Id, 1, title: DesktopTaskField<string>.From("Current")));
        }
        var marker = Path.Combine(Paths.DirectoryPath, "restore-pending.json"); File.WriteAllText(marker, "previous unresolved marker");
        var before = Bytes; var reopened = false;
        var service = new PersonalBackupService(Paths, owner, point => { if (point == stage) throw new IOException("nested failure"); });
        Assert.Throws<IOException>(() => service.Restore(Package, () => { }, () => reopened = true));
        Assert.False(reopened); Assert.Equal(before, Bytes); Assert.Equal("previous unresolved marker", File.ReadAllText(marker));
        Assert.Throws<InvalidDataException>(() => Open(owner));
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HistoricalBackup_RestoresAndMigrates_OrMigrationFailureReturnsCurrentDb(bool failMigration)
    {
        var historical = new PersonalDataPaths(Path.Combine(_root, "historical"));
        using (var old = new PersonalDatabase(historical, targetVersion: 3))
        {
            using var insert = old.Connection.CreateCommand();
            insert.CommandText = "INSERT INTO tasks(id,version,created_at,updated_at,title,status,priority) VALUES('11111111-1111-1111-1111-111111111111',1,'2026-10-05T00:00:00+00:00','2026-10-05T00:00:00+00:00','Historical backup',0,1);";
            insert.ExecuteNonQuery();
        }
        using (var archive = ZipFile.Open(Package, ZipArchiveMode.Create))
        {
            using var input = File.OpenRead(historical.DatabasePath);
            var metadata = new PersonalBackupMetadata("Task.Personal", 1, 3, "0.9.0", DateTimeOffset.UtcNow, Convert.ToHexString(SHA256.HashData(input)));
            using (var entry = archive.CreateEntry("metadata.json").Open()) JsonSerializer.Serialize(entry, metadata, JsonOptions);
            archive.CreateEntryFromFile(historical.DatabasePath, "personal.db");
        }
        using var owner = PersonalStoreOwnership.Acquire(Paths); PersonalTaskStore? store = Open(owner);
        try
        {
            store.Create(new("Current data", DesktopTaskPriority.Normal)); var before = Bytes; var first = true;
            Action reopen = () => store = new PersonalTaskStore(Paths, null, owner, point =>
            {
                if (failMigration && first && point == "migration-commit") { first = false; throw new IOException("restored schema migration failure"); }
            }, restoring: true);
            Action close = () => { store?.Dispose(); store = null; };
            var service = new PersonalBackupService(Paths, owner);
            if (failMigration)
            {
                Assert.Throws<IOException>(() => service.Restore(Package, close, reopen));
                Assert.Equal(before, Bytes); Assert.Equal("Current data", Assert.Single(store!.List()).Title);
            }
            else
            {
                service.Restore(Package, close, reopen); Assert.Equal("Historical backup", Assert.Single(store!.List()).Title);
                Assert.Equal(4, PersonalDatabaseValidation.ValidateFile(Paths.DatabasePath));
            }
            Assert.False(service.HasPendingRestore);
        }
        finally { store?.Dispose(); }
    }
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void OldBuildDataset_ToCurrentAndRestart_PreservesData_MigrationRunsOnce(int version)
    {
        Guid id;
        using (var store = new PersonalTaskStore(Paths)) id = store.Create(new("Task N", DesktopTaskPriority.Normal)).Id;
        MakeOldSchema(version);
        using (var upgraded = new PersonalTaskStore(Paths)) Assert.Equal("Task N", upgraded.Get(id)!.Title);
        var after = Bytes;
        using (var nextBuild = new PersonalTaskStore(Paths)) Assert.Equal(id, Assert.Single(nextBuild.List()).Id);
        Assert.Equal(after, Bytes); Assert.Equal(4, PersonalDatabaseValidation.ValidateFile(Paths.DatabasePath));
        Assert.Single(Directory.GetFiles(Path.Combine(Paths.DirectoryPath, "recovery"), "migration-*.db"));
        Assert.DoesNotContain("bin", Paths.DatabasePath); Assert.DoesNotContain("Release", Paths.DatabasePath);
    }
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void FreshHistoricalSchemaFixture_UpgradesWithoutDependingOnCurrentSchema(int version)
    {
        using var owner = PersonalStoreOwnership.Acquire(Paths);
        var id = Guid.NewGuid();
        using (var oldBuild = new PersonalDatabase(Paths, targetVersion: version))
        {
            using var insert = oldBuild.Connection.CreateCommand();
            insert.CommandText = "INSERT INTO tasks(id,version,created_at,updated_at,title,status,priority) VALUES($id,1,'2026-10-05T00:00:00+00:00','2026-10-05T00:00:00+00:00','Task N',0,1);";
            insert.Parameters.AddWithValue("$id", id.ToString("D")); insert.ExecuteNonQuery();
            Assert.Equal(version, PersonalDatabaseValidation.Validate(oldBuild.Connection));
        }
        using (var nextBuild = Open(owner)) Assert.Equal("Task N", nextBuild.Get(id)!.Title);
        using var restart = Open(owner); Assert.Equal(id, Assert.Single(restart.List()).Id);
    }
    [Fact]
    public void OnlineBackup_IncludesCommittedWalDataWithoutCopyingOpenDatabaseFile()
    {
        using var owner = PersonalStoreOwnership.Acquire(Paths); using var store = Open(owner);
        store.Create(new("Before WAL", DesktopTaskPriority.Normal));
        using var sql = PersonalDatabaseValidation.Open(Paths.DatabasePath, SqliteOpenMode.ReadWrite);
        using var command = sql.CreateCommand(); command.CommandText = "PRAGMA journal_mode=WAL; PRAGMA wal_autocheckpoint=0;"; command.ExecuteNonQuery();
        var task = store.List().Single(); store.Patch(new(task.Id, task.Version, title: DesktopTaskField<string>.From("Committed WAL data")));
        Assert.True(new FileInfo(Paths.DatabasePath + "-wal").Length > 0);
        new PersonalBackupService(Paths, owner).Backup(store, Package);
        using var archive = ZipFile.OpenRead(Package);
        var extracted = Path.Combine(_root, "snapshot.db"); archive.GetEntry("personal.db")!.ExtractToFile(extracted);
        using var snapshot = PersonalDatabaseValidation.Open(extracted, SqliteOpenMode.ReadOnly);
        using var read = snapshot.CreateCommand(); read.CommandText = "SELECT title FROM tasks;";
        Assert.Equal("Committed WAL data", read.ExecuteScalar()); Assert.Equal("Committed WAL data", store.Get(task.Id)!.Title);
    }
    private void MakeOldSchema(int version)
    {
        if (version <= 2) Sql(PersonalPlanningTests.RemovePlanningSchema + (version == 1 ? " DROP TABLE checklist;" : "") + $" PRAGMA user_version={version};");
        else Sql(PersonalWorkspaceTests.RemoveWorkspaceSchema + " PRAGMA user_version=3;");
    }
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void AdjacentUpgrade_AndOlderBuildOpeningNewerDatabase_DoNotDowngrade(int version)
    {
        using (var store = new PersonalTaskStore(Paths)) store.Create(new("Adjacent upgrade", DesktopTaskPriority.Normal));
        MakeOldSchema(version);
        using var owner = PersonalStoreOwnership.Acquire(Paths);
        using (var upgraded = new PersonalDatabase(Paths, targetVersion: version + 1))
            Assert.Equal(version + 1, PersonalDatabaseValidation.Validate(upgraded.Connection));
        var after = Bytes;
        Assert.Throws<InvalidOperationException>(() => new PersonalDatabase(Paths, targetVersion: version));
        Assert.Equal(after, Bytes);
    }
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void MigrationFailure_AfterDdl_RollsBackAndPreservesVerifiedSafety(int version)
    {
        using (var store = new PersonalTaskStore(Paths)) store.Create(new("Keep", DesktopTaskPriority.Normal));
        MakeOldSchema(version); var before = Bytes;
        using var owner = PersonalStoreOwnership.Acquire(Paths);
        Assert.Throws<IOException>(() => new PersonalTaskStore(Paths, null, owner, point => { if (point == "migration-commit") throw new IOException("migration disk failure"); }));
        Assert.Equal(before, Bytes); Assert.Equal(version, PersonalDatabaseValidation.ValidateFile(Paths.DatabasePath));
        var copy = Assert.Single(Directory.GetFiles(Path.Combine(Paths.DirectoryPath, "recovery"), "migration-*.db"));
        Assert.Equal(version, PersonalDatabaseValidation.ValidateFile(copy));
        using var retry = Open(owner); Assert.Equal("Keep", Assert.Single(retry.List()).Title);
    }
    [Fact]
    public async System.Threading.Tasks.Task SQLiteFull_IsRealRollback_AndSaveDoesNotLoseEditorOrClaimSuccess()
    {
        using var model = new PersonalApplicationModel(_root); var vm = model.Tasks!;
        model.Store.LimitPagesForTest();
        await vm.NewCommand.ExecuteAsync(); vm.Editor!.Title = "Retained draft"; vm.Editor.Card.Description = new string('x', 10000);
        var editor = vm.Editor; await vm.SaveCommand.ExecuteAsync();
        Assert.Same(editor, vm.Editor); Assert.Equal("Retained draft", editor.Title); Assert.Equal(10000, editor.Card.Description.Length);
        Assert.Contains("не сохранены", editor.StatusMessage); Assert.Empty(model.Store.List());
        var full = Assert.Throws<SqliteException>(() => model.Store.Create(new("FULL", DesktopTaskPriority.Normal, card: new() { Description = new string('x', 10000) })));
        Assert.Equal(13, full.SqliteErrorCode); Assert.Empty(model.Store.List());
        Assert.False(await ModeSwitchGuard.PrepareAsync(model.InspectDrafts(), () => !model.IsBusy, (_, _) => ModeSwitchDecision.Save));
        Assert.Same(editor, vm.Editor);
    }
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async System.Threading.Tasks.Task StageOneModeSwitch_RealStorageAndBothDirections(int decision)
    {
        var choice = (ModeSwitchDecision)decision;
        using (var model = new PersonalApplicationModel(_root))
        {
            await model.Tasks!.NewCommand.ExecuteAsync(); model.Tasks.Editor!.Title = "Personal draft";
            var result = await ModeSwitchGuard.PrepareAsync(model.InspectDrafts(), () => !model.IsBusy, (_, _) => choice);
            Assert.Equal(choice != ModeSwitchDecision.Cancel, result);
            Assert.Equal(choice == ModeSwitchDecision.Save ? 1 : 0, model.Store.List().Count);
            if (choice != ModeSwitchDecision.Save) Assert.Equal("Personal draft", model.Tasks.Editor!.Title);
        }
        using var reopened = new PersonalApplicationModel(_root);
        Assert.Equal(choice == ModeSwitchDecision.Save ? 1 : 0, reopened.Store.List().Count);
        // Corporate → Personal uses the same guard. A confirmed Corporate save is required.
        var corporateDraft = true; using var command = new AsyncCommand((_, _) => { corporateDraft = false; return System.Threading.Tasks.Task.CompletedTask; });
        var editor = new ModeSwitchEditor("Corporate", () => corporateDraft, () => false, command);
        Assert.Equal(choice != ModeSwitchDecision.Cancel, await ModeSwitchGuard.PrepareAsync([editor], () => true, (_, _) => choice));
        Assert.Equal(choice != ModeSwitchDecision.Save, corporateDraft);
    }
    [Fact]
    public async System.Threading.Tasks.Task ConcurrentOwnership_DifferentThreadsReleaseAndDifferentStores_CorporateStillAvailable()
    {
        using (var store = new PersonalTaskStore(Paths))
        {
            store.Create(new("Keep", DesktopTaskPriority.Normal));
            await System.Threading.Tasks.Task.Run(() => Assert.Throws<PersonalStoreBusyException>(() => new PersonalTaskStore(Paths)));
            new DesktopCredentialVault(_root).SetAccessToken("test access"); // Corporate owns no Personal lease.
            using var different = new PersonalTaskStore(new(Path.Combine(_root, "other-root")));
            different.Create(new("Independent", DesktopTaskPriority.Normal));
        }
        using var afterRelease = new PersonalTaskStore(Paths); Assert.Equal("Keep", Assert.Single(afterRelease.List()).Title);
    }
    [Fact]
    public void CorporateLogoutAndServerChange_NeverAlterPersonalOrItsBackups()
    {
        using (var store = new PersonalTaskStore(Paths)) store.Create(new("Personal", DesktopTaskPriority.Normal)); var before = Bytes;
        var vault = new DesktopCredentialVault(_root); vault.SaveRefreshToken("device", "", "login", "device-key", "refresh"); vault.SetAccessToken("access");
        var settings = new DesktopServerSettingsStore(_root); settings.SaveVerifiedEndpoint(new Uri("https://server-a.test"), vault);
        vault.SaveRefreshToken("device", "", "login", "device-key", "refresh"); vault.Clear(); Assert.Equal(before, Bytes);
        settings.SaveVerifiedEndpoint(new Uri("https://server-b.test"), vault); Assert.Equal(before, Bytes);
        using var reopened = new PersonalTaskStore(Paths); Assert.Equal("Personal", Assert.Single(reopened.List()).Title);
    }
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
