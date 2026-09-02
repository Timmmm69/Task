using Task.Desktop.Calendar;
using Task.Desktop.ViewModels;

namespace Task.Desktop.Tests.Calendar;

public sealed class CalendarAttendeeEditorTests
{
    [Fact]
    public async global::System.Threading.Tasks.Task Attendees_AreCopiedAndTypedDuplicatesAreRejected()
    {
        var id = Guid.NewGuid();
        var editor = new CalendarEventEditorViewModel(Event([new(id, true, "required", "accepted", DateTimeOffset.Parse("2026-01-01T00:00:00Z"))]), TimeZoneInfo.Utc);

        Assert.Single(editor.Attendees);
        editor.AttendeeId = id.ToString();
        editor.IsUserAttendee = true;
        await editor.AddAttendeeCommand.ExecuteAsync();

        Assert.Single(editor.Attendees);
        Assert.Equal("Этот участник уже добавлен.", editor.ValidationMessage);
        editor.IsUserAttendee = false;
        await editor.AddAttendeeCommand.ExecuteAsync();
        Assert.Equal(2, editor.Attendees.Count);
    }

    [Fact]
    public void ResponseTimestamp_IsPreservedUntilResponseChanges()
    {
        var respondedAt = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
        var row = new CalendarAttendeeEditorRow(new(Guid.NewGuid(), true, "required", "accepted", respondedAt));

        row.ResponseStatus = "accepted";
        Assert.Equal(respondedAt, row.RespondedAtUtc);
        row.ResponseStatus = "declined";

        Assert.NotNull(row.RespondedAtUtc);
        Assert.NotEqual(respondedAt, row.RespondedAtUtc);
        Assert.Equal(TimeSpan.Zero, row.RespondedAtUtc!.Value.Offset);
    }

    [Fact]
    public void EditRoundTrip_UsesSourceTimeZoneAndMultiDayEndDate()
    {
        var source = Event([], DateTimeOffset.Parse("2026-03-01T22:30:00Z"), DateTimeOffset.Parse("2026-03-02T01:15:00Z"), "Europe/Minsk");
        var editor = new CalendarEventEditorViewModel(source, TimeZoneInfo.Utc);

        Assert.Equal(new DateTime(2026, 3, 2), editor.Date);
        Assert.Equal(new DateTime(2026, 3, 2), editor.EndDate);
        Assert.Equal("01:30", editor.StartTime);
        Assert.Equal("04:15", editor.EndTime);
        Assert.True(editor.TryBuild(TimeZoneInfo.Utc, out var command));
        Assert.Equal(source.StartAtUtc, command.StartAtUtc);
        Assert.Equal(source.EndAtUtc, command.EndAtUtc);
        Assert.Equal("Europe/Minsk", command.TimeZoneId);
    }

    private static DesktopCalendarEvent Event(IReadOnlyList<DesktopCalendarAttendee> attendees, DateTimeOffset? start = null, DateTimeOffset? end = null, string timeZone = "UTC") => new(
        Guid.NewGuid(), Guid.NewGuid(), 1, DateTimeOffset.Parse("2026-01-01T00:00:00Z"), DateTimeOffset.Parse("2026-01-01T00:00:00Z"), null,
        "Событие", null, new DateOnly(2026, 3, 2), false, start ?? DateTimeOffset.Parse("2026-03-02T09:00:00Z"), end ?? DateTimeOffset.Parse("2026-03-02T10:00:00Z"),
        timeZone, "scheduled", attendees, "\"v1\"");
}
