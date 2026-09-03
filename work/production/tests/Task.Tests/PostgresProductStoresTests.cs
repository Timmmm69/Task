using Npgsql;
using Task.Application.ProductData;
using Task.Domain;
using Task.Domain.Calendar;
using Task.Infrastructure.Persistence;
using Xunit.Abstractions;

namespace Task.Tests;

public sealed class PostgresProductStoresTests(ITestOutputHelper output)
{
    private static readonly DateTimeOffset Now = new(2026, 9, 3, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void InvalidProductPayloadsAreRejectedBeforeConnecting()
    {
        using var runtime = new TaskPersistenceRuntime("Host=127.0.0.1;Port=1;Database=unused;Username=unused;Timeout=1");
        var organizationId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var contact = Contact(organizationId, actorId);
        Assert.Throws<ArgumentException>(() => runtime.CreateContactStore().Add(contact with { FirstName = " " }));
        Assert.Throws<ArgumentException>(() => runtime.CreateContactStore().Save(contact, 1));
        var item = Catalog(organizationId, actorId, CatalogItemType.WebLink);
        Assert.Throws<ArgumentException>(() => runtime.CreateCatalogItemStore().Add(item with { WebUrl = null }));
        Assert.Throws<ArgumentException>(() => runtime.CreateCatalogItemStore().Add(item with { ParentId = item.Metadata.Id }));
        var notification = Notification(organizationId, actorId, null);
        Assert.Throws<ArgumentException>(() => runtime.CreateNotificationStore().Add(notification with { ActionPayloadJson = "[]" }));
        Assert.Throws<ArgumentException>(() => runtime.CreateProductSettingsStore().AddUser(
            UserSettings(organizationId, actorId) with { WeekendDays = new short[] { 6, 6 } }));
        Assert.Throws<ArgumentException>(() => runtime.CreateProductSettingsStore().AddNotificationPreference(
            Preference(organizationId, actorId) with { QuietHoursStart = new TimeOnly(22, 0) }));
    }

    [Fact]
    public async global::System.Threading.Tasks.Task RealPostgres_ProductStoresRoundTripConcurrencyTenantAndLifecycle()
    {
        var adminConnectionString = Environment.GetEnvironmentVariable("TASK_POSTGRES_TEST_ADMIN_CONNECTION");
        if (string.IsNullOrWhiteSpace(adminConnectionString))
        {
            output.WriteLine("NOT RUN: set TASK_POSTGRES_TEST_ADMIN_CONNECTION for the real PostgreSQL integration gate.");
            return;
        }

        var databaseName = $"task_products_{Guid.NewGuid():N}";
        var runtimeRole = $"task_products_role_{Guid.NewGuid():N}";
        var roleCreated = false;
        using var admin = NpgsqlDataSource.Create(adminConnectionString);
        Execute(admin, $"CREATE DATABASE {databaseName};");
        try
        {
            var connectionString = new NpgsqlConnectionStringBuilder(adminConnectionString) { Database = databaseName }.ConnectionString;
            using var dataSource = NpgsqlDataSource.Create(connectionString);
            var migrator = new TaskPersistenceMigrator(dataSource);
            migrator.ApplyPending();
            migrator.ApplyPending();
            Assert.Equal(TaskPersistenceMigrationStatus.Current, (await migrator.InspectAsync()).Status);
            var organizationId = Guid.NewGuid();
            var actorId = Guid.NewGuid();
            var otherOrganizationId = Guid.NewGuid();
            var otherActorId = Guid.NewGuid();
            Seed(dataSource, organizationId, actorId);
            Seed(dataSource, otherOrganizationId, otherActorId);
            Execute(admin, $"CREATE ROLE {runtimeRole} LOGIN PASSWORD 'task-product-runtime-test-only' NOSUPERUSER NOCREATEDB NOCREATEROLE;");
            roleCreated = true;
            ApplyRuntimeGrants(dataSource, runtimeRole);
            var runtimeConnectionString = new NpgsqlConnectionStringBuilder(connectionString)
            {
                Username = runtimeRole,
                Password = "task-product-runtime-test-only",
            }.ConnectionString;
            using var runtime = new TaskPersistenceRuntime(runtimeConnectionString, TimeSpan.FromSeconds(10));

            VerifySettings(runtime, organizationId, actorId, otherOrganizationId);
            VerifyProjects(runtime, dataSource, organizationId, actorId, otherOrganizationId, otherActorId);
            VerifyContactsAndLifecycle(runtime, dataSource, organizationId, actorId, otherOrganizationId);
            VerifyCatalog(runtime, dataSource, organizationId, actorId, otherOrganizationId, otherActorId);
            VerifyNotifications(runtime, dataSource, organizationId, actorId, otherOrganizationId, otherActorId);
            VerifyExistingCalendarLifecycle(runtime, organizationId, actorId);

            using (var restricted = NpgsqlDataSource.Create(runtimeConnectionString))
            {
                Assert.Equal("42501", Assert.Throws<PostgresException>(() =>
                    Execute(restricted, "CREATE TABLE core.forbidden_runtime_ddl (id integer);")).SqlState);
                Assert.Equal("42501", Assert.Throws<PostgresException>(() =>
                    Execute(restricted, "DELETE FROM core.objects WHERE false;")).SqlState);
            }

            Execute(dataSource, "ALTER TABLE core.objects DISABLE TRIGGER trg_record_product_lifecycle;");
            Assert.Equal(TaskPersistenceReadinessCode.SchemaObjectsMissing, (await runtime.CheckReadinessAsync()).Code);
            Execute(dataSource, "ALTER TABLE core.objects ENABLE REPLICA TRIGGER trg_record_product_lifecycle;");
            Assert.Equal(TaskPersistenceReadinessCode.SchemaObjectsMissing, (await runtime.CheckReadinessAsync()).Code);
            Execute(dataSource, "ALTER TABLE core.objects ENABLE TRIGGER trg_record_product_lifecycle;");
            Assert.True((await runtime.CheckReadinessAsync()).Ready);
            output.WriteLine("PASS: migration v9, product round-trips, settings CAS, tenant FKs, rollback, catalog cycles, immutable notifications and shared lifecycle ledger.");
        }
        finally
        {
            Execute(admin, $"DROP DATABASE IF EXISTS {databaseName} WITH (FORCE);");
            if (roleCreated) Execute(admin, $"DROP ROLE {runtimeRole};");
        }
    }

    private static void VerifySettings(TaskPersistenceRuntime runtime, Guid organizationId, Guid actorId, Guid otherOrganizationId)
    {
        var store = runtime.CreateProductSettingsStore();
        var organization = new OrganizationSettingsSnapshot(organizationId, 45, 1095, 90, 90, 20,
            new TimeOnly(9, 0), new TimeOnly(18, 0), 1, 1048576, Now, 1);
        store.AddOrganization(organization);
        Assert.Equal(organization, store.GetOrganization(organizationId));
        Assert.Null(store.GetOrganization(otherOrganizationId));
        var changed = organization with { Version = 2, UpdatedAtUtc = Now.AddMinutes(1), HistoryRetentionDays = 1200 };
        store.SaveOrganization(changed, 1);
        Assert.Equal(changed, store.GetOrganization(organizationId));
        Assert.Throws<ProductEntityConcurrencyException>(() => store.SaveOrganization(changed, 1));
        Assert.Throws<ArgumentException>(() => store.SaveOrganization(changed with { Version = 3, UpdatedAtUtc = Now }, 2));

        var user = UserSettings(organizationId, actorId);
        store.AddUser(user);
        var loadedUser = store.GetUser(actorId, organizationId)!;
        Assert.Equal(user.Language, loadedUser.Language);
        Assert.Equal(user.WeekendDays, loadedUser.WeekendDays);
        Assert.Equal(user.WorkdayStart, loadedUser.WorkdayStart);
        Assert.Null(store.GetUser(actorId, otherOrganizationId));
        store.SaveUser(loadedUser with { Version = 2, UpdatedAtUtc = Now.AddMinutes(1), Language = "en-US", AllowLocalPaths = false }, 1);
        Assert.Equal("en-US", store.GetUser(actorId, organizationId)!.Language);
        Assert.False(store.GetUser(actorId, organizationId)!.AllowLocalPaths);
        Assert.Throws<ProductEntityConcurrencyException>(() => store.SaveUser(loadedUser with { Version = 2 }, 1));

        var preference = Preference(organizationId, actorId);
        store.AddNotificationPreference(preference);
        Assert.Equal(preference, store.GetNotificationPreference(actorId, organizationId, preference.NotificationType));
        Assert.Null(store.GetNotificationPreference(actorId, otherOrganizationId, preference.NotificationType));
        var quiet = preference with
        {
            Version = 2,
            UpdatedAtUtc = Now.AddMinutes(1),
            SoundEnabled = false,
            QuietHoursStart = new TimeOnly(22, 0),
            QuietHoursEnd = new TimeOnly(7, 0),
            QuietHoursTimeZone = "Europe/Minsk"
        };
        store.SaveNotificationPreference(quiet, 1);
        Assert.Equal(quiet, store.GetNotificationPreference(actorId, organizationId, preference.NotificationType));
        Assert.Throws<ProductEntityConcurrencyException>(() => store.SaveNotificationPreference(quiet, 1));
    }

    [Fact]
    public async global::System.Threading.Tasks.Task RealPostgres_UpgradeFromVersionEightPreservesDataAndBackfillsLifecycle()
    {
        var adminConnectionString = Environment.GetEnvironmentVariable("TASK_POSTGRES_TEST_ADMIN_CONNECTION");
        if (string.IsNullOrWhiteSpace(adminConnectionString))
        {
            output.WriteLine("NOT RUN: set TASK_POSTGRES_TEST_ADMIN_CONNECTION for the upgrade gate.");
            return;
        }
        var databaseName = $"task_products_upgrade_{Guid.NewGuid():N}";
        using var admin = NpgsqlDataSource.Create(adminConnectionString);
        Execute(admin, $"CREATE DATABASE {databaseName};");
        try
        {
            var connectionString = new NpgsqlConnectionStringBuilder(adminConnectionString) { Database = databaseName }.ConnectionString;
            using var dataSource = NpgsqlDataSource.Create(connectionString);
            Execute(dataSource, """
                CREATE SCHEMA infrastructure;
                CREATE TABLE infrastructure.schema_migrations (
                    version integer PRIMARY KEY, name text NOT NULL, sha256 char(64) NOT NULL,
                    applied_at timestamptz NOT NULL DEFAULT clock_timestamp());
                """);
            foreach (var migration in TaskPersistenceMigrationCatalog.All.Take(8))
            {
                Execute(dataSource, migration.Sql);
                Execute(dataSource, "INSERT INTO infrastructure.schema_migrations (version, name, sha256) VALUES ($1, $2, $3);",
                    migration.Version, migration.Name, migration.Sha256);
            }
            var organizationId = Guid.NewGuid();
            var actorId = Guid.NewGuid();
            Seed(dataSource, organizationId, actorId);
            using var runtime = new TaskPersistenceRuntime(connectionString);
            var store = runtime.CreateCalendarEventStore();
            var calendarEvent = CalendarEvent.Create(Guid.NewGuid(), organizationId, actorId, null, "Existing v8 event", null,
                CalendarEventTiming.CreateAllDay(new DateOnly(2026, 9, 5), "Europe/Minsk"), Now);
            store.Add(calendarEvent);
            var archived = calendarEvent.Archive(actorId, Now.AddMinutes(1));
            store.Save(archived, 1);
            var trashed = archived.MoveToTrash(actorId, Now.AddMinutes(2));
            store.Save(trashed, 2);
            var migrator = runtime.CreateMigrator();
            Assert.Equal(TaskPersistenceMigrationStatus.Pending, (await migrator.InspectAsync()).Status);
            migrator.ApplyPending();
            migrator.ApplyPending();
            var loaded = store.Get(calendarEvent.Metadata.Id, organizationId)!;
            Assert.Equal(trashed.Metadata, loaded.Metadata);
            Assert.Equal("Existing v8 event", loaded.Title);
            var lifecycle = runtime.CreateProductLifecycleStore();
            Assert.NotNull(lifecycle.GetCurrentArchive(calendarEvent.Metadata.Id, organizationId));
            Assert.Equal(Now.AddMinutes(2).AddDays(30), lifecycle.GetCurrentTrash(calendarEvent.Metadata.Id, organizationId)!.PurgeAfterUtc);
            Assert.Equal(TaskPersistenceMigrationStatus.Current, (await migrator.InspectAsync()).Status);
        }
        finally
        {
            Execute(admin, $"DROP DATABASE IF EXISTS {databaseName} WITH (FORCE);");
        }
    }

    private static void VerifyProjects(
        TaskPersistenceRuntime runtime, NpgsqlDataSource dataSource, Guid organizationId, Guid actorId,
        Guid otherOrganizationId, Guid otherActorId)
    {
        var store = runtime.CreateProjectStore();
        var project = new ProjectSnapshot(Metadata(organizationId, actorId), "Production project", "Description", actorId, actorId,
            ProjectStatus.Active, new DateOnly(2026, 9, 1), new DateOnly(2026, 10, 1), null, "Europe/Minsk", "#336699");
        store.Add(project);
        Assert.Equal(project, store.Get(project.Metadata.Id, organizationId));
        Assert.Null(store.Get(project.Metadata.Id, otherOrganizationId));
        var changed = project with { Metadata = project.Metadata.RecordVisibleChange(actorId, Now.AddMinutes(1)), Name = "Updated project" };
        store.Save(changed, 1);
        Assert.Equal(changed, store.Get(project.Metadata.Id, organizationId));
        Assert.Equal(2, Assert.Throws<ProductEntityConcurrencyException>(() => store.Save(changed, 1)).ActualVersion);
        var invalidManager = changed with
        {
            Metadata = changed.Metadata.RecordVisibleChange(actorId, Now.AddMinutes(2)),
            ManagerUserId = otherActorId,
        };
        Assert.Throws<PostgresException>(() => store.Save(invalidManager, 2));
        Assert.Equal(changed, store.Get(project.Metadata.Id, organizationId));
        var invalidOwner = project with { Metadata = Metadata(organizationId, actorId), OwnerUserId = otherActorId };
        Assert.Throws<InvalidOperationException>(() => store.Add(invalidOwner));
        Assert.Equal(0, CountObject(dataSource, invalidOwner.Metadata.Id));
        var archived = changed with { Metadata = changed.Metadata.Archive(actorId, Now.AddMinutes(2)) };
        store.Save(archived, 2);
        Assert.NotNull(runtime.CreateProductLifecycleStore().GetCurrentArchive(project.Metadata.Id, organizationId));
    }

    private static void VerifyContactsAndLifecycle(
        TaskPersistenceRuntime runtime, NpgsqlDataSource dataSource, Guid organizationId, Guid actorId, Guid otherOrganizationId)
    {
        var store = runtime.CreateContactStore();
        var lifecycle = runtime.CreateProductLifecycleStore();
        var contact = Contact(organizationId, actorId);
        store.Add(contact);
        Assert.Equal(contact, store.Get(contact.Metadata.Id, organizationId));
        Assert.Null(store.Get(contact.Metadata.Id, otherOrganizationId));
        var foreign = contact with
        {
            Metadata = SyncableEntityMetadata.Create(contact.Metadata.Id, otherOrganizationId, actorId, Now)
                .RecordVisibleChange(actorId, Now.AddMinutes(1)),
        };
        Assert.Null(Assert.Throws<ProductEntityConcurrencyException>(() => store.Save(foreign, 1)).ActualVersion);
        var changed = contact with { Metadata = contact.Metadata.RecordVisibleChange(actorId, Now.AddMinutes(1)), Notes = "Changed notes" };
        store.Save(changed, 1);
        Assert.Equal(changed, store.Get(contact.Metadata.Id, organizationId));
        Assert.Throws<ProductEntityConcurrencyException>(() => store.Save(changed, 1));
        var archived = changed with { Metadata = changed.Metadata.Archive(actorId, Now.AddMinutes(2)) };
        Assert.Throws<ArgumentException>(() => store.Save(archived with { Notes = "Hidden edit" }, 2));
        Assert.Equal(2, store.Get(contact.Metadata.Id, organizationId)!.Metadata.Version);
        Assert.Null(lifecycle.GetCurrentArchive(contact.Metadata.Id, organizationId));
        store.Save(archived, 2);
        var archive = lifecycle.GetCurrentArchive(contact.Metadata.Id, organizationId)!;
        Assert.Equal(actorId, archive.ArchivedBy);
        Assert.Equal(archived.Metadata.ArchivedAtUtc, archive.ArchivedAtUtc);
        Assert.Null(lifecycle.GetCurrentArchive(contact.Metadata.Id, otherOrganizationId));
        var trashed = archived with { Metadata = archived.Metadata.MoveToTrash(actorId, Now.AddMinutes(3)) };
        store.Save(trashed, 3);
        var trash = lifecycle.GetCurrentTrash(contact.Metadata.Id, organizationId)!;
        Assert.Equal(trashed.Metadata.DeletedAtUtc!.Value.AddDays(45), trash.PurgeAfterUtc);
        Assert.NotNull(lifecycle.GetCurrentArchive(contact.Metadata.Id, organizationId));
        Assert.Null(lifecycle.GetCurrentTrash(contact.Metadata.Id, otherOrganizationId));
        var restored = trashed with { Metadata = trashed.Metadata.RestoreFromTrash(actorId, Now.AddMinutes(4)) };
        store.Save(restored, 4);
        Assert.Equal(EntityLifecycleState.Archived, store.Get(contact.Metadata.Id, organizationId)!.Metadata.LifecycleState);
        Assert.Null(lifecycle.GetCurrentTrash(contact.Metadata.Id, organizationId));
        var active = restored with { Metadata = restored.Metadata.RestoreFromArchive(actorId, Now.AddMinutes(5)) };
        store.Save(active, 5);
        Assert.Null(lifecycle.GetCurrentArchive(contact.Metadata.Id, organizationId));
        Assert.Equal("Changed notes", store.Get(contact.Metadata.Id, organizationId)!.Notes);
        using var command = dataSource.CreateCommand(
            "SELECT count(*) FROM governance.archive_entries WHERE object_id = $1 AND status = 'restored';");
        command.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = contact.Metadata.Id });
        Assert.Equal(1L, command.ExecuteScalar());
    }

    private static void VerifyCatalog(
        TaskPersistenceRuntime runtime, NpgsqlDataSource dataSource, Guid organizationId, Guid actorId,
        Guid otherOrganizationId, Guid otherActorId)
    {
        var store = runtime.CreateCatalogItemStore();
        var root = Catalog(organizationId, actorId, CatalogItemType.VirtualFolder);
        store.Add(root);
        var child = Catalog(organizationId, actorId, CatalogItemType.TextNote) with { ParentId = root.Metadata.Id, SortOrder = 7 };
        store.Add(child);
        Assert.Equal(child, store.Get(child.Metadata.Id, organizationId));
        Assert.Null(store.Get(child.Metadata.Id, otherOrganizationId));
        foreach (var kind in new[] { CatalogItemType.FileReference, CatalogItemType.FolderReference, CatalogItemType.WebLink })
        {
            var item = Catalog(organizationId, actorId, kind);
            store.Add(item);
            Assert.Equal(item, store.Get(item.Metadata.Id, organizationId));
        }
        var cycle = root with { Metadata = root.Metadata.RecordVisibleChange(actorId, Now.AddMinutes(1)), ParentId = child.Metadata.Id };
        Assert.Throws<InvalidOperationException>(() => store.Save(cycle, 1));
        Assert.Equal(1, store.Get(root.Metadata.Id, organizationId)!.Metadata.Version);
        var other = Catalog(otherOrganizationId, otherActorId, CatalogItemType.VirtualFolder);
        store.Add(other);
        var invalidParent = Catalog(organizationId, actorId, CatalogItemType.TextNote) with { ParentId = other.Metadata.Id };
        Assert.Throws<PostgresException>(() => store.Add(invalidParent));
        Assert.Equal(0, CountObject(dataSource, invalidParent.Metadata.Id));
        var moved = child with { Metadata = child.Metadata.RecordVisibleChange(actorId, Now.AddMinutes(1)), ParentId = null, NoteContent = "Moved" };
        store.Save(moved, 1);
        Assert.Equal(moved, store.Get(child.Metadata.Id, organizationId));
        Assert.Throws<ProductEntityConcurrencyException>(() => store.Save(moved, 1));

        var first = Catalog(organizationId, actorId, CatalogItemType.VirtualFolder);
        var second = Catalog(organizationId, actorId, CatalogItemType.VirtualFolder);
        store.Add(first);
        store.Add(second);
        using var barrier = new Barrier(2);
        var errors = new System.Collections.Concurrent.ConcurrentBag<Exception>();
        void Move(CatalogItemSnapshot item, Guid parentId)
        {
            barrier.SignalAndWait();
            try
            {
                store.Save(item with { Metadata = item.Metadata.RecordVisibleChange(actorId, Now.AddMinutes(1)), ParentId = parentId }, 1);
            }
            catch (Exception exception) { errors.Add(exception); }
        }
        Parallel.Invoke(() => Move(first, second.Metadata.Id), () => Move(second, first.Metadata.Id));
        Assert.IsType<InvalidOperationException>(Assert.Single(errors));
        Assert.Equal(3, store.Get(first.Metadata.Id, organizationId)!.Metadata.Version +
            store.Get(second.Metadata.Id, organizationId)!.Metadata.Version);

        var parent = Catalog(organizationId, actorId, CatalogItemType.VirtualFolder);
        var nested = Catalog(organizationId, actorId, CatalogItemType.TextNote) with { ParentId = parent.Metadata.Id };
        store.Add(parent);
        store.Add(nested);
        var parentTrash = parent with { Metadata = parent.Metadata.MoveToTrash(actorId, Now.AddMinutes(1)) };
        store.Save(parentTrash, 1);
        var nestedTrash = nested with { Metadata = nested.Metadata.MoveToTrash(actorId, Now.AddMinutes(1)) };
        store.Save(nestedTrash, 1);
        Assert.Throws<InvalidOperationException>(() => store.Save(
            nestedTrash with { Metadata = nestedTrash.Metadata.RestoreFromTrash(actorId, Now.AddMinutes(2)) }, 2));
        Assert.Equal(EntityLifecycleState.Trashed, store.Get(nested.Metadata.Id, organizationId)!.Metadata.LifecycleState);
    }

    private static void VerifyNotifications(
        TaskPersistenceRuntime runtime, NpgsqlDataSource dataSource, Guid organizationId, Guid actorId,
        Guid otherOrganizationId, Guid otherActorId)
    {
        var store = runtime.CreateNotificationStore();
        var notification = Notification(organizationId, actorId, null);
        store.Add(notification);
        var loadedNotification = store.Get(notification.Metadata.Id, organizationId)!;
        Assert.Equal(notification with { ActionPayloadJson = loadedNotification.ActionPayloadJson }, loadedNotification);
        Assert.Contains("open", loadedNotification.ActionPayloadJson);
        Assert.Null(store.Get(notification.Metadata.Id, otherOrganizationId));
        var delivered = notification with
        {
            Metadata = notification.Metadata.RecordVisibleChange(actorId, Now.AddMinutes(1)),
            Status = NotificationStatus.Delivered,
            DeliveredAtUtc = Now.AddMinutes(1)
        };
        store.Save(delivered, 1);
        var read = delivered with
        {
            Metadata = delivered.Metadata.RecordVisibleChange(actorId, Now.AddMinutes(2)),
            Status = NotificationStatus.Read,
            ReadAtUtc = Now.AddMinutes(2)
        };
        store.Save(read, 2);
        var loadedRead = store.Get(notification.Metadata.Id, organizationId)!;
        Assert.Equal(read with { ActionPayloadJson = loadedRead.ActionPayloadJson }, loadedRead);
        var hiddenEdit = read with { Metadata = read.Metadata.RecordVisibleChange(actorId, Now.AddMinutes(3)), Title = "Changed content" };
        Assert.Throws<InvalidOperationException>(() => store.Save(hiddenEdit, 3));
        var reversed = read with { Metadata = hiddenEdit.Metadata, Status = NotificationStatus.Pending };
        Assert.Throws<InvalidOperationException>(() => store.Save(reversed, 3));
        Assert.Equal(3, store.Get(notification.Metadata.Id, organizationId)!.Metadata.Version);
        var wrongRecipient = Notification(organizationId, otherActorId, null);
        Assert.Throws<PostgresException>(() => store.Add(wrongRecipient));
        Assert.Equal(0, CountObject(dataSource, wrongRecipient.Metadata.Id));
        var duplicate = notification with { Metadata = Metadata(organizationId, actorId) };
        Assert.Throws<PostgresException>(() => store.Add(duplicate));
        Assert.Equal(0, CountObject(dataSource, duplicate.Metadata.Id));
    }

    private static void VerifyExistingCalendarLifecycle(TaskPersistenceRuntime runtime, Guid organizationId, Guid actorId)
    {
        var store = runtime.CreateCalendarEventStore();
        var calendarEvent = CalendarEvent.Create(Guid.NewGuid(), organizationId, actorId, null, "Shared lifecycle", null,
            CalendarEventTiming.CreateAllDay(new DateOnly(2026, 9, 5), "Europe/Minsk"), Now);
        store.Add(calendarEvent);
        var archived = calendarEvent.Archive(actorId, Now.AddMinutes(1));
        store.Save(archived, 1);
        Assert.NotNull(runtime.CreateProductLifecycleStore().GetCurrentArchive(calendarEvent.Metadata.Id, organizationId));
        var trashed = archived.MoveToTrash(actorId, Now.AddMinutes(2));
        store.Save(trashed, 2);
        Assert.NotNull(runtime.CreateProductLifecycleStore().GetCurrentTrash(calendarEvent.Metadata.Id, organizationId));
    }

    private static SyncableEntityMetadata Metadata(Guid organizationId, Guid actorId) =>
        SyncableEntityMetadata.Create(Guid.NewGuid(), organizationId, actorId, Now);

    private static ContactSnapshot Contact(Guid organizationId, Guid actorId) =>
        new(Metadata(organizationId, actorId), "Иван", "Петров", null, "Иван Петров", "Контакт", ContactStatus.Active);

    private static CatalogItemSnapshot Catalog(Guid organizationId, Guid actorId, CatalogItemType kind) =>
        new(Metadata(organizationId, actorId), null, kind, "Catalog item", "Description",
            kind == CatalogItemType.TextNote ? "Note" : null,
            kind == CatalogItemType.WebLink ? "https://example.com/item" : null,
            null, null, null, null, 0);

    private static NotificationSnapshot Notification(Guid organizationId, Guid recipientId, Guid? sourceObjectId) =>
        new(Metadata(organizationId, recipientId), recipientId, "task.assigned", sourceObjectId, "Task assigned", "Notification body",
            NotificationSeverity.Info, NotificationStatus.Pending, Now, Now.AddDays(1), null, null, null, "dedup-1", "{\"action\":\"open\"}");

    private static UserSettingsSnapshot UserSettings(Guid organizationId, Guid actorId) =>
        new(actorId, organizationId, "ru-RU", "24h", 1, new TimeOnly(9, 0), new TimeOnly(18, 0),
            new short[] { 6, 7 }, 60, 15, true, true, true, "show_actions", "{}", Now, 1);

    private static NotificationPreferenceSnapshot Preference(Guid organizationId, Guid actorId) =>
        new(actorId, organizationId, "task.assigned", true, true, true, 15, null, null, null, Now, 1);

    private static int CountObject(NpgsqlDataSource dataSource, Guid id)
    {
        using var command = dataSource.CreateCommand("SELECT count(*) FROM core.objects WHERE id = $1;");
        command.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = id });
        return checked((int)(long)command.ExecuteScalar()!);
    }

    private static void Seed(NpgsqlDataSource dataSource, Guid organizationId, Guid userId)
    {
        var profileId = Guid.NewGuid();
        Execute(dataSource,
            "INSERT INTO core.organizations (id, code, name, default_time_zone) VALUES ($1, $2, 'Product tests', 'Europe/Minsk');",
            organizationId, organizationId.ToString("N"));
        Execute(dataSource,
            """
            INSERT INTO core.objects (id, organization_id, object_type, created_at, created_by, updated_at, updated_by)
            VALUES ($1, $3, 'employee_profile', $4, $2, $4, $2), ($2, $3, 'user_account', $4, $2, $4, $2);
            """, profileId, userId, organizationId, Now);
        Execute(dataSource,
            """
            INSERT INTO org.employee_profiles (id, organization_id, first_name, last_name, display_name, preferred_time_zone)
            VALUES ($1, $2, 'Test', 'User', 'Test User', 'Europe/Minsk');
            """, profileId, organizationId);
        Execute(dataSource,
            """
            INSERT INTO iam.user_accounts (id, organization_id, employee_profile_id, login, password_hash, password_parameters)
            VALUES ($1, $2, $3, $4, $5, '{}'::jsonb);
            """, userId, organizationId, profileId, userId.ToString("N"), new string('h', 64));
    }

    private static void Execute(NpgsqlDataSource dataSource, string sql, params object[] values)
    {
        using var command = dataSource.CreateCommand(sql);
        foreach (var value in values) command.Parameters.Add(new NpgsqlParameter { Value = value });
        command.ExecuteNonQuery();
    }

    private static void ApplyRuntimeGrants(NpgsqlDataSource dataSource, string runtimeRole)
    {
        var assembly = typeof(PostgresProductStoresTests).Assembly;
        var resourceName = assembly.GetManifestResourceNames().Single(name => name.EndsWith("grant-runtime.sql", StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(resourceName)!;
        using var reader = new StreamReader(stream);
        var sql = string.Join('\n', reader.ReadToEnd().Split('\n').Where(line => !line.TrimStart().StartsWith('\\')))
            .Replace("task_runtime", runtimeRole, StringComparison.Ordinal);
        Execute(dataSource, sql);
    }
}
