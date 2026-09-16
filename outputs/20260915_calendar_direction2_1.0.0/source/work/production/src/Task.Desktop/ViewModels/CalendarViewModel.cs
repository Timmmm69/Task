using System.Collections.ObjectModel;
using System.Globalization;
using Task.Desktop.Calendar;

namespace Task.Desktop.ViewModels;

public enum CalendarScreenState { Inactive, Loading, Loaded, Empty, Refreshing, Error, Forbidden, SessionEnded }
public enum CalendarViewMode { Day, Week, Month }
public sealed record CalendarHourMarker(string Label, double Top);
public sealed record CalendarEventStatusOption(string Value, string Label);

public sealed class CalendarItemViewModel : ViewModelBase
{
    private static readonly CultureInfo RussianCulture = CultureInfo.GetCultureInfo("ru-RU");
    private bool _isSelected;
    public CalendarItemViewModel(DesktopScheduleItem source, DesktopScheduleConflict? conflict, TimeZoneInfo? timeZone = null)
    {
        Source = source;
        TypeText = source.ItemType == DesktopScheduleItemType.CalendarEvent ? "Событие" : source.RecurrenceSeriesId.HasValue ? "Повторяющаяся задача" : "Задача";
        var zone = timeZone ?? TimeZoneInfo.Local;
        string Time(DateTimeOffset value) => TimeZoneInfo.ConvertTime(value, zone).ToString("HH:mm");
        TimeText = source.IsAllDay ? "Весь день" : source.StartAtUtc is null
            ? source.EndAtUtc.HasValue ? $"Срок {Time(source.EndAtUtc.Value)}" : "Без времени"
            : source.EndAtUtc.HasValue ? $"{Time(source.StartAtUtc.Value)}–{Time(source.EndAtUtc.Value)}" : Time(source.StartAtUtc.Value);
        StatusText = source.Status switch
        {
            "scheduled" => "Запланировано",
            "cancelled" => "Отменено",
            "new" => "Новая",
            "in_progress" => "В работе",
            "review" => "На проверке",
            "completed" => "Завершена",
            _ => source.Status,
        };
        PriorityText = source.Priority switch
        {
            DesktopCalendarPriority.Low => "Низкий приоритет",
            DesktopCalendarPriority.Normal => "Обычный приоритет",
            DesktopCalendarPriority.High => "Высокий приоритет",
            DesktopCalendarPriority.Critical => "Критический приоритет",
            _ => null,
        };
        ConflictText = conflict?.Severity switch
        {
            DesktopConflictSeverity.Blocking => "Пересечение от 30 минут",
            DesktopConflictSeverity.Warning => "Предупреждение о пересечении расписания",
            DesktopConflictSeverity.Info => "Пересечение расписания",
            _ => null,
        };
        AutomationName = string.Join(". ", new[] { TypeText, source.Title, TimeText, StatusText, PriorityText, ConflictText }.Where(x => x is not null));
    }
    public DesktopScheduleItem Source { get; }
    public Guid Id => Source.ObjectId;
    public string Title => Source.Title;
    public string? Description => Source.Description;
    public string TypeText { get; }
    public string TimeText { get; }
    public string StatusText { get; }
    public string? PriorityText { get; }
    public string? ConflictText { get; }
    public bool HasConflict => ConflictText is not null;
    public bool IsCalendarEvent => Source.ItemType == DesktopScheduleItemType.CalendarEvent;
    public bool IsTimelineCandidate => !Source.IsAllDay && (Source.StartAtUtc.HasValue || Source.EndAtUtc.HasValue);
    public bool IsHighPriority => Source.Priority is DesktopCalendarPriority.High or DesktopCalendarPriority.Critical;
    public bool IsCompleted => Source.Status is "completed" or "cancelled";
    public bool IsSelected { get => _isSelected; internal set => SetProperty(ref _isSelected, value); }
    public string AutomationName { get; }

    internal bool AppearsOn(DateOnly date, TimeZoneInfo timeZone)
    {
        if (Source.IsAllDay || Source.StartAtUtc is null || Source.EndAtUtc is null) return Source.LocalDate == date;
        var first = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(Source.StartAtUtc.Value, timeZone).DateTime);
        var lastInstant = Source.EndAtUtc.Value.AddTicks(-1);
        var last = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(lastInstant, timeZone).DateTime);
        return date >= first && date <= last;
    }
}

public sealed class CalendarTimelineItemViewModel
{
    private const double HourHeight = 69d;

    public CalendarTimelineItemViewModel(CalendarItemViewModel item, DateOnly date, TimeZoneInfo timeZone)
    {
        Item = item;
        var source = item.Source;
        var fallbackEnd = source.EndAtUtc ?? source.StartAtUtc!.Value.AddMinutes(45);
        var fallbackStart = source.StartAtUtc ?? fallbackEnd.AddMinutes(-30);
        var localStart = TimeZoneInfo.ConvertTime(fallbackStart, timeZone);
        var localEnd = TimeZoneInfo.ConvertTime(fallbackEnd, timeZone);
        var day = date.ToDateTime(TimeOnly.MinValue);
        var startMinutes = localStart.Date < day ? 0d : localStart.TimeOfDay.TotalMinutes;
        var endMinutes = localEnd.Date > day ? 24d * 60d : localEnd.TimeOfDay.TotalMinutes;
        if (endMinutes <= startMinutes) endMinutes = Math.Min(24d * 60d, startMinutes + 30d);
        TimelineTop = startMinutes / 60d * HourHeight;
        TimelineHeight = Math.Max(48d, (endMinutes - startMinutes) / 60d * HourHeight - 5d);
        // Lane occupancy follows the rendered card, including its minimum readable height.
        TimelineEnd = Math.Max(endMinutes / 60d * HourHeight, TimelineTop + TimelineHeight + 4d);
    }

    public CalendarItemViewModel Item { get; }
    public double TimelineTop { get; }
    public double TimelineHeight { get; }
    internal double TimelineEnd { get; }
    public int TimelineLane { get; internal set; }
    public int TimelineLaneCount { get; internal set; } = 1;
}

public sealed class CalendarDayViewModel : ViewModelBase
{
    private const double HourHeight = 69d;
    private readonly TimeZoneInfo _timeZone;
    private bool _isToday;
    private bool _showCurrentTime;
    private string _currentTimeText = string.Empty;
    private double _currentTimeTop;

    public CalendarDayViewModel(
        DateOnly date,
        IEnumerable<CalendarItemViewModel> items,
        bool isCurrentMonth = true,
        TimeZoneInfo? timeZone = null,
        DateTimeOffset? now = null)
    {
        Date = date;
        Items = items.OrderBy(x => x.Source.IsAllDay ? 0 : 1).ThenBy(x => x.Source.StartAtUtc).ToArray();
        Header = date.ToDateTime(TimeOnly.MinValue).ToString("ddd, d MMM", CultureInfo.GetCultureInfo("ru-RU"));
        DayName = date.ToDateTime(TimeOnly.MinValue).ToString("ddd", CultureInfo.GetCultureInfo("ru-RU"));
        DayNumberText = date.Day.ToString(CultureInfo.InvariantCulture);
        IsCurrentMonth = isCurrentMonth;
        var zone = timeZone ?? TimeZoneInfo.Local;
        _timeZone = zone;
        TimelineItems = Items.Where(item => item.IsTimelineCandidate)
            .Select(item => new CalendarTimelineItemViewModel(item, date, zone))
            .OrderBy(item => item.TimelineTop)
            .ThenBy(item => item.Item.Title, StringComparer.CurrentCulture)
            .ToArray();
        AssignTimelineLanes(TimelineItems);
        AllDayItems = Items.Where(item => !item.IsTimelineCandidate).ToArray();
        UpdateCurrentTime(now ?? DateTimeOffset.UtcNow);
    }
    public DateOnly Date { get; }
    public string Header { get; }
    public string DayName { get; }
    public string DayNumberText { get; }
    public IReadOnlyList<CalendarItemViewModel> Items { get; }
    public IReadOnlyList<CalendarTimelineItemViewModel> TimelineItems { get; }
    public IReadOnlyList<CalendarItemViewModel> AllDayItems { get; }
    public bool IsToday { get => _isToday; private set => SetProperty(ref _isToday, value); }
    public bool IsCurrentMonth { get; }
    public bool ShowCurrentTime { get => _showCurrentTime; private set => SetProperty(ref _showCurrentTime, value); }
    public string CurrentTimeText { get => _currentTimeText; private set => SetProperty(ref _currentTimeText, value); }
    public double CurrentTimeTop { get => _currentTimeTop; private set => SetProperty(ref _currentTimeTop, value); }
    public string AutomationName => $"{Header}. {Items.Count} элементов расписания.";

    internal void UpdateCurrentTime(DateTimeOffset now)
    {
        var localNow = TimeZoneInfo.ConvertTime(now, _timeZone);
        IsToday = DateOnly.FromDateTime(localNow.DateTime) == Date;
        ShowCurrentTime = IsToday;
        CurrentTimeText = localNow.ToString("HH:mm", CultureInfo.InvariantCulture);
        CurrentTimeTop = localNow.TimeOfDay.TotalMinutes / 60d * HourHeight;
    }

    private static void AssignTimelineLanes(IReadOnlyList<CalendarTimelineItemViewModel> items)
    {
        var laneEnds = new List<double>();
        var component = new List<CalendarTimelineItemViewModel>();
        var componentEnd = double.MinValue;
        foreach (var item in items)
        {
            if (component.Count > 0 && item.TimelineTop >= componentEnd)
            {
                CompleteComponent(component, laneEnds.Count);
                component.Clear();
                laneEnds.Clear();
                componentEnd = double.MinValue;
            }
            var lane = laneEnds.FindIndex(end => end <= item.TimelineTop);
            if (lane < 0) { lane = laneEnds.Count; laneEnds.Add(0d); }
            item.TimelineLane = lane;
            laneEnds[lane] = item.TimelineEnd;
            componentEnd = Math.Max(componentEnd, item.TimelineEnd);
            component.Add(item);
        }
        CompleteComponent(component, laneEnds.Count);
    }

    private static void CompleteComponent(IEnumerable<CalendarTimelineItemViewModel> items, int laneCount)
    {
        foreach (var item in items) item.TimelineLaneCount = Math.Max(1, laneCount);
    }
}

public sealed record CalendarAttendeeKind(bool IsUser, string Label);
public sealed class CalendarAttendeeEditorRow : ViewModelBase
{
    private string _role;
    private string _responseStatus;
    private DateTimeOffset? _respondedAtUtc;
    private readonly string _initialResponseStatus;
    private readonly DateTimeOffset? _initialRespondedAtUtc;

    public CalendarAttendeeEditorRow(DesktopCalendarAttendee attendee)
    {
        Id = attendee.Id; IsUser = attendee.IsUser; _role = attendee.Role; _responseStatus = attendee.ResponseStatus;
        _respondedAtUtc = attendee.RespondedAtUtc; _initialResponseStatus = attendee.ResponseStatus; _initialRespondedAtUtc = attendee.RespondedAtUtc;
    }
    public Guid Id { get; }
    public bool IsUser { get; }
    public string KindText => IsUser ? "Сотрудник" : "Контакт";
    public string Role { get => _role; set => SetProperty(ref _role, value); }
    public string ResponseStatus
    {
        get => _responseStatus;
        set
        {
            if (!SetProperty(ref _responseStatus, value)) return;
            _respondedAtUtc = value == _initialResponseStatus ? _initialRespondedAtUtc : DateTimeOffset.UtcNow;
            OnPropertyChanged(nameof(RespondedAtUtc));
        }
    }
    public DateTimeOffset? RespondedAtUtc => _respondedAtUtc;
    public DesktopCalendarAttendee ToAttendee() => new(Id, IsUser, Role, ResponseStatus, RespondedAtUtc);
}

public sealed class CalendarEventEditorViewModel : ViewModelBase
{
    private string _title = string.Empty;
    private string? _description;
    private DateTime? _date = DateTime.Today;
    private DateTime? _endDate = DateTime.Today;
    private bool _isAllDay;
    private string _startTime = "09:00";
    private string _endTime = "10:00";
    private string? _validationMessage;
    private string _attendeeId = string.Empty;
    private bool _isUserAttendee = true;
    private readonly TimeZoneInfo _eventTimeZone;
    private string _projectId = string.Empty;
    private string _status = "scheduled";
    private bool _hasConflict;
    private bool _isSaving;
    public CalendarEventEditorViewModel(DesktopCalendarEvent? source, TimeZoneInfo? localTimeZone = null)
    {
        Source = source;
        _eventTimeZone = source is null
            ? localTimeZone ?? TimeZoneInfo.Local
            : FindTimeZone(source.TimeZoneId, localTimeZone ?? TimeZoneInfo.Local);
        Attendees = new(source?.Attendees.Select(a => new CalendarAttendeeEditorRow(a)) ?? []);
        AddAttendeeCommand = new AsyncCommand((_, _) => { AddAttendee(); return global::System.Threading.Tasks.Task.CompletedTask; }, _ => CanAddAttendee);
        RemoveAttendeeCommand = new AsyncCommand((parameter, _) => { if (parameter is CalendarAttendeeEditorRow row) Attendees.Remove(row); return global::System.Threading.Tasks.Task.CompletedTask; });
        if (source is null) return;
        _title = source.Title; _description = source.Description; _date = source.EventDate.ToDateTime(TimeOnly.MinValue); _endDate = _date;
        _projectId = source.ProjectId?.ToString() ?? string.Empty; _status = source.Status;
        _isAllDay = source.IsAllDay;
        if (source.StartAtUtc.HasValue) _startTime = TimeZoneInfo.ConvertTime(source.StartAtUtc.Value, _eventTimeZone!).ToString("HH:mm");
        if (source.EndAtUtc.HasValue)
        {
            var endLocal = TimeZoneInfo.ConvertTime(source.EndAtUtc.Value, _eventTimeZone!);
            _endTime = endLocal.ToString("HH:mm"); _endDate = endLocal.Date;
        }
    }
    public DesktopCalendarEvent? Source { get; private set; }
    public bool IsNew => Source is null;
    public string EditorTitle => IsNew ? "Новое событие" : "Редактор события";
    public string VersionText => IsNew ? $"Новое событие · {_eventTimeZone.Id}" : $"Версия {Source!.Version} · {_eventTimeZone.Id}";
    public string TimeZoneText => _eventTimeZone.Id;
    public string Title { get => _title; set { if (SetProperty(ref _title, value)) ValidationMessage = null; } }
    public string? Description { get => _description; set => SetProperty(ref _description, value); }
    public DateTime? Date { get => _date; set { var previous = _date; if (SetProperty(ref _date, value)) { if (value.HasValue) EndDate = previous.HasValue && EndDate.HasValue ? value.Value.AddDays((EndDate.Value.Date - previous.Value.Date).Days) : value; ValidationMessage = null; } } }
    public DateTime? EndDate { get => _endDate; set { if (SetProperty(ref _endDate, value)) ValidationMessage = null; } }
    public bool IsAllDay { get => _isAllDay; set { if (SetProperty(ref _isAllDay, value)) ValidationMessage = null; } }
    public string StartTime { get => _startTime; set { if (SetProperty(ref _startTime, value)) ValidationMessage = null; } }
    public string EndTime { get => _endTime; set { if (SetProperty(ref _endTime, value)) ValidationMessage = null; } }
    public string? ValidationMessage { get => _validationMessage; set => SetProperty(ref _validationMessage, value); }
    public string ProjectId { get => _projectId; set { if (SetProperty(ref _projectId, value)) ValidationMessage = null; } }
    public string Status { get => _status; set { if (SetProperty(ref _status, value)) ValidationMessage = null; } }
    public bool HasConflict { get => _hasConflict; private set => SetProperty(ref _hasConflict, value); }
    public bool IsSaving { get => _isSaving; internal set => SetProperty(ref _isSaving, value); }
    public string ValidationTitle => HasConflict ? "Конфликт версий" : "Проверьте данные";
    public IReadOnlyList<CalendarEventStatusOption> StatusOptions { get; } =
        [new("scheduled", "Запланировано"), new("cancelled", "Отменено")];
    public ObservableCollection<CalendarAttendeeEditorRow> Attendees { get; }
    public string AttendeeId { get => _attendeeId; set { if (SetProperty(ref _attendeeId, value)) { ValidationMessage = null; AddAttendeeCommand.RaiseCanExecuteChanged(); } } }
    public bool IsUserAttendee { get => _isUserAttendee; set { if (SetProperty(ref _isUserAttendee, value)) AddAttendeeCommand.RaiseCanExecuteChanged(); } }
    public IReadOnlyList<CalendarAttendeeKind> AttendeeKinds { get; } = [new(true, "Сотрудник"), new(false, "Контакт")];
    public bool CanAddAttendee => Guid.TryParse(AttendeeId, out var id) && id != Guid.Empty && Attendees.Count(a => a.IsUser == IsUserAttendee) < 500;
    public AsyncCommand AddAttendeeCommand { get; }
    public AsyncCommand RemoveAttendeeCommand { get; }
    public AsyncCommand? SaveCommand { get; private set; }
    public AsyncCommand? CancelCommand { get; private set; }

    internal void AttachCommands(AsyncCommand saveCommand, AsyncCommand cancelCommand)
    {
        SaveCommand = saveCommand;
        CancelCommand = cancelCommand;
        OnPropertyChanged(nameof(SaveCommand));
        OnPropertyChanged(nameof(CancelCommand));
    }

    private void AddAttendee()
    {
        if (!Guid.TryParse(AttendeeId, out var id) || id == Guid.Empty) { ValidationMessage = "Введите корректный идентификатор участника."; return; }
        if (Attendees.Any(a => a.IsUser == IsUserAttendee && a.Id == id)) { ValidationMessage = "Этот участник уже добавлен."; return; }
        if (Attendees.Count(a => a.IsUser == IsUserAttendee) >= 500) { ValidationMessage = "Можно добавить не более 500 участников каждого типа."; return; }
        Attendees.Add(new(new DesktopCalendarAttendee(id, IsUserAttendee, "required", "pending", null)));
        AttendeeId = string.Empty;
    }

    public bool TryBuild(TimeZoneInfo timeZone, out DesktopCalendarEventCommand command)
    {
        command = null!;
        if (string.IsNullOrWhiteSpace(Title) || Title.Trim().Length > 500 || !Date.HasValue)
        { ValidationMessage = "Укажите название до 500 символов и дату."; return false; }
        var eventDate = DateOnly.FromDateTime(Date.Value);
        var endDate = IsAllDay ? eventDate : DateOnly.FromDateTime((EndDate ?? Date).Value);
        var effectiveTimeZone = _eventTimeZone;
        Guid? projectId = null;
        if (!string.IsNullOrWhiteSpace(ProjectId))
        {
            if (!Guid.TryParse(ProjectId.Trim(), out var parsedProjectId) || parsedProjectId == Guid.Empty)
            { ValidationMessage = "Укажите корректный идентификатор проекта или оставьте поле пустым."; return false; }
            projectId = parsedProjectId;
        }
        if (endDate < eventDate) { ValidationMessage = "Дата окончания не может быть раньше даты начала."; return false; }
        DateTimeOffset? start = null, end = null;
        if (!IsAllDay)
        {
            if (!TimeOnly.TryParseExact(StartTime, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var startTime)
                || !TimeOnly.TryParseExact(EndTime, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var endTime)
                || (endDate == eventDate && endTime <= startTime))
            { ValidationMessage = "Введите время в формате ЧЧ:ММ; окончание должно быть позже начала."; return false; }
            var startLocal = DateTime.SpecifyKind(eventDate.ToDateTime(startTime), DateTimeKind.Unspecified);
            var endLocal = DateTime.SpecifyKind(endDate.ToDateTime(endTime), DateTimeKind.Unspecified);
            if (effectiveTimeZone.IsInvalidTime(startLocal) || effectiveTimeZone.IsInvalidTime(endLocal))
            { ValidationMessage = "Это время отсутствует из-за перехода часового пояса."; return false; }
            start = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(startLocal, effectiveTimeZone), TimeSpan.Zero);
            end = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(endLocal, effectiveTimeZone), TimeSpan.Zero);
        }
        command = new(projectId, Title.Trim(), string.IsNullOrWhiteSpace(Description) ? null : Description.Trim(),
            eventDate, IsAllDay, start, end, effectiveTimeZone.Id, Status, Attendees.Select(a => a.ToAttendee()).ToArray(), endDate);
        return true;
    }

    public void RebaseSource(DesktopCalendarEvent source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (Source?.Id != source.Id) throw new ArgumentException("The server event must match the edited event.", nameof(source));
        Source = source;
        HasConflict = true;
        OnPropertyChanged(nameof(Source));
        OnPropertyChanged(nameof(VersionText));
        OnPropertyChanged(nameof(ValidationTitle));
    }

    private static TimeZoneInfo FindTimeZone(string id, TimeZoneInfo fallback)
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
        catch (TimeZoneNotFoundException) { return fallback; }
        catch (InvalidTimeZoneException) { return fallback; }
    }
}

public sealed class CalendarViewModel : ViewModelBase, IDisposable
{
    private const double TimelineHourHeight = 69d;
    private readonly IDesktopCalendarApiClient _client;
    private readonly TimeZoneInfo _timeZone;
    private readonly Func<DateTimeOffset> _clock;
    private readonly HashSet<string> _capabilities;
    private CancellationTokenSource? _requestCancellation;
    private long _generation;
    private bool _active;
    private bool _sessionAllowsWrites = true;
    private bool _networkAvailable = true;
    private bool _disposed;
    private bool _saving;
    private CalendarScreenState _state = CalendarScreenState.Inactive;
    private DateOnly _weekStart;
    private DateOnly _selectedDate;
    private CalendarViewMode _viewMode = CalendarViewMode.Week;
    private IReadOnlyList<CalendarDayViewModel> _days = Array.Empty<CalendarDayViewModel>();
    private CalendarItemViewModel? _selectedItem;
    private DesktopCalendarEvent? _selectedEvent;
    private string? _message;
    private string? _announcement;
    private DateTimeOffset? _lastRefresh;
    private CalendarEventEditorViewModel? _editor;
    private string? _rollbackMessage;

    public CalendarViewModel(IDesktopCalendarApiClient client, IEnumerable<string>? capabilities = null, TimeZoneInfo? timeZone = null, DateTime? today = null, RecurrencePaneViewModel? recurrence = null, Func<DateTimeOffset>? clock = null)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _timeZone = timeZone ?? TimeZoneInfo.Local;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _capabilities = new(capabilities ?? [], StringComparer.Ordinal);
        Recurrence = recurrence;
        if (Recurrence is not null)
        {
            Recurrence.Changed += OnRecurrenceChanged;
            Recurrence.AccessLost += OnRecurrenceAccessLost;
        }
        _selectedDate = DateOnly.FromDateTime(today ?? DateTime.Today);
        _weekStart = StartOfWeek(_selectedDate);
        RefreshCommand = new AsyncCommand((_, token) => LoadAsync(true, token), _ => IsActive && CanRead && !IsBusy);
        PreviousWeekCommand = new AsyncCommand((_, token) => MoveAsync(-1, token), _ => IsActive && CanRead && !IsBusy);
        NextWeekCommand = new AsyncCommand((_, token) => MoveAsync(1, token), _ => IsActive && CanRead && !IsBusy);
        TodayCommand = new AsyncCommand((_, token) => GoTodayAsync(token), _ => IsActive && CanRead && !IsBusy);
        DayModeCommand = new AsyncCommand((_, token) => SetViewModeAsync(CalendarViewMode.Day, token), _ => IsActive && CanRead && !IsBusy && ViewMode != CalendarViewMode.Day);
        WeekModeCommand = new AsyncCommand((_, token) => SetViewModeAsync(CalendarViewMode.Week, token), _ => IsActive && CanRead && !IsBusy && ViewMode != CalendarViewMode.Week);
        MonthModeCommand = new AsyncCommand((_, token) => SetViewModeAsync(CalendarViewMode.Month, token), _ => IsActive && CanRead && !IsBusy && ViewMode != CalendarViewMode.Month);
        SelectItemCommand = new AsyncCommand((parameter, _) =>
        {
            if (parameter is CalendarItemViewModel item) SelectedItem = item;
            return global::System.Threading.Tasks.Task.CompletedTask;
        }, parameter => IsActive && parameter is CalendarItemViewModel);
        NewEventCommand = new AsyncCommand((_, _) => { Editor = CreateEditor(null); return global::System.Threading.Tasks.Task.CompletedTask; }, _ => CanCreate && Editor is null);
        EditEventCommand = new AsyncCommand((_, _) => { if (SelectedEvent is not null) Editor = CreateEditor(SelectedEvent); return global::System.Threading.Tasks.Task.CompletedTask; }, _ => CanEdit);
        SaveEventCommand = new AsyncCommand((_, token) => SaveAsync(token), _ => CanSave);
        CancelEditorCommand = new AsyncCommand((_, _) => { Editor = null; return global::System.Threading.Tasks.Task.CompletedTask; }, _ => Editor is not null && !IsBusy);
    }

    public IReadOnlyList<CalendarDayViewModel> Days { get => _days; private set => SetProperty(ref _days, value); }
    public IReadOnlyList<CalendarHourMarker> HourMarkers { get; } = Enumerable.Range(0, 24).Select(hour => new CalendarHourMarker($"{hour:00}:00", hour * TimelineHourHeight)).ToArray();
    public double TimelineCanvasHeight => 24d * TimelineHourHeight;
    public double InitialTimelineOffset => 8d * TimelineHourHeight;
    public string TimeZoneText => _timeZone.Id;
    public void RefreshCurrentTime()
    {
        var now = _clock();
        foreach (var day in Days) day.UpdateCurrentTime(now);
    }
    public RecurrencePaneViewModel? Recurrence { get; }
    private void OnRecurrenceChanged() { if (_active && CanRead) _ = LoadAsync(true, CancellationToken.None); }
    private void OnRecurrenceAccessLost() { Message = "Доступ к сериям отозван или сессия завершена. Обновите календарь."; }
    public DateOnly WeekStart => _weekStart;
    public CalendarViewMode ViewMode { get => _viewMode; private set { if (SetProperty(ref _viewMode, value)) { OnPropertyChanged(nameof(WeekRangeText)); OnPropertyChanged(nameof(CalendarColumns)); OnPropertyChanged(nameof(DayCellHeight)); OnPropertyChanged(nameof(IsTimelineMode)); OnPropertyChanged(nameof(IsMonthMode)); RaiseCommands(); } } }
    public int CalendarColumns => ViewMode == CalendarViewMode.Day ? 1 : 7;
    public double DayCellHeight => ViewMode == CalendarViewMode.Month ? 150 : 470;
    public bool IsTimelineMode => ViewMode is CalendarViewMode.Day or CalendarViewMode.Week;
    public bool IsMonthMode => ViewMode == CalendarViewMode.Month;
    public DateTime? SelectedDate
    {
        get => _selectedDate.ToDateTime(TimeOnly.MinValue);
        set
        {
            if (!value.HasValue || _saving) return;
            var date = DateOnly.FromDateTime(value.Value);
            if (date == _selectedDate) return;
            _selectedDate = date; _weekStart = StartOfWeek(date);
            ClearSelection();
            OnPropertyChanged(nameof(SelectedDate)); OnPropertyChanged(nameof(WeekStart)); OnPropertyChanged(nameof(WeekRangeText));
            if (_active && CanRead) _ = LoadAsync(false, CancellationToken.None);
        }
    }
    public string WeekRangeText => ViewMode switch
    {
        CalendarViewMode.Day => _selectedDate.ToDateTime(TimeOnly.MinValue).ToString("dddd, d MMMM yyyy", CultureInfo.GetCultureInfo("ru-RU")),
        CalendarViewMode.Month => _selectedDate.ToDateTime(TimeOnly.MinValue).ToString("MMMM yyyy", CultureInfo.GetCultureInfo("ru-RU")),
        _ => string.Create(CultureInfo.GetCultureInfo("ru-RU"), $"{_weekStart.ToDateTime(TimeOnly.MinValue):d MMM} — {_weekStart.AddDays(6).ToDateTime(TimeOnly.MinValue):d MMM yyyy}"),
    };
    public CalendarScreenState State { get => _state; private set { if (SetProperty(ref _state, value)) { OnPropertyChanged(nameof(IsBusy)); OnPropertyChanged(nameof(IsSaving)); OnPropertyChanged(nameof(ShowMessage)); OnPropertyChanged(nameof(ShowInitialLoading)); OnPropertyChanged(nameof(ShowEmptyState)); OnPropertyChanged(nameof(ShowStateBanner)); OnPropertyChanged(nameof(StateTitle)); OnPropertyChanged(nameof(StateDescription)); RaiseCommands(); } } }
    public bool IsActive => _active;
    public bool IsBusy => _saving || State is CalendarScreenState.Loading or CalendarScreenState.Refreshing;
    public bool IsSaving => _saving;
    public bool ShowInitialLoading => State == CalendarScreenState.Loading && Days.Count == 0;
    public bool ShowEmptyState => State == CalendarScreenState.Empty;
    public bool IsReadOnly => !_networkAvailable || !_sessionAllowsWrites
        || !(_capabilities.Contains("CalendarEvent.Create") || _capabilities.Contains("CalendarEvent.Update"));
    public bool ShowReadOnlyState => _active && CanRead && IsReadOnly;
    public bool ShowStateBanner => State is CalendarScreenState.Error or CalendarScreenState.Forbidden or CalendarScreenState.SessionEnded;
    public string StateTitle => State switch
    {
        CalendarScreenState.Forbidden => "Календарь недоступен",
        CalendarScreenState.SessionEnded => "Сессия завершена",
        _ => "Не удалось обновить календарь",
    };
    public string StateDescription => Message ?? "Повторите обновление.";
    public bool ShowMessage => State is CalendarScreenState.Loading or CalendarScreenState.Error or CalendarScreenState.Forbidden or CalendarScreenState.SessionEnded || Message is not null;
    public string? Message { get => _message; private set { if (SetProperty(ref _message, value)) { OnPropertyChanged(nameof(ShowMessage)); OnPropertyChanged(nameof(StateDescription)); } } }
    public string? Announcement { get => _announcement; private set => SetProperty(ref _announcement, value); }
    public string LastSuccessfulRefreshText => _lastRefresh.HasValue ? $"Обновлено {_lastRefresh.Value.ToLocalTime():HH:mm}" : "Ещё не обновлялось";
    public bool CanRead => _capabilities.Contains("Calendar.Read");
    public bool CanCreate => _active && _sessionAllowsWrites && _networkAvailable && _capabilities.Contains("CalendarEvent.Create") && Editor is null;
    public bool CanEdit => _active && _sessionAllowsWrites && _networkAvailable && _capabilities.Contains("CalendarEvent.Update") && SelectedEvent is not null && Editor is null;
    public bool CanSave => _active && _sessionAllowsWrites && _networkAvailable && Editor is not null && !IsBusy
        && (Editor.Source is null ? _capabilities.Contains("CalendarEvent.Create") : _capabilities.Contains("CalendarEvent.Update"));
    public string WriteAccessText => !_networkAvailable
        ? "Сервер недоступен. Подтверждённый календарь доступен только для просмотра."
        : CanCreate || _capabilities.Contains("CalendarEvent.Update") ? "Изменения календаря синхронизируются с сервером компании." : "Календарь доступен только для просмотра.";
    public CalendarItemViewModel? SelectedItem { get => _selectedItem; set { if (SetProperty(ref _selectedItem, value)) { UpdateSelectionFlags(); OnPropertyChanged(nameof(HasSelectedItem)); _ = LoadSelectedEventAsync(value); } } }
    public bool HasSelectedItem => SelectedItem is not null;
    public DesktopCalendarEvent? SelectedEvent { get => _selectedEvent; private set { if (SetProperty(ref _selectedEvent, value)) { OnPropertyChanged(nameof(CanEdit)); RaiseCommands(); } } }
    public CalendarEventEditorViewModel? Editor { get => _editor; private set { if (SetProperty(ref _editor, value)) { OnPropertyChanged(nameof(CanCreate)); OnPropertyChanged(nameof(CanEdit)); RaiseCommands(); } } }
    public string? RollbackMessage { get => _rollbackMessage; private set { if (SetProperty(ref _rollbackMessage, value)) OnPropertyChanged(nameof(ShowRollback)); } }
    public bool ShowRollback => RollbackMessage is not null;
    public AsyncCommand RefreshCommand { get; }
    public AsyncCommand PreviousWeekCommand { get; }
    public AsyncCommand NextWeekCommand { get; }
    public AsyncCommand TodayCommand { get; }
    public AsyncCommand DayModeCommand { get; }
    public AsyncCommand WeekModeCommand { get; }
    public AsyncCommand MonthModeCommand { get; }
    public AsyncCommand SelectItemCommand { get; }
    public AsyncCommand NewEventCommand { get; }
    public AsyncCommand EditEventCommand { get; }
    public AsyncCommand SaveEventCommand { get; }
    public AsyncCommand CancelEditorCommand { get; }

    private CalendarEventEditorViewModel CreateEditor(DesktopCalendarEvent? source)
    {
        var editor = new CalendarEventEditorViewModel(source, _timeZone);
        editor.AttachCommands(SaveEventCommand, CancelEditorCommand);
        return editor;
    }

    public void Activate()
    {
        if (_disposed || _active) return;
        _active = true; Recurrence?.SetAccess(_capabilities, _sessionAllowsWrites); OnPropertyChanged(nameof(IsActive)); OnPropertyChanged(nameof(CanCreate)); OnPropertyChanged(nameof(ShowReadOnlyState)); RaiseCommands();
        if (!CanRead) { ClearProtectedData(); State = CalendarScreenState.Forbidden; Message = "Нет права Calendar.Read для просмотра расписания."; return; }
        if (!_networkAvailable)
        {
            State = Days.Count > 0 ? CalendarScreenState.Loaded : CalendarScreenState.Error;
            Message = Days.Count > 0 ? null : "Сервер недоступен, а подтверждённый кэш календаря ещё не создан.";
            return;
        }
        _ = LoadAsync(false, CancellationToken.None);
    }

    public async global::System.Threading.Tasks.Task ActivateAsync()
    {
        if (_disposed || _active) return;
        _active = true; Recurrence?.SetAccess(_capabilities, _sessionAllowsWrites); OnPropertyChanged(nameof(IsActive)); OnPropertyChanged(nameof(CanCreate)); OnPropertyChanged(nameof(ShowReadOnlyState)); RaiseCommands();
        if (!CanRead) { ClearProtectedData(); State = CalendarScreenState.Forbidden; Message = "Нет права Calendar.Read для просмотра расписания."; return; }
        if (!_networkAvailable)
        {
            State = Days.Count > 0 ? CalendarScreenState.Loaded : CalendarScreenState.Error;
            Message = Days.Count > 0 ? null : "Сервер недоступен, а подтверждённый кэш календаря ещё не создан.";
            return;
        }
        await LoadAsync(false, CancellationToken.None);
    }

    public void Deactivate()
    {
        if (!_active) return;
        _active = false; Recurrence?.SetAccess(_capabilities, false); Interlocked.Increment(ref _generation); _requestCancellation?.Cancel();
        State = CalendarScreenState.Inactive; OnPropertyChanged(nameof(IsActive)); OnPropertyChanged(nameof(CanCreate)); OnPropertyChanged(nameof(ShowReadOnlyState)); RaiseCommands();
    }

    public void UpdateCapabilities(IEnumerable<string>? capabilities)
    {
        _capabilities.Clear(); foreach (var capability in capabilities ?? []) _capabilities.Add(capability);
        Recurrence?.SetAccess(_capabilities, _active && _sessionAllowsWrites); OnPropertyChanged(nameof(CanRead)); OnPropertyChanged(nameof(CanCreate)); OnPropertyChanged(nameof(CanEdit)); OnPropertyChanged(nameof(WriteAccessText)); RaiseCommands();
        OnPropertyChanged(nameof(IsReadOnly)); OnPropertyChanged(nameof(ShowReadOnlyState));
        if (!CanRead) { Interlocked.Increment(ref _generation); _requestCancellation?.Cancel(); ClearProtectedData(); State = CalendarScreenState.Forbidden; Message = "Право Calendar.Read отозвано. Данные календаря очищены."; }
        else if (_active && State == CalendarScreenState.Forbidden) _ = LoadAsync(false, CancellationToken.None);
    }

    public void UpdateSessionState(bool signedIn)
    {
        _sessionAllowsWrites = signedIn; Recurrence?.SetAccess(_capabilities, _active && signedIn);
        OnPropertyChanged(nameof(CanCreate)); OnPropertyChanged(nameof(CanEdit)); RaiseCommands();
        OnPropertyChanged(nameof(IsReadOnly)); OnPropertyChanged(nameof(ShowReadOnlyState));
        if (signedIn) return;
        Interlocked.Increment(ref _generation); _requestCancellation?.Cancel(); ClearProtectedData(preserveEditor: true); State = CalendarScreenState.SessionEnded;
        if (Editor is not null) Editor.ValidationMessage = "Сессия завершена. Черновик сохранён в форме и не будет отправлен автоматически.";
        Message = "Сессия завершена. Подтверждённые данные календаря очищены; открытый черновик сохранён.";
    }

    public void UpdateConnectivity(bool available)
    {
        if (_networkAvailable == available) return;
        _networkAvailable = available;
        Recurrence?.SetAccess(_capabilities, _active && _sessionAllowsWrites && available);
        OnPropertyChanged(nameof(CanCreate));
        OnPropertyChanged(nameof(CanEdit));
        OnPropertyChanged(nameof(CanSave));
        OnPropertyChanged(nameof(WriteAccessText));
        OnPropertyChanged(nameof(IsReadOnly));
        OnPropertyChanged(nameof(ShowReadOnlyState));
        if (!available && Editor is not null)
        {
            Editor.ValidationMessage = "Сервер недоступен. Черновик сохранён в форме и не будет отправлен автоматически.";
        }
        RaiseCommands();
    }

    private async global::System.Threading.Tasks.Task MoveAsync(int direction, CancellationToken token)
    {
        _selectedDate = ViewMode switch
        {
            CalendarViewMode.Day => _selectedDate.AddDays(direction),
            CalendarViewMode.Month => _selectedDate.AddMonths(direction),
            _ => _selectedDate.AddDays(7 * direction),
        };
        _weekStart = StartOfWeek(_selectedDate);
        ClearSelection();
        OnPropertyChanged(nameof(SelectedDate)); OnPropertyChanged(nameof(WeekStart)); OnPropertyChanged(nameof(WeekRangeText));
        await LoadAsync(false, token);
    }

    private async global::System.Threading.Tasks.Task SetViewModeAsync(CalendarViewMode mode, CancellationToken token)
    {
        if (ViewMode == mode) return;
        ViewMode = mode;
        ClearSelection();
        await LoadAsync(false, token);
    }

    private async global::System.Threading.Tasks.Task GoTodayAsync(CancellationToken token)
    { _selectedDate = DateOnly.FromDateTime(DateTime.Today); _weekStart = StartOfWeek(_selectedDate); ClearSelection(); OnPropertyChanged(nameof(SelectedDate)); OnPropertyChanged(nameof(WeekStart)); OnPropertyChanged(nameof(WeekRangeText)); await LoadAsync(false, token); }

    private async global::System.Threading.Tasks.Task LoadAsync(bool refresh, CancellationToken token)
    {
        if (!_active || !CanRead || _disposed) return;
        _requestCancellation?.Cancel(); _requestCancellation?.Dispose();
        _requestCancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        var generation = Interlocked.Increment(ref _generation); var ct = _requestCancellation.Token;
        if (!refresh) Days = [];
        var hadData = Days.Count > 0; State = refresh && hadData ? CalendarScreenState.Refreshing : CalendarScreenState.Loading;
        Message = refresh && hadData ? "Обновляем календарь; подтверждённые данные остаются видимыми." : "Загрузка расписания…";
        try
        {
            var (firstDate, lastDateExclusive) = GetVisibleDateRange(_selectedDate, ViewMode);
            var (fromUtc, toUtc) = GetUtcRange(firstDate, lastDateExclusive, _timeZone);
            var scheduleTask = _client.GetScheduleAsync(fromUtc, toUtc, _timeZone.Id, ct);
            var conflictsTask = _client.GetConflictsAsync(fromUtc, toUtc, ct);
            await global::System.Threading.Tasks.Task.WhenAll(scheduleTask, conflictsTask);
            if (!_active || generation != _generation || ct.IsCancellationRequested) return;
            if (scheduleTask.Result is DesktopCalendarResult<DesktopSchedulePage>.Succeeded schedule
                && conflictsTask.Result is DesktopCalendarResult<IReadOnlyList<DesktopScheduleConflict>>.Succeeded conflicts)
            {
                Apply(schedule.Value, conflicts.Value, firstDate, lastDateExclusive); _lastRefresh = DateTimeOffset.UtcNow; OnPropertyChanged(nameof(LastSuccessfulRefreshText));
                State = schedule.Value.Items.Count == 0 ? CalendarScreenState.Empty : CalendarScreenState.Loaded;
                Message = schedule.Value.Items.Count == 0 ? "В выбранном периоде нет записей." : null;
                Announcement = schedule.Value.Items.Count == 0 ? "В выбранном периоде нет записей." : $"Загружено записей: {schedule.Value.Items.Count}.";
                return;
            }
            if (scheduleTask.Result is not DesktopCalendarResult<DesktopSchedulePage>.Succeeded) HandleFailure(scheduleTask.Result, hadData);
            else HandleFailure(conflictsTask.Result, hadData);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (InvalidTimeZoneException)
        {
            if (generation == _generation && _active)
            {
                State = CalendarScreenState.Error;
                Message = "Не удалось определить корректные границы выбранного периода.";
            }
        }
        catch (ArgumentException)
        {
            if (generation == _generation && _active)
            {
                State = CalendarScreenState.Error;
                Message = "Не удалось определить корректные границы выбранного периода.";
            }
        }
    }

    private void Apply(DesktopSchedulePage page, IReadOnlyList<DesktopScheduleConflict> conflicts, DateOnly firstDate, DateOnly lastDateExclusive)
    {
        var byId = conflicts.SelectMany(c => new[] { (c.LeftObjectId, c), (c.RightObjectId, c) }).GroupBy(x => x.Item1).ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.c.Severity).First().c);
        var items = page.Items.Select(i => new CalendarItemViewModel(i, byId.GetValueOrDefault(i.ObjectId), _timeZone)).ToArray();
        Days = Enumerable.Range(0, lastDateExclusive.DayNumber - firstDate.DayNumber).Select(offset =>
        {
            var date = firstDate.AddDays(offset);
            return new CalendarDayViewModel(date, items.Where(i => i.AppearsOn(date, _timeZone)), date.Month == _selectedDate.Month, _timeZone, _clock());
        }).ToArray();
        ClearSelection();
    }

    private void HandleFailure<T>(DesktopCalendarResult<T> result, bool hadData)
    {
        Message = result switch
        {
            DesktopCalendarResult<T>.Forbidden => "Нет права Calendar.Read для просмотра расписания.",
            DesktopCalendarResult<T>.AuthenticationFailure => "Сессия завершена. Войдите снова.",
            DesktopCalendarResult<T>.ValidationFailure v => v.Message,
            DesktopCalendarResult<T>.MalformedResponse => "Сервер вернул некорректное расписание.",
            _ => hadData ? "Не удалось обновить календарь. Показаны последние подтверждённые данные." : "Сервер календаря временно недоступен.",
        };
        State = result is DesktopCalendarResult<T>.Forbidden ? CalendarScreenState.Forbidden
            : result is DesktopCalendarResult<T>.AuthenticationFailure ? CalendarScreenState.SessionEnded
            : CalendarScreenState.Error;
        if (State == CalendarScreenState.SessionEnded)
        {
            _sessionAllowsWrites = false;
            OnPropertyChanged(nameof(CanCreate)); OnPropertyChanged(nameof(CanEdit)); RaiseCommands();
        }
        if (State == CalendarScreenState.SessionEnded) ClearProtectedData(preserveEditor: true);
        else if (!hadData || State == CalendarScreenState.Forbidden) ClearProtectedData();
        Announcement = Message;
    }

    private async global::System.Threading.Tasks.Task LoadSelectedEventAsync(CalendarItemViewModel? item)
    {
        SelectedEvent = null;
        if (item?.IsCalendarEvent != true || !_active) return;
        var generation = _generation;
        DesktopCalendarResult<DesktopCalendarEvent> result;
        try { result = await _client.GetEventAsync(item.Id, _requestCancellation?.Token ?? CancellationToken.None); }
        catch (OperationCanceledException) { return; }
        catch
        {
            if (_active && generation == _generation && ReferenceEquals(item, SelectedItem)) Message = "Не удалось загрузить детали события.";
            return;
        }
        if (!_active || generation != _generation || !ReferenceEquals(item, SelectedItem)) return;
        if (result is DesktopCalendarResult<DesktopCalendarEvent>.Succeeded success) { SelectedEvent = success.Value; Announcement = $"Открыто событие {success.Value.Title}."; }
        else if (result is DesktopCalendarResult<DesktopCalendarEvent>.Forbidden or DesktopCalendarResult<DesktopCalendarEvent>.AuthenticationFailure) HandleFailure(result, Days.Count > 0);
        else Message = "Не удалось загрузить детали события.";
    }

    private async global::System.Threading.Tasks.Task SaveAsync(CancellationToken token)
    {
        var editor = Editor;
        if (!CanSave || editor is null || !editor.TryBuild(_timeZone, out var command)) return;
        RollbackMessage = null;
        var generation = _generation;
        DesktopCalendarResult<DesktopCalendarEvent> result;
        _saving = true; editor.IsSaving = true; OnPropertyChanged(nameof(IsBusy)); OnPropertyChanged(nameof(IsSaving)); RaiseCommands();
        try
        {
            result = editor.Source is null
            ? await _client.CreateEventAsync(command, token)
            : await _client.UpdateEventAsync(editor.Source.Id, editor.Source.Version, command, token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { return; }
        finally { _saving = false; editor.IsSaving = false; OnPropertyChanged(nameof(IsBusy)); OnPropertyChanged(nameof(IsSaving)); RaiseCommands(); }
        if (result is DesktopCalendarResult<DesktopCalendarEvent>.Succeeded success)
        {
            if (ReferenceEquals(editor, Editor)) Editor = null;
            if (_active && _sessionAllowsWrites && generation == _generation) await LoadAsync(true, token);
            Announcement = $"Событие «{success.Value.Title}» сохранено.";
            return;
        }
        if (!_active || !_sessionAllowsWrites || generation != _generation || !ReferenceEquals(editor, Editor)) return;
        if (result is DesktopCalendarResult<DesktopCalendarEvent>.VersionConflict)
        {
            RollbackMessage = "Сохранение отменено: подтверждённая версия календаря восстановлена, локальный черновик сохранён.";
            var latest = await _client.GetEventAsync(editor.Source!.Id, token);
            if (_active && _sessionAllowsWrites && generation == _generation && ReferenceEquals(editor, Editor)
                && latest is DesktopCalendarResult<DesktopCalendarEvent>.Succeeded current)
            {
                SelectedEvent = current.Value;
                editor.RebaseSource(current.Value);
                editor.ValidationMessage = "Событие изменено другим пользователем. Серверная версия обновлена, ваш черновик сохранён. Проверьте поля и нажмите «Сохранить» повторно.";
            }
            else if (ReferenceEquals(editor, Editor))
            {
                editor.ValidationMessage = "Событие изменено другим пользователем. Ваш черновик сохранён; обновите серверную версию и повторите сохранение.";
            }
        }
        else if (result is DesktopCalendarResult<DesktopCalendarEvent>.ValidationFailure validation)
            editor.ValidationMessage = validation.Message;
        else if (result is DesktopCalendarResult<DesktopCalendarEvent>.Forbidden or DesktopCalendarResult<DesktopCalendarEvent>.AuthenticationFailure) HandleFailure(result, Days.Count > 0);
        else
        {
            RollbackMessage = "Сохранение отменено: подтверждённая версия календаря не изменена, локальный черновик сохранён.";
            editor.ValidationMessage = "Не удалось сохранить событие. Повторите попытку.";
        }
    }

    private void ClearSelection() { SelectedItem = null; SelectedEvent = null; }
    private void UpdateSelectionFlags()
    {
        foreach (var item in Days.SelectMany(day => day.Items).Distinct()) item.IsSelected = item.Id == SelectedItem?.Id;
    }
    private void ClearProtectedData(bool preserveEditor = false)
    {
        Recurrence?.SetAccess(_capabilities, false);
        Days = Array.Empty<CalendarDayViewModel>();
        ClearSelection();
        if (!preserveEditor) Editor = null;
    }
    private void RaiseCommands() { RefreshCommand.RaiseCanExecuteChanged(); PreviousWeekCommand.RaiseCanExecuteChanged(); NextWeekCommand.RaiseCanExecuteChanged(); TodayCommand.RaiseCanExecuteChanged(); DayModeCommand.RaiseCanExecuteChanged(); WeekModeCommand.RaiseCanExecuteChanged(); MonthModeCommand.RaiseCanExecuteChanged(); SelectItemCommand.RaiseCanExecuteChanged(); NewEventCommand.RaiseCanExecuteChanged(); EditEventCommand.RaiseCanExecuteChanged(); SaveEventCommand.RaiseCanExecuteChanged(); CancelEditorCommand.RaiseCanExecuteChanged(); }
    internal static DateOnly StartOfWeek(DateOnly date) => date.AddDays(-(((int)date.DayOfWeek + 6) % 7));
    internal static (DateOnly FirstDate, DateOnly LastDateExclusive) GetVisibleDateRange(DateOnly anchor, CalendarViewMode mode) => mode switch
    {
        CalendarViewMode.Day => (anchor, anchor.AddDays(1)),
        CalendarViewMode.Month => GetMonthVisibleDateRange(anchor),
        _ => (StartOfWeek(anchor), StartOfWeek(anchor).AddDays(7)),
    };
    private static (DateOnly FirstDate, DateOnly LastDateExclusive) GetMonthVisibleDateRange(DateOnly anchor)
    {
        var first = StartOfWeek(new DateOnly(anchor.Year, anchor.Month, 1));
        var nextMonth = new DateOnly(anchor.Year, anchor.Month, 1).AddMonths(1);
        var last = StartOfWeek(nextMonth);
        return (first, last == nextMonth ? last : last.AddDays(7));
    }
    internal static (DateTimeOffset FromUtc, DateTimeOffset ToUtc) GetUtcRange(DateOnly weekStart, TimeZoneInfo timeZone)
        => GetUtcRange(weekStart, weekStart.AddDays(7), timeZone);
    internal static (DateTimeOffset FromUtc, DateTimeOffset ToUtc) GetUtcRange(DateOnly firstDate, DateOnly lastDateExclusive, TimeZoneInfo timeZone)
    {
        var from = DateTime.SpecifyKind(firstDate.ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified);
        var to = DateTime.SpecifyKind(lastDateExclusive.ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified);
        return (new(TimeZoneInfo.ConvertTimeToUtc(from, timeZone), TimeSpan.Zero), new(TimeZoneInfo.ConvertTimeToUtc(to, timeZone), TimeSpan.Zero));
    }
    public void Dispose()
    {
        if (_disposed) return; _disposed = true; Deactivate(); Recurrence?.Dispose(); _requestCancellation?.Dispose();
        foreach (var command in new[] { RefreshCommand, PreviousWeekCommand, NextWeekCommand, TodayCommand, DayModeCommand, WeekModeCommand, MonthModeCommand, SelectItemCommand, NewEventCommand, EditEventCommand, SaveEventCommand, CancelEditorCommand }) command.Dispose();
    }
}
