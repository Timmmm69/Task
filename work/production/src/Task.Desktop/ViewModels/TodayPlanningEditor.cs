using System.Globalization;
using System.Text.Json.Nodes;
using Task.Desktop.TaskApi;
using Task.Domain;

namespace Task.Desktop.ViewModels;

/// <summary>A local draft; only the planning tuple and duration may enter its PATCH.</summary>
public sealed class TodayPlanningEditor : ViewModelBase
{
    private readonly DesktopTaskDto _source;
    private readonly TimeZoneInfo _zone;
    private readonly DateTime? _originalDate;
    private readonly string _originalTime;
    private DateTime? _date;
    private string _time, _duration;
    private string? _error;
    private string? _dateInputError;

    public TodayPlanningEditor(DesktopTaskDto source, TimeZoneInfo displayZone)
    {
        _source = source;
        // Existing wall-clock fields belong to their saved zone, not the machine's current zone.
        _zone = source.Card?.ScheduleTimeZone is { } id
            ? TimeZoneInfo.FindSystemTimeZoneById(id) : displayZone;
        var local = source.StartAtUtc is { } start ? TimeZoneInfo.ConvertTime(start, _zone) : (DateTimeOffset?)null;
        _date = _originalDate = local?.Date ?? source.Card?.ScheduledDate?.ToDateTime(TimeOnly.MinValue);
        _time = _originalTime = local?.ToString("HH:mm:ss", CultureInfo.InvariantCulture) ?? "";
        if (_time.EndsWith(":00", StringComparison.Ordinal)) _time = _originalTime = _time[..5];
        _duration = source.Card?.PlannedDurationMinutes?.ToString(CultureInfo.InvariantCulture) ?? "";
    }

    public Guid TaskId => _source.Id;
    public long Version => _source.Version;
    public DateTime? Date { get => _date; set { _dateInputError = null; if (SetProperty(ref _date, value?.Date)) ChangedDraft(); } }
    public string Time { get => _time; set { if (SetProperty(ref _time, value)) ChangedDraft(); } }
    public string Duration { get => _duration; set { if (SetProperty(ref _duration, value)) ChangedDraft(); } }
    public string ZoneText => $"Часовой пояс: {_zone.Id}. Пустое время — без точного начала.";
    public string? Error { get => _error; private set => SetProperty(ref _error, value); }
    public event Action? Changed;
    public bool HasChanges => Date != _originalDate || Time.Trim() != _originalTime
        || Duration.Trim() != (_source.Card?.PlannedDurationMinutes?.ToString(CultureInfo.InvariantCulture) ?? "");

    private void ChangedDraft() { Validate(); Changed?.Invoke(); }
    public void SetDateInputError(string? error) { _dateInputError = error; ChangedDraft(); }
    public bool Validate()
    {
        if (_dateInputError is not null) { Error = _dateInputError; return false; }
        try { _ = BuildPatch(); Error = null; return true; }
        catch (ArgumentException error) { Error = error.Message; return false; }
    }

    public DesktopPatchTaskCommand? BuildPatch()
    {
        var before = _source.Card ?? new TaskCardContent();
        var card = before;
        var start = _source.StartAtUtc;
        if (Date != _originalDate || Time.Trim() != _originalTime)
        {
            var day = Date is { } date ? DateOnly.FromDateTime(date) : (DateOnly?)null;
            TimeOnly? time = null;
            if (!string.IsNullOrWhiteSpace(Time))
            {
                if (!TimeOnly.TryParseExact(Time.Trim(), ["HH:mm", "HH:mm:ss"], CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var parsed)) throw new ArgumentException("Время: ЧЧ:ММ (или ЧЧ:ММ:СС).");
                if (day is null) throw new ArgumentException("Для времени начала выберите дату.");
                time = parsed;
                var local = day.Value.ToDateTime(parsed, DateTimeKind.Unspecified);
                if (_zone.IsInvalidTime(local)) throw new ArgumentException("Это локальное время не существует из-за перехода часового пояса.");
                if (_zone.IsAmbiguousTime(local)) throw new ArgumentException("Это локальное время неоднозначно. Выберите время вне перехода часового пояса.");
                start = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, _zone));
            }
            else start = null;
            var zoneId = before.ScheduleTimeZone ?? (TimeZoneInfo.TryConvertWindowsIdToIanaId(_zone.Id, out var iana) ? iana : _zone.Id);
            card = card with { ScheduledDate = day, StartTimeLocal = time, ScheduleTimeZone = time.HasValue ? zoneId : null };
        }
        int? duration = null;
        if (!string.IsNullOrWhiteSpace(Duration))
        {
            if (!int.TryParse(Duration.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var minutes)
                || minutes is < 1 or > 10080) throw new ArgumentException("Длительность: от 1 до 10080 минут или пустое поле.");
            duration = minutes;
        }
        card = card with { PlannedDurationMinutes = duration };
        if (start is { } instant && duration is { } length)
        {
            try { _ = instant.AddMinutes(length); }
            catch (ArgumentOutOfRangeException) { throw new ArgumentException("Окончание выходит за допустимый диапазон дат."); }
        }
        card.Validate(start);
        if (start.HasValue && _source.DeadlineAtUtc is { } deadline && deadline < start)
            throw new ArgumentException("Начало не может быть позже существующего срока выполнения.");
        _ = TaskSchedule.Create(start, _source.DeadlineAtUtc);
        var oldJson = JsonNode.Parse(before.ToJson())!.AsObject();
        var patch = new JsonObject();
        foreach (var (key, value) in JsonNode.Parse(card.ToJson())!.AsObject())
            if (!JsonNode.DeepEquals(oldJson[key], value)) patch[key] = value?.DeepClone();
        var startField = start != _source.StartAtUtc ? DesktopTaskField<DateTimeOffset?>.From(start) : default;
        return patch.Count == 0 && !startField.IsSpecified ? null
            : new DesktopPatchTaskCommand(_source.Id, _source.Version, startAtUtc: startField,
                cardPatch: patch.Count == 0 ? null : patch.ToJsonString());
    }
}
