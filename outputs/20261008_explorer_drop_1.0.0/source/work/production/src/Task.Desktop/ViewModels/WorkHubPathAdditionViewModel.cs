using Task.Desktop.Work;

namespace Task.Desktop.ViewModels;

public sealed partial class WorkHubViewModel
{
    private CatalogPathAddition? _pathAddition;
    public bool CanAddCatalogPaths => CanWriteCatalog && CanCreateCatalog && CanUpdateLocation;
    public Guid? CurrentCatalogFolder => CreationParentId;
    public bool CanDropCatalogPaths(Guid? parent) => CanAddCatalogPaths &&
        (parent is null || _catalogNodes.TryGetValue(parent.Value, out var node) && node.IsFolder);

    public async System.Threading.Tasks.Task AddCatalogPathsAsync(IReadOnlyList<string> paths, Guid? parent, CancellationToken ct = default)
    {
        if (!CanDropCatalogPaths(parent))
        { SetFeedback("Добавление требует подключения, актуального каталога и разрешений на запись.", WorkHubFeedbackKind.Warning); return; }
        _pathAddition ??= new(_client);
        _catalogMutationPending = true; BeginOperation(); NotifyCommands();
        try
        {
            var results = await _pathAddition.AddAsync(paths, parent,
                () => CanUseServerWrites && IsCatalog && Has("FileCatalog.Read") && CanCreateCatalog && CanUpdateLocation && !_disposed,
                Catalog, ct);
            _catalogSnapshotCurrent = false;
            if (CanUseServerWrites) await ReloadCatalogAsync(ct);
            var added = results.Count(r => r.Added);
            var failures = results.Where(r => !r.Added).Select(r => $"#{r.Index} {r.Name}: {r.Message}" + (r.ItemId is { } id ? $" Запись: {id}." : ""));
            SetFeedback($"Добавлено: {added} из {paths.Count}." + (added == results.Count ? "" : "\n" + string.Join("\n", failures)),
                added == results.Count ? WorkHubFeedbackKind.Success : WorkHubFeedbackKind.Warning);
        }
        catch { _catalogSnapshotCurrent = false; SetFeedback("Результат добавления не подтверждён. Обновите каталог.", WorkHubFeedbackKind.Error); }
        finally { _catalogMutationPending = false; EndOperation(); NotifyCommands(); }
    }
}
