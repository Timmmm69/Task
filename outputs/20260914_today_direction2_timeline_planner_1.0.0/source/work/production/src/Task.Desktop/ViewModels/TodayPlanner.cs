using System.Globalization;
using Task.Desktop.Calendar;
using Task.Desktop.TaskApi;

namespace Task.Desktop.ViewModels;

public sealed record TodayHourMarker(string Label, double Top);

/// <summary>
/// Display-only projection shared by the timeline, queue and inspector.
/// The wrapped API view model remains the navigation command parameter.
/// </summary>
public sealed class TodayPlannerItemViewModel : ViewModelBase
{
    private const double HourHeight = 69d;
    private bool _isSelected;

    private TodayPlannerItemViewModel(
        object source,
        Guid id,
        string title,
        string kindText,
        string timeText,
        string statusText,
        string priorityText,
        string statusIconKey,
        string priorityIconKey,
        TaskVisualTone priorityTone,
        string deadlineText,
        string projectText,
        string description,
        string createdText,
        string updatedText,
        double timelineTop,
        double timelineHeight,
        bool isOverdue)
    {
        Source = source;
        Id = id;
        Title = title;
        KindText = kindText;
        TimeText = timeText;
        StatusText = statusText;
        PriorityText = priorityText;
        StatusIconKey = statusIconKey;
        PriorityIconKey = priorityIconKey;
        PriorityTone = priorityTone;
        DeadlineText = deadlineText;
        ProjectText = projectText;
        Description = description;
        CreatedText = createdText;
        UpdatedText = updatedText;
        TimelineTop = timelineTop;
        TimelineHeight = timelineHeight;
        IsOverdue = isOverdue;
        AutomationName = $"{title}. {timeText}. {statusText}. Приоритет: {priorityText}. {deadlineText}.";
    }

    public object Source { get; }
    public Guid Id { get; }
    public string Title { get; }
    public string KindText { get; }
    public string TimeText { get; }
    public string StatusText { get; }
    public string PriorityText { get; }
    public string StatusIconKey { get; }
    public string PriorityIconKey { get; }
    public TaskVisualTone PriorityTone { get; }
    public string DeadlineText { get; }
    public string ProjectText { get; }
    public string Description { get; }
    public string CreatedText { get; }
    public string UpdatedText { get; }
    public double TimelineTop { get; }
    public double TimelineHeight { get; }
    public int TimelineLane { get; internal set; }
    public int TimelineLaneCount { get; internal set; } = 1;
    public bool IsOverdue { get; }
    public bool IsHighPriority => PriorityTone is TaskVisualTone.Critical;
    public bool IsLowPriority => PriorityTone is TaskVisualTone.Success;
    public string AutomationName { get; }

    public bool IsSelected
    {
        get => _isSelected;
        internal set => SetProperty(ref _isSelected, value);
    }

    internal static TodayPlannerItemViewModel FromCalendar(
        CalendarItemViewModel item,
        TaskItemViewModel? taskDetails,
        TimeZoneInfo timeZone,
        DateTimeOffset now)
    {
        var source = item.Source;
        var start = source.StartAtUtc.HasValue
            ? TimeZoneInfo.ConvertTime(source.StartAtUtc.Value, timeZone)
            : (DateTimeOffset?)null;
        var end = source.EndAtUtc.HasValue
            ? TimeZoneInfo.ConvertTime(source.EndAtUtc.Value, timeZone)
            : (DateTimeOffset?)null;
        var durationMinutes = start.HasValue
            ? Math.Max(30d, (end - start)?.TotalMinutes
                ?? taskDetails?.Source.Card?.PlannedDurationMinutes
                ?? 45d)
            : 45d;
        var top = start.HasValue
            ? start.Value.TimeOfDay.TotalMinutes / 60d * HourHeight
            : 0d;
        var priorityText = taskDetails?.PriorityText
            ?? NormalizeCalendarPriority(item.PriorityText);
        var priorityTone = taskDetails?.PriorityTone
            ?? CalendarPriorityTone(source.Priority);
        var projectId = taskDetails?.Source.Card?.ProjectId ?? source.ProjectId;

        return new TodayPlannerItemViewModel(
            item,
            item.Id,
            item.Title,
            item.TypeText,
            item.TimeText,
            taskDetails?.StatusText ?? item.StatusText,
            priorityText,
            taskDetails?.StatusIconKey ?? ResolveStatusIconKey(item.StatusText),
            taskDetails?.PriorityIconKey ?? CalendarPriorityIconKey(source.Priority),
            priorityTone,
            taskDetails?.DeadlineText ?? FormatCalendarDeadline(source, timeZone),
            FormatProject(projectId),
            FirstContent(taskDetails?.Source.Card?.Description, item.Description),
            FormatMoment(taskDetails?.Source.CreatedAtUtc, timeZone, "Не указано"),
            FormatMoment(taskDetails?.Source.UpdatedAtUtc, timeZone, "Не указано"),
            top,
            Math.Max(54d, durationMinutes / 60d * HourHeight - 6d),
            taskDetails?.Source.DeadlineAtUtc < now);
    }

    internal static TodayPlannerItemViewModel FromTask(
        TaskItemViewModel item,
        TimeZoneInfo timeZone,
        DateTimeOffset now)
    {
        var source = item.Source;
        var start = source.StartAtUtc.HasValue
            ? TimeZoneInfo.ConvertTime(source.StartAtUtc.Value, timeZone)
            : (DateTimeOffset?)null;
        var end = source.DeadlineAtUtc.HasValue
            ? TimeZoneInfo.ConvertTime(source.DeadlineAtUtc.Value, timeZone)
            : (DateTimeOffset?)null;
        var timeText = start.HasValue
            ? end.HasValue && end.Value.Date == start.Value.Date
                ? $"{start:HH:mm}–{end:HH:mm}"
                : $"{start:HH:mm}"
            : "Без точного времени";

        return new TodayPlannerItemViewModel(
            item,
            item.Id,
            item.Title,
            "Задача",
            timeText,
            item.StatusText,
            item.PriorityText,
            item.StatusIconKey,
            item.PriorityIconKey,
            item.PriorityTone,
            item.DeadlineText,
            FormatProject(source.Card?.ProjectId),
            FirstContent(source.Card?.Description),
            FormatMoment(source.CreatedAtUtc, timeZone, "Не указано"),
            FormatMoment(source.UpdatedAtUtc, timeZone, "Не указано"),
            start?.TimeOfDay.TotalMinutes / 60d * HourHeight ?? 0d,
            Math.Max(54d, (source.Card?.PlannedDurationMinutes ?? 45) / 60d * HourHeight - 6d),
            source.DeadlineAtUtc < now);
    }

    private static string FirstContent(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim()
        ?? "Описание не добавлено.";

    private static string FormatProject(Guid? projectId) =>
        projectId.HasValue ? "Проект назначен" : "Без проекта";

    private static string FormatMoment(
        DateTimeOffset? value,
        TimeZoneInfo timeZone,
        string fallback) =>
        value.HasValue
            ? TimeZoneInfo.ConvertTime(value.Value, timeZone)
                .ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture)
            : fallback;

    private static string FormatCalendarDeadline(
        DesktopScheduleItem source,
        TimeZoneInfo timeZone)
    {
        if (source.IsAllDay)
        {
            return "Сегодня, весь день";
        }

        var value = source.EndAtUtc ?? source.StartAtUtc;
        return value.HasValue
            ? $"Сегодня, {TimeZoneInfo.ConvertTime(value.Value, timeZone):HH:mm}"
            : "Без срока";
    }

    private static string NormalizeCalendarPriority(string? value) => value switch
    {
        "Низкий приоритет" => "Низкий",
        "Обычный приоритет" => "Обычный",
        "Высокий приоритет" => "Высокий",
        "Критический приоритет" => "Критический",
        null or "" => "Обычный",
        _ => value,
    };

    private static string ResolveStatusIconKey(string status) => status switch
    {
        "В работе" => "Task.Icon.Status.InProgress",
        "На проверке" => "Task.Icon.Status.Review",
        "Завершена" => "Task.Icon.Status.Completed",
        "Отменено" => "Task.Icon.Status.Cancelled",
        _ => "Task.Icon.Status.New",
    };

    private static string CalendarPriorityIconKey(DesktopCalendarPriority? priority) => priority switch
    {
        DesktopCalendarPriority.Low => "Task.Icon.Priority.Low",
        DesktopCalendarPriority.High => "Task.Icon.Priority.High",
        DesktopCalendarPriority.Critical => "Task.Icon.Priority.Critical",
        _ => "Task.Icon.Priority.Normal",
    };

    private static TaskVisualTone CalendarPriorityTone(DesktopCalendarPriority? priority) => priority switch
    {
        DesktopCalendarPriority.Low => TaskVisualTone.Success,
        DesktopCalendarPriority.High or DesktopCalendarPriority.Critical => TaskVisualTone.Critical,
        _ => TaskVisualTone.Warning,
    };
}

public sealed partial class TodayViewModel
{
    private const double TimelineHourHeight = 69d;
    private IReadOnlyList<TaskItemViewModel> _activeTaskItems = [];
    private IReadOnlyList<TodayPlannerItemViewModel> _timelineItems = [];
    private IReadOnlyList<TodayPlannerItemViewModel> _allDayItems = [];
    private IReadOnlyList<TodayPlannerItemViewModel> _queueItems = [];
    private TodayPlannerItemViewModel? _selectedItem;
    private bool _selectionWasExplicit;
    private bool _isConnected = true;
    private string _currentTimeText = string.Empty;
    private double _currentTimeTop;

    public IReadOnlyList<TodayHourMarker> HourMarkers { get; } =
        Enumerable.Range(0, 24)
            .Select(hour => new TodayHourMarker($"{hour:00}:00", hour * TimelineHourHeight))
            .ToArray();

    public double TimelineCanvasHeight => 24 * TimelineHourHeight;
    public double InitialTimelineOffset => 8 * TimelineHourHeight;
    public IReadOnlyList<TodayPlannerItemViewModel> TimelineItems => _timelineItems;
    public IReadOnlyList<TodayPlannerItemViewModel> AllDayItems => _allDayItems;
    public IReadOnlyList<TodayPlannerItemViewModel> QueueItems => _queueItems;
    public int QueueCount => QueueItems.Count;
    public bool HasQueueItems => QueueCount > 0;
    public bool HasAllDayItems => AllDayItems.Count > 0;
    public bool HasPlannerItems => TimelineItems.Count > 0 || AllDayItems.Count > 0;
    public bool IsConnected => _isConnected;
    public bool IsReadOnly => !IsConnected;
    public bool ShowReadOnlyState => IsReadOnly;
    public bool ShowInitialLoading => State == TodayScreenState.Loading && !HasItems;
    public bool ShowStateBanner => IsReadOnly || State is TodayScreenState.Error
        or TodayScreenState.Forbidden or TodayScreenState.SessionEnded;
    public bool IsStateCritical => !IsReadOnly && State is TodayScreenState.Error
        or TodayScreenState.Forbidden or TodayScreenState.SessionEnded;
    public string StateTitle => IsReadOnly
        ? "Нет подключения к серверу"
        : State switch
        {
            TodayScreenState.Forbidden => "Расписание недоступно",
            TodayScreenState.SessionEnded => "Сессия завершена",
            _ => "Не удалось обновить план",
        };
    public string StateDescription => IsReadOnly
        ? $"Показаны последние подтверждённые данные. Изменения недоступны. {LastSuccessfulRefreshText}."
        : Message ?? "Повторите обновление.";

    public TodayPlannerItemViewModel? SelectedItem
    {
        get => _selectedItem;
        private set
        {
            if (!SetProperty(ref _selectedItem, value)) return;
            UpdateSelectionFlags();
            OnPropertyChanged(nameof(HasSelectedItem));
            OpenSelectedItemCommand.RaiseCanExecuteChanged();
        }
    }

    public bool HasSelectedItem => SelectedItem is not null;
    public string CurrentTimeText => _currentTimeText;
    public double CurrentTimeTop => _currentTimeTop;
    public bool ShowCurrentTime { get; private set; }
    public AsyncCommand SelectItemCommand { get; private set; } = null!;
    public AsyncCommand OpenSelectedItemCommand { get; private set; } = null!;

    private void InitializePlannerCommands()
    {
        SelectItemCommand = new AsyncCommand((parameter, _) =>
        {
            if (parameter is TodayPlannerItemViewModel item)
            {
                _selectionWasExplicit = true;
                SelectedItem = item;
            }
            return global::System.Threading.Tasks.Task.CompletedTask;
        }, parameter => IsActive && _sessionAvailable && parameter is TodayPlannerItemViewModel);
        OpenSelectedItemCommand = new AsyncCommand((_, _) =>
        {
            if (SelectedItem is not null) OpenItemRequested?.Invoke(SelectedItem.Source);
            return global::System.Threading.Tasks.Task.CompletedTask;
        }, _ => IsActive && _sessionAvailable && SelectedItem is not null);
    }

    public void UpdateConnectivity(bool isConnected)
    {
        if (_disposed || _isConnected == isConnected) return;
        _isConnected = isConnected;
        OnPropertyChanged(nameof(IsConnected));
        OnPropertyChanged(nameof(IsReadOnly));
        OnPropertyChanged(nameof(ShowReadOnlyState));
        NotifyStatePresentation();
        RefreshCommand.RaiseCanExecuteChanged();
    }

    private void UpdateClockProjection()
    {
        var localNow = TimeZoneInfo.ConvertTime(_clock(), _timeZone);
        _currentTimeText = localNow.ToString("HH:mm", CultureInfo.InvariantCulture);
        _currentTimeTop = localNow.TimeOfDay.TotalMinutes / 60d * TimelineHourHeight;
        ShowCurrentTime = DateOnly.FromDateTime(localNow.DateTime) == _today;
        OnPropertyChanged(nameof(CurrentTimeText));
        OnPropertyChanged(nameof(CurrentTimeTop));
        OnPropertyChanged(nameof(ShowCurrentTime));
    }

    private void RebuildPlannerItems()
    {
        var selectedId = _selectionWasExplicit ? SelectedItem?.Id : null;
        var now = _clock();
        var taskById = _activeTaskItems.ToDictionary(item => item.Id);
        var timeline = TimedItems
            .Select(item => TodayPlannerItemViewModel.FromCalendar(
                item,
                taskById.GetValueOrDefault(item.Id),
                _timeZone,
                now))
            .OrderBy(item => item.TimelineTop)
            .ThenBy(item => item.Title, StringComparer.CurrentCulture)
            .ToArray();
        AssignTimelineLanes(timeline);
        var allDay = UntimedItems
            .Select(item => TodayPlannerItemViewModel.FromCalendar(
                item,
                taskById.GetValueOrDefault(item.Id),
                _timeZone,
                now))
            .ToArray();
        var scheduledIds = TimedItems.Concat(UntimedItems)
            .Select(item => item.Id)
            .ToHashSet();
        var queue = _activeTaskItems
            .Where(item => !scheduledIds.Contains(item.Id))
            .Where(item => item.Source.DeadlineAtUtc < now || !item.Source.StartAtUtc.HasValue)
            .Select(item => TodayPlannerItemViewModel.FromTask(item, _timeZone, now))
            .OrderByDescending(item => item.IsOverdue)
            .ThenBy(item => item.Source is TaskItemViewModel task ? task.Source.DeadlineAtUtc : null)
            .ThenBy(item => item.Title, StringComparer.CurrentCulture)
            .ToArray();

        _timelineItems = timeline;
        _allDayItems = allDay;
        _queueItems = queue;
        OnPropertyChanged(nameof(TimelineItems));
        OnPropertyChanged(nameof(AllDayItems));
        OnPropertyChanged(nameof(QueueItems));
        OnPropertyChanged(nameof(QueueCount));
        OnPropertyChanged(nameof(HasQueueItems));
        OnPropertyChanged(nameof(HasAllDayItems));
        OnPropertyChanged(nameof(HasPlannerItems));

        SelectedItem = timeline.Concat(allDay).Concat(queue)
            .FirstOrDefault(item => item.Id == selectedId)
            ?? timeline.FirstOrDefault()
            ?? queue.FirstOrDefault()
            ?? allDay.FirstOrDefault();
        UpdateSelectionFlags();
        UpdateClockProjection();
    }

    private static void AssignTimelineLanes(IReadOnlyList<TodayPlannerItemViewModel> items)
    {
        var laneEnds = new List<double>();
        var component = new List<TodayPlannerItemViewModel>();
        var componentEnd = double.MinValue;

        foreach (var item in items)
        {
            if (component.Count > 0 && item.TimelineTop >= componentEnd)
            {
                CompleteTimelineComponent(component, laneEnds.Count);
                component.Clear();
                laneEnds.Clear();
                componentEnd = double.MinValue;
            }

            var lane = laneEnds.FindIndex(end => end <= item.TimelineTop);
            if (lane < 0)
            {
                lane = laneEnds.Count;
                laneEnds.Add(0d);
            }

            item.TimelineLane = lane;
            var itemEnd = item.TimelineTop + item.TimelineHeight + 6d;
            laneEnds[lane] = itemEnd;
            componentEnd = Math.Max(componentEnd, itemEnd);
            component.Add(item);
        }

        CompleteTimelineComponent(component, laneEnds.Count);
    }

    private static void CompleteTimelineComponent(
        IEnumerable<TodayPlannerItemViewModel> component,
        int laneCount)
    {
        foreach (var item in component)
        {
            item.TimelineLaneCount = Math.Max(1, laneCount);
        }
    }

    private void UpdateSelectionFlags()
    {
        foreach (var item in TimelineItems.Concat(AllDayItems).Concat(QueueItems))
            item.IsSelected = item.Id == SelectedItem?.Id;
    }

    private void ClearPlannerItems()
    {
        _activeTaskItems = [];
        _timelineItems = [];
        _allDayItems = [];
        _queueItems = [];
        _selectionWasExplicit = false;
        SelectedItem = null;
        OnPropertyChanged(nameof(TimelineItems));
        OnPropertyChanged(nameof(AllDayItems));
        OnPropertyChanged(nameof(QueueItems));
        OnPropertyChanged(nameof(QueueCount));
        OnPropertyChanged(nameof(HasQueueItems));
        OnPropertyChanged(nameof(HasAllDayItems));
        OnPropertyChanged(nameof(HasPlannerItems));
    }

    private void NotifyStatePresentation()
    {
        OnPropertyChanged(nameof(ShowInitialLoading));
        OnPropertyChanged(nameof(ShowStateBanner));
        OnPropertyChanged(nameof(IsStateCritical));
        OnPropertyChanged(nameof(StateTitle));
        OnPropertyChanged(nameof(StateDescription));
    }
}
