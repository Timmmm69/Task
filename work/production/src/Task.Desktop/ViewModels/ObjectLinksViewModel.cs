using System.ComponentModel;
using Task.Desktop.Work;

namespace Task.Desktop.ViewModels;

public sealed record ObjectLinkTypeChoice(string Value, string Label);

/// <summary>Confirmed relations; Corporate uses the stage 10.1 picker, Personal keeps local TargetId.</summary>
public sealed class ObjectLinksViewModel : ViewModelBase, IDisposable
{
    private readonly Infrastructure.ClosePendingOperations _closeOperations = new();
    internal bool HasUnconfirmedMutation => _closeOperations.HasPending;

    private static readonly ObjectLinkTypeChoice[] AllTypes = [new("task_file", "Файл задачи"), new("project_file", "Файл проекта"), new("contact_file", "Файл контакта"), new("task_contact", "Контакт задачи")];
    private static readonly ObjectLinkTypeChoice[] CorporateTypes = [.. AllTypes, new("project_contact", "Контакт проекта"), new("task_project", "Проект задачи"), new("related", "Связанный объект")];
    private readonly IDesktopObjectLinksClient _client;
    private readonly Func<bool> _canWrite, _canRemove, _canRead;
    private readonly bool _corporate;
    private Guid _source;
    private long _version, _generation;
    private bool _disposed, _needsRefresh, _sourceActive = true, _sessionReady = true;
    private string _sourceType = "", _target = "", _type = "task_file", _message = "";
    private IReadOnlyList<DesktopObjectLink> _items = [];
    private DesktopObjectLink? _selected;

    public ObjectLinksViewModel(IDesktopObjectLinksClient client, Func<bool>? canWrite = null,
        Func<bool>? canRemove = null, Func<bool>? canRead = null,
        ObjectLinkTargetPickerViewModel? targetPicker = null, bool corporate = false)
    {
        _client = client; _canWrite = canWrite ?? (() => true); _canRemove = canRemove ?? _canWrite;
        _canRead = canRead ?? (() => true);
        _corporate = corporate; TargetPicker = targetPicker;
        RefreshCommand = new(async (_, ct) =>
        {
            var source = _source; var generation = _generation;
            await ExecuteOperationAsync(() => _client.GetLinksAsync(source, ct), generation, false, ct);
        }, _ => CanRead && !IsBusy);
        AddCommand = new(async (_, ct) =>
        {
            var source = _source; var generation = _generation; var target = TargetObjectId; var type = LinkType; var version = _version;
            await ExecuteOperationAsync(() => _client.AddLinkAsync(source, version, target, type, ct), generation, true, ct);
        }, _ => CanAdd);
        RemoveCommand = new(async (_, ct) =>
        {
            var source = _source; var generation = _generation; var version = _version; var link = Selected!.Id;
            await ExecuteOperationAsync(() => _client.RemoveLinkAsync(source, version, link, ct), generation, true, ct);
        }, _ => CanRead && _canRemove() && _sourceActive && !_needsRefresh && !IsBusy && Selected?.SourceObjectId == _source && _version > 0);
        foreach (var command in new[] { RefreshCommand, AddCommand, RemoveCommand })
        {
            command.CanExecuteChanged += OnExecutionChanged;
        }
        if (TargetPicker is not null) TargetPicker.PropertyChanged += OnPickerChanged;
    }

    public event Action<long>? Changed;
    public bool ShowTargetId { get; init; } = true;
    public ObjectLinkTargetPickerViewModel? TargetPicker { get; }
    public string SourceType => _sourceType;
    public bool HasCreationTypes => !_corporate || LinkTypes.Count > 0;
    public bool ShowPicker => _corporate && HasCreationTypes && TargetPicker is not null;
    public IReadOnlyList<DesktopObjectLink> Items { get => _items; private set => SetProperty(ref _items, value); }
    public DesktopObjectLink? Selected { get => _selected; set { SetProperty(ref _selected, value); RemoveCommand.RaiseCanExecuteChanged(); } }
    public string TargetId { get => _target; set { SetProperty(ref _target, value); AddCommand.RaiseCanExecuteChanged(); } }
    public string LinkType
    {
        get => _type;
        set
        {
            if (!SetProperty(ref _type, value ?? "")) return;
            TargetPicker?.SetContext(_source, _sourceType, _type);
            AddCommand.RaiseCanExecuteChanged();
        }
    }
    public IReadOnlyList<ObjectLinkTypeChoice> LinkTypes => !_corporate ? AllTypes : CorporateTypes.Where(choice => (_sourceType, choice.Value) is
        ("task", "task_file" or "task_contact" or "task_project") or ("project", "project_file" or "project_contact") or ("contact" or "company", "contact_file")
        or ("task" or "project" or "contact" or "company" or "catalog_item" or "calendar_event" or "interaction", "related")).ToArray();
    public string Message { get => _message; private set => SetProperty(ref _message, value); }
    public AsyncCommand RefreshCommand { get; }
    public AsyncCommand AddCommand { get; }
    public AsyncCommand RemoveCommand { get; }
    public bool IsBusy => RefreshCommand.IsExecuting || AddCommand.IsExecuting || RemoveCommand.IsExecuting;
    public bool IsMutating => AddCommand.IsExecuting || RemoveCommand.IsExecuting;
    private bool CanRead => !_disposed && _sessionReady && _canRead() && _source != Guid.Empty;
    private Guid TargetObjectId => _corporate ? TargetPicker?.SelectedObjectId ?? Guid.Empty : Guid.TryParse(TargetId, out var id) ? id : Guid.Empty;
    private bool CanAdd => CanRead && _canWrite() && _sourceActive && !_needsRefresh && !IsBusy && _version > 0
        && TargetObjectId != Guid.Empty && TargetObjectId != _source
        && (!_corporate || TargetPicker is { CanQuery: true, State: ObjectLinkTargetPickerState.Selected, SelectedTarget: not null }
            && LinkTypes.Any(type => type.Value == LinkType))
        && !Items.Any(link => link.SourceObjectId == _source && link.TargetObjectId == TargetObjectId && link.LinkType == LinkType);

    public void SetSource(Guid id, long version, string sourceType = "", bool active = true)
    {
        if (_disposed) return;
        // Never downgrade a version already confirmed by a link operation on this same object.
        if (id == _source && sourceType == _sourceType) version = Math.Max(version, _version);
        if ((_source, _version, _sourceType, _sourceActive) == (id, version, sourceType, active)) return;
        Invalidate();
        _source = id; _version = version; _sourceType = sourceType; _sourceActive = active;
        _needsRefresh = false; Items = []; Selected = null; if (_corporate) TargetId = ""; Message = "";
        TargetPicker?.Cancel();
        // Publish the new choices before their selection (WPF clears SelectedValue when ItemsSource changes).
        OnPropertyChanged(nameof(LinkTypes));
        if (_corporate) LinkType = LinkTypes.FirstOrDefault()?.Value ?? "";
        OnPropertyChanged(nameof(LinkType));
        TargetPicker?.SetContext(id, sourceType, LinkType);
        OnPropertyChanged(nameof(SourceType));
        OnPropertyChanged(nameof(HasCreationTypes)); OnPropertyChanged(nameof(ShowPicker)); NotifyCommands();
    }

    public void UpdateAccess(IEnumerable<string>? capabilities, bool sessionReady = true, bool online = true)
    {
        if (_disposed) return;
        _sessionReady = sessionReady;
        // Capability names do not describe object-level scope. Re-read under the new access context.
        _needsRefresh |= IsMutating;
        Invalidate(); TargetPicker?.UpdateAccess(capabilities, sessionReady, online);
        Items = []; Selected = null;
        NotifyCommands();
    }
    private void Invalidate()
    {
        ++_generation;
        foreach (var command in new[] { RefreshCommand, AddCommand, RemoveCommand }) command.Cancel();
    }
    private async System.Threading.Tasks.Task ExecuteOperationAsync(Func<System.Threading.Tasks.Task<DesktopWorkResult<DesktopObjectLinks>>> operation,
        long generation, bool mutation, CancellationToken ct)
    {
        try
        {
            var result = mutation ? await _closeOperations.TrackAsync(operation) : await operation();
            ct.ThrowIfCancellationRequested();
            Apply(result, generation, mutation);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        { if (mutation) Apply(new DesktopWorkResult<DesktopObjectLinks>.MalformedResponse(), generation, true); }
        catch (Exception) { Apply(new DesktopWorkResult<DesktopObjectLinks>.MalformedResponse(), generation, mutation); }
    }
    private void Apply(DesktopWorkResult<DesktopObjectLinks> result, long generation, bool mutation)
    {
        if (_disposed || generation != _generation || !CanRead) return;
        if (result is DesktopWorkResult<DesktopObjectLinks>.Succeeded ok)
        {
            Items = ok.Value.Items; _version = ok.Value.SourceVersion; _needsRefresh = false;
            Selected = Items.FirstOrDefault(); Message = "Связи обновлены.";
            if (mutation && _corporate) { TargetId = ""; TargetPicker?.ClearSelection(); }
            Changed?.Invoke(_version);
        }
        else
        {
            // A write may have committed before the response/read-back failed. Reconcile before retrying.
            if (mutation) _needsRefresh = true;
            Message = result switch
            {
                DesktopWorkResult<DesktopObjectLinks>.ValidationFailure v => _corporate ? v.Message switch { ObjectLinkMessages.Duplicate or ObjectLinkMessages.InvalidState => v.Message, _ => ObjectLinkMessages.InvalidTarget } : v.Message,
                DesktopWorkResult<DesktopObjectLinks>.Conflict => "Версия изменилась. Обновите связи перед повторной попыткой.",
                DesktopWorkResult<DesktopObjectLinks>.Forbidden => "Нет прав на изменение связей или выбранные объекты. Обновите права и связи.",
                DesktopWorkResult<DesktopObjectLinks>.NotFound => "Объект или связь больше недоступны. Обновите связи и повторите выбор.",
                DesktopWorkResult<DesktopObjectLinks>.AuthenticationFailure => "Сессия завершена. Войдите в систему.",
                DesktopWorkResult<DesktopObjectLinks>.ServerUnavailable => "Нет подключения к серверу. Обновите связи после восстановления подключения.",
                _ => "Не удалось подтвердить связи. Обновите связи перед повторной попыткой.",
            };
            if (result is DesktopWorkResult<DesktopObjectLinks>.AuthenticationFailure)
            {
                _sessionReady = false; Items = []; Selected = null;
                TargetPicker?.UpdateAccess([], sessionReady: false);
            }
            else if (!mutation && result is DesktopWorkResult<DesktopObjectLinks>.Forbidden or DesktopWorkResult<DesktopObjectLinks>.NotFound)
            {
                Items = []; Selected = null; _needsRefresh = true;
                TargetPicker?.UpdateAccess([]);
            }
            else if (mutation) TargetPicker?.ClearSelection();
        }
        NotifyCommands();
    }
    private void OnPickerChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_sessionReady && e.PropertyName == nameof(ObjectLinkTargetPickerViewModel.State)
            && TargetPicker?.State == ObjectLinkTargetPickerState.SessionExpired)
        {
            _sessionReady = false; _needsRefresh |= IsMutating;
            Invalidate(); Items = []; Selected = null;
            Message = "Сессия завершена. Войдите в систему.";
            NotifyCommands();
        }
        AddCommand.RaiseCanExecuteChanged();
    }
    private void OnExecutionChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(IsBusy)); OnPropertyChanged(nameof(IsMutating));
        // AsyncCommand notifies at start/end; also refresh sibling buttons without recursing.
        if (_notifying) return;
        NotifyCommands();
    }
    private bool _notifying;
    private void NotifyCommands()
    {
        if (_notifying) return;
        _notifying = true;
        try { foreach (var command in new[] { RefreshCommand, AddCommand, RemoveCommand }) command.RaiseCanExecuteChanged(); }
        finally { _notifying = false; }
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; ++_generation;
        if (TargetPicker is not null) TargetPicker.PropertyChanged -= OnPickerChanged;
        TargetPicker?.Dispose();
        foreach (var command in new[] { RefreshCommand, AddCommand, RemoveCommand })
        { command.CanExecuteChanged -= OnExecutionChanged; command.Dispose(); }
    }
}
