using Task.Desktop.Work;

namespace Task.Desktop.ViewModels;

public sealed partial class WorkHubViewModel
{
    private IReadOnlyList<CatalogNodeViewModel> _catalogRoots = [];
    private Dictionary<Guid, CatalogNodeViewModel> _catalogNodes = [];
    private bool _catalogMutationPending;
    private bool _catalogSnapshotCurrent;
    private CatalogDestination? _moveDestination;
    public IReadOnlyList<CatalogNodeViewModel> CatalogRoots { get => _catalogRoots; private set => SetProperty(ref _catalogRoots, value); }
    public bool IsCatalogEmpty => CatalogRoots.Count == 0;
    public AsyncCommand CreateCatalogFolderCommand { get; private set; } = null!;
    public AsyncCommand MoveCatalogItemCommand { get; private set; } = null!;
    public AsyncCommand SelectCatalogRootCommand { get; private set; } = null!;
    public CatalogDestination? MoveDestination { get => _moveDestination; set { if (SetProperty(ref _moveDestination, value)) MoveCatalogItemCommand.RaiseCanExecuteChanged(); } }
    public string CatalogCreationLocation => CreationParentId is { } id && _catalogNodes.TryGetValue(id, out var parent) ? $"Создать в: {parent.Name}" : "Создать в: корень каталога";
    private Guid? CreationParentId => SelectedCatalogItem is { IsFolder: true } folder ? folder.Id : SelectedCatalogItem?.ParentId;
    private bool CanWriteCatalog => CanUseServerWrites && IsCatalog && Has("FileCatalog.Read") && _catalogSnapshotCurrent && !_catalogMutationPending && !IsLoading;
    public IReadOnlyList<CatalogDestination> CatalogMoveDestinations => [new(null, "Корень каталога"), .. _catalogNodes.Values
        .Where(n => n.IsFolder && SelectedCatalogItem is { } selected && IsValidCatalogMove(selected.Id, n.Id))
        .Select(n => new CatalogDestination(n.Id, CatalogPath(n)))];

    private string CatalogPath(CatalogNodeViewModel node)
    {
        var names = new Stack<string>();
        names.Push(node.Name);
        while (node.ParentId is { } parent && _catalogNodes.TryGetValue(parent, out node!)) names.Push(node.Name);
        return string.Join(" / ", names);
    }

    public bool CanMoveCatalogItem(Guid itemId, Guid? parentId) => CanWriteCatalog && Has("FileCatalog.Update") && IsValidCatalogMove(itemId, parentId);
    private bool IsValidCatalogMove(Guid itemId, Guid? parentId)
    {
        if (!_catalogNodes.TryGetValue(itemId, out var item) || item.ParentId == parentId) return false;
        if (parentId is null) return true;
        if (!_catalogNodes.TryGetValue(parentId.Value, out var parent) || !parent.IsFolder) return false;
        var visited = new HashSet<Guid>();
        for (CatalogNodeViewModel? ancestor = parent; ancestor is not null; ancestor = ancestor.ParentId is { } id ? _catalogNodes.GetValueOrDefault(id) : null)
            if (ancestor.Id == itemId || !visited.Add(ancestor.Id)) return false;
        return true;
    }

    private CatalogMoveRequest? MoveRequest(object? parameter) => parameter as CatalogMoveRequest ??
        (SelectedCatalogItem is { } item && MoveDestination is { } destination ? new(item.Id, destination.Id) : null);

    private void ReplaceCatalog(IReadOnlyList<DesktopCatalogItem> items, Guid? selectedId = null)
    {
        var expanded = _catalogNodes.Values.Where(n => n.IsExpanded).Select(n => n.Id).ToHashSet();
        selectedId ??= SelectedCatalogItem?.Id;
        var fallback = SelectedCatalogItem?.ParentId;
        var nodes = items.ToDictionary(i => i.Id, i => new CatalogNodeViewModel(i) { IsExpanded = expanded.Contains(i.Id) });
        var roots = new List<CatalogNodeViewModel>();
        foreach (var item in items)
        {
            var node = nodes[item.Id];
            if (node.ParentId is { } parent && nodes.TryGetValue(parent, out var parentNode) && parentNode.IsFolder)
                parentNode.Children.Add(node);
            else roots.Add(node);
        }
        Catalog = items;
        _catalogNodes = nodes;
        CatalogRoots = roots;
        SelectedCatalogItem = selectedId is { } id ? nodes.GetValueOrDefault(id) ?? (fallback is { } fallbackId ? nodes.GetValueOrDefault(fallbackId) : null) : null;
        OnPropertyChanged(nameof(IsCatalogEmpty));
        NotifyCatalogSelection();
    }

    public async System.Threading.Tasks.Task<bool> SelectCatalogItemAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (!Has("FileCatalog.Read") || !_sessionAvailable) return false;
        if (!_catalogNodes.ContainsKey(id) && _networkAvailable) await ReloadCatalogAsync(cancellationToken);
        if (!_catalogNodes.TryGetValue(id, out var node)) return false;
        for (var parent = node.ParentId; parent is { } parentId && _catalogNodes.TryGetValue(parentId, out var ancestor); parent = ancestor.ParentId)
            ancestor.IsExpanded = true;
        SelectedCatalogItem = node;
        return true;
    }

    private void NotifyCatalogSelection()
    {
        OnPropertyChanged(nameof(CatalogCreationLocation));
        OnPropertyChanged(nameof(CatalogMoveDestinations));
        MoveDestination = CatalogMoveDestinations.FirstOrDefault(d => d.Id == MoveDestination?.Id) ?? CatalogMoveDestinations.FirstOrDefault();
    }

    private async System.Threading.Tasks.Task<bool> ReloadCatalogAsync(CancellationToken ct, Guid? selectedId = null, bool reveal = false)
    {
        var result = await _client.GetCatalogAsync(ct);
        if (result is DesktopWorkResult<IReadOnlyList<DesktopCatalogItem>>.Succeeded success)
        {
            Apply(result, value => { ReplaceCatalog(value, selectedId); _catalogSnapshotCurrent = true; });
            if (reveal && selectedId is { } id) await SelectCatalogItemAsync(id, ct);
        }
        else
        {
            _catalogSnapshotCurrent = false;
            Apply(result, _ => { });
        }
        NotifyCommands();
        return result is DesktopWorkResult<IReadOnlyList<DesktopCatalogItem>>.Succeeded;
    }

    private async System.Threading.Tasks.Task CreateCatalogEntryAsync(string type, CancellationToken ct)
    {
        _catalogMutationPending = true;
        BeginOperation();
        NotifyCommands();
        try
        {
            var result = await _client.CreateCatalogItemAsync(NewItemName, type, null, CreationParentId, ct);
            if (result is DesktopWorkResult<DesktopCatalogItem>.Succeeded success)
            {
                _catalogSnapshotCurrent = false;
                ReplaceCatalog([.. Catalog, success.Value], success.Value.Id);
                NewItemName = string.Empty;
                if (await ReloadCatalogAsync(ct, success.Value.Id, true))
                    SetFeedback(type == "virtual_folder" ? "Виртуальная папка создана в каталоге Task." : "Запись создана. Теперь добавьте путь.", WorkHubFeedbackKind.Success);
            }
            else await RecoverCatalogErrorAsync(result, ct);
        }
        catch { _catalogSnapshotCurrent = false; throw; }
        finally { _catalogMutationPending = false; EndOperation(); NotifyCommands(); }
    }

    private async System.Threading.Tasks.Task MoveCatalogItemAsync(object? parameter, CancellationToken ct)
    {
        var request = MoveRequest(parameter);
        if (request is null || !CanMoveCatalogItem(request.ItemId, request.ParentId)) return;
        var item = _catalogNodes[request.ItemId];
        _catalogMutationPending = true;
        BeginOperation();
        NotifyCommands();
        try
        {
            var result = await _client.MoveCatalogItemAsync(item.Id, item.Version, request.ParentId, ct);
            if (result is DesktopWorkResult<DesktopCatalogItem>.Succeeded success)
            {
                _catalogSnapshotCurrent = false;
                ReplaceCatalog(Catalog.Select(i => i.Id == item.Id ? success.Value : i).ToArray(), item.Id);
                if (await ReloadCatalogAsync(ct, item.Id, true)) SetFeedback("Элемент перемещён.", WorkHubFeedbackKind.Success);
            }
            else await RecoverCatalogErrorAsync(result, ct);
        }
        catch { _catalogSnapshotCurrent = false; throw; }
        finally { _catalogMutationPending = false; EndOperation(); NotifyCommands(); }
    }

    private async System.Threading.Tasks.Task RecoverCatalogErrorAsync<T>(DesktopWorkResult<T> result, CancellationToken ct)
    {
        if (result is DesktopWorkResult<T>.ServerUnavailable) _catalogSnapshotCurrent = false;
        if (result is DesktopWorkResult<T>.Conflict or DesktopWorkResult<T>.NotFound or DesktopWorkResult<T>.MalformedResponse)
        {
            _catalogSnapshotCurrent = false;
            if (!await ReloadCatalogAsync(ct)) return;
        }
        Apply(result, _ => { });
    }
}
