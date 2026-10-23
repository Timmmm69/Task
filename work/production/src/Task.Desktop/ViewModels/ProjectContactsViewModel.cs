using System.ComponentModel;
using Task.Desktop.Projects;
using Task.Desktop.Work;
using AsyncTask = System.Threading.Tasks.Task;

namespace Task.Desktop.ViewModels;

public sealed record ProjectContactTarget(Guid Id, string ObjectType, string Name);
public sealed record ProjectContactItem(DesktopObjectLink Link, ProjectContactTarget? Target)
{
    public string Title => Target?.Name ?? "Контакт или компания недоступны";
    public string TypeLabel => Target?.ObjectType switch { "contact" => "Контакт", "company" => "Компания", _ => "Объект CRM" };
    public bool CanNavigate => Target is not null;
}

/// <summary>Only authorized names are retained; CRM detail cards belong to the existing workspace.</summary>
public sealed class ProjectContactsViewModel : ViewModelBase, IDisposable
{
    private readonly Infrastructure.ClosePendingOperations _closeOperations = new();
    internal bool HasUnconfirmedMutation => _closeOperations.HasPending;

    private readonly IDesktopObjectLinksClient _links;
    private readonly IDesktopCompaniesClient _crm;
    private HashSet<string> _capabilities = new(StringComparer.OrdinalIgnoreCase);
    private DesktopProjectDto? _project;
    private long _generation;
    private ProjectObjectLinksState _linkState = new();
    private bool _ready, _online, _writable, _snapshot, _disposed, _choosing;
    private IReadOnlyList<ProjectContactItem> _items = [];
    private ProjectContactItem? _selected;
    private string _message = "";

    public ProjectContactsViewModel(IDesktopObjectLinksClient links, IDesktopCompaniesClient crm, ObjectLinkTargetPickerViewModel picker)
    {
        _links = links; _crm = crm; Picker = picker;
        RefreshCommand = new((_, ct) => RunAsync(() => _links.GetLinksAsync(_project!.Id, ct), false, ct), _ => CanUse && !IsBusy);
        ChooseCommand = new((_, _) => { Choosing = true; return AsyncTask.CompletedTask; }, _ => CanMutate("ObjectLink.Create") && Has("Contact.Update"));
        AddCommand = new(AddAsync, _ => CanMutate("ObjectLink.Create") && Has("Contact.Update") && Picker.State == ObjectLinkTargetPickerState.Selected
            && Picker.CanQuery && Picker.SelectedTarget is { ObjectType: "contact" or "company" } target && target.ObjectId != _project?.Id
            && !Items.Any(row => row.Link.TargetObjectId == target.ObjectId));
        RemoveCommand = new((_, ct) =>
        {
            var source = _project!.Id; var link = Selected!.Link.Id;
            return RemoveAsync(source, link, ct);
        },
            _ => CanMutate("ObjectLink.Delete") && ConfirmedSelection);
        NavigateCommand = new((_, ct) => UseTargetAsync(ct), _ => CanUse && !IsBusy && _snapshot && ConfirmedSelection && Selected!.CanNavigate);
        foreach (var command in Commands) command.CanExecuteChanged += ExecutionChanged;
        Picker.PropertyChanged += PickerChanged;
        _linkState.Changed += NotifyCommands;
    }

    internal void UseLinkState(ProjectObjectLinksState state)
    {
        _linkState.Changed -= NotifyCommands; _linkState = state; _linkState.Changed += NotifyCommands;
    }

    public ProjectDetailSectionViewModel Section { get; } = new("contacts");
    public ObjectLinkTargetPickerViewModel Picker { get; }
    public event Action<Guid, long>? VersionConfirmed;
    public event Action<string, Guid>? OpenObjectRequested;
    public event Action? SessionExpired;
    public IReadOnlyList<ProjectContactItem> Items { get => _items; private set => SetProperty(ref _items, value); }
    public ProjectContactItem? Selected
    {
        get => _selected;
        set { if (ReferenceEquals(_selected, value)) return; _selected = value; OnPropertyChanged(); NotifyCommands(); }
    }
    public string Message { get => _message; private set => SetProperty(ref _message, value); }
    public bool Choosing { get => _choosing; private set => SetProperty(ref _choosing, value); }
    public bool IsBusy => Commands.Any(command => command.IsExecuting);
    public bool CanRead => _ready && Has("Project.Read") && Has("Contact.Read") && Has("ObjectLink.Read");
    private bool CanUse => !_disposed && CanRead && _online && _project is not null;
    private bool ConfirmedSelection => Selected is { } row && Items.Any(item => ReferenceEquals(item, row))
        && row.Link.SourceObjectId == _project?.Id && row.Link.LinkType == "project_contact";
    private bool CanMutate(string permission) => CanUse && _writable && _project?.LifecycleState == "active"
        && Has("Project.Update") && Has(permission) && _snapshot && _linkState.Version > 0 && !IsBusy && !_linkState.IsMutating;
    private bool Has(string permission) => _capabilities.Contains(permission);
    public AsyncCommand RefreshCommand { get; }
    public AsyncCommand ChooseCommand { get; }
    public AsyncCommand AddCommand { get; }
    public AsyncCommand RemoveCommand { get; }
    public AsyncCommand NavigateCommand { get; }
    private IEnumerable<AsyncCommand> Commands => [RefreshCommand, ChooseCommand, AddCommand, RemoveCommand, NavigateCommand];

    public void SetContext(DesktopProjectDto? project, IEnumerable<string> capabilities, bool ready, bool online, bool writable)
    {
        if (_disposed) return;
        var caps = capabilities.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (_project?.Id == project?.Id && _project?.OrganizationId == project?.OrganizationId
            && _project?.LifecycleState == project?.LifecycleState && _ready == ready && _online == online
            && _writable == writable && _capabilities.SetEquals(caps))
        { _project = project; _linkState.SetContext(project); NotifyCommands(); return; }
        Invalidate(); _project = project; _capabilities = caps; _ready = ready; _online = online; _writable = writable;
        _linkState.SetContext(project);
        Section.SetContext(project?.Id, project?.OrganizationId, CanRead, online);
        Picker.SetContext(project?.Id ?? Guid.Empty, "project", "project_contact");
        Picker.UpdateAccess(writable && Has("Contact.Update") && project?.LifecycleState == "active" ? caps : caps.Where(code => code != "ObjectLink.Create"), ready, online);
        NotifyCommands();
    }

    // Also called for unchanged capability names: object/project scopes can have changed.
    public void Invalidate()
    {
        ++_generation; _snapshot = false;
        foreach (var command in Commands) command.Cancel();
        Section.Cancel();
        if (_project is not null) Section.SetState(ProjectSectionState.Cancelled, false);
        Picker.Cancel(); Items = []; Selected = null; Choosing = false; Message = "";
    }

    private bool Current(long generation, CancellationToken ct) => generation == _generation && !ct.IsCancellationRequested && CanUse;

    private async AsyncTask AddAsync(object? _, CancellationToken ct)
    {
        var generation = _generation; var source = _project!.Id;
        using var mutation = _linkState.BeginMutation(source);
        if (mutation is null) return;
        var version = _linkState.Version;
        var target = Picker.SelectedObjectId!.Value;
        try
        {
            // Revalidate the picker's exact type. POST also checks target Contact.Update and object/project scope.
            var result = await ResolveTargetAsync(target, Picker.SelectedTarget!.ObjectType, ct);
            if (!Current(generation, ct)) return;
            if (result is not DesktopWorkResult<ProjectContactTarget>.Succeeded)
            { TargetFailure(result); return; }
            await RunAsync(() => _links.AddLinkAsync(source, version, target, "project_contact", ct), true, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception) { if (Current(generation, ct)) Fail(new DesktopWorkResult<DesktopObjectLinks>.ServerUnavailable()); }
    }

    private async AsyncTask RemoveAsync(Guid source, Guid link, CancellationToken ct)
    {
        using var mutation = _linkState.BeginMutation(source);
        if (mutation is null) return;
        var version = _linkState.Version;
        await RunAsync(() => _links.RemoveLinkAsync(source, version, link, ct), true, ct);
    }

    private async AsyncTask RunAsync(Func<System.Threading.Tasks.Task<DesktopWorkResult<DesktopObjectLinks>>> operation, bool mutation, CancellationToken ct)
    {
        var generation = _generation; var source = _project!.Id;
        using var request = Section.BeginRequest(ct);
        _snapshot = false; Selected = null;
        try
        {
            var result = mutation ? await _closeOperations.TrackAsync(operation) : await operation();
            if (!Current(generation, ct)) return;
            if (result is not DesktopWorkResult<DesktopObjectLinks>.Succeeded success) { Fail(result); return; }
            if (!_linkState.Confirm(source, success.Value.SourceVersion)) { Fail(new DesktopWorkResult<DesktopObjectLinks>.Conflict()); return; }
            VersionConfirmed?.Invoke(source, _linkState.Version);
            // The API returns incoming relations too; only owned outgoing project_contact links belong here.
            var rows = new List<ProjectContactItem>();
            foreach (var link in success.Value.Items.Where(link => link.SourceObjectId == source && link.LinkType == "project_contact"))
            {
                var metadata = await ResolveTargetAsync(link.TargetObjectId, null, request.Token);
                if (!Current(generation, request.Token)) return;
                if (metadata is DesktopWorkResult<ProjectContactTarget>.AuthenticationFailure) { Expire(); return; }
                var item = metadata is DesktopWorkResult<ProjectContactTarget>.Succeeded read ? read.Value : null;
                rows.Add(new(link, item));
            }
            if (success.Value.SourceVersion < _linkState.Version) { Fail(new DesktopWorkResult<DesktopObjectLinks>.Conflict()); return; }
            Items = rows; _snapshot = true;
            Selected = Items.FirstOrDefault();
            Section.SetState(rows.Any(row => row.Target is null) ? ProjectSectionState.PartialAccess
                : rows.Count == 0 ? ProjectSectionState.Empty : ProjectSectionState.Loaded, true);
            Message = mutation ? "Связи подтверждены сервером. Контакты и компании сохранены в CRM." : "";
            if (mutation) { Choosing = false; Picker.ClearSelection(); }
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested) { }
        catch (Exception) { if (Current(generation, ct)) Fail(new DesktopWorkResult<DesktopObjectLinks>.ServerUnavailable()); }
        finally { Section.FinishRequest(request); NotifyCommands(); }
    }

    private async AsyncTask UseTargetAsync(CancellationToken ct)
    {
        var generation = _generation; var row = Selected!; var id = row.Link.TargetObjectId;
        try
        {
            var result = await ResolveTargetAsync(id, row.Target!.ObjectType, ct);
            if (!Current(generation, ct)) return;
            if (result is not DesktopWorkResult<ProjectContactTarget>.Succeeded item) { TargetFailure(result); return; }
            OpenObjectRequested?.Invoke(item.Value.ObjectType, id);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception) { if (Current(generation, ct)) TargetFailure(new DesktopWorkResult<ProjectContactTarget>.ServerUnavailable()); }
    }

    private async System.Threading.Tasks.Task<DesktopWorkResult<ProjectContactTarget>> ResolveTargetAsync(Guid id, string? type, CancellationToken ct)
    {
        if (type is null or "contact")
        {
            var contact = await _crm.GetContactAsync(id, ct);
            ct.ThrowIfCancellationRequested();
            var result = Project(contact, value => value.Contact.Id == id && value.Contact.LifecycleState == "active"
                ? new(id, "contact", value.Contact.DisplayName) : null);
            // UUIDs are global. A 404 may identify a company; a denial or transport error is never retried as another type.
            if (type == "contact" || contact is not DesktopWorkResult<DesktopContactDetails>.NotFound) return result;
        }
        var company = await _crm.GetCompanyAsync(id, ct);
        ct.ThrowIfCancellationRequested();
        return Project(company, value => value.Id == id && value.LifecycleState == "active" ? new(id, "company", value.Name) : null);
    }

    private static DesktopWorkResult<ProjectContactTarget> Project<T>(DesktopWorkResult<T> result, Func<T, ProjectContactTarget?> map) => result switch
    {
        DesktopWorkResult<T>.Succeeded read when map(read.Value) is { } target && !string.IsNullOrWhiteSpace(target.Name)
            => new DesktopWorkResult<ProjectContactTarget>.Succeeded(target),
        DesktopWorkResult<T>.AuthenticationFailure => new DesktopWorkResult<ProjectContactTarget>.AuthenticationFailure(),
        DesktopWorkResult<T>.Forbidden => new DesktopWorkResult<ProjectContactTarget>.Forbidden(),
        DesktopWorkResult<T>.ServerUnavailable => new DesktopWorkResult<ProjectContactTarget>.ServerUnavailable(),
        _ => new DesktopWorkResult<ProjectContactTarget>.NotFound()
    };

    private void TargetFailure(DesktopWorkResult<ProjectContactTarget> result)
    {
        if (result is DesktopWorkResult<ProjectContactTarget>.AuthenticationFailure) { Expire(); return; }
        Picker.ClearSelection();
        Items = Items.Select(row => row with { Target = null }).ToArray(); Selected = null;
        Section.SetState(ProjectSectionState.PartialAccess);
        Message = "Контакт или компания недоступны. Обновите раздел и повторите выбор.";
    }

    private void Fail(DesktopWorkResult<DesktopObjectLinks> result)
    {
        _snapshot = false; Picker.ClearSelection();
        if (result is DesktopWorkResult<DesktopObjectLinks>.AuthenticationFailure) { Expire(); return; }
        Items = []; Selected = null;
        Section.SetState(result switch
        {
            DesktopWorkResult<DesktopObjectLinks>.Forbidden or DesktopWorkResult<DesktopObjectLinks>.NotFound => ProjectSectionState.Forbidden,
            DesktopWorkResult<DesktopObjectLinks>.ServerUnavailable => ProjectSectionState.ServerUnavailable,
            _ => ProjectSectionState.Error,
        }, false);
        Message = result switch
        {
            DesktopWorkResult<DesktopObjectLinks>.Conflict => ProjectObjectLinksState.ConflictMessage,
            DesktopWorkResult<DesktopObjectLinks>.ValidationFailure v when v.Message == ObjectLinkMessages.Duplicate => v.Message,
            DesktopWorkResult<DesktopObjectLinks>.ValidationFailure => "Состояние объекта не допускает связь. Обновите проект и раздел.",
            DesktopWorkResult<DesktopObjectLinks>.Forbidden => "Нет доступа к связям или выбранным объектам.",
            DesktopWorkResult<DesktopObjectLinks>.NotFound => "Проект, объект или связь больше недоступны.",
            _ => "Связи не подтверждены. Обновите раздел перед повторной попыткой.",
        };
    }

    private void Expire()
    {
        _ready = false; Invalidate(); Picker.UpdateAccess([], false);
        Section.SetState(ProjectSectionState.Forbidden, false); Message = "Сессия завершена. Выполните вход снова.";
        SessionExpired?.Invoke();
    }
    private void PickerChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_ready && Picker.State == ObjectLinkTargetPickerState.SessionExpired) Expire();
        NotifyCommands();
    }
    private bool _notifying;
    private void ExecutionChanged(object? sender, EventArgs e) { OnPropertyChanged(nameof(IsBusy)); NotifyCommands(); }
    private void NotifyCommands()
    {
        if (_notifying) return;
        _notifying = true;
        try { foreach (var command in Commands) command.RaiseCanExecuteChanged(); }
        finally { _notifying = false; }
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; Invalidate(); _linkState.Changed -= NotifyCommands; Picker.PropertyChanged -= PickerChanged; Picker.Dispose();
        foreach (var command in Commands) { command.CanExecuteChanged -= ExecutionChanged; command.Dispose(); }
    }
}
