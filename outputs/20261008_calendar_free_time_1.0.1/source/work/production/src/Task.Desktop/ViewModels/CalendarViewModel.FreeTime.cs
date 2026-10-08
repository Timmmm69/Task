using Task.Desktop.Calendar;
using Task.Desktop.Work;

namespace Task.Desktop.ViewModels;

public sealed partial class CalendarViewModel
{
    private FreeTimeSearch _freeTimeSearch = null!;
    private Func<CancellationToken, System.Threading.Tasks.Task<DesktopWorkResult<DesktopUserSettings>>>? _loadFreeTimeSettings;
    private DesktopUserSettings? _freeTimeSettings;
    private CancellationTokenSource? _freeTimeCancellation;
    private long _freeTimeGeneration;
    private DateOnly _searchNextDate;
    private bool _showFreeTime, _findingTime, _canSearchFurther, _freeTimeStale, _showFreeTimeHighlight;
    private int _freeTimeMinutes = 30;
    private string? _freeTimeMessage;
    private IReadOnlyList<FreeTimeSlot> _freeTimeSlots = [];
    private FreeTimeSlot? _chosenFreeTime;

    public bool ShowFreeTime { get => _showFreeTime; private set => SetProperty(ref _showFreeTime, value); }
    public bool IsFindingTime => _findingTime;
    public int FreeTimeMinutes { get => _freeTimeMinutes; private set => SetProperty(ref _freeTimeMinutes, value); }
    public string? FreeTimeMessage { get => _freeTimeMessage; private set => SetProperty(ref _freeTimeMessage, value); }
    public IReadOnlyList<FreeTimeSlot> FreeTimeSlots { get => _freeTimeSlots; private set => SetProperty(ref _freeTimeSlots, value); }
    public FreeTimeSlot? ChosenFreeTime { get => _chosenFreeTime; private set { SetProperty(ref _chosenFreeTime, value); OnPropertyChanged(nameof(HasChosenFreeTime)); OnPropertyChanged(nameof(ShowEmptyState)); } }
    public bool HasChosenFreeTime => ChosenFreeTime is not null;
    public bool ShowFreeTimeHighlight { get => _showFreeTimeHighlight; set => SetProperty(ref _showFreeTimeHighlight, value); }
    public event Action<FreeTimeSlot>? FreeTimeChosen;
    public Func<FreeTimeSlot, CancellationToken, System.Threading.Tasks.Task>? CreateTaskAtFreeTime { get; set; }
    public AsyncCommand FindTimeCommand { get; private set; } = null!;
    public AsyncCommand FreeTimeDurationCommand { get; private set; } = null!;
    public AsyncCommand MoreFreeTimeCommand { get; private set; } = null!;
    public AsyncCommand ChooseFreeTimeCommand { get; private set; } = null!;
    public AsyncCommand CloseFreeTimeCommand { get; private set; } = null!;
    public AsyncCommand CreateTaskAtFreeTimeCommand { get; private set; } = null!;
    private bool CanFindTime => _active && CanRead && _sessionAllowsWrites && !_disposed && !IsBusy && Editor is null;

    private void InitializeFreeTime(Func<CancellationToken, System.Threading.Tasks.Task<DesktopWorkResult<DesktopUserSettings>>>? settings)
    {
        _loadFreeTimeSettings = settings; _freeTimeSearch = new(_client, _timeZone, IsPersonal);
        FindTimeCommand = new(async (_, token) => { ShowFreeTime = true; FreeTimeMinutes = 30; await FindFreeTimeAsync(false, token); }, _ => CanFindTime);
        FreeTimeDurationCommand = new(async (value, token) => { FreeTimeMinutes = int.Parse(value!.ToString()!); await FindFreeTimeAsync(false, token); }, value => CanFindTime && ShowFreeTime && value?.ToString() is "15" or "30" or "60");
        MoreFreeTimeCommand = new(async (_, token) => await FindFreeTimeAsync(true, token), _ => CanFindTime && ShowFreeTime && _canSearchFurther);
        ChooseFreeTimeCommand = new((value, _) => { if (value is FreeTimeSlot slot) ChooseFreeTime(slot); return System.Threading.Tasks.Task.CompletedTask; }, value => CanFindTime && value is FreeTimeSlot slot && FreeTimeSlots.Contains(slot));
        CloseFreeTimeCommand = new((_, _) => { CloseFreeTime(); return System.Threading.Tasks.Task.CompletedTask; });
        CreateTaskAtFreeTimeCommand = new(async (_, token) => { if (ChosenFreeTime is { } slot && CreateTaskAtFreeTime is { } create) await create(slot, token); }, _ => CanFindTime && _networkAvailable && !_freeTimeStale && ChosenFreeTime is { } chosen && chosen.StartUtc >= _clock() && CreateTaskAtFreeTime is not null);
        foreach (var command in FreeTimeCommands) command.ExecutionFailed += _ => FreeTimeMessage = "Не удалось найти время. Повторите поиск.";
    }
    private IEnumerable<AsyncCommand> FreeTimeCommands => [FindTimeCommand, FreeTimeDurationCommand, MoreFreeTimeCommand, ChooseFreeTimeCommand, CloseFreeTimeCommand, CreateTaskAtFreeTimeCommand];
    private void RaiseFreeTimeCommands()
    {
        if (FindTimeCommand is null) return;
        foreach (var command in FreeTimeCommands) command.RaiseCanExecuteChanged();
    }

    private async System.Threading.Tasks.Task FindFreeTimeAsync(bool further, CancellationToken token)
    {
        _freeTimeCancellation?.Cancel(); _freeTimeCancellation?.Dispose();
        _freeTimeCancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        var ct = _freeTimeCancellation.Token;
        var generation = ++_freeTimeGeneration;
        FreeTimeSlots = []; ChosenFreeTime = null; ShowFreeTimeHighlight = false; _canSearchFurther = false;
        _findingTime = true; OnPropertyChanged(nameof(IsBusy)); RaiseCommands();
        FreeTimeMessage = "Ищем ближайшее рабочее время…";
        try
        {
            var settingsStale = !_networkAvailable;
            if (_networkAvailable && _loadFreeTimeSettings is not null)
            {
                var settings = await _loadFreeTimeSettings(ct);
                if (generation != _freeTimeGeneration || ct.IsCancellationRequested) return;
                if (settings is DesktopWorkResult<DesktopUserSettings>.Succeeded success) _freeTimeSettings = success.Value;
                else if (settings is DesktopWorkResult<DesktopUserSettings>.ServerUnavailable) settingsStale = true;
                else
                {
                    _freeTimeSettings = null;
                    if (settings is DesktopWorkResult<DesktopUserSettings>.AuthenticationFailure) UpdateSessionState(false);
                    FreeTimeMessage = "Рабочие настройки пользователя недоступны. Поиск не выполнен."; return;
                }
            }
            if (settingsStale) UpdateConnectivity(false);
            if (_freeTimeSettings is null) { FreeTimeMessage = "Нет подтверждённых рабочих настроек пользователя. Подключитесь к серверу и повторите поиск."; return; }
            var now = _clock();
            var first = further ? _searchNextDate : DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, _timeZone).DateTime);
            var last = first.AddDays(7);
            var result = await _freeTimeSearch.SearchAsync(_freeTimeSettings, first, last, now, FreeTimeMinutes, _networkAvailable && !settingsStale, ct);
            if (generation != _freeTimeGeneration || !_active || !CanRead || !_sessionAllowsWrites || ct.IsCancellationRequested) return;
            if (result.Failure is DesktopCalendarResult<DesktopSchedulePage>.Forbidden or DesktopCalendarResult<DesktopSchedulePage>.AuthenticationFailure)
            { HandleFailure(result.Failure, false); FreeTimeMessage = "Доступ к календарю потерян. Поиск не выполнен."; return; }
            _freeTimeStale = result.Stale || settingsStale;
            if (_freeTimeStale) UpdateConnectivity(false);
            FreeTimeSlots = result.Slots;
            _canSearchFurther = result.Error is null && result.Slots.Count == 0;
            if (result.Error is null) _searchNextDate = last;
            FreeTimeMessage = result.Error ?? $"{first:dd.MM.yyyy} — {last.AddDays(-1):dd.MM.yyyy}, {_timeZone.Id}. "
                + (_freeTimeStale ? "Данные могут быть неактуальны. Создание и сохранение заблокированы. " : "")
                + (result.Slots.Count == 0 ? "Подходящих окон нет. Можно показать следующую неделю." : "Выберите окно для перехода к нему. Оно не резервируется.");
            Announcement = FreeTimeMessage;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        finally
        {
            if (generation == _freeTimeGeneration) { _findingTime = false; OnPropertyChanged(nameof(IsBusy)); RaiseCommands(); }
        }
    }

    private void ChooseFreeTime(FreeTimeSlot slot)
    {
        if (slot.StartUtc < _clock()) { FreeTimeSlots = []; FreeTimeMessage = "Это время уже прошло. Повторите поиск."; return; }
        var from = FreeTimeCalculator.Boundary(slot.Date, TimeOnly.MinValue, _timeZone);
        var to = FreeTimeCalculator.Boundary(slot.Date.AddDays(1), TimeOnly.MinValue, _timeZone);
        if (_freeTimeSearch.CachedRange(from, to) is not { } page) { FreeTimeSlots = []; FreeTimeMessage = "Диапазон больше не находится в кеше. Повторите поиск."; return; }
        _requestCancellation?.Cancel(); Interlocked.Increment(ref _generation);
        _selectedDate = slot.Date; _weekStart = StartOfWeek(slot.Date, FirstDay); ViewMode = CalendarViewMode.Day;
        Apply(page, [], slot.Date, slot.Date.AddDays(1)); State = page.Items.Count == 0 ? CalendarScreenState.Empty : CalendarScreenState.Loaded;
        OnPropertyChanged(nameof(SelectedDate)); OnPropertyChanged(nameof(WeekStart)); OnPropertyChanged(nameof(WeekRangeText));
        ChosenFreeTime = slot; ShowFreeTimeHighlight = true; ShowFreeTime = false;
        Announcement = $"Выбрано окно: {slot.Label}."; Message = _freeTimeStale ? "Данные могут быть неактуальны. Сервер недоступен." : null;
        RaiseCommands(); FreeTimeChosen?.Invoke(slot);
    }
    private void CloseFreeTime()
    {
        ++_freeTimeGeneration; _freeTimeCancellation?.Cancel();
        _findingTime = false; ShowFreeTime = false; FreeTimeSlots = []; ChosenFreeTime = null; ShowFreeTimeHighlight = false; _canSearchFurther = false;
        OnPropertyChanged(nameof(IsBusy)); RaiseFreeTimeCommands();
    }
    private void DisposeFreeTime()
    {
        CloseFreeTime(); _freeTimeCancellation?.Dispose(); _freeTimeCancellation = null;
        foreach (var command in FreeTimeCommands) command.Dispose();
    }
}
