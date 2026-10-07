using Task.Desktop.Work;

namespace Task.Desktop.ViewModels;

public sealed class CatalogNodeViewModel(DesktopCatalogItem item) : ViewModelBase
{
    private bool _isExpanded;
    private bool _isSelected;
    internal DesktopCatalogItem Item { get; private set; } = item;
    internal void UpdateItem(DesktopCatalogItem value)
    {
        Item = value;
        foreach (var property in new[] { nameof(Version), nameof(Name), nameof(Description), nameof(ParentId) }) OnPropertyChanged(property);
    }
    public Guid Id => Item.Id;
    public long Version => Item.Version;
    public string Name => Item.Name;
    public string ItemType => Item.ItemType;
    public string ItemTypeLabel => Item.ItemTypeLabel;
    public string? Description => Item.Description;
    public Guid? ParentId => Item.ParentId;
    public bool IsFolder => ItemType == "virtual_folder";
    public string Icon => IsFolder ? "📁" : "📄";
    public List<CatalogNodeViewModel> Children { get; } = [];
    public bool IsExpanded { get => _isExpanded; set => SetProperty(ref _isExpanded, value); }
    public bool IsSelected { get => _isSelected; set => SetProperty(ref _isSelected, value); }
}

public sealed record CatalogMoveRequest(Guid ItemId, Guid? ParentId);
public sealed record CatalogDestination(Guid? Id, string Label);
