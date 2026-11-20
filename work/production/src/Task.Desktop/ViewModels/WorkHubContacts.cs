using Task.Desktop.Work;

namespace Task.Desktop.ViewModels;

public sealed partial class WorkHubViewModel
{
    private string _contactQuery = "", _contactResultsQuery = "";

    public string ContactQuery
    {
        get => _contactQuery;
        set => SetProperty(ref _contactQuery, value);
    }

    public string ContactResultsQuery
    {
        get => _contactResultsQuery;
        private set => SetProperty(ref _contactResultsQuery, value);
    }

    private bool HasContactCardDraft => ContactCard is { IsEditing: true } or { HasRelationDraft: true }
        || ContactInteractions?.IsEditing == true;

    public ContactCardViewModel? ContactCard { get; private set; }
    public System.Threading.Tasks.Task ContactSelectionTask { get; private set; } = System.Threading.Tasks.Task.CompletedTask;

    private void InitializeContactCard()
    {
        if (IsPersonal || _client is not IDesktopContactDetailsClient client) return;
        ContactCard = new(client, () => !_disposed && _sessionAvailable && Has("Contact.Read"),
            () => !_disposed && _active && IsContacts && !ShowCompanies && _sessionAvailable && _networkAvailable && Has("Contact.Read"),
            () => Has("Contact.Update"));
        ContactCard.Saved += contact =>
        {
            Contacts = Contacts.Select(item => item.Id == contact.Id ? contact : item).ToArray();
            SetCurrentLinkSource();
        };
    }

    private void InvalidateContactScope()
    {
        if (ContactCard is null) return;
        ContactQuery = ContactResultsQuery = "";
        Contacts = [];
        ContactSelectionTask = ContactCard.SelectAsync(null);
    }
}
