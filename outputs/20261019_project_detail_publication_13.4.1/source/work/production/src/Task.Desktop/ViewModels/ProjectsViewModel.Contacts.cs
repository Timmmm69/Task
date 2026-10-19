namespace Task.Desktop.ViewModels;

public sealed partial class ProjectsViewModel
{
    public ProjectContactsViewModel? Contacts { get; }
    private bool _contactsExpanded;
    public System.Threading.Tasks.Task OpenContactsSectionAsync()
    {
        _contactsExpanded = true;
        ConfigureContacts();
        return StartContactsLoad();
    }
    private System.Threading.Tasks.Task StartContactsLoad()
    {
        var previous = CurrentContactsOperation;
        var item = SelectedItem;
        var token = _activation?.Token ?? default;
        return CurrentContactsOperation = LoadAfterPreviousAsync();
        async System.Threading.Tasks.Task LoadAfterPreviousAsync()
        {
            // AsyncCommand serializes requests. Reconcile the new selection after an old transport finishes.
            await previous;
            if (!token.IsCancellationRequested && _contactsExpanded && _projectConfirmed && ReferenceEquals(SelectedItem, item)
                && IsActive && Contacts is not null) await Contacts.RefreshCommand.ExecuteAsync(cancellationToken: token);
        }
    }
    public void CloseContactsSection()
    {
        _contactsExpanded = false;
        Contacts?.Invalidate();
    }
    public System.Threading.Tasks.Task CurrentContactsOperation { get; private set; } = System.Threading.Tasks.Task.CompletedTask;
    private void ConfigureContacts() => Contacts?.SetContext(_projectConfirmed ? SelectedItem?.Source : null, _capabilities,
        IsActive && _sessionAvailable && CanRead, _networkAvailable, CanUpdate);
}
