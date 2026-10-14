using System.Globalization;
using Task.Desktop.Work;
using Task.Domain.Reminders;

namespace Task.Desktop.ViewModels;

public sealed record TaskReminderOption(int? Minutes, string Text);

internal static class TaskReminderDisplay
{
    public static string Text(Task.Desktop.TaskApi.DesktopTaskDto task)
    {
        if (task.Status is Task.Desktop.TaskApi.DesktopTaskStatus.Completed or Task.Desktop.TaskApi.DesktopTaskStatus.Cancelled) return "Задача закрыта; напоминание не ожидается.";
        var card = task.Card;
        if (task.DeadlineAtUtc is null && card?.DeadlineDate is null) return "Без срока — автоматическое напоминание не настроено.";
        if (card?.DeadlineReminderOffsetMinutes == -1) return "Напомнить: не напоминать.";
        if (card?.DeadlineReminderOffsetMinutes is not { } offset) return "Автоматически напомним исполнителям; без исполнителей — автору.";
        var due = DeadlineReminderTime.Due(task.DeadlineAtUtc, card.DeadlineDate, card.DeadlineTimeZone, card.DeadlineReminderTime ?? "09:00", offset);
        return due is { } at ? $"Напомним {at.ToLocalTime().ToString("d MMMM в HH:mm", CultureInfo.GetCultureInfo("ru-RU"))}." : "Напоминание выключено.";
    }
}

public sealed partial class TaskEditorViewModel
{
    public IReadOnlyList<TaskReminderOption> ReminderOptions { get; } =
    [
        new(null,"По настройке"), new(-1,"Не напоминать"), new(5,"За 5 минут"), new(15,"За 15 минут"),
        new(30,"За 30 минут"), new(60,"За 1 час"), new(1440,"За 1 день"), new(0,"В момент срока"), new(-2,"Другой интервал")
    ];
    private TaskReminderOption _reminderOption = null!;
    private string _customReminderMinutes = "15";
    private int _defaultReminderMinutes = 15;
    private string _dateOnlyReminderTime = "09:00";
    private DateTimeOffset? _reminderDeadline;
    public TaskReminderOption ReminderOption
    {
        get => _reminderOption;
        set { if (value is not null && SetProperty(ref _reminderOption, value)) { Changed(); OnPropertyChanged(nameof(IsCustomReminder)); } }
    }
    public bool IsCustomReminder => ReminderOption?.Minutes == -2;
    public string CustomReminderMinutes { get => _customReminderMinutes; set { if (SetProperty(ref _customReminderMinutes, value)) Changed(); } }
    public string ReminderPreview
    {
        get
        {
            if (DeadlineError is not null || CardError is not null) return "Проверьте срок и интервал напоминания.";
            var offset = Card.DeadlineReminderOffsetMinutes ?? _defaultReminderMinutes;
            if (offset == -1) return "Автоматическое напоминание выключено.";
            var time = Card.DeadlineReminderTime ?? _dateOnlyReminderTime;
            var due = DeadlineReminderTime.Due(_reminderDeadline, Card.DeadlineDate, Card.DeadlineTimeZone, time, offset);
            if (due is null) return "Укажите срок — напоминание включится автоматически.";
            var text = $"Напомним {due.Value.ToLocalTime().ToString("d MMMM в HH:mm", RussianCulture)}.";
            if (Card.DeadlineDate is not null) text += $" Срок без времени: интервал отсчитывается от {time[..5]} в часовом поясе срока.";
            if (due <= DateTimeOffset.UtcNow) text += " Если срок ещё впереди, напомним при ближайшей проверке.";
            return text;
        }
    }
    public string ReminderRecipientHint => _personal ? "Личное напоминание." : "Выбор относится к вашему напоминанию. У каждого исполнителя своё; без исполнителей напомним автору.";
    public void SetReminderDefaults(DesktopUserSettings settings)
    {
        _defaultReminderMinutes = settings.DefaultReminderOffsetMinutes; _dateOnlyReminderTime = settings.DateOnlyReminderTime;
        OnPropertyChanged(nameof(ReminderPreview));
    }
    private void LoadReminderChoice()
    {
        var offset = _source?.Card?.DeadlineReminderOffsetMinutes;
        _reminderOption = ReminderOptions.FirstOrDefault(o => o.Minutes == offset) ?? ReminderOptions[^1];
        _customReminderMinutes = (offset ?? 15).ToString(CultureInfo.InvariantCulture);
        OnPropertyChanged(nameof(ReminderOption)); OnPropertyChanged(nameof(CustomReminderMinutes)); OnPropertyChanged(nameof(IsCustomReminder));
    }
    private void ValidateReminder()
    {
        var offset = ReminderOption?.Minutes;
        if (offset == -2)
        {
            if (!int.TryParse(CustomReminderMinutes, out var parsed) || parsed is < 0 or > 525600)
                throw new ArgumentException("Интервал: от 0 до 525600 минут.");
            offset = parsed;
        }
        Card.DeadlineReminderOffsetMinutes = offset;
    }
}
