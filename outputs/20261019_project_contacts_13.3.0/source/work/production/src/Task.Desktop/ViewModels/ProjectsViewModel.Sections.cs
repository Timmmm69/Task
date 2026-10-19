using Task.Desktop.Projects;

namespace Task.Desktop.ViewModels;

public sealed partial class ProjectsViewModel
{
    public ProjectDetailSectionViewModel OverviewSection { get; } = new("overview");
    public ProjectDetailSectionViewModel MembersSection { get; } = new("members");
    public ProjectDetailSectionViewModel TasksSection { get; } = new("tasks");
    // Integration slots: no UI or requests until a real loader and its access policy are connected.
    public ProjectDetailSectionViewModel FilesSection { get; } = new("files", false);
    public ProjectDetailSectionViewModel ContactsSection { get; } = new("contacts", false);
    public ProjectDetailSectionViewModel HistorySection { get; } = new("history", false);
    public IReadOnlyList<ProjectDetailSectionViewModel> DetailSections =>
        [OverviewSection, MembersSection, TasksSection, FilesSection, ContactsSection, HistorySection];
    public AsyncCommand ReloadMembersCommand { get; private set; } = null!;
    private bool CanReadMembers => CanRead && _capabilities.Contains("Project.ManageMembers");

    public void CancelMembersSection()
    {
        MembersSection.Cancel(); NotifyCommands();
    }

    private void SetDetailContext()
    {
        foreach (var section in DetailSections.Where(section => (Files is null || section != FilesSection) && (Contacts is null || section != ContactsSection)))
            section.SetContext(SelectedItem?.Id, SelectedItem?.Source.OrganizationId,
                _sessionAvailable && CanRead && (section != MembersSection || CanReadMembers)
                && (section != TasksSection || _capabilities.Contains("Task.Read") && _tasks is not null), _networkAvailable);
        ConfigureFiles(); ConfigureContacts();
    }

    public void CancelDetailSections()
    {
        Files?.Invalidate(); Contacts?.Invalidate();
        foreach (var section in DetailSections) section.Cancel();
        _detailRequest?.Cancel(); CancelRelatedTasks(); IsDetailLoading = false;
        ReloadMembersCommand.RaiseCanExecuteChanged();
    }

    private async System.Threading.Tasks.Task LoadMembersSectionAsync(ProjectItemViewModel item, CancellationToken token)
    {
        if (!IsActive || !_sessionAvailable || !_networkAvailable || !CanReadMembers || !_projectConfirmed
            || !ReferenceEquals(SelectedItem, item)) return;
        using var request = MembersSection.BeginRequest(token);
        bool Current() => MembersSection.IsCurrent(request, item.Id) && ReferenceEquals(SelectedItem, item)
            && IsActive && _sessionAvailable && _networkAvailable && CanReadMembers && !_disposed;
        ReloadMembersCommand.RaiseCanExecuteChanged();
        try
        {
            // Role failures belong to this section; overview and task paging remain usable.
            var roles = await _client.GetRolesAsync(request.Token);
            if (!Current()) return;
            if (roles is DesktopProjectResult<IReadOnlyList<DesktopProjectRoleDto>>.AuthenticationFailure)
            { UpdateSessionState(false); return; }
            Roles = roles is DesktopProjectResult<IReadOnlyList<DesktopProjectRoleDto>>.Succeeded catalog ? catalog.Value : [];
            var result = await _client.GetMembersAsync(item.Id, request.Token);
            if (!Current()) return;
            switch (result)
            {
                case DesktopProjectResult<IReadOnlyList<DesktopProjectMemberDto>>.Succeeded success:
                    Members = success.Value.Select(member => new ProjectMemberItemViewModel(member, Roles)).ToArray();
                    _membersConfirmed = true;
                    MembersSection.SetState(roles is not DesktopProjectResult<IReadOnlyList<DesktopProjectRoleDto>>.Succeeded
                        ? ProjectSectionState.PartialAccess : Members.Count == 0 ? ProjectSectionState.Empty : ProjectSectionState.Loaded, true);
                    try
                    {
                        await ResolvePeopleAsync(item, request.Token);
                        if (Current() && _directoryForbidden)
                            MembersSection.SetState(ProjectSectionState.PartialAccess, message: "Участники загружены. Нет доступа к именам сотрудников (User.Read).");
                    }
                    catch (OperationCanceledException) when (request.IsCancellationRequested) { }
                    catch (Exception) { if (Current()) MembersSection.SetState(ProjectSectionState.PartialAccess, message: "Участники загружены. Имена сотрудников временно недоступны."); }
                    break;
                case DesktopProjectResult<IReadOnlyList<DesktopProjectMemberDto>>.AuthenticationFailure:
                    UpdateSessionState(false); break;
                case DesktopProjectResult<IReadOnlyList<DesktopProjectMemberDto>>.Forbidden:
                case DesktopProjectResult<IReadOnlyList<DesktopProjectMemberDto>>.NotFound:
                    Members = []; _membersConfirmed = false;
                    MembersSection.SetState(ProjectSectionState.Forbidden, false); break;
                case DesktopProjectResult<IReadOnlyList<DesktopProjectMemberDto>>.ServerUnavailable:
                    MembersSection.SetState(ProjectSectionState.ServerUnavailable); break;
                default: MembersSection.SetState(ProjectSectionState.Error); break;
            }
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested) { }
        catch (Exception) { if (Current()) MembersSection.SetState(ProjectSectionState.Error); }
        finally
        {
            MembersSection.FinishRequest(request);
            NotifyCommands(); ReloadMembersCommand.RaiseCanExecuteChanged();
        }
    }
}
