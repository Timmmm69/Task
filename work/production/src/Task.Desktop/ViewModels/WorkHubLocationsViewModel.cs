using Task.Desktop.Work;

namespace Task.Desktop.ViewModels;

public sealed partial class WorkHubViewModel
{
    public FileLocationsViewModel? Locations { get; private set; }
    private void InitializeLocations()
    {
        if (_client is not IDesktopFileLocationsClient client) return;
        Locations = new(client,
            () => _sessionAvailable && Has("FileCatalog.Read") && Has("FileReference.Open"),
            () => CanUseServerWrites && IsCatalog && Has("FileLocation.Update") && _catalogSnapshotCurrent && !_catalogMutationPending && !IsLoading);
        Locations.ParentVersionChanged += (id, version) =>
        {
            if (_catalogNodes.TryGetValue(id, out var node) && version >= node.Version)
                UpdateCatalogItem(node.Item with { Version = version });
        };
        Locations.ParentChanged += UpdateCatalogItem;
        Locations.MutationStateChanged += NotifyCommands;
    }

    private void UpdateCatalogItem(DesktopCatalogItem item)
    {
        if (!_catalogNodes.TryGetValue(item.Id, out var node) || item.Version < node.Version) return;
        node.UpdateItem(item);
        Catalog = Catalog.Select(i => i.Id == item.Id ? item : i).ToArray();
        if (SelectedCatalogItem?.Id == item.Id) Links?.SetSource(item.Id, item.Version);
    }
}
