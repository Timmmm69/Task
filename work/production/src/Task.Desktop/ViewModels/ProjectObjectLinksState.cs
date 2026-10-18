using Task.Desktop.Projects;

namespace Task.Desktop.ViewModels;

/// <summary>UI-thread owned CAS version and mutation lease shared by a project's link sections.</summary>
public sealed class ProjectObjectLinksState
{
    private Guid? _project, _organization;
    private IDisposable? _mutation;
    public long Version { get; private set; }
    public bool IsMutating => _mutation is not null;
    public event Action? Changed;
    internal const string ConflictMessage = "Версия проекта изменилась. Обновите раздел перед повторной попыткой.";

    internal void SetContext(DesktopProjectDto? project)
    {
        if (_project != project?.Id || _organization != project?.OrganizationId)
        {
            _project = project?.Id; _organization = project?.OrganizationId;
            Version = project?.Version ?? 0; _mutation = null; Changed?.Invoke();
        }
        else if (project is not null) Confirm(project.Id, project.Version);
    }

    internal bool Confirm(Guid project, long version)
    {
        if (_project != project || version < Version || version < 1) return false;
        if (version > Version) { Version = version; Changed?.Invoke(); }
        return true;
    }

    internal IDisposable? BeginMutation(Guid project)
    {
        if (_project != project || IsMutating || Version < 1) return null;
        var lease = new MutationLease(this);
        _mutation = lease; Changed?.Invoke(); return lease;
    }

    private sealed class MutationLease(ProjectObjectLinksState owner) : IDisposable
    {
        public void Dispose()
        {
            if (!ReferenceEquals(owner._mutation, this)) return;
            owner._mutation = null; owner.Changed?.Invoke();
        }
    }
}
