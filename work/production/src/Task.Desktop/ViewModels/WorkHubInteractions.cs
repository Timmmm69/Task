using Task.Desktop.Work;

namespace Task.Desktop.ViewModels;

public sealed partial class WorkHubViewModel
{
    public InteractionsViewModel? ContactInteractions { get; private set; }
    public InteractionsViewModel? CompanyInteractions { get; private set; }

    private void InitializeInteractions()
    {
        if (IsPersonal || _client is not IDesktopInteractionsClient client) return;
        bool Read() => !_disposed && _sessionAvailable && Has("Contact.Read");
        bool Request() => Read() && _active && IsContacts && _networkAvailable;
        ContactInteractions = new(client, Read, () => Request() && !ShowCompanies && ContactCard?.IsEditing != true && ContactCard?.IsSaving != true,
            () => Has("Interaction.Create"), () => Has("Interaction.Update"));
        CompanyInteractions = new(client, Read, () => Request() && ShowCompanies && Companies?.IsEditing != true && Companies?.IsBusy != true,
            () => Has("Interaction.Create"), () => Has("Interaction.Update"));
        if (ContactCard is not null) ContactCard.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ContactCard.Details))
                ContactInteractions.SetCounterparty(ContactCard.Details is { Contact.LifecycleState: "active" } d ? new(d.Contact.Id, "contact", d.Contact.DisplayName) : null);
            ContactInteractions.UpdateAccess();
        };
        if (Companies is not null)
        {
            Companies.Interactions = CompanyInteractions;
            Companies.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(Companies.Details))
                    CompanyInteractions.SetCounterparty(Companies.Details is { LifecycleState: "active" } d ? new(d.Id, "company", d.Name) : null);
                CompanyInteractions.UpdateAccess();
            };
        }
    }
}
