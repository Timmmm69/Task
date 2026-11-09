using Task.Desktop.Calendar;

namespace Task.Desktop.Personal;

// A presentation projection only; delivery, snooze and reminder rules stay in the store.
public sealed record PersonalNotificationRow(PersonalNotification Source, string SourceLabel, string Period, bool CanSnooze)
{
    public string Title => Source.Title;
    public string Metadata => $"{Period} · {Source.DeliveredText} · {SourceLabel}";
    public string ReadLabel => Source.IsRead ? "Прочитано" : "Новое";
    public override string ToString() => $"{Title} · {Metadata} · {ReadLabel}";
}

public sealed partial class PersonalPlanningViewModel
{
    private string _notificationQuery = "", _notificationReadFilter = "Все", _notificationSourceFilter = "Все источники", _notificationPeriodFilter = "За всё время";
    private string _notificationLoadError = "";
    private IReadOnlyList<PersonalNotificationRow> _notificationRows = [], _visibleNotifications = [];
    private bool _refreshingReminderTargets;
    public IReadOnlyList<string> NotificationReadFilters { get; } = ["Все", "Новые", "Прочитанные"];
    public IReadOnlyList<string> NotificationSourceFilters { get; } = ["Все источники", "Задачи", "События"];
    public IReadOnlyList<string> NotificationPeriodFilters { get; } = ["За всё время", "Сегодня", "Последние 7 дней"];
    public string NotificationQuery { get => _notificationQuery; set { if (SetProperty(ref _notificationQuery, value)) FilterNotifications(); } }
    public string NotificationReadFilter { get => _notificationReadFilter; set { if (SetProperty(ref _notificationReadFilter, value)) FilterNotifications(); } }
    public string NotificationSourceFilter { get => _notificationSourceFilter; set { if (SetProperty(ref _notificationSourceFilter, value)) FilterNotifications(); } }
    public string NotificationPeriodFilter { get => _notificationPeriodFilter; set { if (SetProperty(ref _notificationPeriodFilter, value)) FilterNotifications(); } }
    public IReadOnlyList<PersonalNotificationRow> VisibleNotifications { get => _visibleNotifications; private set => SetProperty(ref _visibleNotifications, value); }
    public PersonalNotificationRow? SelectedNotificationRow
    {
        get => VisibleNotifications.FirstOrDefault(n => n.Source.Id == SelectedNotification?.Id);
        set => SelectedNotification = value?.Source;
    }
    public bool HasNotificationSelection => SelectedNotificationRow is not null;
    public bool CanSnoozeSelectedNotification => SelectedNotificationRow is { CanSnooze: true, Source.IsRead: false };
    public bool HasNotificationFilters => NotificationQuery.Length > 0 || NotificationReadFilter != "Все" || NotificationSourceFilter != "Все источники" || NotificationPeriodFilter != "За всё время";
    public int UnreadNotificationCount => Notifications.Count(n => !n.IsRead);
    public string NotificationSummary => $"{UnreadNotificationCount} новых · {Notifications.Count} всего";
    public string NotificationResultText => $"Найдено: {VisibleNotifications.Count} из {Notifications.Count}";
    public string NotificationLoadError { get => _notificationLoadError; private set { SetProperty(ref _notificationLoadError, value); RaiseNotificationState(); } }
    public bool HasNotificationLoadError => NotificationLoadError.Length > 0;
    public bool ShowNotificationEmpty => !IsBusy && !HasNotificationLoadError && VisibleNotifications.Count == 0;
    public string NotificationEmptyTitle => Notifications.Count == 0 ? "Уведомлений пока нет" : NotificationReadFilter == "Новые" && !HasOtherNotificationFilters ? "Новых уведомлений нет" : "Ничего не найдено";
    private bool HasOtherNotificationFilters => NotificationQuery.Length > 0 || NotificationSourceFilter != "Все источники" || NotificationPeriodFilter != "За всё время";
    public string NotificationEmptyHint => Notifications.Count == 0 ? "Доставленные напоминания о задачах и событиях появятся здесь." : "Измените условия или сбросьте фильтры, чтобы увидеть все записи.";
    public bool IsCustomSnooze => SnoozeChoice == "Дата и время";
    public void ResetNotificationFilters()
    {
        _notificationQuery = ""; _notificationReadFilter = "Все"; _notificationSourceFilter = "Все источники"; _notificationPeriodFilter = "За всё время";
        foreach (var name in new[] { nameof(NotificationQuery), nameof(NotificationReadFilter), nameof(NotificationSourceFilter), nameof(NotificationPeriodFilter) }) OnPropertyChanged(name);
        FilterNotifications();
    }
    private void RefreshNotificationBrowser()
    {
        var today = _clock.GetLocalNow().Date;
        var reminders = Reminders.ToDictionary(r => r.Source.Id, r => r.Source);
        _notificationRows = Notifications.OrderByDescending(n => n.DeliveredAt).ThenBy(n => n.Id).Select(n =>
        {
            reminders.TryGetValue(n.ReminderId, out var reminder);
            var date = n.DeliveredAt.ToLocalTime().Date;
            var period = date == today ? "Сегодня" : date == today.AddDays(-1) ? "Вчера" : "Ранее";
            return new PersonalNotificationRow(n, reminder?.TargetKind == DesktopScheduleItemType.Task ? "Задачи" : reminder?.TargetKind == DesktopScheduleItemType.CalendarEvent ? "События" : "Напоминание", period, reminder?.State == "delivered");
        }).ToArray();
        NotificationLoadError = "";
        FilterNotifications();
    }
    private void FilterNotifications()
    {
        var selected = SelectedNotification?.Id;
        var query = NotificationQuery.Trim();
        var today = _clock.GetLocalNow().Date;
        VisibleNotifications = _notificationRows.Where(n =>
            (query.Length == 0 || n.Title.Contains(query, StringComparison.CurrentCultureIgnoreCase) || n.Metadata.Contains(query, StringComparison.CurrentCultureIgnoreCase)) &&
            (NotificationReadFilter == "Все" || n.Source.IsRead == (NotificationReadFilter == "Прочитанные")) &&
            (NotificationSourceFilter == "Все источники" || n.SourceLabel == NotificationSourceFilter) &&
            (NotificationPeriodFilter == "За всё время" || n.Source.DeliveredAt.ToLocalTime().Date >= (NotificationPeriodFilter == "Сегодня" ? today : today.AddDays(-6)))).ToArray();
        SelectedNotification = VisibleNotifications.FirstOrDefault(n => n.Source.Id == selected)?.Source;
        OnPropertyChanged(nameof(SelectedNotificationRow));
        RaiseNotificationState();
    }
    private void RaiseNotificationState()
    {
        foreach (var name in new[] { nameof(HasNotificationSelection), nameof(CanSnoozeSelectedNotification), nameof(HasNotificationFilters), nameof(UnreadNotificationCount), nameof(NotificationSummary), nameof(NotificationResultText), nameof(ShowNotificationEmpty), nameof(NotificationEmptyTitle), nameof(NotificationEmptyHint), nameof(HasNotificationLoadError) }) OnPropertyChanged(name);
    }
    private async System.Threading.Tasks.Task ReloadPlanningAsync()
    {
        IsBusy = true;
        await System.Threading.Tasks.Task.Yield(); // Let the dispatcher paint the loading state.
        await Run(Refresh, "Список обновлён.");
    }
}
