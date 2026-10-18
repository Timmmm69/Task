using System.ComponentModel;
using Task.Desktop.Projects;
using Task.Desktop.Work;
using AsyncTask = System.Threading.Tasks.Task;

namespace Task.Desktop.ViewModels;

public sealed record ProjectFileItem(DesktopObjectLink Link, DesktopCatalogItem? Catalog)
{
    public string Title => Catalog?.Name ?? "Объект каталога недоступен";
    public string TypeLabel => Catalog?.ItemTypeLabel ?? "Объект каталога";
    public bool CanNavigate => Catalog is { LifecycleState: "active" };
    public bool CanOpen => CanNavigate && Catalog?.ItemType is "file_reference" or "folder_reference";
}

/// <summary>Confirmed project_file snapshot. Paths are resolved only at the user's open action.</summary>
public sealed class ProjectFilesViewModel : ViewModelBase, IDisposable
{
    private readonly IDesktopObjectLinksClient _links;
    private readonly IDesktopFileLocationsClient _catalog;
    private readonly Func<Guid, CancellationToken, System.Threading.Tasks.Task<DesktopWorkResult<DesktopFileLocation?>>> _resolve;
    private readonly IFileAccessAdapter _files;
    private HashSet<string> _capabilities = new(StringComparer.OrdinalIgnoreCase);
    private DesktopProjectDto? _project;
    private long _generation, _version;
    private bool _ready, _online, _writable, _snapshot, _disposed, _choosing;
    private IReadOnlyList<ProjectFileItem> _items = [];
    private ProjectFileItem? _selected;
    private string _message = "";

    public ProjectFilesViewModel(IDesktopObjectLinksClient links, IDesktopFileLocationsClient catalog,
        ObjectLinkTargetPickerViewModel picker,
        Func<Guid, CancellationToken, System.Threading.Tasks.Task<DesktopWorkResult<DesktopFileLocation?>>> resolve,
        IFileAccessAdapter? files = null)
    {
        _links = links; _catalog = catalog; Picker = picker; _resolve = resolve;
        _files = files ?? new WindowsFileAccessAdapter();
        RefreshCommand = new((_, ct) => RunAsync(() => _links.GetLinksAsync(_project!.Id, ct), false, ct), _ => CanUse && !IsBusy);
        ChooseCommand = new((_, _) => { Choosing = true; return AsyncTask.CompletedTask; }, _ => CanMutate("ObjectLink.Create"));
        AddCommand = new(AddAsync, _ => CanMutate("ObjectLink.Create") && Picker.State == ObjectLinkTargetPickerState.Selected
            && Picker.CanQuery && Picker.SelectedTarget is { ObjectType: "catalog_item" } target && target.ObjectId != _project?.Id
            && !Items.Any(row => row.Link.TargetObjectId == target.ObjectId));
        RemoveCommand = new((_, ct) =>
        {
            var source = _project!.Id; var version = _version; var link = Selected!.Link.Id;
            return RunAsync(() => _links.RemoveLinkAsync(source, version, link, ct), true, ct);
        },
            _ => CanMutate("ObjectLink.Delete") && ConfirmedSelection);
        NavigateCommand = new((_, ct) => UseTargetAsync(false, ct), _ => CanUse && !IsBusy && _snapshot && ConfirmedSelection && Selected!.CanNavigate);
        OpenCommand = new((_, ct) => UseTargetAsync(true, ct), _ => CanUse && !IsBusy && _snapshot && ConfirmedSelection
            && Selected!.CanOpen && Has("FileReference.Open"));
        foreach (var command in Commands) command.CanExecuteChanged += ExecutionChanged;
        Picker.PropertyChanged += PickerChanged;
    }

    public ProjectDetailSectionViewModel Section { get; } = new("files");
    public ObjectLinkTargetPickerViewModel Picker { get; }
    public event Action<Guid, long>? VersionConfirmed;
    public event Action<string, Guid>? OpenObjectRequested;
    public event Action? SessionExpired;
    public IReadOnlyList<ProjectFileItem> Items { get => _items; private set => SetProperty(ref _items, value); }
    public ProjectFileItem? Selected
    {
        get => _selected;
        set { if (ReferenceEquals(_selected, value)) return; _selected = value; OnPropertyChanged(); NotifyCommands(); }
    }
    public string Message { get => _message; private set => SetProperty(ref _message, value); }
    public bool Choosing { get => _choosing; private set => SetProperty(ref _choosing, value); }
    public bool IsBusy => Commands.Any(command => command.IsExecuting);
    public bool CanRead => _ready && Has("Project.Read") && Has("FileCatalog.Read") && Has("ObjectLink.Read");
    private bool CanUse => !_disposed && CanRead && _online && _project is not null;
    private bool ConfirmedSelection => Selected is { } row && Items.Any(item => ReferenceEquals(item, row))
        && row.Link.SourceObjectId == _project?.Id && row.Link.LinkType == "project_file";
    private bool CanMutate(string permission) => CanUse && _writable && _project?.LifecycleState == "active"
        && Has("Project.Update") && Has(permission) && _snapshot && _version > 0 && !IsBusy;
    private bool Has(string permission) => _capabilities.Contains(permission);
    public AsyncCommand RefreshCommand { get; }
    public AsyncCommand ChooseCommand { get; }
    public AsyncCommand AddCommand { get; }
    public AsyncCommand RemoveCommand { get; }
    public AsyncCommand NavigateCommand { get; }
    public AsyncCommand OpenCommand { get; }
    private IEnumerable<AsyncCommand> Commands => [RefreshCommand, ChooseCommand, AddCommand, RemoveCommand, NavigateCommand, OpenCommand];

    public void SetContext(DesktopProjectDto? project, IEnumerable<string> capabilities, bool ready, bool online, bool writable)
    {
        if (_disposed) return;
        var caps = capabilities.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (_project?.Id == project?.Id && _project?.OrganizationId == project?.OrganizationId
            && _project?.LifecycleState == project?.LifecycleState && _ready == ready && _online == online
            && _writable == writable && _capabilities.SetEquals(caps))
        { _project = project; return; }
        Invalidate(); _project = project; _capabilities = caps; _ready = ready; _online = online; _writable = writable;
        _version = project?.Version ?? 0;
        Section.SetContext(project?.Id, project?.OrganizationId, CanRead, online);
        Picker.SetContext(project?.Id ?? Guid.Empty, "project", "project_file");
        Picker.UpdateAccess(writable && project?.LifecycleState == "active" ? caps : caps.Where(code => code != "ObjectLink.Create"), ready, online);
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
        var generation = _generation; var source = _project!.Id; var version = _version;
        var target = Picker.SelectedObjectId!.Value;
        try
        {
            // Revalidate metadata, type and lifecycle; the POST enforces target update and project/object scope.
            var result = await _catalog.GetCatalogItemAsync(target, ct);
            if (!Current(generation, ct)) return;
            if (result is not DesktopWorkResult<DesktopCatalogItem>.Succeeded { Value.LifecycleState: "active" } item
                || item.Value.Id != target || item.Value.ItemType is not ("file_reference" or "folder_reference" or "virtual_folder"))
            { TargetFailure(result); return; }
            await RunAsync(() => _links.AddLinkAsync(source, version, target, "project_file", ct), true, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception) { if (Current(generation, ct)) Fail(new DesktopWorkResult<DesktopObjectLinks>.ServerUnavailable()); }
    }

    private async AsyncTask RunAsync(Func<System.Threading.Tasks.Task<DesktopWorkResult<DesktopObjectLinks>>> operation, bool mutation, CancellationToken ct)
    {
        var generation = _generation; var source = _project!.Id;
        using var request = Section.BeginRequest(ct);
        _snapshot = false; Selected = null;
        try
        {
            var result = await operation();
            if (!Current(generation, ct)) return;
            if (result is not DesktopWorkResult<DesktopObjectLinks>.Succeeded success) { Fail(result); return; }
            // The API returns incoming relations too; only owned outgoing project_file links belong here.
            var rows = new List<ProjectFileItem>();
            foreach (var link in success.Value.Items.Where(link => link.SourceObjectId == source && link.LinkType == "project_file"))
            {
                var metadata = await _catalog.GetCatalogItemAsync(link.TargetObjectId, request.Token);
                if (!Current(generation, request.Token)) return;
                if (metadata is DesktopWorkResult<DesktopCatalogItem>.AuthenticationFailure) { Expire(); return; }
                var item = metadata is DesktopWorkResult<DesktopCatalogItem>.Succeeded read && read.Value.Id == link.TargetObjectId
                    && read.Value.LifecycleState == "active" ? read.Value : null;
                rows.Add(new(link, item));
            }
            Items = rows; _version = success.Value.SourceVersion; _snapshot = true;
            Selected = Items.FirstOrDefault();
            Section.SetState(rows.Any(row => row.Catalog is null) ? ProjectSectionState.PartialAccess
                : rows.Count == 0 ? ProjectSectionState.Empty : ProjectSectionState.Loaded, true);
            Message = mutation ? "Связи подтверждены сервером. Файлы и объекты каталога не изменены." : "";
            if (mutation) { Choosing = false; Picker.ClearSelection(); }
            VersionConfirmed?.Invoke(source, _version);
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested) { }
        catch (Exception) { if (Current(generation, ct)) Fail(new DesktopWorkResult<DesktopObjectLinks>.ServerUnavailable()); }
        finally { Section.FinishRequest(request); NotifyCommands(); }
    }

    private async AsyncTask UseTargetAsync(bool open, CancellationToken ct)
    {
        var generation = _generation; var row = Selected!; var id = row.Link.TargetObjectId;
        try
        {
            var result = await _catalog.GetCatalogItemAsync(id, ct);
            if (!Current(generation, ct)) return;
            if (result is not DesktopWorkResult<DesktopCatalogItem>.Succeeded { Value.LifecycleState: "active" } item || item.Value.Id != id)
            { TargetFailure(result); return; }
            if (!open) { OpenObjectRequested?.Invoke("catalog_item", id); return; }
            if (item.Value.ItemType is not ("file_reference" or "folder_reference")) { Message = "У объекта нет файлового расположения."; return; }
            var resolved = await _resolve(id, ct);
            if (!Current(generation, ct) || !Has("FileReference.Open")) return;
            if (resolved is DesktopWorkResult<DesktopFileLocation?>.AuthenticationFailure) { Expire(); return; }
            if (resolved is DesktopWorkResult<DesktopFileLocation?>.Succeeded location)
                Message = location.Value is { CanOpenOnDevice: true, IsEnabled: true, RawPath: { Length: > 0 } path }
                    ? _files.Open(path).Message : "На этом компьютере нет доступного расположения файла.";
            else Message = resolved is DesktopWorkResult<DesktopFileLocation?>.Forbidden ? "Нет доступа к открытию файла."
                : "Не удалось подтвердить расположение файла. Проверьте подключение и повторите попытку.";
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception) { if (Current(generation, ct)) Message = "Не удалось открыть объект. Проверьте подключение и повторите попытку."; }
    }

    private void TargetFailure(DesktopWorkResult<DesktopCatalogItem> result)
    {
        if (result is DesktopWorkResult<DesktopCatalogItem>.AuthenticationFailure) { Expire(); return; }
        Picker.ClearSelection();
        // Remove previously resolved names after denied/not-found revalidation.
        Items = Items.Select(row => row with { Catalog = null }).ToArray(); Selected = null;
        Section.SetState(ProjectSectionState.PartialAccess);
        Message = "Объект каталога недоступен. Обновите файлы и повторите выбор.";
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
            DesktopWorkResult<DesktopObjectLinks>.Conflict => "Версия проекта изменилась. Обновите файлы перед повторной попыткой.",
            DesktopWorkResult<DesktopObjectLinks>.ValidationFailure v when v.Message == ObjectLinkMessages.Duplicate => v.Message,
            DesktopWorkResult<DesktopObjectLinks>.ValidationFailure => "Состояние объекта не допускает связь. Обновите проект и файлы.",
            DesktopWorkResult<DesktopObjectLinks>.Forbidden => "Нет доступа к связям или выбранным объектам.",
            DesktopWorkResult<DesktopObjectLinks>.NotFound => "Проект, объект или связь больше недоступны.",
            _ => "Связи не подтверждены. Обновите файлы перед повторной попыткой.",
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
        _disposed = true; Invalidate(); Picker.PropertyChanged -= PickerChanged; Picker.Dispose();
        foreach (var command in Commands) { command.CanExecuteChanged -= ExecutionChanged; command.Dispose(); }
    }
}
