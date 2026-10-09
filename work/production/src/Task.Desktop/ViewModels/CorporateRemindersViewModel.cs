using System.Globalization;
using Task.Desktop.Work;

namespace Task.Desktop.ViewModels;

public sealed partial class WorkHubViewModel
{
    private IReadOnlyList<DesktopReminder> _reminders = [];
    private IReadOnlyList<DesktopSearchResult> _reminderTargets = [];
    private DesktopReminder? _selectedReminder;
    private DesktopSearchResult? _reminderTarget;
    private string _reminderSearch = "", _reminderTrigger = "До срока", _reminderAbsolute = "", _snoozeUntil = "", _snoozePreset = "По настройке";
    private int? _reminderOffset;
    private readonly List<AsyncCommand> _reminderCommands = [];
    public bool CanManageCorporateReminders => IsCorporate && _client is IDesktopReminderApiClient && Has("Reminder.ManageOwn");
    internal bool HasReminderDraft => CanManageCorporateReminders && (SelectedCorporateReminder is null
        ? ReminderTarget is not null
        : (ReminderTarget is not null && ReminderTarget.ObjectId != SelectedCorporateReminder.TargetObjectId)
            || ReminderTrigger != (SelectedCorporateReminder.TriggerType switch { "before_start" => "До начала", "at_start" => "В начале", "at_deadline" => "В срок", "absolute" => "Дата и время", _ => "До срока" })
            || (SelectedCorporateReminder.TriggerType.StartsWith("before_",StringComparison.Ordinal) && ReminderOffset != SelectedCorporateReminder.OffsetMinutes)
            || (SelectedCorporateReminder.TriggerType == "absolute" && ReminderAbsolute != SelectedCorporateReminder.AbsoluteTriggerAt?.ToLocalTime().ToString("dd.MM.yyyy HH:mm")));
    public IReadOnlyList<DesktopReminder> CorporateReminders { get => _reminders; private set => SetProperty(ref _reminders, value); }
    public IReadOnlyList<DesktopSearchResult> ReminderTargets { get => _reminderTargets; private set => SetProperty(ref _reminderTargets, value); }
    public DesktopSearchResult? ReminderTarget { get => _reminderTarget; set => SetProperty(ref _reminderTarget, value); }
    public string ReminderSearch { get => _reminderSearch; set => SetProperty(ref _reminderSearch, value); }
    public string ReminderTrigger { get => _reminderTrigger; set => SetProperty(ref _reminderTrigger, value); }
    public int? ReminderOffset { get => _reminderOffset; set => SetProperty(ref _reminderOffset, value); }
    public string ReminderAbsolute { get => _reminderAbsolute; set => SetProperty(ref _reminderAbsolute, value); }
    public string SnoozePreset { get => _snoozePreset; set => SetProperty(ref _snoozePreset, value); }
    public string SnoozeUntil { get => _snoozeUntil; set => SetProperty(ref _snoozeUntil, value); }
    public IReadOnlyList<string> ReminderTriggers { get; } = ["До срока", "До начала", "В начале", "В срок", "Дата и время"];
    public IReadOnlyList<string> SnoozePresets { get; } = ["По настройке", "5 минут", "15 минут", "30 минут", "60 минут", "Завтра в 09:00", "Дата и время"];
    public DesktopReminder? SelectedCorporateReminder
    {
        get => _selectedReminder;
        set
        {
            if (!SetProperty(ref _selectedReminder, value)) return;
            ReminderTarget = null;
            ReminderTrigger = value?.TriggerType switch { "before_start" => "До начала", "at_start" => "В начале", "at_deadline" => "В срок", "absolute" => "Дата и время", _ => "До срока" };
            ReminderOffset = value?.OffsetMinutes;
            ReminderAbsolute = value?.AbsoluteTriggerAt?.ToLocalTime().ToString("dd.MM.yyyy HH:mm") ?? "";
            foreach (var command in _reminderCommands) command.RaiseCanExecuteChanged();
        }
    }
    public AsyncCommand RefreshRemindersCommand { get; private set; } = null!;
    public AsyncCommand SearchReminderTargetsCommand { get; private set; } = null!;
    public AsyncCommand NewReminderCommand { get; private set; } = null!;
    public AsyncCommand SaveReminderCommand { get; private set; } = null!;
    public AsyncCommand CancelReminderCommand { get; private set; } = null!;
    public AsyncCommand RestoreReminderCommand { get; private set; } = null!;
    public AsyncCommand SnoozeReminderCommand { get; private set; } = null!;
    public AsyncCommand DismissReminderCommand { get; private set; } = null!;
    public AsyncCommand SnoozeNotificationCommand { get; private set; } = null!;
    private void InitializeReminderCommands()
    {
        bool Allowed() => CanManageCorporateReminders && !_disposed && _sessionAvailable && _networkAvailable;
        bool Actionable() => Allowed() && SelectedCorporateReminder is { Status: not "cancelled" and not "expired", CurrentOccurrence.Status: "created" or "claimed" or "failed" or "delivered" };
        RefreshRemindersCommand = new(async (_, ct) => await RefreshRemindersAsync(ct), _ => Allowed());
        SearchReminderTargetsCommand = new(SearchReminderTargetsAsync, _ => Allowed() && Has("Search.Use"));
        NewReminderCommand = new(async (_, ct) =>
        {
            var generation = _notificationAccessGeneration;
            if (!CanReadSettings) { SelectedCorporateReminder = null; ReminderTarget = null; ReminderOffset = null; return; }
            var settings = await _client.GetUserSettingsAsync(ct);
            if (!ReminderResponseCurrent(generation, ct)) return;
            Apply(settings, value => { SelectedCorporateReminder = null; ReminderTarget = null; ReminderOffset = value.DefaultReminderOffsetMinutes; });
        }, _ => Allowed());
        SaveReminderCommand = new(SaveCorporateReminderAsync, _ => Allowed());
        CancelReminderCommand = new(async (_, ct) => await ChangeReminderAsync("cancel", ct), _ => Allowed() && SelectedCorporateReminder is { Status: not "cancelled" and not "expired" });
        RestoreReminderCommand = new(async (_, ct) => await ChangeReminderAsync("restore", ct), _ => Allowed() && SelectedCorporateReminder is { Status: "cancelled" or "expired" });
        SnoozeReminderCommand = new(async (_, ct) => await ChangeReminderAsync("snooze", ct), _ => Actionable());
        DismissReminderCommand = new(async (_, ct) => await ChangeReminderAsync("dismiss", ct), _ => Actionable());
        SnoozeNotificationCommand = new(SnoozeNotificationAsync, p => Allowed() && p is DesktopNotification { IsUnread: true, ReminderId: not null });
        _reminderCommands.AddRange([RefreshRemindersCommand, SearchReminderTargetsCommand, NewReminderCommand, SaveReminderCommand, CancelReminderCommand, RestoreReminderCommand, SnoozeReminderCommand, DismissReminderCommand, SnoozeNotificationCommand]);
    }
    private bool ReminderResponseCurrent(long generation, CancellationToken ct) => !ct.IsCancellationRequested && generation == _notificationAccessGeneration && !_disposed && _sessionAvailable && CanManageCorporateReminders;
    private async System.Threading.Tasks.Task RefreshRemindersAsync(CancellationToken ct)
    {
        if (!CanManageCorporateReminders || _client is not IDesktopReminderApiClient api) return;
        var generation = _notificationAccessGeneration;
        var result = await api.GetRemindersAsync(ct);
        if (!ReminderResponseCurrent(generation, ct)) return;
        Apply(result, items => { CorporateReminders = items; var selected = SelectedCorporateReminder?.Id; SelectedCorporateReminder = items.FirstOrDefault(x => x.Id == selected); });
    }
    private async System.Threading.Tasks.Task SearchReminderTargetsAsync(object? _, CancellationToken ct)
    {
        var generation = _notificationAccessGeneration;
        var result = await _client.SearchAsync(ReminderSearch.Trim(), ct);
        if (!ReminderResponseCurrent(generation, ct)) return;
        Apply(result, items => ReminderTargets = items.Where(x => x.ObjectType is "task" or "calendar_event").ToArray());
    }
    private async System.Threading.Tasks.Task SaveCorporateReminderAsync(object? _, CancellationToken ct)
    {
        if (_client is not IDesktopReminderApiClient api) return;
        var target = ReminderTarget?.ObjectId ?? SelectedCorporateReminder?.TargetObjectId;
        if (target is null) { SetFeedback("Найдите и выберите задачу или событие.", WorkHubFeedbackKind.Warning); return; }
        var trigger = ReminderTrigger switch { "До начала" => "before_start", "В начале" => "at_start", "В срок" => "at_deadline", "Дата и время" => "absolute", _ => "before_deadline" };
        DateTimeOffset? absolute = null;
        if (trigger == "absolute") { if (!TryLocalReminderInstant(ReminderAbsolute, out var value)) return; absolute = value; }
        if (trigger.StartsWith("before_", StringComparison.Ordinal) && ReminderOffset is < 0 or > 525600) { SetFeedback("Отступ должен быть от 0 до 525600 минут.", WorkHubFeedbackKind.Warning); return; }
        var generation = _notificationAccessGeneration;
        var result = await api.SaveReminderAsync(target.Value, trigger, trigger.StartsWith("before_", StringComparison.Ordinal) ? ReminderOffset : null, absolute, SelectedCorporateReminder, ct);
        if (!ReminderResponseCurrent(generation, ct)) return;
        Apply(result, value => { SelectedCorporateReminder = value; SetFeedback("Напоминание сохранено на сервере.", WorkHubFeedbackKind.Success); });
        if (result is DesktopWorkResult<DesktopReminder>.Succeeded) await RefreshRemindersAsync(ct);
    }
    private bool TryLocalReminderInstant(string text, out DateTimeOffset instant)
    {
        instant = default;
        if (!DateTime.TryParseExact(text, "dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var local) || TimeZoneInfo.Local.IsInvalidTime(local) || TimeZoneInfo.Local.IsAmbiguousTime(local))
        { SetFeedback("Укажите местное время dd.MM.yyyy HH:mm вне перехода летнего времени.", WorkHubFeedbackKind.Warning); return false; }
        instant = new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local)).ToUniversalTime(); return true;
    }
    private async System.Threading.Tasks.Task ChangeReminderAsync(string action, CancellationToken ct, DesktopReminder? reminder = null)
    {
        if (_client is not IDesktopReminderApiClient api || (reminder ?? SelectedCorporateReminder) is not { } item) return;
        DateTimeOffset? until = null;
        if (action == "snooze")
        {
            var snoozeMinutes = DefaultSnoozeMinutes;
            if (SnoozePreset == "По настройке")
            {
                if (!CanReadSettings) { SetFeedback("Для отсрочки по настройке нужен доступ к личным настройкам.", WorkHubFeedbackKind.Warning); return; }
                var preferencesGeneration = _notificationAccessGeneration;
                var preferences = await _client.GetNotificationPreferencesAsync(ct);
                if (!ReminderResponseCurrent(preferencesGeneration, ct)) return;
                if (preferences is not DesktopWorkResult<DesktopNotificationPreferences>.Succeeded saved) { Apply(preferences, _ => { }); return; }
                snoozeMinutes = saved.Value.DefaultSnoozeMinutes;
            }
            var now = DateTimeOffset.Now;
            if (SnoozePreset == "Дата и время") { if (!TryLocalReminderInstant(SnoozeUntil, out var custom)) return; until = custom; }
            else if (SnoozePreset == "Завтра в 09:00") { if (!TryLocalReminderInstant(now.Date.AddDays(1).AddHours(9).ToString("dd.MM.yyyy HH:mm"), out var tomorrow)) return; until = tomorrow; }
            else until = now.AddMinutes(SnoozePreset switch { "5 минут" => 5, "15 минут" => 15, "30 минут" => 30, "60 минут" => 60, _ => snoozeMinutes }).ToUniversalTime();
        }
        var generation = _notificationAccessGeneration;
        var result = action switch
        {
            "cancel" => await api.CancelReminderAsync(item, ct), "restore" => await api.RestoreReminderAsync(item, ct),
            _ when item.CurrentOccurrence is { } x => await api.ActOnReminderAsync(item.Id, x.Version, until, ct),
            _ => new DesktopWorkResult<bool>.ValidationFailure("Срабатывание недоступно. Обновите список.")
        };
        if (!ReminderResponseCurrent(generation, ct)) return;
        Apply(result, _ => SetFeedback("Изменение напоминания сохранено на сервере.", WorkHubFeedbackKind.Success));
        if (result is DesktopWorkResult<bool>.Succeeded) { await RefreshRemindersAsync(ct); await EnsureNotificationsAsync(ct); }
    }
    private async System.Threading.Tasks.Task SnoozeNotificationAsync(object? parameter, CancellationToken ct)
    {
        if (parameter is not DesktopNotification { ReminderId: { } id } notification || _client is not IDesktopReminderApiClient api) return;
        var generation = _notificationAccessGeneration;
        var result = await api.GetReminderAsync(id, ct);
        if (!ReminderResponseCurrent(generation, ct)) return;
        if (result is DesktopWorkResult<DesktopReminder>.Succeeded ok)
        { if (ok.Value.CurrentOccurrence?.Id == notification.Id) await ChangeReminderAsync("snooze", ct, ok.Value); else SetFeedback("Напоминание изменилось. Обновите список.", WorkHubFeedbackKind.Warning); }
        else Apply(result, _ => { });
    }
    private void ClearCorporateReminders()
    { CorporateReminders = []; ReminderTargets = []; SelectedCorporateReminder = null; ReminderTarget = null; }
}
