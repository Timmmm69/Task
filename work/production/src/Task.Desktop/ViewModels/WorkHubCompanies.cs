using Task.Desktop.Work;

namespace Task.Desktop.ViewModels;

public sealed partial class WorkHubViewModel
{
    private bool _showCompanies;
    public CompaniesViewModel? Companies { get; private set; }
    public bool HasCompanies => Companies is not null;
    public bool ShowCompanies => _showCompanies;
    public bool ShowContactPeople => !_showCompanies;
    public AsyncCommand ShowCompaniesCommand { get; private set; } = null!;
    public AsyncCommand ShowContactsCommand { get; private set; } = null!;

    private void InitializeCompanies()
    {
        if (!IsPersonal && _client is IDesktopCompaniesClient client)
        {
            Companies = new(client, () => !_disposed && _sessionAvailable && Has("Contact.Read"),
                () => !_disposed && _active && IsContacts && ShowCompanies && _sessionAvailable && _networkAvailable,
                () => Has("Contact.Create"), () => Has("Contact.Update"));
            Companies.OpenContactRequested += async id =>
            {
                SetCompanySurface(false);
                await OpenSearchObjectAsync("contact", id);
            };
        }
        ShowCompaniesCommand = new(async (_, ct) =>
        {
            SetCompanySurface(true);
            await Companies!.LoadListAsync(ct: ct);
            // Returning from a contact rechecks the selected company's current scope.
            if (Companies.SelectedCompany is { } selected && !Companies.IsEditing)
                await Companies.OpenAsync(selected.Id);
        }, _ => HasCompanies && _active && IsContacts && _sessionAvailable && Has("Contact.Read") && !HasRunningMutation);
        ShowContactsCommand = new((_, _) => { SetCompanySurface(false); return System.Threading.Tasks.Task.CompletedTask; },
            _ => _active && IsContacts && !HasRunningMutation);
    }

    private void SetCompanySurface(bool show)
    {
        _showCompanies = show && HasCompanies;
        OnPropertyChanged(nameof(ShowCompanies)); OnPropertyChanged(nameof(ShowContactPeople));
        ContactCard?.UpdateAccess(); Companies?.UpdateAccess(); NotifyPagination();
    }
}
