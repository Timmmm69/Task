using Task.Desktop.Work;

namespace Task.Desktop.ViewModels;

public sealed partial class WorkHubViewModel
{
    public AsyncCommand SelectLinkSourceCommand { get; }
    public string LinksHeader => "Связи: " + (IsContacts ? SelectedContact?.DisplayName : IsCatalog ? SelectedCatalogItem?.Item.Name : IsSearch ? SelectedSearchHit?.Title : null);
    private bool CanReadLinkSource => Links?.SourceType switch
    {
        "task" => Has("Task.Read"), "project" => Has("Project.Read"),
        "contact" or "company" or "interaction" => Has("Contact.Read"), "catalog_item" => Has("FileCatalog.Read"),
        "calendar_event" => Has("Calendar.Read") || Has("Task.Read"), _ => false,
    };
    private bool CanChangeLinks(string permission) => CanUseServerWrites && Has(permission) && CanReadLinkSource
        && (Links?.SourceType switch
        {
            "task" => true, "project" => Has("Project.Update"), "contact" or "company" => Has("Contact.Update"),
            "catalog_item" => Has("FileCatalog.Update"), "calendar_event" => Has("CalendarEvent.Update"),
            "interaction" => Has("Interaction.Update"), _ => false,
        });
    private void UpdateLinkAccess() => Links?.UpdateAccess(_capabilities, _sessionAvailable && _active, _networkAvailable);
    private void SetCurrentLinkSource()
    {
        OnPropertyChanged(nameof(LinksHeader));
        if (!_active || !_sessionAvailable || !Has("ObjectLink.Read") || !CanReadCurrentArea)
        { Links?.SetSource(Guid.Empty, 0); return; }
        if (IsContacts && SelectedContact is { } contact)
            Links?.SetSource(contact.Id, contact.Version, "contact", contact.LifecycleState == "active");
        else if (IsCatalog && SelectedCatalogItem is { } item)
            Links?.SetSource(item.Id, item.Version, "catalog_item", item.Item.LifecycleState == "active");
        else if (IsSearch && SelectedSearchHit is { } hit)
            Links?.SetSource(hit.Source.ObjectId, hit.Source.Version, hit.ObjectType);
        else Links?.SetSource(Guid.Empty, 0);
    }
    private void ApplyLinkVersion(long version)
    {
        // Keep host selections at the confirmed version when navigating away and back.
        if (IsContacts && _selectedContact is { } contact)
        {
            _selectedContact = contact with { Version = version };
            _contacts = _contacts.Select(row => row.Id == contact.Id ? row with { Version = version } : row).ToArray();
            OnPropertyChanged(nameof(Contacts)); OnPropertyChanged(nameof(SelectedContact));
        }
        else if (IsCatalog && SelectedCatalogItem is { } item) UpdateCatalogItem(item.Item with { Version = version });
        else if (IsSearch && _selectedSearchHit is { } hit)
        {
            _selectedSearchHit = hit with { Source = hit.Source with { Version = version } };
            _searchResults = _searchResults.Select(row => row.ObjectId == hit.Source.ObjectId ? row with { Version = version } : row).ToArray();
            OnPropertyChanged(nameof(SelectedSearchHit)); OnPropertyChanged(nameof(SearchResults));
            OnPropertyChanged(nameof(SearchGroups)); OnPropertyChanged(nameof(OverlaySearchHits));
        }
    }
}
