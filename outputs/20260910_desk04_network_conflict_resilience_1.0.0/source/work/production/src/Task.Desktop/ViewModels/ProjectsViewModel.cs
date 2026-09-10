using System.ComponentModel;
using System.Globalization;
using Task.Desktop.Projects;
using Task.Desktop.TaskApi;

namespace Task.Desktop.ViewModels;

public enum ProjectsScreenState { Idle, Loading, Loaded, Empty, Forbidden, SessionEnded, Failed }

public sealed record ProjectStatusOption(DesktopProjectStatus Value, string Text);

public sealed class ProjectItemViewModel : ViewModelBase
{
    public ProjectItemViewModel(DesktopProjectDto source) => Source = source;
    public DesktopProjectDto Source { get; private set; }
    public Guid Id => Source.Id;
    public string Name => Source.Name;
    public string Description => string.IsNullOrWhiteSpace(Source.Description) ? "Описание не указано" : Source.Description;
    public string StatusText => LocalizeStatus(Source.Status);
    public string DatesText => Source.StartDate is null && Source.PlannedEndDate is null ? "Сроки не указаны"
        : $"{Source.StartDate?.ToString("dd.MM.yyyy") ?? "—"} — {Source.PlannedEndDate?.ToString("dd.MM.yyyy") ?? "—"}";
    public void Replace(DesktopProjectDto source)
    {
        Source = source;
        OnPropertyChanged(nameof(Source)); OnPropertyChanged(nameof(Name)); OnPropertyChanged(nameof(Description));
        OnPropertyChanged(nameof(StatusText)); OnPropertyChanged(nameof(DatesText));
    }
    public static string LocalizeStatus(DesktopProjectStatus status) => status switch
    { DesktopProjectStatus.Planning => "Планируется", DesktopProjectStatus.Active => "Активный", DesktopProjectStatus.Paused => "Приостановлен", DesktopProjectStatus.Completed => "Завершён", _ => status.ToString() };
}

public sealed class ProjectMemberItemViewModel : ViewModelBase
{
    private DesktopProjectRoleDto? _selectedRole;
    public ProjectMemberItemViewModel(DesktopProjectMemberDto source, IReadOnlyList<DesktopProjectRoleDto> roles)
    { Source = source; Roles = roles; _selectedRole = roles.FirstOrDefault(role => role.Id == source.ProjectRoleId); }
    public DesktopProjectMemberDto Source { get; }
    public Guid UserAccountId => Source.UserAccountId;
    public string UserText => Source.UserAccountId.ToString("D");
    public IReadOnlyList<DesktopProjectRoleDto> Roles { get; }
    public DesktopProjectRoleDto? SelectedRole { get => _selectedRole; set => SetProperty(ref _selectedRole, value); }
    public string RoleText => SelectedRole?.Name ?? Source.ProjectRoleId.ToString("D");
}

public sealed class ProjectEditorViewModel : ViewModelBase
{
    private string _name;
    private string _description;
    private string _managerId;
    private ProjectStatusOption _status;
    private string _startDate;
    private string _plannedEndDate;
    private string? _error;

    public ProjectEditorViewModel(Guid ownerId, DesktopProjectDto? source = null)
    {
        OwnerId = ownerId; Source = source;
        Statuses = new[]
        {
            new ProjectStatusOption(DesktopProjectStatus.Planning, "Планируется"),
            new ProjectStatusOption(DesktopProjectStatus.Active, "Активный"),
            new ProjectStatusOption(DesktopProjectStatus.Paused, "Приостановлен"),
            new ProjectStatusOption(DesktopProjectStatus.Completed, "Завершён")
        };
        _name = source?.Name ?? string.Empty;
        _description = source?.Description ?? string.Empty;
        _managerId = source?.ManagerUserId?.ToString("D") ?? string.Empty;
        _status = Statuses.First(option => option.Value == (source?.Status ?? DesktopProjectStatus.Planning));
        _startDate = source?.StartDate?.ToString("dd.MM.yyyy") ?? string.Empty;
        _plannedEndDate = source?.PlannedEndDate?.ToString("dd.MM.yyyy") ?? string.Empty;
    }

    public DesktopProjectDto? Source { get; private set; }
    public Guid OwnerId { get; }
    public string Heading => Source is null ? "Новый проект" : "Изменить проект";
    public string SubmitText => Source is null ? "Создать" : "Сохранить";
    public IReadOnlyList<ProjectStatusOption> Statuses { get; }
    public string Name { get => _name; set { if (SetProperty(ref _name, value)) Error = null; } }
    public string Description { get => _description; set { if (SetProperty(ref _description, value)) Error = null; } }
    public string ManagerId { get => _managerId; set { if (SetProperty(ref _managerId, value)) Error = null; } }
    public ProjectStatusOption Status { get => _status; set { if (SetProperty(ref _status, value)) Error = null; } }
    public string StartDate { get => _startDate; set { if (SetProperty(ref _startDate, value)) Error = null; } }
    public string PlannedEndDate { get => _plannedEndDate; set { if (SetProperty(ref _plannedEndDate, value)) Error = null; } }
    public string? Error { get => _error; private set => SetProperty(ref _error, value); }

    public bool TryBuild(out DesktopProjectDraft draft)
    {
        draft = null!;
        var name = Name.Trim();
        if (name.Length is < 1 or > 300) return Fail("Укажите название проекта (до 300 символов).");
        if (Description.Length > 10000) return Fail("Описание не должно превышать 10 000 символов.");
        if (!TryGuid(ManagerId, out var manager)) return Fail("Идентификатор руководителя должен быть UUID или пустым.");
        if (!TryDate(StartDate, out var start)) return Fail("Дата начала: используйте формат ДД.ММ.ГГГГ.");
        if (!TryDate(PlannedEndDate, out var end)) return Fail("Плановая дата: используйте формат ДД.ММ.ГГГГ.");
        if (start.HasValue && end < start) return Fail("Плановая дата завершения не может быть раньше даты начала.");
        DateTimeOffset? completedAt = Status.Value == DesktopProjectStatus.Completed
            ? Source?.ActualEndAt ?? DateTimeOffset.UtcNow
            : null;
        draft = new(name, string.IsNullOrWhiteSpace(Description) ? null : Description.Trim(), OwnerId, manager,
            Status.Value, start, end, completedAt, Source?.DefaultTimeZone, Source?.ColorCode);
        Error = null; return true;
    }

    public void SetError(string message) => Error = message;
    public void RebaseSource(DesktopProjectDto source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (Source?.Id != source.Id) throw new ArgumentException("The server project must match the edited project.", nameof(source));
        Source = source;
        OnPropertyChanged(nameof(Source));
    }
    private bool Fail(string message) { Error = message; return false; }
    private static bool TryGuid(string text, out Guid? value)
    { value = null; if (string.IsNullOrWhiteSpace(text)) return true; if (Guid.TryParse(text.Trim(), out var parsed) && parsed != Guid.Empty) { value = parsed; return true; } return false; }
    private static bool TryDate(string text, out DateOnly? value)
    { value = null; if (string.IsNullOrWhiteSpace(text)) return true; if (DateOnly.TryParseExact(text.Trim(), "dd.MM.yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)) { value = parsed; return true; } return false; }
}

public sealed class ProjectsViewModel : ViewModelBase, IDisposable
{
    private readonly IDesktopProjectsApiClient _client;
    private readonly IDesktopTasksApiClient? _tasks;
    private readonly Guid _currentUserId;
    private readonly HashSet<string> _capabilities;
    private CancellationTokenSource? _activation;
    private IReadOnlyList<ProjectItemViewModel> _items = Array.Empty<ProjectItemViewModel>();
    private IReadOnlyList<ProjectMemberItemViewModel> _members = Array.Empty<ProjectMemberItemViewModel>();
    private IReadOnlyList<TaskItemViewModel> _relatedTasks = Array.Empty<TaskItemViewModel>();
    private IReadOnlyList<DesktopProjectRoleDto> _roles = Array.Empty<DesktopProjectRoleDto>();
    private ProjectItemViewModel? _selectedItem;
    private ProjectEditorViewModel? _editor;
    private ProjectsScreenState _state;
    private string? _message;
    private string _newMemberUserId = string.Empty;
    private DesktopProjectRoleDto? _newMemberRole;
    private bool _isDetailLoading;
    private DateTimeOffset? _lastRefresh;
    private bool _sessionAvailable = true;
    private bool _networkAvailable = true;
    private bool _disposed;

    public ProjectsViewModel(IDesktopProjectsApiClient client, Guid currentUserId,
        IEnumerable<string>? capabilities = null, IDesktopTasksApiClient? tasks = null)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        if (currentUserId == Guid.Empty) throw new ArgumentException("Current user id must not be empty.", nameof(currentUserId));
        _currentUserId = currentUserId; _tasks = tasks;
        _capabilities = (capabilities ?? []).ToHashSet(StringComparer.OrdinalIgnoreCase);
        RefreshCommand = new(RefreshAsync, _ => IsActive && _sessionAvailable && CanRead);
        NewProjectCommand = new((_, _) => { Editor = new(_currentUserId); return System.Threading.Tasks.Task.CompletedTask; }, _ => CanCreate);
        EditProjectCommand = new((_, _) => { if (SelectedItem is not null) Editor = new(SelectedItem.Source.OwnerUserId, SelectedItem.Source); return System.Threading.Tasks.Task.CompletedTask; }, _ => CanUpdate && SelectedItem is not null);
        SaveProjectCommand = new(SaveAsync, _ => Editor is not null && (Editor.Source is null ? CanCreate : CanUpdate));
        CancelEditorCommand = new((_, _) => { Editor = null; return System.Threading.Tasks.Task.CompletedTask; });
        ArchiveProjectCommand = new(ArchiveAsync, _ => CanArchive && SelectedItem?.Source.Status == DesktopProjectStatus.Completed);
        AddMemberCommand = new(AddMemberAsync, _ => CanManageMembers && SelectedItem is not null && NewMemberRole is not null && Guid.TryParse(NewMemberUserId, out var id) && id != Guid.Empty);
        ChangeMemberRoleCommand = new(ChangeMemberRoleAsync, parameter => CanManageMembers && parameter is ProjectMemberItemViewModel { SelectedRole: not null });
        RemoveMemberCommand = new(RemoveMemberAsync, parameter => CanManageMembers && parameter is ProjectMemberItemViewModel member && member.UserAccountId != SelectedItem?.Source.OwnerUserId);
    }

    public IReadOnlyList<ProjectItemViewModel> Items { get => _items; private set { if (SetProperty(ref _items, value)) OnPropertyChanged(nameof(HasItems)); } }
    public bool HasItems => Items.Count > 0;
    public IReadOnlyList<ProjectMemberItemViewModel> Members { get => _members; private set => SetProperty(ref _members, value); }
    public IReadOnlyList<TaskItemViewModel> RelatedTasks { get => _relatedTasks; private set => SetProperty(ref _relatedTasks, value); }
    public IReadOnlyList<DesktopProjectRoleDto> Roles { get => _roles; private set { if (SetProperty(ref _roles, value) && NewMemberRole is null) NewMemberRole = value.FirstOrDefault(); } }
    public ProjectItemViewModel? SelectedItem
    {
        get => _selectedItem;
        set
        {
            if (!SetProperty(ref _selectedItem, value)) return;
            NotifyCommands();
            if (value is null) { Members = []; RelatedTasks = []; }
            else if (IsActive) _ = LoadDetailsAsync(value, _activation?.Token ?? CancellationToken.None);
        }
    }
    public ProjectEditorViewModel? Editor { get => _editor; private set { if (SetProperty(ref _editor, value)) NotifyCommands(); } }
    public ProjectsScreenState State { get => _state; private set => SetProperty(ref _state, value); }
    public string? Message { get => _message; private set => SetProperty(ref _message, value); }
    public bool IsDetailLoading { get => _isDetailLoading; private set => SetProperty(ref _isDetailLoading, value); }
    public string NewMemberUserId { get => _newMemberUserId; set { if (SetProperty(ref _newMemberUserId, value)) AddMemberCommand.RaiseCanExecuteChanged(); } }
    public DesktopProjectRoleDto? NewMemberRole { get => _newMemberRole; set { if (SetProperty(ref _newMemberRole, value)) AddMemberCommand.RaiseCanExecuteChanged(); } }
    public bool IsActive { get; private set; }
    public bool CanRead => _capabilities.Contains("Project.Read");
    public bool CanCreate => CanWrite("Project.Create");
    public bool CanUpdate => CanWrite("Project.Update");
    public bool CanArchive => CanWrite("Project.Archive");
    public bool CanManageMembers => CanWrite("Project.ManageMembers");
    public string AccessText => !CanRead ? "Нет права Project.Read."
        : !_sessionAvailable ? "Сессия завершена."
        : !_networkAvailable ? "Сервер недоступен · подтверждённые данные только для просмотра."
        : CanUpdate ? "Проекты доступны для изменения." : "Проекты доступны только для просмотра.";
    public string LastSuccessfulRefreshText => _lastRefresh is null ? "Проекты ещё не обновлялись" : $"Проекты обновлены {_lastRefresh:HH:mm}";
    public AsyncCommand RefreshCommand { get; }
    public AsyncCommand NewProjectCommand { get; }
    public AsyncCommand EditProjectCommand { get; }
    public AsyncCommand SaveProjectCommand { get; }
    public AsyncCommand CancelEditorCommand { get; }
    public AsyncCommand ArchiveProjectCommand { get; }
    public AsyncCommand AddMemberCommand { get; }
    public AsyncCommand ChangeMemberRoleCommand { get; }
    public AsyncCommand RemoveMemberCommand { get; }

    public void Activate()
    {
        if (_disposed || IsActive) return;
        IsActive = true; _activation = new(); NotifyCommands();
        if (!CanRead) { State = ProjectsScreenState.Forbidden; Message = "Раздел проектов недоступен для текущей роли."; return; }
        _ = RefreshCommand.ExecuteAsync(cancellationToken: _activation.Token);
    }
    public void Deactivate()
    {
        if (!IsActive) return;
        IsActive = false; _activation?.Cancel(); _activation?.Dispose(); _activation = null; NotifyCommands();
    }

    public void UpdateCapabilities(IEnumerable<string>? capabilities)
    {
        _capabilities.Clear();
        foreach (var capability in capabilities ?? []) _capabilities.Add(capability);
        NotifyCommands();
    }

    public void UpdateSessionState(bool available)
    {
        if (_sessionAvailable == available) return;
        _sessionAvailable = available;
        if (!available)
        {
            _activation?.Cancel();
            State = ProjectsScreenState.SessionEnded;
            Message = "Сессия завершена. Черновик проекта сохранён в форме.";
        }
        NotifyCommands();
    }

    public void UpdateConnectivity(bool available)
    {
        if (_networkAvailable == available) return;
        _networkAvailable = available;
        if (!available && Editor is not null)
            Editor.SetError("Сервер недоступен. Черновик сохранён в форме и не будет отправлен автоматически.");
        NotifyCommands();
    }

    private async System.Threading.Tasks.Task RefreshAsync(object? _, CancellationToken cancellationToken)
    {
        State = ProjectsScreenState.Loading; Message = null;
        var rolesTask = _client.GetRolesAsync(cancellationToken);
        var projectsTask = _client.GetProjectsAsync(cancellationToken: cancellationToken);
        await System.Threading.Tasks.Task.WhenAll(rolesTask, projectsTask).ConfigureAwait(true);
        if (!IsActive) return;
        if (rolesTask.Result is DesktopProjectResult<IReadOnlyList<DesktopProjectRoleDto>>.Succeeded roles) Roles = roles.Value;
        var result = projectsTask.Result;
        if (result is DesktopProjectResult<DesktopProjectPage>.Succeeded success)
        {
            var projects = success.Value.Items.ToList(); var cursor = success.Value.NextCursor; var seen = new HashSet<string>();
            while (!string.IsNullOrWhiteSpace(cursor) && seen.Add(cursor) && projects.Count <= 10000)
            {
                var next = await _client.GetProjectsAsync(cursor, cancellationToken).ConfigureAwait(true);
                if (next is not DesktopProjectResult<DesktopProjectPage>.Succeeded page) { ApplyFailure(next); return; }
                projects.AddRange(page.Value.Items); cursor = page.Value.NextCursor;
            }
            if (projects.Count > 10000 || !string.IsNullOrWhiteSpace(cursor)) { State = ProjectsScreenState.Failed; Message = "Список проектов слишком велик или курсор повторился."; return; }
            var selectedId = SelectedItem?.Id;
            Items = projects.Select(project => new ProjectItemViewModel(project)).ToArray();
            _lastRefresh = DateTimeOffset.Now; OnPropertyChanged(nameof(LastSuccessfulRefreshText));
            State = Items.Count == 0 ? ProjectsScreenState.Empty : ProjectsScreenState.Loaded;
            Message = Items.Count == 0 ? "Активных проектов нет." : null;
            SelectedItem = Items.FirstOrDefault(item => item.Id == selectedId) ?? Items.FirstOrDefault();
        }
        else ApplyFailure(result);
    }

    private async System.Threading.Tasks.Task LoadDetailsAsync(ProjectItemViewModel item, CancellationToken cancellationToken)
    {
        IsDetailLoading = true;
        try
        {
            var projectResult = await _client.GetProjectAsync(item.Id, cancellationToken).ConfigureAwait(true);
            if (!ReferenceEquals(SelectedItem, item)) return;
            if (projectResult is DesktopProjectResult<DesktopProjectDto>.Succeeded project) item.Replace(project.Value);
            else { ApplyFailure(projectResult); return; }

            if (CanManageMembers)
            {
                var memberResult = await _client.GetMembersAsync(item.Id, cancellationToken).ConfigureAwait(true);
                if (!ReferenceEquals(SelectedItem, item)) return;
                Members = memberResult is DesktopProjectResult<IReadOnlyList<DesktopProjectMemberDto>>.Succeeded members
                    ? members.Value.Select(member => new ProjectMemberItemViewModel(member, Roles)).ToArray() : [];
            }
            else Members = [];
            RelatedTasks = await LoadRelatedTasksAsync(item.Id, cancellationToken).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        finally { if (ReferenceEquals(SelectedItem, item)) IsDetailLoading = false; }
    }

    private async System.Threading.Tasks.Task<IReadOnlyList<TaskItemViewModel>> LoadRelatedTasksAsync(Guid projectId, CancellationToken cancellationToken)
    {
        if (_tasks is null || !_capabilities.Contains("Task.Read")) return [];
        var result = new List<TaskItemViewModel>(); string? cursor = null; var seen = new HashSet<string>();
        do
        {
            var page = await _tasks.GetTasksAsync(cursor, cancellationToken).ConfigureAwait(true);
            if (page is not DesktopTasksApiResult<DesktopTaskPage>.Succeeded success) return [];
            result.AddRange(success.Value.Items.Where(task => task.Card?.ProjectId == projectId).Select(task => new TaskItemViewModel(task)));
            cursor = success.Value.NextCursor;
        } while (!string.IsNullOrWhiteSpace(cursor) && seen.Add(cursor) && seen.Count <= 200);
        return result;
    }

    private async System.Threading.Tasks.Task SaveAsync(object? _, CancellationToken cancellationToken)
    {
        var editor = Editor; if (editor is null || !editor.TryBuild(out var draft)) return;
        DesktopProjectResult<DesktopProjectDto> result = editor.Source is null
            ? await _client.CreateProjectAsync(draft, Guid.NewGuid().ToString("D"), cancellationToken).ConfigureAwait(true)
            : await _client.UpdateProjectAsync(editor.Source.Id, editor.Source.Version, draft, cancellationToken).ConfigureAwait(true);
        if (result is DesktopProjectResult<DesktopProjectDto>.Succeeded success)
        {
            Upsert(success.Value); Editor = null; Message = "Проект сохранён сервером.";
        }
        else if (result is DesktopProjectResult<DesktopProjectDto>.ValidationFailure validation) editor.SetError(validation.Message);
        else if (result is DesktopProjectResult<DesktopProjectDto>.VersionConflict)
        {
            var latest = await _client.GetProjectAsync(editor.Source!.Id, cancellationToken).ConfigureAwait(true);
            if (latest is DesktopProjectResult<DesktopProjectDto>.Succeeded current)
            {
                editor.RebaseSource(current.Value);
                Upsert(current.Value);
                editor.SetError("Проект изменён другим пользователем. Серверная версия обновлена, ваш черновик сохранён. Проверьте поля и нажмите «Сохранить» повторно.");
            }
            else
            {
                editor.SetError("Проект изменён другим пользователем. Ваш черновик сохранён; восстановите связь или доступ и повторите обновление.");
                ApplyFailure(latest);
            }
        }
        else ApplyFailure(result);
    }

    private async System.Threading.Tasks.Task ArchiveAsync(object? _, CancellationToken cancellationToken)
    {
        var item = SelectedItem; if (item is null) return;
        var result = await _client.ArchiveProjectAsync(item.Id, item.Source.Version, Guid.NewGuid().ToString("D"), cancellationToken).ConfigureAwait(true);
        if (result is DesktopProjectResult<DesktopProjectDto>.Succeeded) { Items = Items.Where(candidate => candidate.Id != item.Id).ToArray(); SelectedItem = Items.FirstOrDefault(); State = Items.Count == 0 ? ProjectsScreenState.Empty : ProjectsScreenState.Loaded; Message = "Завершённый проект перемещён в архив."; }
        else ApplyFailure(result);
    }

    private async System.Threading.Tasks.Task AddMemberAsync(object? _, CancellationToken cancellationToken)
    {
        var item = SelectedItem; var role = NewMemberRole;
        if (item is null || role is null || !Guid.TryParse(NewMemberUserId.Trim(), out var user) || user == Guid.Empty) return;
        var result = await _client.AddMemberAsync(item.Id, item.Source.Version, user, role.Id, Guid.NewGuid().ToString("D"), cancellationToken).ConfigureAwait(true);
        if (result is DesktopProjectResult<DesktopProjectMemberDto>.Succeeded success)
        { AdvanceVersion(item, success.EntityVersion); NewMemberUserId = string.Empty; await ReloadMembersAsync(item, cancellationToken); Message = "Участник добавлен."; }
        else ApplyFailure(result);
    }

    private async System.Threading.Tasks.Task ChangeMemberRoleAsync(object? parameter, CancellationToken cancellationToken)
    {
        var item = SelectedItem; if (item is null || parameter is not ProjectMemberItemViewModel { SelectedRole: not null } member) return;
        var result = await _client.ChangeMemberRoleAsync(item.Id, item.Source.Version, member.UserAccountId, member.SelectedRole.Id, Guid.NewGuid().ToString("D"), cancellationToken).ConfigureAwait(true);
        if (result is DesktopProjectResult<DesktopProjectMemberDto>.Succeeded success)
        { AdvanceVersion(item, success.EntityVersion); await ReloadMembersAsync(item, cancellationToken); Message = "Роль участника изменена."; }
        else ApplyFailure(result);
    }

    private async System.Threading.Tasks.Task RemoveMemberAsync(object? parameter, CancellationToken cancellationToken)
    {
        var item = SelectedItem; if (item is null || parameter is not ProjectMemberItemViewModel member) return;
        var result = await _client.RemoveMemberAsync(item.Id, item.Source.Version, member.UserAccountId, cancellationToken).ConfigureAwait(true);
        if (result is DesktopProjectResult<DesktopProjectDto>.Succeeded success)
        { AdvanceVersion(item, success.EntityVersion); await ReloadMembersAsync(item, cancellationToken); Message = "Участник удалён из проекта."; }
        else ApplyFailure(result);
    }

    private async System.Threading.Tasks.Task ReloadMembersAsync(ProjectItemViewModel item, CancellationToken cancellationToken)
    {
        var result = await _client.GetMembersAsync(item.Id, cancellationToken).ConfigureAwait(true);
        if (ReferenceEquals(SelectedItem, item) && result is DesktopProjectResult<IReadOnlyList<DesktopProjectMemberDto>>.Succeeded success)
            Members = success.Value.Select(member => new ProjectMemberItemViewModel(member, Roles)).ToArray();
    }

    private void AdvanceVersion(ProjectItemViewModel item, long? version)
    { if (version is > 0) item.Replace(item.Source with { Version = version.Value }); }
    private void Upsert(DesktopProjectDto project)
    {
        var existing = Items.FirstOrDefault(item => item.Id == project.Id);
        if (existing is null) { existing = new(project); Items = new[] { existing }.Concat(Items).ToArray(); }
        else existing.Replace(project);
        SelectedItem = existing; State = ProjectsScreenState.Loaded;
    }
    private void ApplyFailure<T>(DesktopProjectResult<T> result) where T : class
    {
        switch (result)
        {
            case DesktopProjectResult<T>.AuthenticationFailure: State = ProjectsScreenState.SessionEnded; Message = "Сессия завершена. Выполните вход снова."; break;
            case DesktopProjectResult<T>.Forbidden: State = ProjectsScreenState.Forbidden; Message = "Права на проекты изменились. Действие недоступно."; break;
            case DesktopProjectResult<T>.VersionConflict: Message = "Данные проекта изменились. Обновите раздел и повторите действие."; break;
            case DesktopProjectResult<T>.ValidationFailure validation: Message = validation.Message; break;
            case DesktopProjectResult<T>.InvalidState: Message = "Текущее состояние проекта не допускает это действие."; break;
            case DesktopProjectResult<T>.NotFound: Message = "Проект или участник больше не доступен."; break;
            default: State = ProjectsScreenState.Failed; Message = "Не удалось получить подтверждённые данные проектов."; break;
        }
    }
    private void NotifyCommands()
    {
        OnPropertyChanged(nameof(IsActive)); OnPropertyChanged(nameof(CanCreate)); OnPropertyChanged(nameof(CanUpdate));
        OnPropertyChanged(nameof(CanArchive)); OnPropertyChanged(nameof(CanManageMembers)); OnPropertyChanged(nameof(AccessText));
        foreach (var command in new[] { RefreshCommand, NewProjectCommand, EditProjectCommand, SaveProjectCommand, CancelEditorCommand, ArchiveProjectCommand, AddMemberCommand, ChangeMemberRoleCommand, RemoveMemberCommand }) command.RaiseCanExecuteChanged();
    }

    private bool CanWrite(string capability) => IsActive && _sessionAvailable && _networkAvailable
        && CanRead && _capabilities.Contains(capability);
    public void Dispose()
    {
        if (_disposed) return; _disposed = true; Deactivate();
        foreach (var command in new[] { RefreshCommand, NewProjectCommand, EditProjectCommand, SaveProjectCommand, CancelEditorCommand, ArchiveProjectCommand, AddMemberCommand, ChangeMemberRoleCommand, RemoveMemberCommand }) command.Dispose();
    }
}
