using System.Collections.ObjectModel;
using System.Globalization;
using Task.Desktop.Calendar;

namespace Task.Desktop.ViewModels;

public enum CalendarScreenState { Inactive, Loading, Loaded, Empty, Refreshing, Error, Forbidden, SessionEnded }

public sealed class CalendarItemViewModel
{
    private static readonly CultureInfo RussianCulture = CultureInfo.GetCultureInfo("ru-RU");
    public CalendarItemViewModel(DesktopScheduleItem source, DesktopScheduleConflict? conflict)
    {
        Source = source;
        TypeText = source.ItemType == DesktopScheduleItemType.CalendarEvent ? "Событие" : "Задача";
        TimeText = source.IsAllDay ? "Весь день" : source.StartAtUtc is null
            ? "Точка во времени"
            : $"{source.StartAtUtc.Value.ToLocalTime():HH:mm}–{source.EndAtUtc!.Value.ToLocalTime():HH:mm}";
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
            DesktopConflictSeverity.Blocking => "Блокирующее пересечение расписания",
            DesktopConflictSeverity.Warning => "Предупреждение о пересечении расписания",
            DesktopConflictSeverity.Info => "Пересечение расписания",
            _ => null,
        };
        AutomationName = string.Join(". ", new[] { TypeText, source.Title, TimeText, StatusText, PriorityText, ConflictText }.Where(x => x is not null));
    }
    public DesktopScheduleItem Source { get; }
    public Guid Id => Source.ObjectId;
    public string Title => Source.Title;
    public string TypeText { get; }
    public string TimeText { get; }
    public string StatusText { get; }
    public string? PriorityText { get; }
    public string? ConflictText { get; }
    public bool HasConflict => ConflictText is not null;
    public bool IsCalendarEvent => Source.ItemType == DesktopScheduleItemType.CalendarEvent;
    public string AutomationName { get; }
}

public sealed class CalendarDayViewModel
{
    public CalendarDayViewModel(DateOnly date, IEnumerable<CalendarItemViewModel> items)
    {
        Date = date;
        Items = items.OrderBy(x => x.Source.IsAllDay ? 0 : 1).ThenBy(x => x.Source.StartAtUtc).ToArray();
        Header = date.ToDateTime(TimeOnly.MinValue).ToString("ddd, d MMM", CultureInfo.GetCultureInfo("ru-RU"));
    }
    public DateOnly Date { get; }
    public string Header { get; }
    public IReadOnlyList<CalendarItemViewModel> Items { get; }
    public bool IsToday => Date == DateOnly.FromDateTime(DateTime.Today);
    public string AutomationName => $"{Header}. {Items.Count} элементов расписания.";
}

public sealed class CalendarEventEditorViewModel : ViewModelBase
{
    private string _title = string.Empty;
    private string? _description;
    private DateTime? _date = DateTime.Today;
    private bool _isAllDay;
    private string _startTime = "09:00";
    private string _endTime = "10:00";
    private string? _validationMessage;
    public CalendarEventEditorViewModel(DesktopCalendarEvent? source)
    {
        Source = source;
        if (source is null) return;
        _title = source.Title; _description = source.Description; _date = source.EventDate.ToDateTime(TimeOnly.MinValue);
        _isAllDay = source.IsAllDay;
        if (source.StartAtUtc.HasValue) _startTime = source.StartAtUtc.Value.ToLocalTime().ToString("HH:mm");
        if (source.EndAtUtc.HasValue) _endTime = source.EndAtUtc.Value.ToLocalTime().ToString("HH:mm");
    }
    public DesktopCalendarEvent? Source { get; }
    public bool IsNew => Source is null;
    public string Title { get => _title; set { if (SetProperty(ref _title, value)) ValidationMessage = null; } }
    public string? Description { get => _description; set => SetProperty(ref _description, value); }
    public DateTime? Date { get => _date; set { if (SetProperty(ref _date, value)) ValidationMessage = null; } }
    public bool IsAllDay { get => _isAllDay; set { if (SetProperty(ref _isAllDay, value)) ValidationMessage = null; } }
    public string StartTime { get => _startTime; set { if (SetProperty(ref _startTime, value)) ValidationMessage = null; } }
    public string EndTime { get => _endTime; set { if (SetProperty(ref _endTime, value)) ValidationMessage = null; } }
    public string? ValidationMessage { get => _validationMessage; set => SetProperty(ref _validationMessage, value); }

    public bool TryBuild(TimeZoneInfo timeZone, out DesktopCalendarEventCommand command)
    {
        command = null!;
        if (string.IsNullOrWhiteSpace(Title) || Title.Trim().Length > 500 || !Date.HasValue)
        { ValidationMessage = "Укажите название до 500 символов и дату."; return false; }
        var eventDate = DateOnly.FromDateTime(Date.Value);
        DateTimeOffset? start = null, end = null;
        if (!IsAllDay)
        {
            if (!TimeOnly.TryParseExact(StartTime, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var startTime)
                || !TimeOnly.TryParseExact(EndTime, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var endTime)
                || endTime <= startTime)
            { ValidationMessage = "Введите время в формате ЧЧ:ММ; окончание должно быть позже начала."; return false; }
            var startLocal = DateTime.SpecifyKind(eventDate.ToDateTime(startTime), DateTimeKind.Unspecified);
            var endLocal = DateTime.SpecifyKind(eventDate.ToDateTime(endTime), DateTimeKind.Unspecified);
            if (timeZone.IsInvalidTime(startLocal) || timeZone.IsInvalidTime(endLocal))
            { ValidationMessage = "Это время отсутствует из-за перехода часового пояса."; return false; }
            start = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(startLocal, timeZone), TimeSpan.Zero);
            end = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(endLocal, timeZone), TimeSpan.Zero);
        }
        command = new(Source?.ProjectId, Title.Trim(), string.IsNullOrWhiteSpace(Description) ? null : Description.Trim(),
            eventDate, IsAllDay, start, end, timeZone.Id, Source?.Status ?? "scheduled");
        return true;
    }
}

public sealed class CalendarViewModel : ViewModelBase, IDisposable
{
    private readonly IDesktopCalendarApiClient _client;
    private readonly TimeZoneInfo _timeZone;
    private readonly HashSet<string> _capabilities;
    private CancellationTokenSource? _requestCancellation;
    private long _generation;
    private bool _active;
    private bool _sessionAllowsWrites = true;
    private bool _disposed;
    private CalendarScreenState _state = CalendarScreenState.Inactive;
    private DateOnly _weekStart;
    private IReadOnlyList<CalendarDayViewModel> _days = Array.Empty<CalendarDayViewModel>();
    private CalendarItemViewModel? _selectedItem;
    private DesktopCalendarEvent? _selectedEvent;
    private string? _message;
    private string? _announcement;
    private DateTimeOffset? _lastRefresh;
    private CalendarEventEditorViewModel? _editor;

    public CalendarViewModel(IDesktopCalendarApiClient client, IEnumerable<string>? capabilities = null, TimeZoneInfo? timeZone = null, DateTime? today = null)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _timeZone = timeZone ?? TimeZoneInfo.Local;
        _capabilities = new(capabilities ?? [], StringComparer.Ordinal);
        _weekStart = StartOfWeek(DateOnly.FromDateTime(today ?? DateTime.Today));
        RefreshCommand = new AsyncCommand((_, token) => LoadAsync(true, token), _ => IsActive && CanRead && !IsBusy);
        PreviousWeekCommand = new AsyncCommand((_, token) => MoveWeekAsync(-7, token), _ => IsActive && CanRead && !IsBusy);
        NextWeekCommand = new AsyncCommand((_, token) => MoveWeekAsync(7, token), _ => IsActive && CanRead && !IsBusy);
        TodayCommand = new AsyncCommand((_, token) => GoTodayAsync(token), _ => IsActive && CanRead && !IsBusy);
        NewEventCommand = new AsyncCommand((_, _) => { Editor = new(null); return global::System.Threading.Tasks.Task.CompletedTask; }, _ => CanCreate && Editor is null);
        EditEventCommand = new AsyncCommand((_, _) => { if (SelectedEvent is not null) Editor = new(SelectedEvent); return global::System.Threading.Tasks.Task.CompletedTask; }, _ => CanEdit);
        SaveEventCommand = new AsyncCommand((_, token) => SaveAsync(token), _ => Editor is not null && !IsBusy);
        CancelEditorCommand = new AsyncCommand((_, _) => { Editor = null; return global::System.Threading.Tasks.Task.CompletedTask; }, _ => Editor is not null && !IsBusy);
    }

    public IReadOnlyList<CalendarDayViewModel> Days { get => _days; private set => SetProperty(ref _days, value); }
    public DateOnly WeekStart => _weekStart;
    public string WeekRangeText => $"{_weekStart.ToDateTime(TimeOnly.MinValue):d MMM} — {_weekStart.AddDays(6).ToDateTime(TimeOnly.MinValue):d MMM yyyy}";
    public CalendarScreenState State { get => _state; private set { if (SetProperty(ref _state, value)) { OnPropertyChanged(nameof(IsBusy)); OnPropertyChanged(nameof(ShowMessage)); RaiseCommands(); } } }
    public bool IsActive => _active;
    public bool IsBusy => State is CalendarScreenState.Loading or CalendarScreenState.Refreshing;
    public bool ShowMessage => State is CalendarScreenState.Loading or CalendarScreenState.Error or CalendarScreenState.Forbidden or CalendarScreenState.SessionEnded || Message is not null;
    public string? Message { get => _message; private set { if (SetProperty(ref _message, value)) OnPropertyChanged(nameof(ShowMessage)); } }
    public string? Announcement { get => _announcement; private set => SetProperty(ref _announcement, value); }
    public string LastSuccessfulRefreshText => _lastRefresh.HasValue ? $"Обновлено {_lastRefresh.Value.ToLocalTime():HH:mm}" : "Ещё не обновлялось";
    public bool CanRead => _capabilities.Contains("Calendar.Read");
    public bool CanCreate => _active && _sessionAllowsWrites && _capabilities.Contains("CalendarEvent.Create") && Editor is null;
    public bool CanEdit => _active && _sessionAllowsWrites && _capabilities.Contains("CalendarEvent.Update") && SelectedEvent is not null && Editor is null;
    public string WriteAccessText => CanCreate || _capabilities.Contains("CalendarEvent.Update") ? "Изменения календаря синхронизируются с сервером компании." : "Календарь доступен только для просмотра.";
    public CalendarItemViewModel? SelectedItem { get => _selectedItem; set { if (SetProperty(ref _selectedItem, value)) _ = LoadSelectedEventAsync(value); } }
    public DesktopCalendarEvent? SelectedEvent { get => _selectedEvent; private set { if (SetProperty(ref _selectedEvent, value)) { OnPropertyChanged(nameof(CanEdit)); RaiseCommands(); } } }
    public CalendarEventEditorViewModel? Editor { get => _editor; private set { if (SetProperty(ref _editor, value)) { OnPropertyChanged(nameof(CanCreate)); OnPropertyChanged(nameof(CanEdit)); RaiseCommands(); } } }
    public AsyncCommand RefreshCommand { get; }
    public AsyncCommand PreviousWeekCommand { get; }
    public AsyncCommand NextWeekCommand { get; }
    public AsyncCommand TodayCommand { get; }
    public AsyncCommand NewEventCommand { get; }
    public AsyncCommand EditEventCommand { get; }
    public AsyncCommand SaveEventCommand { get; }
    public AsyncCommand CancelEditorCommand { get; }

    public void Activate()
    {
        if (_disposed || _active) return;
        _active = true; OnPropertyChanged(nameof(IsActive)); OnPropertyChanged(nameof(CanCreate)); RaiseCommands();
        if (!CanRead) { ClearProtectedData(); State = CalendarScreenState.Forbidden; Message = "Нет права Calendar.Read для просмотра расписания."; return; }
        _ = LoadAsync(false, CancellationToken.None);
    }

    public async global::System.Threading.Tasks.Task ActivateAsync()
    {
        if (_disposed || _active) return;
        _active = true; OnPropertyChanged(nameof(IsActive)); OnPropertyChanged(nameof(CanCreate)); RaiseCommands();
        if (!CanRead) { ClearProtectedData(); State = CalendarScreenState.Forbidden; Message = "Нет права Calendar.Read для просмотра расписания."; return; }
        await LoadAsync(false, CancellationToken.None);
    }

    public void Deactivate()
    {
        if (!_active) return;
        _active = false; Interlocked.Increment(ref _generation); _requestCancellation?.Cancel();
        State = CalendarScreenState.Inactive; Editor = null; OnPropertyChanged(nameof(IsActive)); OnPropertyChanged(nameof(CanCreate)); RaiseCommands();
    }

    public void UpdateCapabilities(IEnumerable<string>? capabilities)
    {
        _capabilities.Clear(); foreach (var capability in capabilities ?? []) _capabilities.Add(capability);
        OnPropertyChanged(nameof(CanRead)); OnPropertyChanged(nameof(CanCreate)); OnPropertyChanged(nameof(CanEdit)); OnPropertyChanged(nameof(WriteAccessText)); RaiseCommands();
        if (!CanRead) { Interlocked.Increment(ref _generation); _requestCancellation?.Cancel(); ClearProtectedData(); State = CalendarScreenState.Forbidden; Message = "Право Calendar.Read отозвано. Данные календаря очищены."; }
        else if (_active && State == CalendarScreenState.Forbidden) _ = LoadAsync(false, CancellationToken.None);
    }

    public void UpdateSessionState(bool signedIn)
    {
        _sessionAllowsWrites = signedIn;
        OnPropertyChanged(nameof(CanCreate)); OnPropertyChanged(nameof(CanEdit)); RaiseCommands();
        if (signedIn) return;
        Interlocked.Increment(ref _generation); _requestCancellation?.Cancel(); ClearProtectedData(); State = CalendarScreenState.SessionEnded;
        Message = "Сессия завершена. Данные календаря очищены.";
    }

    private async global::System.Threading.Tasks.Task MoveWeekAsync(int days, CancellationToken token)
    { _weekStart = _weekStart.AddDays(days); OnPropertyChanged(nameof(WeekStart)); OnPropertyChanged(nameof(WeekRangeText)); await LoadAsync(false, token); }
    private async global::System.Threading.Tasks.Task GoTodayAsync(CancellationToken token)
    { _weekStart = StartOfWeek(DateOnly.FromDateTime(DateTime.Today)); OnPropertyChanged(nameof(WeekStart)); OnPropertyChanged(nameof(WeekRangeText)); await LoadAsync(false, token); }

    private async global::System.Threading.Tasks.Task LoadAsync(bool refresh, CancellationToken token)
    {
        if (!_active || !CanRead || _disposed) return;
        _requestCancellation?.Cancel(); _requestCancellation?.Dispose();
        _requestCancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        var generation = Interlocked.Increment(ref _generation); var ct = _requestCancellation.Token;
        var hadData = Days.Count > 0; State = refresh && hadData ? CalendarScreenState.Refreshing : CalendarScreenState.Loading;
        Message = refresh && hadData ? "Обновляем календарь; подтверждённые данные остаются видимыми." : "Загрузка расписания…";
        var (fromUtc, toUtc) = GetUtcRange(_weekStart, _timeZone);
        try
        {
            var scheduleTask = _client.GetScheduleAsync(fromUtc, toUtc, _timeZone.Id, ct);
            var conflictsTask = _client.GetConflictsAsync(fromUtc, toUtc, ct);
            await global::System.Threading.Tasks.Task.WhenAll(scheduleTask, conflictsTask);
            if (!_active || generation != _generation || ct.IsCancellationRequested) return;
            if (scheduleTask.Result is DesktopCalendarResult<DesktopSchedulePage>.Succeeded schedule
                && conflictsTask.Result is DesktopCalendarResult<IReadOnlyList<DesktopScheduleConflict>>.Succeeded conflicts)
            {
                Apply(schedule.Value, conflicts.Value); _lastRefresh = DateTimeOffset.UtcNow; OnPropertyChanged(nameof(LastSuccessfulRefreshText));
                State = schedule.Value.Items.Count == 0 ? CalendarScreenState.Empty : CalendarScreenState.Loaded;
                Announcement = schedule.Value.Items.Count == 0 ? "В выбранной неделе нет записей." : $"Загружено записей: {schedule.Value.Items.Count}.";
                return;
            }
            HandleFailure(scheduleTask.Result, hadData);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
    }

    private void Apply(DesktopSchedulePage page, IReadOnlyList<DesktopScheduleConflict> conflicts)
    {
        var byId = conflicts.SelectMany(c => new[] { (c.LeftObjectId, c), (c.RightObjectId, c) }).GroupBy(x => x.Item1).ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.c.Severity).First().c);
        var items = page.Items.Select(i => new CalendarItemViewModel(i, byId.GetValueOrDefault(i.ObjectId))).ToArray();
        Days = Enumerable.Range(0, 7).Select(offset => { var date = _weekStart.AddDays(offset); return new CalendarDayViewModel(date, items.Where(i => i.Source.LocalDate == date)); }).ToArray();
        SelectedItem = null; SelectedEvent = null;
    }

    private void HandleFailure(DesktopCalendarResult<DesktopSchedulePage> result, bool hadData)
    {
        Message = result switch
        {
            DesktopCalendarResult<DesktopSchedulePage>.Forbidden => "Нет права Calendar.Read для просмотра расписания.",
            DesktopCalendarResult<DesktopSchedulePage>.AuthenticationFailure => "Сессия завершена. Войдите снова.",
            DesktopCalendarResult<DesktopSchedulePage>.ValidationFailure v => v.Message,
            DesktopCalendarResult<DesktopSchedulePage>.MalformedResponse => "Сервер вернул некорректное расписание.",
            _ => hadData ? "Не удалось обновить календарь. Показаны последние подтверждённые данные." : "Сервер календаря временно недоступен.",
        };
        State = result is DesktopCalendarResult<DesktopSchedulePage>.Forbidden ? CalendarScreenState.Forbidden
            : result is DesktopCalendarResult<DesktopSchedulePage>.AuthenticationFailure ? CalendarScreenState.SessionEnded
            : CalendarScreenState.Error;
        if (!hadData || State is CalendarScreenState.Forbidden or CalendarScreenState.SessionEnded) ClearProtectedData();
        Announcement = Message;
    }

    private async global::System.Threading.Tasks.Task LoadSelectedEventAsync(CalendarItemViewModel? item)
    {
        SelectedEvent = null;
        if (item?.IsCalendarEvent != true || !_active) return;
        var generation = _generation;
        var result = await _client.GetEventAsync(item.Id, _requestCancellation?.Token ?? CancellationToken.None);
        if (!_active || generation != _generation || !ReferenceEquals(item, SelectedItem)) return;
        if (result is DesktopCalendarResult<DesktopCalendarEvent>.Succeeded success) { SelectedEvent = success.Value; Announcement = $"Открыто событие {success.Value.Title}."; }
        else Message = "Не удалось загрузить детали события.";
    }

    private async global::System.Threading.Tasks.Task SaveAsync(CancellationToken token)
    {
        if (Editor is null || !Editor.TryBuild(_timeZone, out var command)) return;
        DesktopCalendarResult<DesktopCalendarEvent> result = Editor.Source is null
            ? await _client.CreateEventAsync(command, token)
            : await _client.UpdateEventAsync(Editor.Source.Id, Editor.Source.Version, command, token);
        if (result is DesktopCalendarResult<DesktopCalendarEvent>.Succeeded success)
        { Editor = null; await LoadAsync(true, token); Announcement = $"Событие «{success.Value.Title}» сохранено."; }
        else if (result is DesktopCalendarResult<DesktopCalendarEvent>.VersionConflict)
            Editor.ValidationMessage = "Событие изменилось на сервере. Закройте форму, обновите календарь и повторите изменение.";
        else if (result is DesktopCalendarResult<DesktopCalendarEvent>.ValidationFailure validation)
            Editor.ValidationMessage = validation.Message;
        else Editor.ValidationMessage = "Не удалось сохранить событие. Повторите попытку.";
    }

    private void ClearProtectedData() { Days = Array.Empty<CalendarDayViewModel>(); SelectedItem = null; SelectedEvent = null; Editor = null; }
    private void RaiseCommands() { RefreshCommand.RaiseCanExecuteChanged(); PreviousWeekCommand.RaiseCanExecuteChanged(); NextWeekCommand.RaiseCanExecuteChanged(); TodayCommand.RaiseCanExecuteChanged(); NewEventCommand.RaiseCanExecuteChanged(); EditEventCommand.RaiseCanExecuteChanged(); SaveEventCommand.RaiseCanExecuteChanged(); CancelEditorCommand.RaiseCanExecuteChanged(); }
    internal static DateOnly StartOfWeek(DateOnly date) => date.AddDays(-(((int)date.DayOfWeek + 6) % 7));
    internal static (DateTimeOffset FromUtc, DateTimeOffset ToUtc) GetUtcRange(DateOnly weekStart, TimeZoneInfo timeZone)
    {
        var from = DateTime.SpecifyKind(weekStart.ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified);
        var to = DateTime.SpecifyKind(weekStart.AddDays(7).ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified);
        return (new(TimeZoneInfo.ConvertTimeToUtc(from, timeZone), TimeSpan.Zero), new(TimeZoneInfo.ConvertTimeToUtc(to, timeZone), TimeSpan.Zero));
    }
    public void Dispose()
    {
        if (_disposed) return; _disposed = true; Deactivate(); _requestCancellation?.Dispose();
        foreach (var command in new[] { RefreshCommand, PreviousWeekCommand, NextWeekCommand, TodayCommand, NewEventCommand, EditEventCommand, SaveEventCommand, CancelEditorCommand }) command.Dispose();
    }
}
