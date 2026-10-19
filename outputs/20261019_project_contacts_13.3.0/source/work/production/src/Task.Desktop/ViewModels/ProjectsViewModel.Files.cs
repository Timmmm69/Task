namespace Task.Desktop.ViewModels;

public sealed partial class ProjectsViewModel
{
    private readonly ProjectObjectLinksState _projectLinks = new();
    public ProjectFilesViewModel? Files { get; }
    private bool _filesExpanded;
    public System.Threading.Tasks.Task OpenFilesSectionAsync()
    {
        _filesExpanded = true;
        ConfigureFiles();
        return StartFilesLoad();
    }
    private System.Threading.Tasks.Task StartFilesLoad()
    {
        var previous = CurrentFilesOperation;
        var item = SelectedItem;
        var token = _activation?.Token ?? default;
        return CurrentFilesOperation = LoadAfterPreviousAsync();
        async System.Threading.Tasks.Task LoadAfterPreviousAsync()
        {
            // AsyncCommand serializes requests. Reconcile the new selection after an old transport finishes.
            await previous;
            if (!token.IsCancellationRequested && _filesExpanded && _projectConfirmed && ReferenceEquals(SelectedItem, item)
                && IsActive && Files is not null) await Files.RefreshCommand.ExecuteAsync(cancellationToken: token);
        }
    }
    public void CloseFilesSection()
    {
        _filesExpanded = false;
        Files?.Invalidate();
    }
    public System.Threading.Tasks.Task CurrentFilesOperation { get; private set; } = System.Threading.Tasks.Task.CompletedTask;
    private void ConfigureFiles() => Files?.SetContext(_projectConfirmed ? SelectedItem?.Source : null, _capabilities,
        IsActive && _sessionAvailable && CanRead, _networkAvailable, CanUpdate);
    private void LinksSessionExpired() => UpdateSessionState(false);
    private void LinksVersionConfirmed(Guid id, long version)
    {
        if (SelectedItem is not { } item || item.Id != id) return;
        item.ConfirmVersion(version);
        // Existing editor drafts must retain their captured version and detect concurrent changes.
        NotifyCommands();
    }
}
