using Task.Desktop.Modes;
using Task.Desktop.ViewModels;
using Task.Desktop.Work;

namespace Task.Desktop.Personal;

public sealed record PersonalWorkspaceArea(string Title, WorkHubArea Area);
public sealed class PersonalWorkspaceViewModel : ViewModelBase, IDisposable
{
    private readonly PersonalTaskStore _store;
    private PersonalWorkspaceArea _area;
    private IReadOnlyList<PersonalObject> _objects = [];
    private PersonalObject? _selected, _target, _folder;
    private string _message = "";
    public PersonalWorkspaceViewModel(PersonalTaskStore store, IFileAccessAdapter? files = null)
    {
        _store = store; _area = Areas[0]; var client = new PersonalWorkClient(store);
        Hub = new(client, ["FileCatalog.Read", "FileCatalog.Create", "FileCatalog.Update", "FileLocation.Update", "FileReference.Open", "Contact.Read", "Contact.Create", "Search.Use", "History.Read", "Archive.Restore", "Trash.Read", "Trash.Restore", "Settings.ReadOwn", "Settings.UpdateOwn"], files, personal: true);
        Links = new(client, () => Selected?.Lifecycle == "active") { ShowTargetId = false };
        Links.Changed += version => { if (_selected is not null) { _selected = _selected with { Version = version }; OnPropertyChanged(nameof(Selected)); } };
        RefreshCommand = new((_, _) => Run(Refresh), _ => !IsBusy);
        ArchiveCommand = new((_, _) => Change("archive"), _ => Selected?.Lifecycle == "active" && !IsBusy);
        TrashCommand = new((_, _) => Change("trash"), _ => Selected is { Lifecycle: not "trashed" } && !IsBusy);
        RestoreCommand = new((_, _) => Change(Selected?.Lifecycle == "trashed" ? "restore" : "unarchive"), _ => Selected is { Lifecycle: not "active" } && !IsBusy);
        PurgeCommand = new((_, _) => Run(() => { _store.PurgeWorkspaceObject(Selected!.Id, Selected.Version); Refresh(); }), _ => Selected?.Lifecycle == "trashed" && !IsBusy);
        MoveCommand = new((_, _) => Run(() => { _store.MoveCatalog(Selected!.Id, Selected.Version, Folder?.Id); Refresh(); }), _ => Selected is { ObjectType: "catalog_item", Lifecycle: "active" } && !IsBusy);
        RecoverProjectCommand = new((state, _) => Run(() => { _store.ChangeWorkspaceLifecycle(Selected!.Id, Selected.Version, "restore", state as string); Refresh(); Hub.Activate(Area.Area); }), state => NeedsProjectRecovery && state is "active" or "archived" && !IsBusy);
        Hub.OpenObjectRequested += OpenObject;
        Hub.Activate(WorkHubArea.Settings);
        Refresh();
    }
    public event Action<string, Guid>? OpenObjectRequested;
    private void OpenObject(string type, Guid id)
    {
        Refresh(); Selected = Objects.FirstOrDefault(o => o.Id == id);
        if (type == "catalog_item") { Area = Areas[0]; _ = Hub.SelectCatalogItemAsync(id); }
        else if (type == "contact") { Area = Areas[1]; Hub.SelectedContact = Hub.Contacts.FirstOrDefault(c => c.Id == id); }
        OpenObjectRequested?.Invoke(type, id);
    }
    public WorkHubViewModel Hub { get; }
    public ObjectLinksViewModel Links { get; }
    public IReadOnlyList<PersonalWorkspaceArea> Areas { get; } = [new("Каталог", WorkHubArea.Catalog), new("Контакты", WorkHubArea.Contacts), new("Поиск", WorkHubArea.Search), new("Архив", WorkHubArea.Archive), new("Корзина", WorkHubArea.Trash), new("Настройки", WorkHubArea.Settings)];
    public PersonalWorkspaceArea Area { get => _area; set { if (SetProperty(ref _area, value)) { OnPropertyChanged(nameof(ShowObjectActions)); Hub.Activate(value.Area); } } }
    public bool ShowObjectActions => Area.Area is not (WorkHubArea.Settings or WorkHubArea.Search);
    public IReadOnlyList<PersonalObject> Objects { get => _objects; private set { SetProperty(ref _objects, value); OnPropertyChanged(nameof(ActiveObjects)); OnPropertyChanged(nameof(Folders)); } }
    public IReadOnlyList<PersonalObject> ActiveObjects => Objects.Where(o => o.Lifecycle == "active").ToArray();
    public IReadOnlyList<PersonalObject> Folders => Objects.Where(o => o.Lifecycle == "active" && _store.Catalog().Any(c => c.Id == o.Id && c.ItemType == "virtual_folder")).ToArray();
    public PersonalObject? Selected { get => _selected; set { SetProperty(ref _selected, value); Links.SetSource(value?.Id ?? Guid.Empty, value?.Version ?? 0); Notify(); } }
    public PersonalObject? Target { get => _target; set { SetProperty(ref _target, value); Links.TargetId = value?.Id.ToString("D") ?? ""; if (value is not null && Selected is not null) Links.LinkType = (Selected.ObjectType, value.ObjectType) switch { ("task", "contact") => "task_contact", ("task", "catalog_item") => "task_file", ("project", "catalog_item") => "project_file", ("contact", "catalog_item") => "contact_file", _ => "task_file" }; } }
    public PersonalObject? Folder { get => _folder; set => SetProperty(ref _folder, value); }
    public string Message { get => _message; private set => SetProperty(ref _message, value); }
    public bool IsBusy => Hub.IsLoading || Hub.HasRunningMutation || Links.IsBusy;
    public bool CanTrash => Selected is { Lifecycle: not "trashed" } && !IsBusy;
    public bool CanPurge => Selected?.Lifecycle == "trashed" && !IsBusy;
    public bool NeedsProjectRecovery => Selected is { ObjectType: "project", Lifecycle: "trashed" } && _store.NeedsLegacyProjectRecovery(Selected.Id);
    public AsyncCommand RecoverProjectCommand { get; }
    public AsyncCommand RefreshCommand { get; }
    public AsyncCommand ArchiveCommand { get; }
    public AsyncCommand TrashCommand { get; }
    public AsyncCommand RestoreCommand { get; }
    public AsyncCommand PurgeCommand { get; }
    public AsyncCommand MoveCommand { get; }
    public void Refresh()
    {
        var id = Selected?.Id; Objects = _store.WorkspaceObjects(); Selected = Objects.FirstOrDefault(o => o.Id == id);
    }
    private System.Threading.Tasks.Task Change(string action) => Run(() => { _store.ChangeWorkspaceLifecycle(Selected!.Id, Selected.Version, action); Refresh(); Hub.Activate(Area.Area); });
    private System.Threading.Tasks.Task Run(Action action)
    {
        try { action(); Message = "Изменения сохранены. Файлы на диске не изменены."; }
        catch (PersonalVersionConflictException) { Message = "Версия изменилась. Обновите список объектов."; }
        catch (PersonalTaskNotFoundException) { Message = "Объект больше недоступен. Обновите список."; }
        catch (PersonalTransitionException) { Message = "Недопустимое изменение состояния."; }
        catch (ArgumentException e) { Message = e.Message; }
        catch (Exception e) when (e is Microsoft.Data.Sqlite.SqliteException or System.IO.IOException or UnauthorizedAccessException)
        { Message = "Данные не сохранены. Введённые значения остались в форме; проверьте свободное место и доступ к личной базе."; }
        return System.Threading.Tasks.Task.CompletedTask;
    }
    internal IReadOnlyList<ModeSwitchEditor> InspectDrafts() =>
    [new("Личная запись каталога", () => Hub.NewItemName.Length > 0 || Hub.NewItemPath.Length > 0, () => IsBusy, null),
     new("Личный контакт", () => Hub.NewContactFirstName.Length > 0 || Hub.NewContactLastName.Length > 0 || Hub.NewContactDisplayName.Length > 0, () => IsBusy, null),
     new("Личные настройки", () => Hub.HasProfileDraft, () => IsBusy, Hub.SaveUserSettingsCommand),
     new("Личные уведомления", () => Hub.HasNotificationDraft, () => IsBusy, Hub.SaveNotificationPreferencesCommand)];
    private void Notify() { OnPropertyChanged(nameof(NeedsProjectRecovery)); OnPropertyChanged(nameof(CanTrash)); OnPropertyChanged(nameof(CanPurge)); foreach (var c in new[] { ArchiveCommand, TrashCommand, RestoreCommand, PurgeCommand, MoveCommand, RecoverProjectCommand }) c.RaiseCanExecuteChanged(); }
    public void Dispose() { Hub.OpenObjectRequested -= OpenObject; Hub.Dispose(); Links.Dispose(); foreach (var c in new[] { RefreshCommand, ArchiveCommand, TrashCommand, RestoreCommand, PurgeCommand, MoveCommand, RecoverProjectCommand }) c.Dispose(); }
}
