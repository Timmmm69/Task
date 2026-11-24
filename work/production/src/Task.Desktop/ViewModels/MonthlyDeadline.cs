using System.Globalization;
using Task.Desktop.TaskApi;
using Task.Domain;
using Task.Domain.Reminders;

namespace Task.Desktop.ViewModels;

internal enum MonthlyUrgency { Closed, NoDeadline, Later, Soon, Today, Overdue }

internal readonly record struct MonthlyDeadline(MonthlyUrgency Urgency, string? DateText, DateTimeOffset? NextChange)
{
    private static readonly CultureInfo Russian = CultureInfo.GetCultureInfo("ru-RU");

    public static MonthlyDeadline Evaluate(DesktopTaskDto task, DateTimeOffset now, TimeZoneInfo localZone)
    {
        var instant = task.DeadlineAtUtc;
        var localDeadline = instant is { } deadline ? TimeZoneInfo.ConvertTime(deadline, localZone) : (DateTimeOffset?)null;
        var day = TaskDeadlineFiltering.CalendarDate(task, localZone);
        var dateText = localDeadline?.ToString("dd.MM.yyyy HH:mm", Russian) ?? day?.ToString("dd.MM.yyyy", Russian);
        if (TaskRules.IsTerminal((TaskWorkStatus)task.Status)) return new(MonthlyUrgency.Closed, dateText, null);
        if (day is not { } deadlineDay) return new(MonthlyUrgency.NoDeadline, null, null);
        if (TaskRules.IsOverdue(instant, task.Card, now)) return new(MonthlyUrgency.Overdue, dateText, null);

        var zone = instant.HasValue ? localZone : TimeZoneInfo.FindSystemTimeZoneById(task.Card!.DeadlineTimeZone!);
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, zone).DateTime);
        var days = deadlineDay.DayNumber - today.DayNumber;
        return days switch
        {
            > 2 => new(MonthlyUrgency.Later, dateText, Midnight(deadlineDay.AddDays(-2), zone)),
            > 0 => new(MonthlyUrgency.Soon, dateText, Midnight(deadlineDay, zone)),
            _ => new(MonthlyUrgency.Today, dateText, instant is { } at
                ? at == DateTimeOffset.MaxValue ? null : at.ToUniversalTime().AddTicks(1)
                : deadlineDay == DateOnly.MaxValue ? null : Midnight(deadlineDay.AddDays(1), zone))
        };
    }

    private static DateTimeOffset Midnight(DateOnly date, TimeZoneInfo zone) => DeadlineReminderTime.Anchor(date, TimeOnly.MinValue, zone);
}

public sealed class MonthlyTaskRow : ViewModelBase
{
    private MonthlyDeadline _deadline;

    internal MonthlyTaskRow(DesktopTaskDto source, DateTimeOffset now, TimeZoneInfo zone)
    {
        Source = source;
        _deadline = MonthlyDeadline.Evaluate(source, now, zone);
    }

    internal DesktopTaskDto Source { get; }
    internal DateTimeOffset? NextChange => _deadline.NextChange;
    public string Title => Source.Title;
    public string StatusText => TaskItemViewModel.LocalizeStatus(Source.Status);
    public string StatusIcon => TaskItemViewModel.StatusIcon(Source.Status);
    public TaskVisualTone StatusTone => TaskItemViewModel.StatusVisualTone(Source.Status);
    public TaskVisualTone UrgencyTone => _deadline.Urgency switch
    {
        MonthlyUrgency.Overdue => TaskVisualTone.Critical,
        MonthlyUrgency.Today or MonthlyUrgency.Soon => TaskVisualTone.Warning,
        _ => TaskVisualTone.Neutral
    };
    public string UrgencyText => _deadline.Urgency switch
    {
        MonthlyUrgency.Closed => "Действий не требуется",
        MonthlyUrgency.NoDeadline => "Без срока",
        MonthlyUrgency.Overdue => "Просрочена",
        MonthlyUrgency.Today => "Срок сегодня",
        MonthlyUrgency.Soon => "Срок близко",
        _ => "Срок установлен"
    };
    public string DeadlineText => _deadline.DateText is { } date ? $"{UrgencyText} · {date}" : UrgencyText;
    public string AutomationName => $"{Title}. Статус: {StatusText}. {DeadlineText}.";

    internal void UpdateUrgency(DateTimeOffset now, TimeZoneInfo zone)
    {
        if (!SetProperty(ref _deadline, MonthlyDeadline.Evaluate(Source, now, zone), nameof(DeadlineText))) return;
        OnPropertyChanged(nameof(UrgencyText));
        OnPropertyChanged(nameof(UrgencyTone));
        OnPropertyChanged(nameof(AutomationName));
    }
}
