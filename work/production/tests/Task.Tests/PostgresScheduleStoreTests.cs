using Npgsql;
using Task.Application.Calendar;
using Task.Domain;
using Task.Domain.Calendar;
using Task.Infrastructure.Persistence;
using Xunit.Abstractions;

namespace Task.Tests;

public sealed class PostgresScheduleStoreTests
{
    private const string ConnectionEnvironmentVariable = "TASK_POSTGRES_TEST_ADMIN_CONNECTION";
    private readonly ITestOutputHelper _output;

    public PostgresScheduleStoreTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async global::System.Threading.Tasks.Task RealPostgres_ScheduleWindowTenantBoundaryFiltersAndDiCoverage()
    {
        var adminConnectionString = Environment.GetEnvironmentVariable(ConnectionEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(adminConnectionString))
        {
            _output.WriteLine(
                $"NOT RUN: set {ConnectionEnvironmentVariable} to execute the real PostgreSQL integration gate.");
            return;
        }

        var databaseName = $"task_schedule_{Guid.NewGuid():N}";
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
            SeedOrganization(dataSource, otherOrganizationId);

            await using var runtime = new TaskPersistenceRuntime(databaseConnection, TimeSpan.FromSeconds(10));
            var taskStore = runtime.CreateTaskStore();
            var calendarEventStore = runtime.CreateCalendarEventStore();
            var scheduleStore = runtime.CreateScheduleStore();
            var service = new ScheduleQueryService(scheduleStore);

            var creatorId = Guid.NewGuid();
            var editorId = Guid.NewGuid();
            var userId = Guid.NewGuid();
            var projectId = Guid.NewGuid();
            var created = new DateTimeOffset(2026, 8, 10, 8, 0, 0, TimeSpan.Zero);
            var fromUtc = new DateTimeOffset(2026, 8, 19, 10, 0, 0, TimeSpan.Zero);
            var toUtc = new DateTimeOffset(2026, 8, 21, 0, 0, 0, TimeSpan.Zero);

            var taskIntervalId = Guid.NewGuid();
            var taskIntervalBase = TaskAggregate.Create(taskIntervalId, organizationId, creatorId, "Interval task", created);
            taskStore.Add(taskIntervalBase);
            var taskInterval = taskIntervalBase.Reschedule(
                editorId,
                TaskSchedule.Create(
                    new DateTimeOffset(2026, 8, 20, 9, 0, 0, TimeSpan.Zero),
                    new DateTimeOffset(2026, 8, 20, 11, 0, 0, TimeSpan.Zero)),
                created.AddMinutes(1));
            taskStore.Save(taskInterval, 1);
            taskStore.Save(taskInterval.ChangePriority(editorId, TaskPriority.High, created.AddMinutes(2)), 2);

            var taskDeadlineId = Guid.NewGuid();
            var taskDeadlineBase = TaskAggregate.Create(taskDeadlineId, organizationId, creatorId, "Deadline task", created);
            taskStore.Add(taskDeadlineBase);
            taskStore.Save(
                taskDeadlineBase.Reschedule(
                    editorId,
                    TaskSchedule.Create(null, new DateTimeOffset(2026, 8, 20, 12, 0, 0, TimeSpan.Zero)),
                    created.AddMinutes(1)),
                1);

            var taskStartId = Guid.NewGuid();
            var taskStartBase = TaskAggregate.Create(taskStartId, organizationId, creatorId, "Start task", created);
            taskStore.Add(taskStartBase);
            taskStore.Save(
                taskStartBase.Reschedule(
                    editorId,
                    TaskSchedule.Create(new DateTimeOffset(2026, 8, 20, 7, 0, 0, TimeSpan.Zero), null),
                    created.AddMinutes(1)),
                1);

            var taskOutsideId = Guid.NewGuid();
            var taskOutsideBase = TaskAggregate.Create(taskOutsideId, organizationId, creatorId, "Outside task", created);
            taskStore.Add(taskOutsideBase);
            taskStore.Save(
                taskOutsideBase.Reschedule(
                    editorId,
                    TaskSchedule.Create(
                        new DateTimeOffset(2026, 8, 21, 1, 0, 0, TimeSpan.Zero),
                        new DateTimeOffset(2026, 8, 21, 2, 0, 0, TimeSpan.Zero)),
                    created.AddMinutes(1)),
                1);

            var taskZeroId = Guid.NewGuid();
            var taskZeroBase = TaskAggregate.Create(taskZeroId, organizationId, creatorId, "Zero task", created);
            taskStore.Add(taskZeroBase);
            taskStore.Save(
                taskZeroBase.Reschedule(
                    editorId,
                    TaskSchedule.Create(
                        new DateTimeOffset(2026, 8, 20, 9, 30, 0, TimeSpan.Zero),
                        new DateTimeOffset(2026, 8, 20, 9, 30, 0, TimeSpan.Zero)),
                    created.AddMinutes(1)),
                1);

            var cancelledTaskId = Guid.NewGuid();
            var cancelledTaskBase = TaskAggregate.Create(cancelledTaskId, organizationId, creatorId, "Cancelled task", created);
            taskStore.Add(cancelledTaskBase);
            var cancelledTask = cancelledTaskBase.Reschedule(
                editorId,
                TaskSchedule.Create(new DateTimeOffset(2026, 8, 20, 18, 0, 0, TimeSpan.Zero), null),
                created.AddMinutes(1));
            taskStore.Save(cancelledTask, 1);
            taskStore.Save(cancelledTask.Cancel(editorId, created.AddMinutes(2)), 2);

            var archivedTaskId = Guid.NewGuid();
            var archivedTaskBase = TaskAggregate.Create(archivedTaskId, organizationId, creatorId, "Archived task", created);
            taskStore.Add(archivedTaskBase);
            var archivedTask = archivedTaskBase.Reschedule(
                editorId,
                TaskSchedule.Create(null, new DateTimeOffset(2026, 8, 20, 15, 0, 0, TimeSpan.Zero)),
                created.AddMinutes(1));
            taskStore.Save(archivedTask, 1);
            var archivedStarted = archivedTask.Start(editorId, created.AddMinutes(2));
            taskStore.Save(archivedStarted, 2);
            var archivedCompleted = archivedStarted.Complete(editorId, created.AddMinutes(3));
            taskStore.Save(archivedCompleted, 3);
            taskStore.Save(archivedCompleted.Archive(editorId, created.AddMinutes(4)), 4);

            var timedEventId = Guid.NewGuid();
            calendarEventStore.Add(CalendarEvent.Create(
                timedEventId,
                organizationId,
                creatorId,
                null,
                "Timed event",
                null,
                CalendarEventTiming.CreateTimed(
                    new DateOnly(2026, 8, 20),
                    new DateTimeOffset(2026, 8, 20, 9, 0, 0, TimeSpan.Zero),
                    new DateTimeOffset(2026, 8, 20, 10, 30, 0, TimeSpan.Zero),
                    "Europe/Minsk"),
                created));

            var endAtFromEventId = Guid.NewGuid();
            calendarEventStore.Add(CalendarEvent.Create(
                endAtFromEventId,
                organizationId,
                creatorId,
                null,
                "End equals from",
                null,
                CalendarEventTiming.CreateTimed(
                    new DateOnly(2026, 8, 19),
                    new DateTimeOffset(2026, 8, 19, 8, 0, 0, TimeSpan.Zero),
                    new DateTimeOffset(2026, 8, 19, 10, 0, 0, TimeSpan.Zero),
                    "Europe/Minsk"),
                created));

            var startAtToEventId = Guid.NewGuid();
            calendarEventStore.Add(CalendarEvent.Create(
                startAtToEventId,
                organizationId,
                creatorId,
                null,
                "Start equals to",
                null,
                CalendarEventTiming.CreateTimed(
                    new DateOnly(2026, 8, 21),
                    new DateTimeOffset(2026, 8, 21, 0, 0, 0, TimeSpan.Zero),
                    new DateTimeOffset(2026, 8, 21, 1, 0, 0, TimeSpan.Zero),
                    "Europe/Minsk"),
                created));

            var allDayEventId = Guid.NewGuid();
            calendarEventStore.Add(CalendarEvent.Create(
                allDayEventId,
                organizationId,
                creatorId,
                null,
                "All day Kiritimati",
                null,
                CalendarEventTiming.CreateAllDay(new DateOnly(2026, 8, 20), "Pacific/Kiritimati"),
                created));

            var otherEventId = Guid.NewGuid();
            calendarEventStore.Add(CalendarEvent.Create(
                otherEventId,
                otherOrganizationId,
                creatorId,
                null,
                "Other organization event",
                null,
                CalendarEventTiming.CreateTimed(
                    new DateOnly(2026, 8, 20),
                    new DateTimeOffset(2026, 8, 20, 10, 0, 0, TimeSpan.Zero),
                    new DateTimeOffset(2026, 8, 20, 11, 0, 0, TimeSpan.Zero),
                    "Europe/Minsk"),
                created));

            var eventWithAttendeeId = Guid.NewGuid();
            calendarEventStore.Add(CalendarEvent.Create(
                eventWithAttendeeId,
                organizationId,
                creatorId,
                null,
                "With attendee",
                null,
                CalendarEventTiming.CreateTimed(
                    new DateOnly(2026, 8, 20),
                    new DateTimeOffset(2026, 8, 20, 13, 0, 0, TimeSpan.Zero),
                    new DateTimeOffset(2026, 8, 20, 14, 0, 0, TimeSpan.Zero),
                    "Europe/Minsk"),
                created,
                new[] { EventAttendee.Create(userId, CalendarAttendeeRole.Required, CalendarAttendeeResponseStatus.Accepted, created) },
                Array.Empty<ContactAttendee>()));

            var eventWithoutAttendeeId = Guid.NewGuid();
            calendarEventStore.Add(CalendarEvent.Create(
                eventWithoutAttendeeId,
                organizationId,
                creatorId,
                null,
                "Without attendee",
                null,
                CalendarEventTiming.CreateTimed(
                    new DateOnly(2026, 8, 20),
                    new DateTimeOffset(2026, 8, 20, 14, 0, 0, TimeSpan.Zero),
                    new DateTimeOffset(2026, 8, 20, 15, 0, 0, TimeSpan.Zero),
                    "Europe/Minsk"),
                created));

            var eventWithProjectId = Guid.NewGuid();
            calendarEventStore.Add(CalendarEvent.Create(
                eventWithProjectId,
                organizationId,
                creatorId,
                projectId,
                "With project",
                null,
                CalendarEventTiming.CreateTimed(
                    new DateOnly(2026, 8, 20),
                    new DateTimeOffset(2026, 8, 20, 15, 0, 0, TimeSpan.Zero),
                    new DateTimeOffset(2026, 8, 20, 16, 0, 0, TimeSpan.Zero),
                    "Europe/Minsk"),
                created));

            var eventWithoutProjectId = Guid.NewGuid();
            calendarEventStore.Add(CalendarEvent.Create(
                eventWithoutProjectId,
                organizationId,
                creatorId,
                null,
                "Without project",
                null,
                CalendarEventTiming.CreateTimed(
                    new DateOnly(2026, 8, 20),
                    new DateTimeOffset(2026, 8, 20, 16, 0, 0, TimeSpan.Zero),
                    new DateTimeOffset(2026, 8, 20, 17, 0, 0, TimeSpan.Zero),
                    "Europe/Minsk"),
                created));

            var cancelledEventId = Guid.NewGuid();
            var cancelledEventBase = CalendarEvent.Create(
                cancelledEventId,
                organizationId,
                creatorId,
                null,
                "Cancelled event",
                null,
                CalendarEventTiming.CreateTimed(
                    new DateOnly(2026, 8, 20),
                    new DateTimeOffset(2026, 8, 20, 17, 0, 0, TimeSpan.Zero),
                    new DateTimeOffset(2026, 8, 20, 18, 0, 0, TimeSpan.Zero),
                    "Europe/Minsk"),
                created);
            calendarEventStore.Add(cancelledEventBase);
            calendarEventStore.Save(cancelledEventBase.Cancel(editorId, created.AddMinutes(1)), 1);

            var archivedEventId = Guid.NewGuid();
            var archivedEventBase = CalendarEvent.Create(
                archivedEventId,
                organizationId,
                creatorId,
                null,
                "Archived event",
                null,
                CalendarEventTiming.CreateTimed(
                    new DateOnly(2026, 8, 20),
                    new DateTimeOffset(2026, 8, 20, 18, 30, 0, TimeSpan.Zero),
                    new DateTimeOffset(2026, 8, 20, 19, 0, 0, TimeSpan.Zero),
                    "Europe/Minsk"),
                created);
            calendarEventStore.Add(archivedEventBase);
            calendarEventStore.Save(archivedEventBase.Archive(editorId, created.AddMinutes(1)), 1);

            var trashedEventId = Guid.NewGuid();
            var trashedEventBase = CalendarEvent.Create(
                trashedEventId,
                organizationId,
                creatorId,
                null,
                "Trashed event",
                null,
                CalendarEventTiming.CreateTimed(
                    new DateOnly(2026, 8, 20),
                    new DateTimeOffset(2026, 8, 20, 19, 0, 0, TimeSpan.Zero),
                    new DateTimeOffset(2026, 8, 20, 20, 0, 0, TimeSpan.Zero),
                    "Europe/Minsk"),
                created);
            calendarEventStore.Add(trashedEventBase);
            calendarEventStore.Save(trashedEventBase.MoveToTrash(editorId, created.AddMinutes(1)), 1);

            var page = service.GetSchedule(organizationId, fromUtc, toUtc, null, null, null, null);

            Assert.Equal(
                new[]
                {
                    allDayEventId,
                    taskStartId,
                    taskIntervalId,
                    timedEventId,
                    taskDeadlineId,
                    eventWithAttendeeId,
                    eventWithoutAttendeeId,
                    eventWithProjectId,
                    eventWithoutProjectId,
                    cancelledEventId,
                    cancelledTaskId,
                },
                page.Items.Select(i => i.ObjectId).ToArray());
            Assert.Null(page.NextCursor);
            Assert.Equal(fromUtc, page.RangeStart);
            Assert.Equal(toUtc, page.RangeEnd);
            Assert.DoesNotContain(page.Items, i => i.ObjectId == taskOutsideId);
            Assert.DoesNotContain(page.Items, i => i.ObjectId == taskZeroId);
            Assert.DoesNotContain(page.Items, i => i.ObjectId == endAtFromEventId);
            Assert.DoesNotContain(page.Items, i => i.ObjectId == startAtToEventId);
            Assert.DoesNotContain(page.Items, i => i.ObjectId == otherEventId);
            Assert.DoesNotContain(page.Items, i => i.ObjectId == archivedTaskId);
            Assert.DoesNotContain(page.Items, i => i.ObjectId == archivedEventId);
            Assert.DoesNotContain(page.Items, i => i.ObjectId == trashedEventId);

            var allDay = Assert.Single(page.Items, i => i.ObjectId == allDayEventId);
            Assert.Equal(ScheduleItemType.CalendarEvent, allDay.ItemType);
            Assert.Equal(new DateOnly(2026, 8, 20), allDay.LocalDate);
            Assert.True(allDay.IsAllDay);
            Assert.Null(allDay.StartAtUtc);
            Assert.Null(allDay.EndAtUtc);
            Assert.Null(allDay.ProjectId);
            Assert.Equal("scheduled", allDay.Status);
            Assert.Null(allDay.Priority);

            var timed = Assert.Single(page.Items, i => i.ObjectId == timedEventId);
            Assert.Equal(new DateOnly(2026, 8, 20), timed.LocalDate);
            Assert.Equal(new DateTimeOffset(2026, 8, 20, 9, 0, 0, TimeSpan.Zero), timed.StartAtUtc);
            Assert.Equal(new DateTimeOffset(2026, 8, 20, 10, 30, 0, TimeSpan.Zero), timed.EndAtUtc);
            Assert.False(timed.IsAllDay);
            Assert.Null(timed.ProjectId);

            var intervalTask = Assert.Single(page.Items, i => i.ObjectId == taskIntervalId);
            Assert.Equal(ScheduleItemType.Task, intervalTask.ItemType);
            Assert.Equal(new DateOnly(2026, 8, 20), intervalTask.LocalDate);
            Assert.Equal(new DateTimeOffset(2026, 8, 20, 9, 0, 0, TimeSpan.Zero), intervalTask.StartAtUtc);
            Assert.Equal(new DateTimeOffset(2026, 8, 20, 11, 0, 0, TimeSpan.Zero), intervalTask.EndAtUtc);
            Assert.Equal(ScheduleItemPriority.High, intervalTask.Priority);
            Assert.Equal("new", intervalTask.Status);
            Assert.False(intervalTask.IsAllDay);
            Assert.Null(intervalTask.ProjectId);

            Assert.Equal("cancelled", Assert.Single(page.Items, i => i.ObjectId == cancelledTaskId).Status);
            Assert.Equal(projectId, Assert.Single(page.Items, i => i.ObjectId == eventWithProjectId).ProjectId);
            Assert.Equal("cancelled", Assert.Single(page.Items, i => i.ObjectId == cancelledEventId).Status);

            var otherOrgPage = service.GetSchedule(otherOrganizationId, fromUtc, toUtc, null, null, null, null);
            var otherItem = Assert.Single(otherOrgPage.Items);
            Assert.Equal(otherEventId, otherItem.ObjectId);

            var usersPage = service.GetSchedule(organizationId, fromUtc, toUtc, null, new[] { userId }, null, null);
            Assert.Equal(
                new[] { taskStartId, taskIntervalId, taskDeadlineId, eventWithAttendeeId, cancelledTaskId },
                usersPage.Items.Select(i => i.ObjectId).ToArray());

            var projectsPage = service.GetSchedule(organizationId, fromUtc, toUtc, null, null, new[] { projectId }, null);
            Assert.Equal(
                new[] { taskStartId, taskIntervalId, taskDeadlineId, eventWithProjectId, cancelledTaskId },
                projectsPage.Items.Select(i => i.ObjectId).ToArray());

            var statusPage = service.GetSchedule(organizationId, fromUtc, toUtc, null, null, null, "cancelled");
            Assert.Equal(
                new[] { cancelledEventId, cancelledTaskId },
                statusPage.Items.Select(i => i.ObjectId).ToArray());

            var emptyFiltersPage = service.GetSchedule(organizationId, fromUtc, toUtc, null, Array.Empty<Guid>(), Array.Empty<Guid>(), null);
            Assert.Equal(page.Items.Select(i => i.ObjectId), emptyFiltersPage.Items.Select(i => i.ObjectId));
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
