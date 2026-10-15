using System.ComponentModel;
using System.Globalization;

namespace Task.Desktop.ViewModels;

public sealed partial class WorkHubViewModel
{
    public IReadOnlyList<KeyValuePair<string, string>> LanguageChoices { get; } = [new("ru-RU", "Русский")];
    public IReadOnlyList<KeyValuePair<int, string>> WeekdayChoices { get; } =
    [new(1, "Понедельник"), new(2, "Вторник"), new(3, "Среда"), new(4, "Четверг"), new(5, "Пятница"), new(6, "Суббота"), new(7, "Воскресенье")];
    public IReadOnlyList<KeyValuePair<string, string>> TimeFormatChoices { get; } =
    [new("24h", "24 часа (14:30)"), new("12h", "12 часов (2:30 PM)")];

    public string WorkdayStartDisplay { get => DisplayTime(UserWorkdayStart); set => UserWorkdayStart = NormalizeTime(value); }
    public string WorkdayEndDisplay { get => DisplayTime(UserWorkdayEnd); set => UserWorkdayEnd = NormalizeTime(value); }
    public string ReminderTimeDisplay { get => DisplayTime(DateOnlyReminderTime); set => DateOnlyReminderTime = NormalizeTime(value); }
    public string QuietStartDisplay { get => DisplayTime(QuietHoursStart); set => QuietHoursStart = NormalizeTime(value); }
    public string QuietEndDisplay { get => DisplayTime(QuietHoursEnd); set => QuietHoursEnd = NormalizeTime(value); }

    public string? ProfileSettingsError => UserLanguage != "ru-RU" ? "Выберите поддерживаемый язык: Русский."
        : FirstDayOfWeek is < 1 or > 7 ? "Выберите первый день недели."
        : !TryTime(UserWorkdayStart, out _) ? "Начало рабочего дня: введите часы и минуты, например 09:00."
        : !TryTime(UserWorkdayEnd, out _) ? "Конец рабочего дня: введите часы и минуты, например 18:00."
        : !TryTime(DateOnlyReminderTime, out _) ? "Время напоминания: введите часы и минуты, например 09:00."
        : null;

    public string? NotificationSettingsError
    {
        get
        {
            var fields = new[] { QuietHoursStart, QuietHoursEnd, QuietHoursTimeZone };
            if (fields.All(string.IsNullOrWhiteSpace)) return null;
            if (fields.Any(string.IsNullOrWhiteSpace)) return "Для режима «Не беспокоить» укажите начало, конец и часовой пояс либо очистите все три поля.";
            if (!TryTime(QuietHoursStart, out _) || !TryTime(QuietHoursEnd, out _))
                return "Время «Не беспокоить»: введите часы и минуты, например 22:00 и 08:00.";
            return null;
        }
    }

    private static bool TryTime(string? value, out TimeOnly time) => TimeOnly.TryParseExact(value?.Trim(),
        ["HH:mm", "HH:mm:ss", "HH:mm:ss.FFFFFFF"], CultureInfo.InvariantCulture, DateTimeStyles.None, out time);
    private static string NormalizeTime(string? value) => TryTime(value, out var time)
        ? time.ToString(time.Ticks % TimeSpan.TicksPerSecond == 0 ? "HH:mm:ss" : "HH:mm:ss.FFFFFFF", CultureInfo.InvariantCulture) : value ?? "";
    private static string DisplayTime(string? value) => TryTime(value, out var time)
        ? time.ToString(time.Ticks % TimeSpan.TicksPerMinute == 0 ? "HH:mm" : "HH:mm:ss.FFFFFFF", CultureInfo.InvariantCulture) : value ?? "";

    private void OnSettingsValueChanged(object? sender, PropertyChangedEventArgs e)
    {
        var display = e.PropertyName switch
        {
            nameof(UserWorkdayStart) => nameof(WorkdayStartDisplay), nameof(UserWorkdayEnd) => nameof(WorkdayEndDisplay),
            nameof(DateOnlyReminderTime) => nameof(ReminderTimeDisplay), nameof(QuietHoursStart) => nameof(QuietStartDisplay),
            nameof(QuietHoursEnd) => nameof(QuietEndDisplay), _ => null,
        };
        if (display is not null) OnPropertyChanged(display);
        if (display is not null || e.PropertyName is nameof(UserLanguage) or nameof(FirstDayOfWeek) or nameof(QuietHoursTimeZone))
        {
            OnPropertyChanged(nameof(ProfileSettingsError));
            OnPropertyChanged(nameof(NotificationSettingsError));
        }
    }
}
