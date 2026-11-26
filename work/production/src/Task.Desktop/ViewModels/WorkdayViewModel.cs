using System.Net.Http;
using System.IO;
using Microsoft.Data.Sqlite;
using Task.Application;
using Task.Desktop.Infrastructure;
using Task.Desktop.Notifications;
using Task.Desktop.Work;
using AsyncTask = System.Threading.Tasks.Task;

namespace Task.Desktop.ViewModels;

public sealed class WorkdayViewModel : ViewModelBase, IDisposable
{
    private readonly ViewStateStore _store;
    private readonly TimeProvider _clock;
    private readonly Func<CancellationToken, Task<DesktopUserSettings?>> _settings;
    private readonly Func<TaskTodayWindow, DateTimeOffset, CancellationToken, Task<WorkdayResult?>> _readDay;
    private readonly Func<DateTimeOffset, DateTimeOffset, CancellationToken, Task<WorkweekResults?>> _readWeek;
    private readonly Func<CancellationToken, Task<bool>> _allowPopup;
    private readonly Func<bool> _canReadDay, _canReadWeek;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly DateTimeOffset _openedAt;
    private WorkdayCalendar? _calendar;
    private DateTimeOffset _nextSettingsRead, _nextAttempt;
    private DateOnly? _phraseDay, _handled, _closedDay, _weekEnd;
    private NotificationPresentationJournal? _journal;
    private int _dayRequest, _weekRequest;
    private bool _busy, _disposed;
    private string _dayText = "", _weekText = "", _phrase = "";

    internal WorkdayViewModel(ViewStateStore store, TimeProvider clock,
        Func<CancellationToken, Task<DesktopUserSettings?>> settings,
        Func<TaskTodayWindow, DateTimeOffset, CancellationToken, Task<WorkdayResult?>> day,
        Func<DateTimeOffset, DateTimeOffset, CancellationToken, Task<WorkweekResults?>> week,
        Func<bool> canReadDay, Func<bool> canReadWeek, Func<CancellationToken, Task<bool>> allowPopup)
    {
        _store = store; _clock = clock; _settings = settings; _readDay = day; _readWeek = week;
        _canReadDay = canReadDay; _canReadWeek = canReadWeek; _allowPopup = allowPopup;
        _openedAt = clock.GetUtcNow();
        AnotherPhraseCommand = new((_, _) =>
        {
            if (_phraseDay is { } date) Phrase = _store.ChoosePhrase(date, true);
            return AsyncTask.CompletedTask;
        }, _ => _phraseDay.HasValue && !_disposed);
    }

    public string Phrase { get => _phrase; private set => SetProperty(ref _phrase, value); }
    public string DayText { get => _dayText; private set => SetProperty(ref _dayText, value); }
    public string WeekText { get => _weekText; private set => SetProperty(ref _weekText, value); }
    public bool IsReady => _calendar is not null;
    public string DayHeading => _closedDay is { } day ? $"Итог дня · {day:dd.MM}" : "Итог дня";
    public string WeekHeading => _weekEnd is { } day ? $"Пятничная открытка · {day:dd.MM}" : "Пятничная открытка";
    public AsyncCommand AnotherPhraseCommand { get; }
    public bool NotifyAtDayEnd
    {
        get => _store.NotifyAtDayEnd;
        set { _store.SetDayEndNotifications(value); OnPropertyChanged(); }
    }

    internal void InvalidateAccess()
    {
        ClearCards();
        _calendar = null;
        _nextSettingsRead = default;
        OnPropertyChanged(nameof(IsReady));
    }

    internal void ClearCards()
    {
        _dayRequest++;
        _weekRequest++;
        DayText = WeekText = "";
    }

    internal event Action<TimeZoneInfo>? ZoneApplied;

    internal void UpdateSettings(DesktopUserSettings? settings)
    {
        var calendar = WorkdayCalendar.From(settings, _clock.LocalTimeZone);
        _calendar = calendar;
        _nextSettingsRead = _clock.GetUtcNow().AddMinutes(calendar is null ? 1 : 5);
        OnPropertyChanged(nameof(IsReady));
        if (calendar is not null)
        {
            ZoneApplied?.Invoke(calendar.Zone);
            if (_store.Ready.IsCompletedSuccessfully) UpdateDates(calendar, _clock.GetUtcNow());
        }
    }

    private void UpdateDates(WorkdayCalendar calendar, DateTimeOffset now)
    {
        var today = calendar.LocalDate(now);
        var phraseDay = calendar.WorkDate(today) ?? today;
        if (_phraseDay != phraseDay)
        {
            _phraseDay = phraseDay;
            Phrase = _store.ChoosePhrase(phraseDay, false);
            AnotherPhraseCommand.RaiseCanExecuteChanged();
        }
        var closedDay = calendar.LastClosedDay(now);
        var weekEnd = calendar.LastClosedWeek(now)?.Last;
        if (_closedDay == closedDay && _weekEnd == weekEnd) return;
        _closedDay = closedDay; _weekEnd = weekEnd;
        ClearCards();
        OnPropertyChanged(nameof(DayHeading));
        OnPropertyChanged(nameof(WeekHeading));
    }

    public async AsyncTask TickAsync(Func<string, bool>? present = null)
    {
        if (_disposed || _busy) return;
        _busy = true;
        var token = _lifetime.Token;
        try
        {
            await _store.Ready;
            token.ThrowIfCancellationRequested();
            var now = _clock.GetUtcNow();
            if (now >= _nextSettingsRead)
            {
                var settings = await _settings(token);
                token.ThrowIfCancellationRequested();
                UpdateSettings(settings);
            }
            if (_calendar is not { } calendar) return;
            UpdateDates(calendar, now);
            if (present is null || _closedDay is not { } date || date != calendar.LocalDate(now)
                || _handled == date || now < _nextAttempt || !_canReadDay()) return;
            var cutoff = calendar.Instant(date, calendar.End);
            if (_openedAt >= cutoff || !NotifyAtDayEnd) { _handled = date; return; }
            _nextAttempt = now.AddMinutes(1);
            if (!await _allowPopup(token)) return;
            var result = await ReadDayAsync(calendar, date, token);
            token.ThrowIfCancellationRequested();
            if (!_canReadDay() || !NotifyAtDayEnd || result is null || !ReferenceEquals(_calendar, calendar)) return;
            if (result.Planned == 0) { _handled = date; return; }
            _journal ??= new(Path.GetDirectoryName(_store.FilePath)!, Path.GetFileName(_store.FilePath) + "/workday");
            var id = new Guid(date.DayNumber, 0, 0, new byte[8]);
            if (_journal.Claim([id], new HashSet<Guid> { id }).Contains(id) && !present(Describe(result)))
            {
                _journal.Release(id);
                return;
            }
            _handled = date;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or SqliteException or HttpRequestException)
        { _nextAttempt = _clock.GetUtcNow().AddMinutes(1); }
        finally { _busy = false; }
    }

    private Task<WorkdayResult?> ReadDayAsync(WorkdayCalendar calendar, DateOnly date, CancellationToken token) =>
        _readDay(new(date, calendar.Instant(date, TimeOnly.MinValue), calendar.Instant(date.AddDays(1), TimeOnly.MinValue)),
            calendar.Instant(date, calendar.End), token);

    internal static string Describe(WorkdayResult result) => result.Planned == 0 ? "На этот день задач в плане не было."
        : result.Completed == result.Planned ? $"На сегодня выполнено {result.Completed} из {result.Planned} задач. Отличный день — можно выдохнуть."
        : $"Готово {result.Completed} из {result.Planned}. К оставшимся можно вернуться завтра.";

    internal async AsyncTask OpenDayAsync()
    {
        var request = ++_dayRequest;
        DayText = "Данные об итоге дня недоступны.";
        if (_disposed || !_canReadDay() || _closedDay is not { } date || _calendar is not { } calendar) return;
        DayText = "Считаем задачи…";
        try
        {
            var result = await ReadDayAsync(calendar, date, _lifetime.Token);
            if (!_disposed && request == _dayRequest)
                DayText = result is not null && _canReadDay() ? Describe(result) : "Недостаточно данных о плане на конец рабочего дня.";
        }
        catch (Exception e) when (e is OperationCanceledException or SqliteException or IOException or HttpRequestException)
        { if (!_disposed && request == _dayRequest) DayText = "Данные об итоге дня недоступны."; }
    }

    internal async AsyncTask OpenWeekAsync()
    {
        var request = ++_weekRequest;
        WeekText = "Данные о завершённых задачах недоступны.";
        if (_disposed || !_canReadWeek() || _calendar is not { } calendar || calendar.LastClosedWeek(_clock.GetUtcNow()) is not { } week) return;
        WeekText = "Читаем историю завершений…";
        try
        {
            var result = await _readWeek(calendar.Instant(week.First, TimeOnly.MinValue), calendar.Instant(week.Last, calendar.End), _lifetime.Token);
            if (_disposed || request != _weekRequest) return;
            if (result is null || !_canReadWeek()) { WeekText = "Данные о завершённых задачах недоступны."; return; }
            var lines = result.Items.Select(item => $"{calendar.LocalDate(item.CompletedAt):dd.MM}  ·  {item.Title}").ToList();
            var rest = result.Total - lines.Count;
            if (rest > 0) lines.Add(rest % 100 is >= 11 and <= 14 ? $"Ещё {rest} задач завершено."
                : (rest % 10) switch { 1 => $"Ещё {rest} задача завершена.", 2 or 3 or 4 => $"Ещё {rest} задачи завершены.", _ => $"Ещё {rest} задач завершено." });
            WeekText = lines.Count > 0 ? string.Join(Environment.NewLine, lines) : "За эту неделю завершённых задач нет.";
        }
        catch (Exception e) when (e is OperationCanceledException or SqliteException or IOException or HttpRequestException)
        { if (!_disposed && request == _weekRequest) WeekText = "Данные о завершённых задачах недоступны."; }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; _lifetime.Cancel(); ClearCards();
        AnotherPhraseCommand.Dispose(); _journal?.Dispose(); _lifetime.Dispose();
    }
}
