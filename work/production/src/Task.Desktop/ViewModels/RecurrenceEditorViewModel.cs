using System.Globalization;
using Task.Application.Calendar;
using Task.Desktop.TaskApi;

namespace Task.Desktop.ViewModels;

public sealed record RecurrenceChoice(string Code, string Label);
public sealed class RecurrenceEditorViewModel : ViewModelBase
{
    private string _title = "";
    private string? _description;
    private string _frequency = "weekly", _interval = "1", _weekdays = "1", _monthDays = "1", _month = "1";
    private DateTime? _start = DateTime.Today, _until;
    private string _time = "09:00", _duration = "60", _count = "", _termination = "none", _priority = "normal";
    private bool _allDay, _monthOnly;
    private string _firstYear = DateTime.Today.Year.ToString(CultureInfo.InvariantCulture);
    private int _firstMonth = DateTime.Today.Month;
    private RecurrenceSourceTask? _sourceTask;
    public RecurrenceDefinition? Source { get; }
    public RecurrenceEditorViewModel(RecurrenceDefinition? source = null)
    {
        Source = source;
        if (source is null) return;
        _monthOnly = source.MonthOnly;
        _firstYear = source.OccurrenceStartDate.Year.ToString(CultureInfo.InvariantCulture);
        _firstMonth = source.OccurrenceStartDate.Month;
        _title = source.Template.Title; _description = source.Template.Description;
        _frequency = source.Frequency == "daily" && source.Weekdays.SequenceEqual(new[] { 1, 2, 3, 4, 5 }) ? "workdays" : source.Frequency;
        _interval = source.Interval.ToString(); _weekdays = string.Join(",", source.Weekdays); _monthDays = string.Join(",", source.MonthDays);
        _month = (source.MonthOfYear ?? source.OccurrenceStartDate.Month).ToString(); _start = source.OccurrenceStartDate.ToDateTime(TimeOnly.MinValue);
        _time = source.LocalStartTime?.ToString("HH:mm") ?? "09:00"; _allDay = source.LocalStartTime is null;
        _duration = source.Template.PlannedDurationMinutes?.ToString() ?? ""; _priority = source.Template.Priority;
        _until = source.UntilDate?.ToDateTime(TimeOnly.MinValue); _count = source.MaxOccurrences?.ToString() ?? "";
        _termination = source.UntilDate.HasValue ? "until" : source.MaxOccurrences.HasValue ? "count" : "none";
    }
    private static readonly RecurrenceChoice[] MonthFrequencies = [new("monthly", "Ежемесячно"), new("yearly", "Ежегодно")];
    private static readonly RecurrenceChoice[] DatedFrequencies = [new("daily", "Ежедневно"), new("workdays", "По рабочим дням"), new("weekly", "Еженедельно"), .. MonthFrequencies];
    public IReadOnlyList<RecurrenceChoice> Frequencies => MonthOnly ? MonthFrequencies : DatedFrequencies;
    public IReadOnlyList<TaskFilterOption<int>> Months { get; } = CultureInfo.GetCultureInfo("ru-RU").DateTimeFormat.MonthNames
        .Take(12).Select((name, index) => new TaskFilterOption<int>(index + 1, name)).ToArray();
    public bool MonthOnly
    {
        get => _monthOnly;
        set
        {
            if (!SetProperty(ref _monthOnly, value)) return;
            var frequency = Frequency;
            OnPropertyChanged(nameof(Frequencies));
            Frequency = value && frequency is not ("monthly" or "yearly") ? "monthly" : frequency;
        }
    }
    public string FirstYear { get => _firstYear; set => SetProperty(ref _firstYear, value); }
    public int FirstMonth { get => _firstMonth; set => SetProperty(ref _firstMonth, value); }
    public RecurrenceSourceTask? SourceTask
    {
        get => _sourceTask;
        set
        {
            if (!SetProperty(ref _sourceTask, value) || value is null) return;
            var task = value.Task;
            Title = task.Title;
            Description = task.Card?.Description;
            Priority = task.Priority.ToString().ToLowerInvariant();
            if (task.Card?.EffectivePlanningMonth is { } month &&
                DateOnly.TryParseExact(month + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var first))
            {
                FirstMonth = first.Month;
                FirstYear = first.Year.ToString(CultureInfo.InvariantCulture);
                if (task.Card.ScheduledDate is null && task.Card.DeadlineDate is null && task.StartAtUtc is null && task.DeadlineAtUtc is null)
                    MonthOnly = true;
            }
        }
    }
    internal void RestoreSourceSelection(RecurrenceSourceTask task) =>
        SetProperty(ref _sourceTask, task, nameof(SourceTask));

    public IReadOnlyList<RecurrenceChoice> Terminations { get; } = [new("none", "Без ограничения"), new("until", "До даты включительно"), new("count", "Количество повторений")];
    public IReadOnlyList<RecurrenceChoice> Priorities { get; } = [new("low", "Низкий"), new("normal", "Обычный"), new("high", "Высокий"), new("critical", "Критический")];
    public string Title { get => _title; set => SetProperty(ref _title, value); }
    public string? Description { get => _description; set => SetProperty(ref _description, value); }
    public string Frequency { get => _frequency; set { if (SetProperty(ref _frequency, value)) { if (value == "daily") Weekdays = ""; if (value == "weekly" && string.IsNullOrWhiteSpace(Weekdays)) Weekdays = "1"; } } }
    public string Interval { get => _interval; set => SetProperty(ref _interval, value); }
    public string Weekdays { get => _weekdays; set => SetProperty(ref _weekdays, value); }
    public string MonthDays { get => _monthDays; set => SetProperty(ref _monthDays, value); }
    public string Month { get => _month; set => SetProperty(ref _month, value); }
    public DateTime? StartDate { get => _start; set => SetProperty(ref _start, value); }
    public DateTime? UntilDate { get => _until; set => SetProperty(ref _until, value); }
    public string StartTime { get => _time; set => SetProperty(ref _time, value); }
    public bool IsAllDay { get => _allDay; set => SetProperty(ref _allDay, value); }
    public string Duration { get => _duration; set => SetProperty(ref _duration, value); }
    public string Count { get => _count; set => SetProperty(ref _count, value); }
    public string Termination { get => _termination; set => SetProperty(ref _termination, value); }
    public string Priority { get => _priority; set => SetProperty(ref _priority, value); }
    public string TimeZoneText => Source?.TimeZone ?? TimeZoneInfo.Local.Id;

    public RecurrenceDefinition Build(Guid actor)
    {
        var start = MonthOnly
            ? new DateOnly(int.Parse(FirstYear, CultureInfo.InvariantCulture), FirstMonth, 1)
            : DateOnly.FromDateTime(StartDate ?? throw new ArgumentException("Укажите дату начала."));
        var definition = new RecurrenceDefinition
        {
            Status = Source?.Status ?? "active",
            Frequency = Frequency == "workdays" ? "daily" : Frequency,
            Interval = int.Parse(Interval, CultureInfo.InvariantCulture),
            Weekdays = Frequency == "workdays" ? [1, 2, 3, 4, 5] : Frequency is "weekly" or "daily" ? ParseDays(Weekdays) : [],
            MonthOnly = MonthOnly,
            SourceTaskId = SourceTask?.Task.Id,
            MonthDays = MonthOnly ? [1] : Frequency is "monthly" or "yearly" ? ParseDays(MonthDays) : [],
            MonthOfYear = Frequency == "yearly" ? MonthOnly ? FirstMonth : int.Parse(Month, CultureInfo.InvariantCulture) : null,
            OccurrenceStartDate = start,
            LocalStartTime = MonthOnly || IsAllDay ? null : TimeOnly.ParseExact(StartTime, "HH:mm", CultureInfo.InvariantCulture),
            TimeZone = TimeZoneText,
            UntilDate = Termination == "until" ? DateOnly.FromDateTime(UntilDate ?? throw new ArgumentException("Укажите дату окончания серии.")) : null,
            MaxOccurrences = Termination == "count" ? int.Parse(Count, CultureInfo.InvariantCulture) : null,
            Template = (Source?.Template ?? new RecurrenceTemplateData { Title = "", AuthorUserId = actor }) with
            {
                Title = Title.Trim(),
                Description = string.IsNullOrWhiteSpace(Description) ? null : Description.Trim(),
                Priority = Priority,
                DeadlineOffsetMinutes = MonthOnly ? null : Source?.Template.DeadlineOffsetMinutes,
                DeadlineReminderOffsetMinutes = MonthOnly ? null : Source?.Template.DeadlineReminderOffsetMinutes,
                PlannedDurationMinutes = MonthOnly || IsAllDay || string.IsNullOrWhiteSpace(Duration) ? null : int.Parse(Duration, CultureInfo.InvariantCulture),
            },
        };
        definition.Validate(); return definition;
    }
    private static int[] ParseDays(string value) => value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(v => int.Parse(v, CultureInfo.InvariantCulture)).ToArray();
}

public sealed record RecurrenceSourceTask(DesktopTaskDto Task)
{
    public string Label => Task.Card?.EffectivePlanningMonth is { } month ? $"{Task.Title} ({month})" : Task.Title;
}
