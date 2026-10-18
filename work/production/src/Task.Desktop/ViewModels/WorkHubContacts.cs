using Task.Desktop.Work;

namespace Task.Desktop.ViewModels;

public sealed partial class WorkHubViewModel
{
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
        Contacts = [];
        ContactSelectionTask = ContactCard.SelectAsync(null);
    }
}
