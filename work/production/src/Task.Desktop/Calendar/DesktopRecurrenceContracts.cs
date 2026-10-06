using Task.Application.Calendar;
using Task.Domain.Recurrence;

namespace Task.Desktop.Calendar;

public sealed record DesktopRecurrenceSeries(Guid Id, long Version, RecurrenceDefinition Definition);
public abstract record DesktopRecurrenceCommand
{
    public sealed record List : DesktopRecurrenceCommand;
    public sealed record Occurrences(Guid SeriesId) : DesktopRecurrenceCommand;
    public sealed record Preview(RecurrenceDefinition Definition, DateOnly From, int Limit) : DesktopRecurrenceCommand;
    public sealed record Save(Guid? SeriesId, long? Version, RecurrenceDefinition Definition, string Key) : DesktopRecurrenceCommand;
    public sealed record Generate(Guid SeriesId, long Version, DateOnly Through, string Key) : DesktopRecurrenceCommand;
    public sealed record SetStatus(Guid SeriesId, long Version, string Status, string Key) : DesktopRecurrenceCommand;
    public sealed record Apply(Guid SeriesId, long Version, DateOnly Date, long TaskVersion, RecurrenceChangeScope Scope,
        string Title, string Priority, int? Duration, string Key) : DesktopRecurrenceCommand;
}
public sealed record DesktopRecurrenceReply(
    DesktopRecurrenceSeries? Series = null, IReadOnlyList<DesktopRecurrenceSeries>? Items = null,
    IReadOnlyList<RecurrenceOccurrenceDetails>? Occurrences = null, IReadOnlyList<RecurrencePreviewItem>? Preview = null,
    int GeneratedCount = 0, long SeriesVersion = 0);

/// <summary>Execution-neutral recurrence boundary: no HTTP routes, methods or JSON.</summary>
public interface IDesktopRecurrenceClient
{
    System.Threading.Tasks.Task<DesktopCalendarResult<DesktopRecurrenceReply>> ExecuteAsync(
        DesktopRecurrenceCommand command, CancellationToken cancellationToken);
}
