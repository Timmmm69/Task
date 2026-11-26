using System.ComponentModel;
using Task.Desktop.ViewModels;
using Task.Desktop.Work;

namespace Task.Desktop.Personal;

public sealed record PersonalCatalogRow(DesktopCatalogItem Source, string Path, string Location)
{
    public string ParentLocation { get; init; } = "Каталог";
    public bool CopyAllowed { get; init; }
    public Guid Id => Source.Id;
    public string Name => Source.Name;
    public string TypeLabel => Source.ItemTypeLabel;
    public bool IsFolder => Source.ItemType == "virtual_folder";
    public string PathLabel => IsFolder ? Location : Path.Length == 0 ? "Путь не указан" : Path;
}

/// <summary>Personal-only catalog presentation. All writes use the existing hub commands.</summary>
public sealed partial class PersonalWorkspaceViewModel
{
    private IReadOnlyList<PersonalCatalogRow> _catalogRows = [], _visibleCatalogRows = [];
    private PersonalCatalogRow? _catalogSelection;
    private Guid? _catalogFolderId;
    private string _catalogQuery = "", _catalogType = "Все типы", _catalogSort = "Название: А–Я";
    private bool _catalogEverywhere, _replacingCatalogRows;
    private readonly Dictionary<Guid, string> _catalogPathDrafts = [];
    public IReadOnlyList<PersonalCatalogRow> VisibleCatalogRows => _visibleCatalogRows;
    public IReadOnlyList<string> CatalogTypeFilters { get; } = ["Все типы", "Файл", "Папка на диске", "Виртуальная папка"];
    public IReadOnlyList<string> CatalogSortOrders { get; } = ["Название: А–Я", "Название: Я–А", "Тип и название"];
    public string CatalogQuery { get => _catalogQuery; set { if (SetProperty(ref _catalogQuery, value)) FilterCatalog(); } }
    public string CatalogTypeFilter { get => _catalogType; set { if (value is not null && SetProperty(ref _catalogType, value)) FilterCatalog(); } }
    public string CatalogSort { get => _catalogSort; set { if (value is not null && SetProperty(ref _catalogSort, value)) FilterCatalog(); } }
    public bool CatalogEverywhere { get => _catalogEverywhere; set { if (SetProperty(ref _catalogEverywhere, value)) FilterCatalog(); } }
    public string CatalogCountText => $"Найдено: {VisibleCatalogRows.Count} · Всего в каталоге: {_catalogRows.Count}";
    public bool CatalogLoadFailed => IsCatalog && !Hub.IsLoading && (Hub.IsFeedbackError || !Hub.CatalogSnapshotCurrent);
    public bool ShowCatalogEmpty => !Hub.IsLoading && !CatalogLoadFailed && VisibleCatalogRows.Count == 0;
    public string CatalogEmptyTitle => _catalogRows.Count == 0 ? "Каталог пока пуст" : CatalogQuery.Length > 0 || CatalogTypeFilter != "Все типы" ? "Ничего не найдено" : "В этой папке пусто";
    public string CatalogEmptyHint => _catalogRows.Count == 0 ? "Добавьте ссылку на файл или создайте виртуальную папку." : "Измените поиск, сбросьте фильтры или перейдите в другую папку.";
    public string CatalogLocation => _catalogFolderId is { } id ? _catalogRows.FirstOrDefault(r => r.Id == id)?.Location ?? "Каталог" : "Каталог";
    public bool CanGoCatalogUp => _catalogFolderId is not null;
    public PersonalCatalogRow? CatalogSelection
    {
        get => _catalogSelection;
        set
        {
            // Selector clears its selection while replacing/filtering ItemsSource.
            // Keep the inspector and its per-record path draft until an explicit selection.
            if (_replacingCatalogRows || value is null || !SetProperty(ref _catalogSelection, value)) return;
            _ = Hub.SelectCatalogItemAsync(value.Id);
            SyncCatalogSelection(true);
        }
    }
    public bool HasCatalogSelection => CatalogSelection is not null;
    public bool CanEditCatalogPath => CatalogSelection is { IsFolder: false };
    public bool CanCopyCatalogPath => CatalogSelection is { CopyAllowed: true, Path: { Length: > 0 } path }
        && Work.CatalogPathClipboard.IsAvailable(path);
    public string CatalogPathDraft
    {
        get => CatalogSelection is { } row ? _catalogPathDrafts.GetValueOrDefault(row.Id, row.Path) : "";
        set
        {
            if (CatalogSelection is not { } row) return;
            if (value == row.Path) _catalogPathDrafts.Remove(row.Id); else _catalogPathDrafts[row.Id] = value;
            OnPropertyChanged(); SaveCatalogPathCommand.RaiseCanExecuteChanged();
        }
    }
    public bool HasCatalogPathDrafts => _catalogPathDrafts.Count != 0;
    public AsyncCommand SaveCatalogPathCommand { get; private set; } = null!;

    private void InitializeCatalogBrowser()
    {
        SaveCatalogPathCommand = new(async (_, _) =>
        {
            if (CatalogSelection is not { } row) return;
            var path = CatalogPathDraft;
            var previous = Hub.NewItemPath;
            try
            {
                Hub.NewItemPath = path;
                await Hub.AddLocationCommand.ExecuteAsync();
                if (_store.PersonalLocation(row.Id)?.RawPath == path.Trim()) _catalogPathDrafts.Remove(row.Id);
                RebuildCatalogRows();
            }
            finally { Hub.NewItemPath = previous; OnPropertyChanged(nameof(CatalogPathDraft)); }
        }, _ => CanEditCatalogPath && !IsBusy && Hub.CanAddCatalogPaths && CatalogSelection is { } row && _catalogPathDrafts.ContainsKey(row.Id) && !string.IsNullOrWhiteSpace(CatalogPathDraft));
        Hub.PropertyChanged += OnCatalogHubChanged;
        RebuildCatalogRows();
    }
    private void OnCatalogHubChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Hub.Catalog)) RebuildCatalogRows();
        if (e.PropertyName == nameof(Hub.SelectedCatalogItem)) SyncCatalogSelection();
        if (e.PropertyName is nameof(Hub.IsLoading) or nameof(Hub.FeedbackKind) or nameof(Hub.CanAddCatalogPaths))
        {
            OnPropertyChanged(nameof(ShowCatalogEmpty));
            OnPropertyChanged(nameof(CatalogLoadFailed));
            SaveCatalogPathCommand.RaiseCanExecuteChanged();
        }
    }
    private void RebuildCatalogRows()
    {
        var items = Hub.Catalog.ToDictionary(i => i.Id);
        string Location(DesktopCatalogItem item)
        {
            var names = new Stack<string>(); var seen = new HashSet<Guid>();
            for (DesktopCatalogItem? current = item; current is not null && seen.Add(current.Id);
                 current = current.ParentId is { } id ? items.GetValueOrDefault(id) : null) names.Push(current.Name);
            return "Каталог / " + string.Join(" / ", names);
        }
        try
        {
            var allowLocal = _store.WorkspaceSettings().AllowLocalPaths;
            _catalogRows = Hub.Catalog.Select(item =>
            {
                var location = item.ItemType == "virtual_folder" ? null : _store.PersonalLocation(item.Id);
                return new PersonalCatalogRow(item, location?.RawPath ?? "", Location(item))
                {
                    ParentLocation = item.ParentId is { } parent && items.TryGetValue(parent, out var folder) ? Location(folder) : "Каталог",
                    CopyAllowed = location is { CanOpenOnDevice: true, IsEnabled: true } && (location.LocationType != "local_path" || allowLocal)
                };
            }).ToArray();
            if (_catalogFolderId is { } id && !_catalogRows.Any(r => r.Id == id && r.IsFolder)) _catalogFolderId = null;
            Refresh();
            var selectedId = Hub.SelectedCatalogItem?.Id ?? CatalogSelection?.Id;
            _catalogSelection = _catalogRows.FirstOrDefault(r => r.Id == selectedId);
            FilterCatalog();
            NotifyCatalogInspector();
        }
        catch (Exception e) when (e is Microsoft.Data.Sqlite.SqliteException or System.IO.IOException or UnauthorizedAccessException)
        { Message = "Не удалось прочитать сведения каталога. Обновите список; черновики сохранены в форме."; }
    }
    private void SyncCatalogSelection(bool explicitSelection = false)
    {
        if (!explicitSelection && Selected is { ObjectType: not "catalog_item" }) return;
        _catalogSelection = _catalogRows.FirstOrDefault(r => r.Id == Hub.SelectedCatalogItem?.Id);
        if (_catalogSelection is { } row) Selected = Objects.FirstOrDefault(o => o.Id == row.Id);
        else Selected = null;
        NotifyCatalogInspector();
    }
    private void NotifyCatalogInspector()
    {
        foreach (var name in new[] { nameof(CatalogSelection), nameof(HasCatalogSelection), nameof(CanEditCatalogPath), nameof(CanCopyCatalogPath), nameof(CatalogPathDraft) }) OnPropertyChanged(name);
        SaveCatalogPathCommand.RaiseCanExecuteChanged();
    }

    internal void SetCatalogMessage(string message) => Message = message;


    private void FilterCatalog()
    {
        IEnumerable<PersonalCatalogRow> rows = _catalogRows;
        if (!CatalogEverywhere) rows = rows.Where(r => r.Source.ParentId == _catalogFolderId);
        if (CatalogQuery.Trim() is { Length: > 0 } query) rows = rows.Where(r => r.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase));
        if (CatalogTypeFilter != "Все типы") rows = rows.Where(r => r.TypeLabel == CatalogTypeFilter);
        rows = CatalogSort switch
        {
            "Название: Я–А" => rows.OrderByDescending(r => r.Name, StringComparer.CurrentCultureIgnoreCase),
            "Тип и название" => rows.OrderBy(r => r.TypeLabel).ThenBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase),
            _ => rows.OrderBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase)
        };
        _visibleCatalogRows = rows.ToArray();
        _replacingCatalogRows = true;
        try { OnPropertyChanged(nameof(VisibleCatalogRows)); OnPropertyChanged(nameof(CatalogSelection)); }
        finally { _replacingCatalogRows = false; }
        foreach (var name in new[] { nameof(CatalogCountText), nameof(ShowCatalogEmpty), nameof(CatalogEmptyTitle), nameof(CatalogEmptyHint), nameof(CatalogLocation), nameof(CanGoCatalogUp) }) OnPropertyChanged(name);
    }
    public void ResetCatalogFilters()
    {
        _catalogQuery = ""; _catalogType = "Все типы"; _catalogSort = "Название: А–Я"; _catalogEverywhere = false;
        foreach (var name in new[] { nameof(CatalogQuery), nameof(CatalogTypeFilter), nameof(CatalogSort), nameof(CatalogEverywhere) }) OnPropertyChanged(name);
        FilterCatalog();
    }
    public void BrowseCatalogFolder(Guid? id)
    {
        if (id is not null && !_catalogRows.Any(r => r.Id == id && r.IsFolder)) return;
        _catalogFolderId = id;
        CatalogEverywhere = false;
        if (id is { } folderId) _ = Hub.SelectCatalogItemAsync(folderId);
        else Hub.SelectCatalogRootCommand.Execute(null);
        FilterCatalog();
    }
    public void GoCatalogUp() => BrowseCatalogFolder(_catalogRows.FirstOrDefault(r => r.Id == _catalogFolderId)?.Source.ParentId);
    public void ActivateCatalogBrowser() => SyncCatalogSelection(true);
    private void DisposeCatalogBrowser() { Hub.PropertyChanged -= OnCatalogHubChanged; SaveCatalogPathCommand.Dispose(); }
}
