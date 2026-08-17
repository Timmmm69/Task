using Npgsql;
using NpgsqlTypes;
using Task.Application.Calendar;
using Task.Domain;
using Task.Domain.Calendar;

namespace Task.Infrastructure.Persistence;

public sealed class PostgresCalendarEventStore : ICalendarEventStore
{
    private readonly NpgsqlDataSource _dataSource;

    public PostgresCalendarEventStore(NpgsqlDataSource dataSource)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        _dataSource = dataSource;
    }

    public CalendarEvent? Get(Guid eventId, Guid organizationId)
    {
        EnsureIdentifier(eventId, nameof(eventId));
        EnsureIdentifier(organizationId, nameof(organizationId));

        using var command = _dataSource.CreateCommand(
            """
            SELECT
                o.id,
                o.organization_id,
                o.created_by,
                o.created_at,
                o.updated_by,
                o.updated_at,
                o.version,
                o.lifecycle_state,
                o.lifecycle_state_before_trash,
                o.deleted_at,
                o.deleted_by,
                o.archived_at,
                e.project_id,
                e.title,
                e.description,
                e.event_date,
                e.is_all_day,
                e.start_at_utc,
                e.end_at_utc,
                e.time_zone_id,
                e.status
            FROM core.objects AS o
            INNER JOIN calendar.events AS e
                ON e.organization_id = o.organization_id AND e.id = o.id
            WHERE o.organization_id = $1 AND o.id = $2 AND o.object_type = 'calendar_event';
            """);
        command.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = organizationId });
        command.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = eventId });

        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return null;
        }

        var metadata = ReadMetadata(reader);
        var projectId = reader.IsDBNull(12) ? null : (Guid?)reader.GetGuid(12);
        var title = reader.GetString(13);
        var description = reader.IsDBNull(14) ? null : reader.GetString(14);
        var timing = CalendarEventTiming.Create(
            reader.GetFieldValue<DateOnly>(15),
            reader.GetBoolean(16),
            ReadNullableTimestamp(reader, 17),
            ReadNullableTimestamp(reader, 18),
            reader.GetString(19));
        var status = ParseEventStatus(reader.GetString(20));
        reader.Close();

        var userAttendees = ReadUserAttendees(organizationId, eventId);
        var contactAttendees = ReadContactAttendees(organizationId, eventId);

        return CalendarEvent.Reconstitute(
            metadata,
            projectId,
            title,
            description,
            timing,
            status,
            userAttendees,
            contactAttendees);
    }

    public void Add(CalendarEvent calendarEvent)
    {
        ArgumentNullException.ThrowIfNull(calendarEvent);
        if (calendarEvent.Metadata.Version != 1 ||
            calendarEvent.Metadata.LifecycleState != EntityLifecycleState.Active ||
            calendarEvent.Metadata.CreatedAtUtc != calendarEvent.Metadata.UpdatedAtUtc ||
            calendarEvent.Metadata.CreatedBy != calendarEvent.Metadata.UpdatedBy ||
            calendarEvent.Status != CalendarEventStatus.Scheduled)
        {
            throw new ArgumentException(
                "A new calendar event must be in its initial version-1 aggregate state.",
                nameof(calendarEvent));
        }

        using var connection = _dataSource.OpenConnection();
        using var transaction = connection.BeginTransaction();

        using (var objectCommand = new NpgsqlCommand(
            """
            INSERT INTO core.objects (
                id, organization_id, object_type, lifecycle_state, lifecycle_state_before_trash,
                version, created_at, created_by, updated_at, updated_by,
                archived_at, deleted_at, deleted_by)
            VALUES (
                $1, $2, 'calendar_event', $3, $4, $5, $6, $7, $8, $9, $10, $11, $12);
            """,
            connection,
            transaction))
        {
            AddMetadataParameters(objectCommand, calendarEvent.Metadata, includeIdentity: true);
            objectCommand.ExecuteNonQuery();
        }

        using (var eventCommand = new NpgsqlCommand(
            """
            INSERT INTO calendar.events (
                id, organization_id, project_id, title, description, event_date,
                is_all_day, start_at_utc, end_at_utc, time_zone_id, status)
            VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10, $11);
            """,
            connection,
            transaction))
        {
            AddEventParameters(eventCommand, calendarEvent, includeIdentity: true);
            eventCommand.ExecuteNonQuery();
        }

        InsertUserAttendees(connection, transaction, calendarEvent);
        InsertContactAttendees(connection, transaction, calendarEvent);

        transaction.Commit();
    }

    public void Save(CalendarEvent calendarEvent, int expectedVersion)
    {
        ArgumentNullException.ThrowIfNull(calendarEvent);
        if (expectedVersion < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(expectedVersion), "Expected version must be positive.");
        }

        if (calendarEvent.Metadata.Version != checked(expectedVersion + 1))
        {
            throw new ArgumentException(
                "The saved aggregate version must be exactly one greater than the expected version.",
                nameof(calendarEvent));
        }

        using var connection = _dataSource.OpenConnection();
        using var transaction = connection.BeginTransaction();
        using (var command = new NpgsqlCommand(
            """
            WITH updated_object AS (
                UPDATE core.objects
                SET lifecycle_state = $3,
                    lifecycle_state_before_trash = $4,
                    version = $5,
                    updated_at = $6,
                    updated_by = $7,
                    archived_at = $8,
                    deleted_at = $9,
                    deleted_by = $10
                WHERE organization_id = $1 AND id = $2 AND object_type = 'calendar_event' AND version = $11
                RETURNING organization_id, id
            ),
            updated_event AS (
                UPDATE calendar.events AS e
                SET project_id = $12,
                    title = $13,
                    description = $14,
                    event_date = $15,
                    is_all_day = $16,
                    start_at_utc = $17,
                    end_at_utc = $18,
                    time_zone_id = $19,
                    status = $20
                FROM updated_object AS o
                WHERE e.organization_id = o.organization_id AND e.id = o.id
                RETURNING e.id
            )
            SELECT EXISTS (SELECT 1 FROM updated_object), EXISTS (SELECT 1 FROM updated_event);
            """,
            connection,
            transaction))
        {
            AddSaveParameters(command, calendarEvent, expectedVersion);
            using var reader = command.ExecuteReader();
            reader.Read();
            var objectUpdated = reader.GetBoolean(0);
            var eventUpdated = reader.GetBoolean(1);
            reader.Close();

            if (!objectUpdated)
            {
                transaction.Rollback();
                ThrowMissingOrConcurrency(calendarEvent.Metadata.OrganizationId, calendarEvent.Metadata.Id, expectedVersion);
            }

            if (!eventUpdated)
            {
                transaction.Rollback();
                throw new InvalidOperationException(
                    $"Persistence corruption: calendar event row '{calendarEvent.Metadata.Id}' is missing for its core object.");
            }
        }

        using (var deleteUserAttendeesCommand = new NpgsqlCommand(
            "DELETE FROM calendar.event_user_attendees WHERE organization_id = $1 AND event_id = $2;",
            connection,
            transaction))
        {
            deleteUserAttendeesCommand.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = calendarEvent.Metadata.OrganizationId });
            deleteUserAttendeesCommand.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = calendarEvent.Metadata.Id });
            deleteUserAttendeesCommand.ExecuteNonQuery();
        }

        using (var deleteContactAttendeesCommand = new NpgsqlCommand(
            "DELETE FROM calendar.event_contact_attendees WHERE organization_id = $1 AND event_id = $2;",
            connection,
            transaction))
        {
            deleteContactAttendeesCommand.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = calendarEvent.Metadata.OrganizationId });
            deleteContactAttendeesCommand.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = calendarEvent.Metadata.Id });
            deleteContactAttendeesCommand.ExecuteNonQuery();
        }

        InsertUserAttendees(connection, transaction, calendarEvent);
        InsertContactAttendees(connection, transaction, calendarEvent);

        transaction.Commit();
    }

    private void ThrowMissingOrConcurrency(Guid organizationId, Guid eventId, int expectedVersion)
    {
        using var command = _dataSource.CreateCommand(
            """
            SELECT version
            FROM core.objects
            WHERE organization_id = $1 AND id = $2 AND object_type = 'calendar_event';
            """);
        command.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = organizationId });
        command.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = eventId });
        var actual = command.ExecuteScalar();
        if (actual is null)
        {
            throw new KeyNotFoundException(
                $"Calendar event '{eventId}' was not found in organization '{organizationId}'.");
        }

        throw new CalendarEventConcurrencyException(eventId, expectedVersion, checked((int)(long)actual));
    }

    private static SyncableEntityMetadata ReadMetadata(NpgsqlDataReader reader)
    {
        var version = reader.GetInt64(6);
        if (version > int.MaxValue)
        {
            throw new InvalidOperationException("Stored calendar event version exceeds the supported domain range.");
        }

        return SyncableEntityMetadata.Reconstitute(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetGuid(2),
            reader.GetFieldValue<DateTimeOffset>(3),
            reader.GetGuid(4),
            reader.GetFieldValue<DateTimeOffset>(5),
            (int)version,
            ParseLifecycle(reader.GetString(7)),
            reader.IsDBNull(8) ? null : ParseLifecycle(reader.GetString(8)),
            ReadNullableTimestamp(reader, 9),
            reader.IsDBNull(10) ? null : reader.GetGuid(10),
            ReadNullableTimestamp(reader, 11));
    }

    private IReadOnlyList<EventAttendee> ReadUserAttendees(Guid organizationId, Guid eventId)
    {
        using var command = _dataSource.CreateCommand(
            """
            SELECT user_account_id, role, response_status, responded_at
            FROM calendar.event_user_attendees
            WHERE organization_id = $1 AND event_id = $2
            ORDER BY position;
            """);
        command.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = organizationId });
        command.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = eventId });

        var attendees = new List<EventAttendee>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            attendees.Add(EventAttendee.Create(
                reader.GetGuid(0),
                ParseRole(reader.GetString(1)),
                ParseResponseStatus(reader.GetString(2)),
                ReadNullableTimestamp(reader, 3)));
        }

        return attendees;
    }

    private IReadOnlyList<ContactAttendee> ReadContactAttendees(Guid organizationId, Guid eventId)
    {
        using var command = _dataSource.CreateCommand(
            """
            SELECT contact_id, role, response_status, responded_at
            FROM calendar.event_contact_attendees
            WHERE organization_id = $1 AND event_id = $2
            ORDER BY position;
            """);
        command.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = organizationId });
        command.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = eventId });

        var attendees = new List<ContactAttendee>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            attendees.Add(ContactAttendee.Create(
                reader.GetGuid(0),
                ParseRole(reader.GetString(1)),
                ParseResponseStatus(reader.GetString(2)),
                ReadNullableTimestamp(reader, 3)));
        }

        return attendees;
    }

    private static void AddMetadataParameters(
        NpgsqlCommand command,
        SyncableEntityMetadata metadata,
        bool includeIdentity)
    {
        if (includeIdentity)
        {
            command.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = metadata.Id });
            command.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = metadata.OrganizationId });
        }

        command.Parameters.Add(new NpgsqlParameter<string> { TypedValue = ToDatabase(metadata.LifecycleState) });
        AddNullableText(command, metadata.LifecycleStateBeforeTrash is null
            ? null
            : ToDatabase(metadata.LifecycleStateBeforeTrash.Value));
        command.Parameters.Add(new NpgsqlParameter<long> { TypedValue = metadata.Version });
        command.Parameters.Add(new NpgsqlParameter<DateTimeOffset> { TypedValue = metadata.CreatedAtUtc });
        command.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = metadata.CreatedBy });
        command.Parameters.Add(new NpgsqlParameter<DateTimeOffset> { TypedValue = metadata.UpdatedAtUtc });
        command.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = metadata.UpdatedBy });
        AddNullableTimestamp(command, metadata.ArchivedAtUtc);
        AddNullableTimestamp(command, metadata.DeletedAtUtc);
        AddNullableGuid(command, metadata.DeletedBy);
    }

    private static void AddEventParameters(
        NpgsqlCommand command,
        CalendarEvent calendarEvent,
        bool includeIdentity)
    {
        if (includeIdentity)
        {
            command.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = calendarEvent.Metadata.Id });
            command.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = calendarEvent.Metadata.OrganizationId });
        }

        AddNullableGuid(command, calendarEvent.ProjectId);
        command.Parameters.Add(new NpgsqlParameter<string> { TypedValue = calendarEvent.Title });
        AddNullableText(command, calendarEvent.Description);
        command.Parameters.Add(new NpgsqlParameter<DateOnly> { TypedValue = calendarEvent.Timing.EventDate });
        command.Parameters.Add(new NpgsqlParameter<bool> { TypedValue = calendarEvent.Timing.IsAllDay });
        AddNullableTimestamp(command, calendarEvent.Timing.StartAtUtc);
        AddNullableTimestamp(command, calendarEvent.Timing.EndAtUtc);
        command.Parameters.Add(new NpgsqlParameter<string> { TypedValue = calendarEvent.Timing.TimeZoneId });
        command.Parameters.Add(new NpgsqlParameter<string> { TypedValue = ToDatabase(calendarEvent.Status) });
    }

    private static void AddSaveParameters(NpgsqlCommand command, CalendarEvent calendarEvent, int expectedVersion)
    {
        command.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = calendarEvent.Metadata.OrganizationId });
        command.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = calendarEvent.Metadata.Id });
        command.Parameters.Add(new NpgsqlParameter<string> { TypedValue = ToDatabase(calendarEvent.Metadata.LifecycleState) });
        AddNullableText(command, calendarEvent.Metadata.LifecycleStateBeforeTrash is null
            ? null
            : ToDatabase(calendarEvent.Metadata.LifecycleStateBeforeTrash.Value));
        command.Parameters.Add(new NpgsqlParameter<long> { TypedValue = calendarEvent.Metadata.Version });
        command.Parameters.Add(new NpgsqlParameter<DateTimeOffset> { TypedValue = calendarEvent.Metadata.UpdatedAtUtc });
        command.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = calendarEvent.Metadata.UpdatedBy });
        AddNullableTimestamp(command, calendarEvent.Metadata.ArchivedAtUtc);
        AddNullableTimestamp(command, calendarEvent.Metadata.DeletedAtUtc);
        AddNullableGuid(command, calendarEvent.Metadata.DeletedBy);
        command.Parameters.Add(new NpgsqlParameter<long> { TypedValue = expectedVersion });
        AddEventParameters(command, calendarEvent, includeIdentity: false);
    }

    private static void InsertUserAttendees(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CalendarEvent calendarEvent)
    {
        for (var position = 0; position < calendarEvent.UserAttendees.Count; position++)
        {
            var attendee = calendarEvent.UserAttendees[position];
            using var command = new NpgsqlCommand(
                """
                INSERT INTO calendar.event_user_attendees (
                    event_id, organization_id, position, user_account_id, role, response_status, responded_at)
                VALUES ($1, $2, $3, $4, $5, $6, $7);
                """,
                connection,
                transaction);
            command.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = calendarEvent.Metadata.Id });
            command.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = calendarEvent.Metadata.OrganizationId });
            command.Parameters.Add(new NpgsqlParameter<short> { TypedValue = (short)position });
            command.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = attendee.UserAccountId });
            command.Parameters.Add(new NpgsqlParameter<string> { TypedValue = ToDatabase(attendee.Role) });
            command.Parameters.Add(new NpgsqlParameter<string> { TypedValue = ToDatabase(attendee.ResponseStatus) });
            AddNullableTimestamp(command, attendee.RespondedAtUtc);
            command.ExecuteNonQuery();
        }
    }

    private static void InsertContactAttendees(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CalendarEvent calendarEvent)
    {
        for (var position = 0; position < calendarEvent.ContactAttendees.Count; position++)
        {
            var attendee = calendarEvent.ContactAttendees[position];
            using var command = new NpgsqlCommand(
                """
                INSERT INTO calendar.event_contact_attendees (
                    event_id, organization_id, position, contact_id, role, response_status, responded_at)
                VALUES ($1, $2, $3, $4, $5, $6, $7);
                """,
                connection,
                transaction);
            command.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = calendarEvent.Metadata.Id });
            command.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = calendarEvent.Metadata.OrganizationId });
            command.Parameters.Add(new NpgsqlParameter<short> { TypedValue = (short)position });
            command.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = attendee.ContactId });
            command.Parameters.Add(new NpgsqlParameter<string> { TypedValue = ToDatabase(attendee.Role) });
            command.Parameters.Add(new NpgsqlParameter<string> { TypedValue = ToDatabase(attendee.ResponseStatus) });
            AddNullableTimestamp(command, attendee.RespondedAtUtc);
            command.ExecuteNonQuery();
        }
    }

    private static void AddNullableText(NpgsqlCommand command, string? value) =>
        command.Parameters.Add(new NpgsqlParameter
        {
            NpgsqlDbType = NpgsqlDbType.Text,
            Value = value is null ? DBNull.Value : value,
        });

    private static void AddNullableTimestamp(NpgsqlCommand command, DateTimeOffset? value) =>
        command.Parameters.Add(new NpgsqlParameter
        {
            NpgsqlDbType = NpgsqlDbType.TimestampTz,
            Value = value is null ? DBNull.Value : value.Value,
        });

    private static void AddNullableGuid(NpgsqlCommand command, Guid? value) =>
        command.Parameters.Add(new NpgsqlParameter
        {
            NpgsqlDbType = NpgsqlDbType.Uuid,
            Value = value is null ? DBNull.Value : value.Value,
        });

    private static DateTimeOffset? ReadNullableTimestamp(NpgsqlDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetFieldValue<DateTimeOffset>(ordinal);

    private static EntityLifecycleState ParseLifecycle(string value) => value switch
    {
        "active" => EntityLifecycleState.Active,
        "archived" => EntityLifecycleState.Archived,
        "trashed" => EntityLifecycleState.Trashed,
        _ => throw new InvalidOperationException($"Unknown stored lifecycle state '{value}'."),
    };

    private static CalendarEventStatus ParseEventStatus(string value) => value switch
    {
        "scheduled" => CalendarEventStatus.Scheduled,
        "cancelled" => CalendarEventStatus.Cancelled,
        _ => throw new InvalidOperationException($"Unknown stored calendar event status '{value}'."),
    };

    private static CalendarAttendeeRole ParseRole(string value) => value switch
    {
        "required" => CalendarAttendeeRole.Required,
        "optional" => CalendarAttendeeRole.Optional,
        "observer" => CalendarAttendeeRole.Observer,
        _ => throw new InvalidOperationException($"Unknown stored attendee role '{value}'."),
    };

    private static CalendarAttendeeResponseStatus ParseResponseStatus(string value) => value switch
    {
        "pending" => CalendarAttendeeResponseStatus.Pending,
        "accepted" => CalendarAttendeeResponseStatus.Accepted,
        "declined" => CalendarAttendeeResponseStatus.Declined,
        "tentative" => CalendarAttendeeResponseStatus.Tentative,
        _ => throw new InvalidOperationException($"Unknown stored attendee response status '{value}'."),
    };

    private static string ToDatabase(EntityLifecycleState value) => value switch
    {
        EntityLifecycleState.Active => "active",
        EntityLifecycleState.Archived => "archived",
        EntityLifecycleState.Trashed => "trashed",
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
    };

    private static string ToDatabase(CalendarEventStatus value) => value switch
    {
        CalendarEventStatus.Scheduled => "scheduled",
        CalendarEventStatus.Cancelled => "cancelled",
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
    };

    private static string ToDatabase(CalendarAttendeeRole value) => value switch
    {
        CalendarAttendeeRole.Required => "required",
        CalendarAttendeeRole.Optional => "optional",
        CalendarAttendeeRole.Observer => "observer",
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
    };

    private static string ToDatabase(CalendarAttendeeResponseStatus value) => value switch
    {
        CalendarAttendeeResponseStatus.Pending => "pending",
        CalendarAttendeeResponseStatus.Accepted => "accepted",
        CalendarAttendeeResponseStatus.Declined => "declined",
        CalendarAttendeeResponseStatus.Tentative => "tentative",
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
    };

    private static void EnsureIdentifier(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("Identifier must not be empty.", parameterName);
        }
    }
}
