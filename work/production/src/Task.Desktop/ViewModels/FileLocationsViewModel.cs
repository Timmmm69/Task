using System.Collections.ObjectModel;
using System.Globalization;
using Task.Desktop.Administration;
using Task.Desktop.Work;

namespace Task.Desktop.ViewModels;

/// <summary>Location metadata only. No physical file operations or offline writes.</summary>
public sealed class FileLocationsViewModel : ViewModelBase, IDisposable
{
    private readonly IDesktopFileLocationsClient _client;
    private readonly Func<bool> _canRead, _canWrite;
    private CancellationTokenSource? _selectionCancellation;
    private long _generation;
    private Guid _itemId;
    private long _parentVersion;
    private bool _snapshotCurrent, _disposed, _accessAllowed;
    private bool _loading, _mutating, _editorVisible, _pathEditable = true, _resourcesLoading;
    private string _message = "", _rawPath = "", _locationType = "local_path", _priority = "0";
    private bool _enabled = true, _primary;
    private Guid? _editingId;
    private DesktopFileLocation? _pendingDelete;
    private DesktopNetworkResource? _networkResource;
    private IReadOnlyList<DesktopNetworkResource> _networkResources = [];

    public FileLocationsViewModel(IDesktopFileLocationsClient client, Func<bool> canRead, Func<bool> canWrite)
    {
        _client = client; _canRead = canRead; _canWrite = canWrite;
        RefreshCommand = new((_, ct) => ReloadAsync(ct), _ => IsVisible && _canRead() && !IsLoading && !IsMutating);
        AddCommand = new(async (_, ct) => { StartEditor(null); await LoadResourcesAsync(ct); }, _ => CanMutate && !ResourcesLoading && !EditorVisible);
        EditCommand = new(async (p, ct) => { StartEditor((DesktopFileLocation)p!); await LoadResourcesAsync(ct); }, p => CanMutate && !ResourcesLoading && !EditorVisible && Contains(p));
        SaveCommand = new(SaveAsync, _ => CanMutate && EditorVisible && ValidDraft);
        MakePrimaryCommand = new((p, ct) => MutateAsync((DesktopFileLocation)p!, false, ct), p => CanMutate && !EditorVisible && p is DesktopFileLocation { IsPrimary: false } && Contains(p));
        DeleteCommand = new((p, _) => { PendingDelete = (DesktopFileLocation)p!; return System.Threading.Tasks.Task.CompletedTask; }, p => CanMutate && !EditorVisible && Contains(p));
        ConfirmDeleteCommand = new((_, ct) => MutateAsync(PendingDelete!, true, ct), _ => CanMutate && PendingDelete is not null);
        CancelDeleteCommand = new((_, _) => { PendingDelete = null; return System.Threading.Tasks.Task.CompletedTask; }, _ => !IsMutating);
        CancelEditCommand = new((_, _) => { EditorVisible = false; return System.Threading.Tasks.Task.CompletedTask; }, _ => !IsMutating);
        foreach (var command in Commands) command.ExecutionFailed += _ => { _snapshotCurrent = false; Message = "Не удалось завершить действие. Обновите расположения и повторите попытку."; NotifyCommands(); };
    }

    public event Action<Guid, long>? ParentVersionChanged;
    public event Action<DesktopCatalogItem>? ParentChanged;
    public event Action? MutationStateChanged;
    public ObservableCollection<DesktopFileLocation> Items { get; } = [];
    public System.Threading.Tasks.Task SelectionLoad { get; private set; } = System.Threading.Tasks.Task.CompletedTask;
    public bool IsVisible => _itemId != Guid.Empty;
    public bool IsLoading { get => _loading; private set { SetProperty(ref _loading, value); NotifyPresentation(); } }
    public bool IsMutating { get => _mutating; private set { SetProperty(ref _mutating, value); MutationStateChanged?.Invoke(); NotifyCommands(); } }
    public bool IsEmpty => IsVisible && _snapshotCurrent && !IsLoading && Items.Count == 0;
    public bool HasPartialAccess => Items.Any(i => !i.IsPathVisible || i.LocationType != "unc_path" && !i.CanOpenOnDevice);
    public string Message { get => _message; private set => SetProperty(ref _message, value); }
    public bool EditorVisible { get => _editorVisible; private set { SetProperty(ref _editorVisible, value); NotifyCommands(); } }
    public bool IsFormEnabled => !IsMutating && !IsLoading && _canWrite();
    public bool PathEditable { get => _pathEditable; private set => SetProperty(ref _pathEditable, value); }
    public bool IsUnc => LocationType == "unc_path";
    public bool ResourcesLoading { get => _resourcesLoading; private set { SetProperty(ref _resourcesLoading, value); NotifyCommands(); } }
    public string EditorTitle => _editingId is null ? "Новое расположение" : "Изменить расположение";
    public string LocationType { get => _locationType; set { SetProperty(ref _locationType, value); OnPropertyChanged(nameof(IsUnc)); NotifyCommands(); } }
    public string RawPath { get => _rawPath; set { SetProperty(ref _rawPath, value); NotifyCommands(); } }
    public string Priority { get => _priority; set { SetProperty(ref _priority, value); NotifyCommands(); } }
    public bool IsEnabled { get => _enabled; set => SetProperty(ref _enabled, value); }
    public bool IsPrimary { get => _primary; set => SetProperty(ref _primary, value); }
    public IReadOnlyList<CatalogItemTypeChoice> LocationTypes { get; } = [new("local_path", "Локальный путь"), new("unc_path", "Сетевое расположение"), new("mapped_drive", "Подключённый диск")];
    public IReadOnlyList<DesktopNetworkResource> NetworkResources { get => _networkResources; private set => SetProperty(ref _networkResources, value); }
    public DesktopNetworkResource? NetworkResource { get => _networkResource; set { SetProperty(ref _networkResource, value); NotifyCommands(); } }
    public DesktopFileLocation? PendingDelete { get => _pendingDelete; private set { SetProperty(ref _pendingDelete, value); OnPropertyChanged(nameof(ShowDeleteConfirmation)); NotifyCommands(); } }
    public bool ShowDeleteConfirmation => PendingDelete is not null;
    public AsyncCommand RefreshCommand { get; }
    public AsyncCommand AddCommand { get; }
    public AsyncCommand EditCommand { get; }
    public AsyncCommand SaveCommand { get; }
    public AsyncCommand MakePrimaryCommand { get; }
    public AsyncCommand DeleteCommand { get; }
    public AsyncCommand ConfirmDeleteCommand { get; }
    public AsyncCommand CancelDeleteCommand { get; }
    public AsyncCommand CancelEditCommand { get; }
    private IEnumerable<AsyncCommand> Commands => [RefreshCommand, AddCommand, EditCommand, SaveCommand, MakePrimaryCommand, DeleteCommand, ConfirmDeleteCommand, CancelDeleteCommand, CancelEditCommand];
    private bool CanMutate => IsVisible && _canRead() && _canWrite() && _snapshotCurrent && !IsLoading && !IsMutating;
    private bool Contains(object? value) => value is DesktopFileLocation location && Items.Contains(location);
    private bool ValidDraft => int.TryParse(Priority, NumberStyles.Integer, CultureInfo.InvariantCulture, out _) && !ResourcesLoading &&
        (!PathEditable || RawPath.Trim().Length is >= 3 and <= 4096 && (!IsUnc || NetworkResource is { Status: "active" }));

    public void SetItem(DesktopCatalogItem? item)
    {
        var id = item?.ItemType is "file_reference" or "folder_reference" ? item.Id : Guid.Empty;
        if (id == _itemId)
        {
            if (item is not null && item.Version > _parentVersion && _canRead() && !IsMutating)
            {
                _selectionCancellation?.Cancel(); _selectionCancellation?.Dispose(); _selectionCancellation = new();
                _generation++; _snapshotCurrent = false;
                SelectionLoad = LoadSelectionAsync(_selectionCancellation.Token);
            }
            UpdateAccess(); return;
        }
        _selectionCancellation?.Cancel(); _selectionCancellation?.Dispose();
        _selectionCancellation = new();
        _generation++;
        _itemId = id; _parentVersion = item?.Version ?? 0; _snapshotCurrent = false;
        IsLoading = false; ResourcesLoading = false;
        Items.Clear(); EditorVisible = false; PendingDelete = null; Message = "";
        RawPath = ""; NetworkResources = []; NetworkResource = null;
        OnPropertyChanged(nameof(IsVisible)); NotifyPresentation();
        _accessAllowed = _canRead();
        SelectionLoad = LoadSelectionAsync(_selectionCancellation.Token);
    }

    public void UpdateAccess()
    {
        var allowed = _canRead();
        if (!allowed)
        {
            _selectionCancellation?.Cancel(); _generation++; _snapshotCurrent = false;
            Items.Clear(); EditorVisible = false; PendingDelete = null;
            RawPath = "";
            Message = IsVisible ? "Для просмотра расположений нужен доступ к каталогу и открытию файлов." : "";
            IsLoading = false; ResourcesLoading = false; NetworkResources = []; NetworkResource = null;
        }
        else if (!_accessAllowed && IsVisible)
        {
            _selectionCancellation?.Dispose(); _selectionCancellation = new();
            SelectionLoad = LoadSelectionAsync(_selectionCancellation.Token);
        }
        _accessAllowed = allowed;
        NotifyCommands(); NotifyPresentation();
    }

    private async System.Threading.Tasks.Task LoadSelectionAsync(CancellationToken ct)
    {
        var id = _itemId; var generation = _generation;
        try { if (IsVisible && _canRead()) await ReloadAsync(ct); else UpdateAccess(); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch { if (Current(id, generation)) { _snapshotCurrent = false; Message = "Не удалось загрузить расположения. Повторите обновление."; NotifyCommands(); } }
    }

    private async System.Threading.Tasks.Task<bool> ReloadAsync(CancellationToken ct)
    {
        var id = _itemId; var generation = _generation;
        if (id == Guid.Empty || !_canRead()) return false;
        IsLoading = true;
        try
        {
            var result = await _client.GetLocationsAsync(id, ct);
            if (!Current(id, generation)) return false;
            if (result is DesktopWorkResult<IReadOnlyList<DesktopFileLocation>>.Succeeded success)
            {
                var version = success.EntityVersion;
                if (version is null)
                {
                    var parent = await _client.GetCatalogItemAsync(id, ct);
                    if (!Current(id, generation)) return false;
                    if (parent is not DesktopWorkResult<DesktopCatalogItem>.Succeeded read) { Fail(parent); return false; }
                    ParentChanged?.Invoke(read.Value); version = read.Value.Version;
                }
                AcceptVersion(id, version.Value);
                Items.Clear(); foreach (var location in success.Value) Items.Add(location);
                _snapshotCurrent = true; Message = ""; NotifyPresentation();
                return true;
            }
            Fail(result); return false;
        }
        finally { if (Current(id, generation)) { IsLoading = false; NotifyCommands(); } }
    }

    private bool Current(Guid id, long generation) => !_disposed && id == _itemId && generation == _generation && _canRead();
    private void AcceptVersion(Guid id, long version) { _parentVersion = version; ParentVersionChanged?.Invoke(id, version); }

    private void StartEditor(DesktopFileLocation? location)
    {
        PendingDelete = null; _editingId = location?.Id;
        PathEditable = location is null || location.IsPathVisible;
        LocationType = location?.LocationType ?? "local_path"; RawPath = location?.RawPath ?? "";
        Priority = (location?.Priority ?? 0).ToString(CultureInfo.InvariantCulture);
        IsEnabled = location?.IsEnabled ?? true; IsPrimary = location?.IsPrimary ?? Items.Count == 0;
        NetworkResource = NetworkResources.FirstOrDefault(n => n.Id == location?.NetworkResourceId);
        EditorVisible = true; Message = PathEditable ? "" : "Путь скрыт. Можно изменить только приоритет и признаки расположения.";
        OnPropertyChanged(nameof(EditorTitle));
    }

    private async System.Threading.Tasks.Task LoadResourcesAsync(CancellationToken ct)
    {
        var id = _itemId; var generation = _generation; var editing = _editingId;
        var resourceId = editing is { } locationId ? Items.FirstOrDefault(i => i.Id == locationId)?.NetworkResourceId : null;
        ResourcesLoading = true;
        try
        {
            var result = await _client.GetNetworkResourcesAsync(ct);
            if (!Current(id, generation) || _editingId != editing) return;
            if (result is DesktopWorkResult<IReadOnlyList<DesktopNetworkResource>>.Succeeded success)
            {
                NetworkResources = success.Value; NetworkResource = NetworkResources.FirstOrDefault(n => n.Id == resourceId);
                if (IsUnc && NetworkResources.Count == 0) Message = "Нет доступных сетевых ресурсов. Обратитесь к администратору или выберите локальный путь.";
            }
            else { NetworkResources = []; NetworkResource = null; Message = "Не удалось получить разрешённые сетевые ресурсы. Для сетевого пути повторно откройте форму после восстановления доступа."; }
        }
        finally { if (Current(id, generation)) ResourcesLoading = false; }
    }

    private DesktopLocationDraft Draft() => new(LocationType, PathEditable ? RawPath.Trim() : null,
        int.Parse(Priority, CultureInfo.InvariantCulture), IsEnabled, IsPrimary, NetworkResource?.Id);
    private System.Threading.Tasks.Task SaveAsync(object? _, CancellationToken ct) => ExecuteMutationAsync(
        (id, version, token) => _editingId is { } editing ? _client.PatchLocationAsync(id, version, editing, Draft(), token) : _client.CreateLocationAsync(id, version, Draft(), token), null, ct);
    private System.Threading.Tasks.Task MutateAsync(DesktopFileLocation location, bool delete, CancellationToken ct) => ExecuteMutationAsync(
        (id, version, token) => delete ? _client.DeleteLocationAsync(id, version, location.Id, token) :
            _client.PatchLocationAsync(id, version, location.Id, new(location.LocationType, null, location.Priority, location.IsEnabled, true), token), delete ? location.Id : null, ct);

    private async System.Threading.Tasks.Task ExecuteMutationAsync(Func<Guid, long, CancellationToken, System.Threading.Tasks.Task<DesktopWorkResult<bool>>> mutation, Guid? deletedId, CancellationToken ct)
    {
        var id = _itemId; var generation = _generation;
        IsMutating = true;
        try
        {
            var result = await mutation(id, _parentVersion, ct);
            if (!Current(id, generation)) return;
            if (result is DesktopWorkResult<bool>.Succeeded success)
            {
                _snapshotCurrent = false;
                if (success.EntityVersion is { } version) AcceptVersion(id, version);
                EditorVisible = false; PendingDelete = null;
                if (deletedId is { } removed && Items.FirstOrDefault(i => i.Id == removed) is { } row) Items.Remove(row);
                var parent = await _client.GetCatalogItemAsync(id, ct);
                if (!Current(id, generation)) return;
                if (parent is not DesktopWorkResult<DesktopCatalogItem>.Succeeded read) { Fail(parent); Message = "Изменение сохранено. Не удалось обновить запись; обновите расположения перед следующим действием."; return; }
                AcceptVersion(id, read.Value.Version); ParentChanged?.Invoke(read.Value);
                if (await ReloadAsync(ct) && Current(id, generation)) Message = deletedId is null ? "Расположение сохранено." : "Расположение удалено. Файл на диске не изменён.";
            }
            else if (result is DesktopWorkResult<bool>.Conflict)
            {
                _snapshotCurrent = false; PendingDelete = null;
                var parent = await _client.GetCatalogItemAsync(id, ct);
                if (!Current(id, generation)) return;
                if (parent is not DesktopWorkResult<DesktopCatalogItem>.Succeeded read) { Fail(parent); return; }
                AcceptVersion(id, read.Value.Version); ParentChanged?.Invoke(read.Value);
                // Preserve the draft; reload metadata without retrying the write.
                if (await ReloadAsync(ct) && Current(id, generation)) Message = "Данные изменены другим пользователем. Список обновлён. Проверьте форму и повторите действие.";
            }
            else Fail(result);
        }
        finally { IsMutating = false; NotifyPresentation(); }
    }

    private void Fail<T>(DesktopWorkResult<T> result)
    {
        _snapshotCurrent = false;
        if (result is DesktopWorkResult<T>.Forbidden or DesktopWorkResult<T>.NotFound or DesktopWorkResult<T>.AuthenticationFailure)
        { Items.Clear(); EditorVisible = false; PendingDelete = null; RawPath = ""; NetworkResources = []; NetworkResource = null; }
        Message = result switch
        {
            DesktopWorkResult<T>.Forbidden => "Недостаточно прав. Локальный путь может изменять только его пользователь на своём компьютере; проверьте также разрешение локальных путей в настройках.",
            DesktopWorkResult<T>.NotFound => "Запись или расположение больше недоступны. Обновите каталог.",
            DesktopWorkResult<T>.AuthenticationFailure => "Сессия завершена. Выполните вход снова.",
            DesktopWorkResult<T>.ValidationFailure failure => failure.Message,
            DesktopWorkResult<T>.ServerUnavailable => "Сервер недоступен. Последние подтверждённые расположения сохранены для просмотра. Обновите список и повторите действие после восстановления связи.",
            _ => "Ответ сервера не удалось проверить. Обновите расположения перед следующим действием.",
        };
        // Validation rejects the write without changing the confirmed snapshot.
        if (result is DesktopWorkResult<T>.ValidationFailure) _snapshotCurrent = true;
        NotifyPresentation(); NotifyCommands();
    }
    private void NotifyPresentation() { OnPropertyChanged(nameof(IsEmpty)); OnPropertyChanged(nameof(HasPartialAccess)); }
    private void NotifyCommands() { OnPropertyChanged(nameof(IsFormEnabled)); foreach (var command in Commands) command.RaiseCanExecuteChanged(); }
    public void Dispose() { _disposed = true; _generation++; _selectionCancellation?.Cancel(); _selectionCancellation?.Dispose(); foreach (var command in Commands) command.Dispose(); }
}
