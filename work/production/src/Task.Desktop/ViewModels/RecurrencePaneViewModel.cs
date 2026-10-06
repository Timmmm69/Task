using System.Text.Json;
using System.Text.Json.Nodes;
using Task.Application.Calendar;
using Task.Desktop.Calendar;
using Task.Domain.Recurrence;

namespace Task.Desktop.ViewModels;

public sealed record RecurrenceListItem(Guid Id, long Version, RecurrenceDefinition Definition)
{
    public string Title => Definition.Template.Title;
    public override string ToString() => $"{Title} — {StatusText}";
    public string StatusText => Definition.Status switch { "active" => "Активна", "paused" => "Приостановлена", "cancelled" => "Отменена", _ => "Завершена" };
}

public sealed class RecurrencePaneViewModel : ViewModelBase, IDisposable
{
    private readonly IDesktopRecurrenceClient _client;
    private readonly bool _personal;
    private readonly Guid _actor;
    private CancellationTokenSource _lifetime = new();
    private long _generation;
    private bool _isOpen, _isBusy, _canRead, _canManage;
    private string? _message, _preview;
    private IReadOnlyList<RecurrenceListItem> _series = [];
    private IReadOnlyList<RecurrenceOccurrenceDetails> _occurrences = [];
    private RecurrenceListItem? _selected;
    private RecurrenceOccurrenceDetails? _occurrence;
    private RecurrenceEditorViewModel _editor = new();
    private DateTime? _throughDate = DateTime.Today.AddDays(90);
    private string _scope = "this_occurrence";
    private string? _attemptIdentity, _attemptKey;

    public RecurrencePaneViewModel(IDesktopRecurrenceApiClient client, Guid actor)
        : this(new DesktopRecurrenceHttpAdapter(client), actor, false) { }

    public RecurrencePaneViewModel(IDesktopRecurrenceClient client, Guid actor, bool personal)
    {
        _client = client; _actor = actor; _personal = personal;
        OpenCommand = new AsyncCommand(async (_, _) => { IsOpen = true; await RunAsync(LoadAsync); }, _ => _canRead && !IsBusy);
        CloseCommand = new AsyncCommand((_, _) => { Close(); return global::System.Threading.Tasks.Task.CompletedTask; });
        NewCommand = new AsyncCommand((_, _) => { Selected = null; Editor = new(); PreviewText = null; return global::System.Threading.Tasks.Task.CompletedTask; }, _ => _canManage && !IsBusy);
        ReloadCommand = new AsyncCommand((_, _) => RunAsync(LoadAsync), _ => _canRead && !IsBusy);
        PreviewCommand = new AsyncCommand((_, _) => RunAsync(PreviewAsync), _ => _canRead && !IsBusy);
        SaveCommand = new AsyncCommand((_, _) => RunAsync(SaveAsync), _ => _canManage && !IsBusy);
        GenerateCommand = new AsyncCommand((_, _) => RunAsync(GenerateAsync), _ => _canManage && !IsBusy && Selected?.Definition.Status == "active");
        PauseCommand = new AsyncCommand((_, _) => RunAsync(() => StatusAsync("paused")), _ => _canManage && !IsBusy && Selected?.Definition.Status == "active");
        ResumeCommand = new AsyncCommand((_, _) => RunAsync(() => StatusAsync("active")), _ => _canManage && !IsBusy && Selected?.Definition.Status == "paused");
        CancelSeriesCommand = new AsyncCommand((_, _) => RunAsync(() => StatusAsync("cancelled")), _ => _canManage && !IsBusy && Selected?.Definition.Status is "active" or "paused");
        ApplyCommand = new AsyncCommand((_, _) => RunAsync(ApplyAsync), _ => _canManage && !IsBusy && SelectedOccurrence is not null && Selected?.Definition.Status is "active" or "paused");
    }
    public event Action? Changed;
    public event Action? AccessLost;
    internal long SuccessfulSaveRevision { get; private set; }
    public bool IsOpen { get => _isOpen; private set => SetProperty(ref _isOpen, value); }
    public bool IsBusy { get => _isBusy; private set { if (SetProperty(ref _isBusy, value)) { OnPropertyChanged(nameof(CanInteract)); RaiseCommands(); } } }
    public bool CanInteract => !IsBusy;
    public bool CanManage => _canManage;
    public string? Message { get => _message; private set => SetProperty(ref _message, value); }
    public string? PreviewText { get => _preview; private set => SetProperty(ref _preview, value); }
    public IReadOnlyList<RecurrenceListItem> Series { get => _series; private set => SetProperty(ref _series, value); }
    public IReadOnlyList<RecurrenceOccurrenceDetails> Occurrences { get => _occurrences; private set => SetProperty(ref _occurrences, value); }
    public RecurrenceListItem? Selected { get => _selected; set { if (SetProperty(ref _selected, value)) { Editor = new(value?.Definition); SelectedOccurrence = null; Occurrences = []; PreviewText = null; RaiseCommands(); if (value is not null && !IsBusy) _ = RunAsync(LoadOccurrencesAsync); } } }
    public RecurrenceOccurrenceDetails? SelectedOccurrence { get => _occurrence; set { if (SetProperty(ref _occurrence, value)) RaiseCommands(); } }
    public RecurrenceEditorViewModel Editor { get => _editor; private set => SetProperty(ref _editor, value); }
    public DateTime? ThroughDate { get => _throughDate; set => SetProperty(ref _throughDate, value); }
    public string Scope { get => _scope; set => SetProperty(ref _scope, value); }
    public IReadOnlyList<RecurrenceChoice> Scopes { get; } = [new("this_occurrence", "Только выбранная задача"), new("this_and_future", "Выбранная и последующие"), new("entire_series", "Вся серия")];
    public AsyncCommand OpenCommand { get; }
    public AsyncCommand CloseCommand { get; }
    public AsyncCommand NewCommand { get; }
    public AsyncCommand ReloadCommand { get; }
    public AsyncCommand PreviewCommand { get; }
    public AsyncCommand SaveCommand { get; }
    public AsyncCommand GenerateCommand { get; }
    public AsyncCommand PauseCommand { get; }
    public AsyncCommand ResumeCommand { get; }
    public AsyncCommand CancelSeriesCommand { get; }
    public AsyncCommand ApplyCommand { get; }

    public void SetAccess(IEnumerable<string> capabilities, bool active)
    {
        var previousRead = _canRead; var previousManage = _canManage;
        var set = capabilities.ToHashSet(StringComparer.Ordinal);
        _canRead = active && set.Contains("Recurrence.Read"); _canManage = _canRead && set.Contains("Recurrence.Manage");
        if (!_canRead || previousManage && !_canManage) { Close(); Series = []; Selected = null; Editor = new(); }
        OnPropertyChanged(nameof(CanManage)); RaiseCommands();
    }
    public void Close()
    {
        _generation++; _lifetime.Cancel(); _lifetime.Dispose(); _lifetime = new();
        IsOpen = false; IsBusy = false; Series = []; Selected = null; Occurrences = []; Editor = new(); Message = null; PreviewText = null;
    }
    private async global::System.Threading.Tasks.Task RunAsync(Func<global::System.Threading.Tasks.Task> action)
    {
        if (!IsOpen || !_canRead || IsBusy) return;
        var generation = _generation; IsBusy = true; Message = null;
        try { await action(); }
        catch (OperationCanceledException) { }
        catch (Exception exception) when (exception is ArgumentException or FormatException or OverflowException)
        { if (generation == _generation) Message = "Проверьте название, интервал, дни и окончание серии. " + exception.Message; }
        catch (Exception) { if (generation == _generation) Message = "Не удалось выполнить действие. Данные формы сохранены; повторите попытку."; }
        finally { if (generation == _generation) IsBusy = false; }
    }
    private string WriteKey(object identity)
    {
        var text = JsonSerializer.Serialize(identity, RecurrenceService.JsonOptions);
        if (_attemptIdentity != text) { _attemptIdentity = text; _attemptKey = Guid.NewGuid().ToString("N"); }
        return _attemptKey!;
    }
    private async global::System.Threading.Tasks.Task<DesktopRecurrenceReply?> Request(DesktopRecurrenceCommand command)
    {
        var write = command is not (DesktopRecurrenceCommand.List or DesktopRecurrenceCommand.Occurrences or DesktopRecurrenceCommand.Preview);
        if (write && !_canManage) return null;
        var generation = _generation;
        var result = await _client.ExecuteAsync(command, _lifetime.Token);
        if (generation != _generation || !IsOpen || !_canRead) return null;
        if (result is DesktopCalendarResult<DesktopRecurrenceReply>.Succeeded success)
        { if (write) { _attemptIdentity = null; _attemptKey = null; } return success.Value; }
        if (result is DesktopCalendarResult<DesktopRecurrenceReply>.AuthenticationFailure or DesktopCalendarResult<DesktopRecurrenceReply>.Forbidden)
        { Close(); AccessLost?.Invoke(); return null; }
        Message = result switch
        {
            DesktopCalendarResult<DesktopRecurrenceReply>.VersionConflict => "Серия или задача изменена. Обновите список и повторите изменение.",
            DesktopCalendarResult<DesktopRecurrenceReply>.ValidationFailure validation => validation.Message,
            DesktopCalendarResult<DesktopRecurrenceReply>.NotFound => "Серия недоступна. Обновите список.",
            _ => _personal ? "Не удалось прочитать личную базу. Данные формы сохранены." : "Сервер недоступен. Данные формы сохранены; повторите попытку.",
        };
        return null;
    }
    private async global::System.Threading.Tasks.Task LoadAsync()
    {
        var selectedId = Selected?.Id;
        var result = await Request(new DesktopRecurrenceCommand.List()); if (result is null) return;
        Series = (result.Items ?? []).Select(s => new RecurrenceListItem(s.Id, s.Version, s.Definition)).ToArray();
        if (selectedId.HasValue) { Selected = Series.FirstOrDefault(s => s.Id == selectedId); await LoadOccurrencesAsync(); }
    }
    private async global::System.Threading.Tasks.Task LoadOccurrencesAsync()
    {
        var selected = Selected; if (selected is null) return;
        var result = await Request(new DesktopRecurrenceCommand.Occurrences(selected.Id));
        if (result is not null && Selected?.Id == selected.Id) Occurrences = result.Occurrences ?? [];
    }
    private async global::System.Threading.Tasks.Task PreviewAsync()
    {
        var rule = Editor.Build(_actor);
        var result = await Request(new DesktopRecurrenceCommand.Preview(rule, rule.OccurrenceStartDate, 10));
        if (result is null) return;
        var dates = result.Preview ?? [];
        PreviewText = dates.Count == 0 ? "В этом диапазоне нет повторений." : string.Join(Environment.NewLine, dates.Select(d => $"{d.LocalDate:dd.MM.yyyy}  {(d.StartAtUtc.HasValue ? TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(d.StartAtUtc.Value, DateTimeKind.Utc), TimeZoneInfo.FindSystemTimeZoneById(rule.TimeZone)).ToString("HH:mm") : "весь день")}{(d.DstAdjustment != "none" ? " · переход часового пояса" : "")}"));
    }
    private async global::System.Threading.Tasks.Task SaveAsync()
    {
        var rule = Editor.Build(_actor); var selected = Selected;
        var result = await Request(new DesktopRecurrenceCommand.Save(selected?.Id, selected?.Version, rule,
            WriteKey(new { action = "save", id = selected?.Id, version = selected?.Version, rule })));
        if (result?.Series is not { } saved) return;
        SuccessfulSaveRevision++;
        _selected = new(saved.Id, saved.Version, saved.Definition); await LoadAsync(); Message = "Серия сохранена. Созданные задачи доступны в календаре."; Changed?.Invoke();
    }
    private async global::System.Threading.Tasks.Task GenerateAsync()
    {
        var selected = Selected; if (selected is null || !ThroughDate.HasValue) return;
        var through = DateOnly.FromDateTime(ThroughDate.Value);
        var result = await Request(new DesktopRecurrenceCommand.Generate(selected.Id, selected.Version, through, WriteKey(new { action = "generate", selected.Id, selected.Version, through })));
        if (result is not null) { await LoadAsync(); Message = "Горизонт расписания расширен."; Changed?.Invoke(); }
    }
    private async global::System.Threading.Tasks.Task StatusAsync(string status)
    {
        var selected = Selected; if (selected is null) return;
        var result = await Request(new DesktopRecurrenceCommand.SetStatus(selected.Id, selected.Version, status, WriteKey(new { action = status, selected.Id, selected.Version })));
        if (result is not null) { await LoadAsync(); Message = "Состояние серии изменено."; Changed?.Invoke(); }
    }
    private async global::System.Threading.Tasks.Task ApplyAsync()
    {
        var selected = Selected; var occurrence = SelectedOccurrence; if (selected is null || occurrence is null) return;
        var template = Editor.Build(_actor).Template;
        var scope = Scope switch { "this_occurrence" => RecurrenceChangeScope.ThisOccurrence, "this_and_future" => RecurrenceChangeScope.ThisAndFuture, "entire_series" => RecurrenceChangeScope.EntireSeries, _ => throw new ArgumentException("Выберите область изменения.") };
        var result = await Request(new DesktopRecurrenceCommand.Apply(selected.Id, selected.Version, occurrence.LocalDate, occurrence.TaskVersion,
            scope, template.Title, template.Priority, template.PlannedDurationMinutes,
            WriteKey(new { action = "apply", selected.Id, selected.Version, occurrence.LocalDate, occurrence.TaskVersion, scope, template })));
        if (result is not null) { await LoadAsync(); Message = "Изменение применено к выбранной области серии."; Changed?.Invoke(); }
    }
    internal static RecurrenceListItem ParseSeries(JsonElement json)
    {
        var definition = JsonNode.Parse(json.GetRawText())!.AsObject();
        foreach (var name in new[] { "id", "organizationId", "version", "createdAt", "updatedAt" }) definition.Remove(name);
        return new(json.GetProperty("id").GetGuid(), json.GetProperty("version").GetInt64(), definition.Deserialize<RecurrenceDefinition>(RecurrenceService.JsonOptions)!);
    }
    private IEnumerable<AsyncCommand> Commands => new[] { OpenCommand, CloseCommand, NewCommand, ReloadCommand, PreviewCommand, SaveCommand, GenerateCommand, PauseCommand, ResumeCommand, CancelSeriesCommand, ApplyCommand };
    private void RaiseCommands() { foreach (var command in Commands) command.RaiseCanExecuteChanged(); }
    public void Dispose() { Close(); _lifetime.Dispose(); foreach (var command in Commands) command.Dispose(); }
}
