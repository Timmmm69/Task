using System.Globalization;
using Task.Desktop.TaskApi;
using Task.Domain;
using Operation = System.Threading.Tasks.Task;

namespace Task.Desktop.ViewModels;

public sealed class MonthlyTasksViewModel : ViewModelBase, IDisposable
{
    private readonly IDesktopTasksApiClient _client;
    private readonly Func<bool> _canCreate;
    private readonly Func<bool> _canOpen;
    private readonly Action<DesktopTaskDto>? _onCreated;
    private readonly TimeProvider _clock;
    private CancellationTokenSource? _load;
    private DateOnly _period;
    private string _yearText;
    private bool _active, _withoutMonth, _hideCompleted, _isLoading, _isSaving, _disposed;
    private string _title = "";
    private string? _message;
    private DateTimeOffset? _lastLoadedAt;
    private DesktopCreateTaskCommand? _pendingCreate;
    private IReadOnlyList<DesktopTaskDto> _tasks = [];
    private IReadOnlyList<MonthlyTaskRow> _rows = [];
    private DateTimeOffset? _nextUrgencyChange;
    private TaskSortOrder _sort = TaskSortOrder.UpdatedNewest;

    internal MonthlyTasksViewModel(IDesktopTasksApiClient client, Func<bool> canCreate,
        Func<bool> canOpen, Func<Guid, CancellationToken, System.Threading.Tasks.Task<bool>> open, TimeProvider? clock = null,
        Action<DesktopTaskDto>? onCreated = null)
    {
        _client = client;
        _canCreate = canCreate;
        _canOpen = canOpen;
        _onCreated = onCreated;
        _clock = clock ?? TimeProvider.System;
        _period = CurrentMonth();
        _yearText = FormatYear(_period);
        PreviousCommand = new((_, _) => SelectMonthAsync(_period.AddMonths(-1)), _ => CanChoosePeriod && !_withoutMonth && _period != DateOnly.MinValue);
        NextCommand = new((_, _) => SelectMonthAsync(_period.AddMonths(1)), _ => CanChoosePeriod && !_withoutMonth && _period != new DateOnly(9999, 12, 1));
        CurrentCommand = new((_, _) => SelectMonthAsync(CurrentMonth()), _ => CanChoosePeriod);
        WithoutMonthCommand = new((_, _) => ChangePeriodAsync(_period, !_withoutMonth), _ => CanChoosePeriod);
        ApplyYearCommand = new((_, _) => YearError is null
            ? SelectMonthAsync(new DateOnly(int.Parse(_yearText, CultureInfo.InvariantCulture), _period.Month, 1))
            : Operation.CompletedTask, _ => CanChoosePeriod && YearError is null);
        RefreshCommand = new((_, _) => ReloadAsync(), _ => _active && !IsLoading && !IsSaving);
        CreateCommand = new((_, token) => CreateAsync(token), _ => CanQuickCreate && Title.Trim().Length is > 0 and <= 500);
        OpenCommand = new(async (row, token) =>
        {
            if (!await open(((MonthlyTaskRow)row!).Source.Id, token) && _active)
                Message = "Не удалось открыть задачу. Обновите список и повторите попытку.";
        },
            row => _active && _canOpen() && !IsSaving && row is MonthlyTaskRow task && Rows.Contains(task));
        OpenCommand.ExecutionFailed += _ => Message = "Не удалось открыть задачу. Повторите попытку.";
    }

    public IReadOnlyList<TaskFilterOption<int>> Months { get; } = CultureInfo.GetCultureInfo("ru-RU").DateTimeFormat.MonthNames
        .Take(12).Select((name, index) => new TaskFilterOption<int>(index + 1, name)).ToArray();
    public IReadOnlyList<TaskFilterOption<TaskSortOrder>> SortOrders { get; } =
        [
            new(TaskSortOrder.UpdatedNewest, "Изменены недавно"),
            new(TaskSortOrder.DeadlineNearest, "Срок: ближайшие"),
            new(TaskSortOrder.PriorityHighest, "Приоритет: высокий"),
            new(TaskSortOrder.TitleAscending, "Название: А–Я")
        ];
    public string YearText
    {
        get => _yearText;
        set
        {
            if (!SetProperty(ref _yearText, value)) return;
            OnPropertyChanged(nameof(YearError));
            ApplyYearCommand.RaiseCanExecuteChanged();
        }
    }
    public string? YearError => _yearText.Length == 4 && TaskCardContent.IsValidPlanningMonth(_yearText + "-01")
        ? null : "Введите год четырьмя цифрами от 0001 до 9999.";
    public int SelectedMonth
    {
        get => _period.Month;
        set
        {
            if (!CanChoosePeriod || value is < 1 or > 12 || value == _period.Month && !_withoutMonth) return;
            var year = YearError is null ? int.Parse(_yearText, CultureInfo.InvariantCulture) : _period.Year;
            _ = SelectMonthAsync(new DateOnly(year, value, 1));
        }
    }
    public string PeriodText => _withoutMonth ? "Без месяца" : _period.ToString("MMMM yyyy", CultureInfo.GetCultureInfo("ru-RU"));
    public string WithoutMonthAction => _withoutMonth ? "Вернуться к месяцу" : "Без месяца";
    public string Title
    {
        get => _title;
        set
        {
            if (!SetProperty(ref _title, value)) return;
            _pendingCreate = null;
            CreateCommand.RaiseCanExecuteChanged();
        }
    }
    public bool HideCompleted { get => _hideCompleted; set { if (SetProperty(ref _hideCompleted, value)) ApplyFilters(); } }
    public TaskSortOrder SortOrder { get => _sort; set { if (SetProperty(ref _sort, value)) ApplyFilters(); } }
    public bool IsLoading { get => _isLoading; private set { if (SetProperty(ref _isLoading, value)) NotifyCommands(); } }
    internal bool IsActive => _active;
    public bool IsSaving { get => _isSaving; private set { if (SetProperty(ref _isSaving, value)) NotifyCommands(); } }
    public bool CanChoosePeriod => _active && !IsSaving;
    public bool CanEnterTitle => _active && _canCreate();
    public bool CanQuickCreate => CanEnterTitle && !IsSaving;
    public string? Message { get => _message; private set => SetProperty(ref _message, value); }
    public string LastSuccessfulRefreshText => _lastLoadedAt is { } loaded
        ? $"Обновлено в {loaded.ToString("HH:mm", CultureInfo.GetCultureInfo("ru-RU"))}"
        : "Данные ещё не обновлялись";
    public IReadOnlyList<MonthlyTaskRow> Rows { get => _rows; private set => SetProperty(ref _rows, value); }
    public AsyncCommand PreviousCommand { get; }
    public AsyncCommand NextCommand { get; }
    public AsyncCommand CurrentCommand { get; }
    public AsyncCommand WithoutMonthCommand { get; }
    public AsyncCommand ApplyYearCommand { get; }
    public AsyncCommand RefreshCommand { get; }
    public AsyncCommand CreateCommand { get; }
    public AsyncCommand OpenCommand { get; }

    private DateOnly CurrentMonth()
    {
        var now = _clock.GetLocalNow();
        return new(now.Year, now.Month, 1);
    }
    private static string FormatYear(DateOnly month) => month.ToString("yyyy", CultureInfo.InvariantCulture);
    private string? PlanningMonth => _withoutMonth ? null : _period.ToString("yyyy-MM", CultureInfo.InvariantCulture);

    internal Operation ActivateAsync()
    {
        if (_active || _disposed) return Operation.CompletedTask;
        _active = true;
        NotifyCommands();
        return ReloadAsync();
    }

    internal void Deactivate()
    {
        if (!_active) return;
        _active = false;
        CancelLoad();
        IsLoading = false;
        _tasks = [];
        ApplyFilters();
        NotifyCommands();
    }

    private Operation SelectMonthAsync(DateOnly month) => ChangePeriodAsync(month, false);

    private Operation ChangePeriodAsync(DateOnly month, bool withoutMonth)
    {
        if (_period == month && _withoutMonth == withoutMonth)
        {
            YearText = FormatYear(month);
            return Operation.CompletedTask;
        }
        _period = month;
        _withoutMonth = withoutMonth;
        YearText = FormatYear(month);
        OnPropertyChanged(nameof(SelectedMonth));
        OnPropertyChanged(nameof(PeriodText));
        OnPropertyChanged(nameof(WithoutMonthAction));
        _tasks = [];
        ApplyFilters();
        NotifyCommands();
        return ReloadAsync();
    }

    private void CancelLoad()
    {
        var previous = _load;
        _load = null;
        previous?.Cancel();
    }

    private async Operation ReloadAsync()
    {
        CancelLoad();
        using var load = new CancellationTokenSource();
        _load = load;
        IsLoading = true;
        Message = "Загружаем задачи…";
        try
        {
            var result = await _client.GetMonthlyTasksAsync(PlanningMonth, load.Token);
            if (!ReferenceEquals(_load, load)) return;
            if (result is DesktopTasksApiResult<DesktopTaskPage>.Succeeded success)
            {
                _tasks = success.Value.Items;
                ApplyFilters();
                _lastLoadedAt = _clock.GetLocalNow();
                OnPropertyChanged(nameof(LastSuccessfulRefreshText));
                Message = CountMessage();
            }
            else
            {
                _tasks = [];
                ApplyFilters();
                Message = result is DesktopTasksApiResult<DesktopTaskPage>.AuthenticationFailure or DesktopTasksApiResult<DesktopTaskPage>.Forbidden
                    ? "Нет доступа к задачам. Проверьте права и рабочую сессию."
                    : "Не удалось загрузить весь список. Повторите обновление.";
            }
        }
        catch (OperationCanceledException) when (load.IsCancellationRequested) { }
        catch (Exception)
        {
            if (ReferenceEquals(_load, load))
            {
                _tasks = [];
                ApplyFilters();
                Message = "Не удалось загрузить весь список. Повторите обновление.";
            }
        }
        finally
        {
            if (ReferenceEquals(_load, load))
            {
                _load = null;
                IsLoading = false;
            }
        }
    }

    private async Operation CreateAsync(CancellationToken token)
    {
        var title = TaskRules.NormalizeTitle(Title);
        if (_pendingCreate is not { } pending || pending.Title != title || pending.Card?.PlanningMonth != PlanningMonth)
            _pendingCreate = new(title, DesktopTaskPriority.Normal, card: new() { PlanningMonth = PlanningMonth });
        IsSaving = true;
        try
        {
            var result = await _client.CreateTaskAsync(_pendingCreate, token);
            if (_disposed) return;
            if (result is DesktopTaskWriteResult<DesktopTaskDto>.Succeeded success)
            {
                Title = "";
                _onCreated?.Invoke(success.Value);
                if (_active) await ReloadAsync();
            }
            else
            {
                Message = result is DesktopTaskWriteResult<DesktopTaskDto>.ValidationFailure validation
                    ? validation.Message : "Не удалось подтвердить создание. Повторите попытку.";
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception)
        {
            if (!_disposed) Message = "Не удалось подтвердить создание. Повторите попытку.";
        }
        finally { if (!_disposed) IsSaving = false; }
    }

    private string CountMessage() => Rows.Count > 0 ? $"Задач в списке: {Rows.Count}"
        : HideCompleted && _tasks.Count > 0 ? "Нет незавершённых задач." : "В этом списке пока нет задач.";

    private void ApplyFilters()
    {
        var now = _clock.GetUtcNow();
        var zone = _clock.LocalTimeZone;
        var tasks = HideCompleted ? _tasks.Where(task => !TaskRules.IsTerminal((TaskWorkStatus)task.Status)) : _tasks;
        Rows = TaskSorting.Apply(tasks, SortOrder, zone).Select(task => new MonthlyTaskRow(task, now, zone)).ToArray();
        _nextUrgencyChange = Rows.Select(row => row.NextChange).Min();
        if (_active && !IsLoading && _tasks.Count > 0) Message = CountMessage();
        OpenCommand.RaiseCanExecuteChanged();
    }

    internal void RefreshUrgency(bool force = false)
    {
        if (!_active || Rows.Count == 0) return;
        var now = _clock.GetUtcNow();
        if (!force && (_nextUrgencyChange is null || now < _nextUrgencyChange)) return;
        var zone = _clock.LocalTimeZone;
        foreach (var row in Rows) row.UpdateUrgency(now, zone);
        _nextUrgencyChange = Rows.Select(row => row.NextChange).Min();
    }

    internal void NotifyCommands()
    {
        OnPropertyChanged(nameof(CanChoosePeriod));
        OnPropertyChanged(nameof(CanEnterTitle));
        OnPropertyChanged(nameof(CanQuickCreate));
        foreach (var command in Commands) command.RaiseCanExecuteChanged();
    }

    private AsyncCommand[] Commands =>
        [PreviousCommand, NextCommand, CurrentCommand, WithoutMonthCommand, ApplyYearCommand, RefreshCommand, CreateCommand, OpenCommand];

    public void Dispose()
    {
        Deactivate();
        _disposed = true;
        foreach (var command in Commands) command.Dispose();
    }
}
