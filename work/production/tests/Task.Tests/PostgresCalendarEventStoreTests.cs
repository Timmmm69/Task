using Npgsql;
using Task.Application.Calendar;
using Task.Domain;
using Task.Domain.Calendar;
using Task.Infrastructure.Persistence;
using Xunit.Abstractions;

namespace Task.Tests;

public sealed class PostgresCalendarEventStoreTests
{
    private const string ConnectionEnvironmentVariable = "TASK_POSTGRES_TEST_ADMIN_CONNECTION";
    private readonly ITestOutputHelper _output;

    public PostgresCalendarEventStoreTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async global::System.Threading.Tasks.Task RealPostgres_CalendarEventRoundTripTenantBoundaryAndConcurrency()
    {
        var adminConnectionString = Environment.GetEnvironmentVariable(ConnectionEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(adminConnectionString))
        {
            _output.WriteLine(
                $"NOT RUN: set {ConnectionEnvironmentVariable} to execute the real PostgreSQL integration gate.");
            return;
        }

        var databaseName = $"task_calendar_{Guid.NewGuid():N}";
        using var adminDataSource = NpgsqlDataSource.Create(adminConnectionString);
        CreateDatabase(adminDataSource, databaseName);

        try
        {
            var databaseConnection = new NpgsqlConnectionStringBuilder(adminConnectionString)
            {
                Database = databaseName,
            }.ConnectionString;

            using var dataSource = NpgsqlDataSource.Create(databaseConnection);
            new TaskPersistenceMigrator(dataSource).ApplyPending();

            var organizationId = Guid.NewGuid();
            var otherOrganizationId = Guid.NewGuid();
            SeedOrganization(dataSource, organizationId);

            await using var runtime = new TaskPersistenceRuntime(databaseConnection, TimeSpan.FromSeconds(10));
            var store = runtime.CreateCalendarEventStore();

            var eventId = Guid.NewGuid();
            var creatorId = Guid.NewGuid();
            var editorId = Guid.NewGuid();
            var projectId = Guid.NewGuid();
            var createdAt = new DateTimeOffset(2026, 8, 15, 10, 0, 0, TimeSpan.Zero);

            Assert.Throws<ArgumentException>(() => store.Get(Guid.Empty, organizationId));
            Assert.Throws<ArgumentException>(() => store.Get(eventId, Guid.Empty));
            Assert.Null(store.Get(eventId, organizationId));

            var cancelledVersionOne = CalendarEvent.Reconstitute(
                SyncableEntityMetadata.Reconstitute(
                    Guid.NewGuid(),
                    organizationId,
                    creatorId,
                    createdAt,
                    creatorId,
                    createdAt,
                    1,
                    EntityLifecycleState.Active,
                    null,
                    null,
                    null,
                    null),
                null,
                "Invalid initial state",
                null,
                CalendarEventTiming.CreateAllDay(new DateOnly(2026, 8, 16), "Europe/Minsk"),
                CalendarEventStatus.Cancelled,
                Array.Empty<EventAttendee>(),
                Array.Empty<ContactAttendee>());
            Assert.Throws<ArgumentException>(() => store.Add(cancelledVersionOne));

            var timing = CalendarEventTiming.CreateTimed(
                new DateOnly(2026, 8, 20),
                new DateTimeOffset(2026, 8, 20, 9, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 8, 20, 10, 0, 0, TimeSpan.Zero),
                "Europe/Minsk");
            var firstUserAttendee = EventAttendee.Create(
                Guid.NewGuid(), CalendarAttendeeRole.Required, CalendarAttendeeResponseStatus.Accepted, createdAt);
            var secondUserAttendee = EventAttendee.Create(
                Guid.NewGuid(), CalendarAttendeeRole.Observer, CalendarAttendeeResponseStatus.Pending, null);
            var firstContactAttendee = ContactAttendee.Create(
                Guid.NewGuid(), CalendarAttendeeRole.Optional, CalendarAttendeeResponseStatus.Declined, createdAt.AddHours(1));
            var secondContactAttendee = ContactAttendee.Create(
                Guid.NewGuid(), CalendarAttendeeRole.Required, CalendarAttendeeResponseStatus.Tentative, null);

            var original = CalendarEvent.Create(
                eventId,
                organizationId,
                creatorId,
                projectId,
                "Persist me",
                "Round trip description",
                timing,
                createdAt,
                new[] { firstUserAttendee, secondUserAttendee },
                new[] { firstContactAttendee, secondContactAttendee });

            store.Add(original);
            Assert.Null(store.Get(eventId, otherOrganizationId));

            var loaded = store.Get(eventId, organizationId);
            Assert.NotNull(loaded);
            Assert.Equal(1, loaded.Metadata.Version);
            Assert.Equal("Persist me", loaded.Title);
            Assert.Equal("Round trip description", loaded.Description);
            Assert.Equal(projectId, loaded.ProjectId);
            Assert.Equal(timing, loaded.Timing);
            Assert.Equal(CalendarEventStatus.Scheduled, loaded.Status);
            Assert.Equal(2, loaded.UserAttendees.Count);
            Assert.Equal(firstUserAttendee, loaded.UserAttendees[0]);
            Assert.Equal(secondUserAttendee, loaded.UserAttendees[1]);
            Assert.Equal(2, loaded.ContactAttendees.Count);
            Assert.Equal(firstContactAttendee, loaded.ContactAttendees[0]);
            Assert.Equal(secondContactAttendee, loaded.ContactAttendees[1]);

            var allDayEventId = Guid.NewGuid();
            var allDayTiming = CalendarEventTiming.CreateAllDay(new DateOnly(2026, 8, 25), "Europe/Minsk");
            store.Add(CalendarEvent.Create(
                allDayEventId, organizationId, creatorId, null, "All day", null, allDayTiming, createdAt));
            var allDayLoaded = store.Get(allDayEventId, organizationId);
            Assert.NotNull(allDayLoaded);
            Assert.True(allDayLoaded.Timing.IsAllDay);
            Assert.Null(allDayLoaded.Timing.StartAtUtc);
            Assert.Null(allDayLoaded.Timing.EndAtUtc);
            Assert.Equal(new DateOnly(2026, 8, 25), allDayLoaded.Timing.EventDate);
            Assert.Equal("Europe/Minsk", allDayLoaded.Timing.TimeZoneId);
            Assert.Null(allDayLoaded.ProjectId);
            Assert.Null(allDayLoaded.Description);

            var wrongArithmetic = CalendarEvent.Reconstitute(
                SyncableEntityMetadata.Reconstitute(
                    eventId,
                    organizationId,
                    creatorId,
                    createdAt,
                    editorId,
                    createdAt.AddMinutes(1),
                    3,
                    EntityLifecycleState.Active,
                    null,
                    null,
                    null,
                    null),
                projectId,
                "Persist me",
                "Round trip description",
                timing,
                CalendarEventStatus.Scheduled,
                new[] { firstUserAttendee, secondUserAttendee },
                new[] { firstContactAttendee, secondContactAttendee });
            Assert.Throws<ArgumentException>(() => store.Save(wrongArithmetic, 1));
            Assert.Equal(1, store.Get(eventId, organizationId)!.Metadata.Version);

            var updatedTiming = CalendarEventTiming.CreateTimed(
                new DateOnly(2026, 8, 21),
                new DateTimeOffset(2026, 8, 21, 9, 30, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 8, 21, 11, 0, 0, TimeSpan.Zero),
                "Europe/Minsk");
            var updated = loaded.UpdateDetails(
                editorId,
                createdAt.AddMinutes(2),
                projectId,
                "Updated title",
                "Updated description",
                updatedTiming);
            store.Save(updated, 1);

            var reloaded = store.Get(eventId, organizationId);
            Assert.NotNull(reloaded);
            Assert.Equal(2, reloaded.Metadata.Version);
            Assert.Equal("Updated title", reloaded.Title);
            Assert.Equal("Updated description", reloaded.Description);
            Assert.Equal(updatedTiming, reloaded.Timing);

            var unchanged = reloaded.UpdateDetails(
                editorId,
                createdAt.AddMinutes(3),
                projectId,
                "Updated title",
                "Updated description",
                reloaded.Timing);
            Assert.Same(reloaded, unchanged);
            Assert.Equal(2, unchanged.Metadata.Version);
            Assert.Equal(2, store.Get(eventId, organizationId)!.Metadata.Version);

            var stale = loaded.UpdateDetails(
                editorId,
                createdAt.AddMinutes(4),
                projectId,
                "Stale title",
                "Stale description",
                timing);
            var conflict = Assert.Throws<CalendarEventConcurrencyException>(() => store.Save(stale, 1));
            Assert.Equal(1, conflict.ExpectedVersion);
            Assert.Equal(2, conflict.ActualVersion);

            var missing = CalendarEvent.Reconstitute(
                SyncableEntityMetadata.Reconstitute(
                    Guid.NewGuid(),
                    organizationId,
                    creatorId,
                    createdAt,
                    editorId,
                    createdAt.AddMinutes(1),
                    2,
                    EntityLifecycleState.Active,
                    null,
                    null,
                    null,
                    null),
                projectId,
                "Missing",
                null,
                timing,
                CalendarEventStatus.Scheduled,
                Array.Empty<EventAttendee>(),
                Array.Empty<ContactAttendee>());
            var missingException = Assert.Throws<KeyNotFoundException>(() => store.Save(missing, 1));
            Assert.Contains("Calendar event", missingException.Message, StringComparison.Ordinal);

            var cancelled = reloaded.Cancel(editorId, createdAt.AddMinutes(5));
            store.Save(cancelled, 2);
            var afterCancel = store.Get(eventId, organizationId);
            Assert.NotNull(afterCancel);
            Assert.Equal(3, afterCancel.Metadata.Version);
            Assert.Equal(CalendarEventStatus.Cancelled, afterCancel.Status);

            var rescheduled = afterCancel.Reschedule(editorId, createdAt.AddMinutes(6));
            store.Save(rescheduled, 3);
            var afterReschedule = store.Get(eventId, organizationId);
            Assert.NotNull(afterReschedule);
            Assert.Equal(4, afterReschedule.Metadata.Version);
            Assert.Equal(CalendarEventStatus.Scheduled, afterReschedule.Status);
            Assert.Equal("Updated title", afterReschedule.Title);

            var archived = afterReschedule.Archive(editorId, createdAt.AddMinutes(7));
            store.Save(archived, 4);
            var afterArchive = store.Get(eventId, organizationId);
            Assert.NotNull(afterArchive);
            Assert.Equal(5, afterArchive.Metadata.Version);
            Assert.Equal(EntityLifecycleState.Archived, afterArchive.Metadata.LifecycleState);
            Assert.Equal(createdAt.AddMinutes(7), afterArchive.Metadata.ArchivedAtUtc);
            Assert.Null(afterArchive.Metadata.DeletedAtUtc);
            Assert.Null(afterArchive.Metadata.DeletedBy);

            var restoredFromArchive = afterArchive.RestoreFromArchive(editorId, createdAt.AddMinutes(8));
            store.Save(restoredFromArchive, 5);
            var afterRestore = store.Get(eventId, organizationId);
            Assert.NotNull(afterRestore);
            Assert.Equal(6, afterRestore.Metadata.Version);
            Assert.Equal(EntityLifecycleState.Active, afterRestore.Metadata.LifecycleState);
            Assert.Null(afterRestore.Metadata.ArchivedAtUtc);

            var trashed = afterRestore.MoveToTrash(editorId, createdAt.AddMinutes(9));
            store.Save(trashed, 6);
            var afterTrash = store.Get(eventId, organizationId);
            Assert.NotNull(afterTrash);
            Assert.Equal(7, afterTrash.Metadata.Version);
            Assert.Equal(EntityLifecycleState.Trashed, afterTrash.Metadata.LifecycleState);
            Assert.Equal(EntityLifecycleState.Active, afterTrash.Metadata.LifecycleStateBeforeTrash);
            Assert.Equal(createdAt.AddMinutes(9), afterTrash.Metadata.DeletedAtUtc);
            Assert.Equal(editorId, afterTrash.Metadata.DeletedBy);

            var restoredFromTrash = afterTrash.RestoreFromTrash(editorId, createdAt.AddMinutes(10));
            store.Save(restoredFromTrash, 7);
            var afterRestoreFromTrash = store.Get(eventId, organizationId);
            Assert.NotNull(afterRestoreFromTrash);
            Assert.Equal(8, afterRestoreFromTrash.Metadata.Version);
            Assert.Equal(EntityLifecycleState.Active, afterRestoreFromTrash.Metadata.LifecycleState);
            Assert.Null(afterRestoreFromTrash.Metadata.LifecycleStateBeforeTrash);
            Assert.Null(afterRestoreFromTrash.Metadata.DeletedAtUtc);
            Assert.Null(afterRestoreFromTrash.Metadata.DeletedBy);
            Assert.Equal(CalendarEventStatus.Scheduled, afterRestoreFromTrash.Status);
            Assert.Equal("Updated title", afterRestoreFromTrash.Title);
            Assert.Equal("Updated description", afterRestoreFromTrash.Description);
            Assert.Equal(updatedTiming, afterRestoreFromTrash.Timing);
            Assert.Equal(2, afterRestoreFromTrash.UserAttendees.Count);
            Assert.Equal(2, afterRestoreFromTrash.ContactAttendees.Count);

            Assert.Throws<ArgumentOutOfRangeException>(() => store.Save(afterRestoreFromTrash, 0));
            Assert.Equal(8, store.Get(eventId, organizationId)!.Metadata.Version);

            var replacementUsers = new[]
            {
                EventAttendee.Create(
                    Guid.NewGuid(), CalendarAttendeeRole.Required, CalendarAttendeeResponseStatus.Accepted, createdAt.AddHours(2)),
                EventAttendee.Create(
                    Guid.NewGuid(), CalendarAttendeeRole.Observer, CalendarAttendeeResponseStatus.Pending, null),
                EventAttendee.Create(
                    Guid.NewGuid(), CalendarAttendeeRole.Optional, CalendarAttendeeResponseStatus.Declined, createdAt.AddHours(3)),
            };
            var replacementContacts = new[]
            {
                ContactAttendee.Create(
                    Guid.NewGuid(), CalendarAttendeeRole.Required, CalendarAttendeeResponseStatus.Tentative, null),
                ContactAttendee.Create(
                    Guid.NewGuid(), CalendarAttendeeRole.Observer, CalendarAttendeeResponseStatus.Accepted, createdAt.AddHours(4)),
            };
            var replaced = afterRestoreFromTrash.ReplaceAttendees(
                editorId, createdAt.AddMinutes(11), replacementUsers, replacementContacts);
            store.Save(replaced, 8);
            var afterReplace = store.Get(eventId, organizationId);
            Assert.NotNull(afterReplace);
            Assert.Equal(9, afterReplace.Metadata.Version);
            Assert.Equal(replacementUsers, afterReplace.UserAttendees);
            Assert.Equal(replacementContacts, afterReplace.ContactAttendees);

            using (var userCount = dataSource.CreateCommand(
                "SELECT count(*) FROM calendar.event_user_attendees WHERE organization_id = $1 AND event_id = $2;"))
            {
                userCount.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = organizationId });
                userCount.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = eventId });
                Assert.Equal(3L, (long)userCount.ExecuteScalar()!);
            }

            using (var contactCount = dataSource.CreateCommand(
                "SELECT count(*) FROM calendar.event_contact_attendees WHERE organization_id = $1 AND event_id = $2;"))
            {
                contactCount.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = organizationId });
                contactCount.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = eventId });
                Assert.Equal(2L, (long)contactCount.ExecuteScalar()!);
            }
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            DropDatabase(adminDataSource, databaseName);
        }
    }

    private static void CreateDatabase(NpgsqlDataSource adminDataSource, string databaseName)
    {
        using var command = adminDataSource.CreateCommand($"CREATE DATABASE {databaseName};");
        command.ExecuteNonQuery();
    }

    private static void DropDatabase(NpgsqlDataSource adminDataSource, string databaseName)
    {
        using var command = adminDataSource.CreateCommand($"DROP DATABASE IF EXISTS {databaseName} WITH (FORCE);");
        command.ExecuteNonQuery();
    }

    private static void SeedOrganization(NpgsqlDataSource dataSource, Guid organizationId)
    {
        using var command = dataSource.CreateCommand(
            """
            INSERT INTO core.organizations (id, code, name, default_time_zone)
            VALUES ($1, $2, $3, $4);
            """);
        command.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = organizationId });
        command.Parameters.Add(new NpgsqlParameter<string> { TypedValue = $"org-{organizationId:N}" });
        command.Parameters.Add(new NpgsqlParameter<string> { TypedValue = "Integration Test Organization" });
        command.Parameters.Add(new NpgsqlParameter<string> { TypedValue = "Europe/Minsk" });
        command.ExecuteNonQuery();
    }
}
