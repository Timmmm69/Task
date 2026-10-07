using Microsoft.Data.Sqlite;
using Task.Desktop.Work;

namespace Task.Desktop.Personal;

/// <summary>Local use-case adapter. No transport, authentication or server resolution.</summary>
public sealed class PersonalWorkClient(PersonalTaskStore store) : IDesktopWorkApiClient, IDesktopObjectLinksClient
{
    private static System.Threading.Tasks.Task<DesktopWorkResult<T>> Run<T>(Func<T> action, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        DesktopWorkResult<T> result;
        try { result = new DesktopWorkResult<T>.Succeeded(action()); }
        catch (PersonalVersionConflictException) { result = new DesktopWorkResult<T>.Conflict(); }
        catch (PersonalTaskNotFoundException) { result = new DesktopWorkResult<T>.NotFound(); }
        catch (PersonalTransitionException) { result = new DesktopWorkResult<T>.ValidationFailure("Сначала восстановите объект или выберите допустимое действие."); }
        catch (ArgumentException e) { result = new DesktopWorkResult<T>.ValidationFailure(e.Message); }
        catch (SqliteException e) { result = new DesktopWorkResult<T>.ValidationFailure(e.SqliteErrorCode == 19 ? "Запись уже существует или используется другим объектом." : "Данные не сохранены. Введённые значения остались в форме; проверьте свободное место и повторите действие."); }
        catch (Exception e) when (e is System.IO.IOException or UnauthorizedAccessException)
        { result = new DesktopWorkResult<T>.ValidationFailure("Данные не сохранены. Введённые значения остались в форме; проверьте свободное место и доступ к Personal DB."); }
        return System.Threading.Tasks.Task.FromResult(result);
    }
    private static System.Threading.Tasks.Task<DesktopWorkResult<T>> Unavailable<T>() => System.Threading.Tasks.Task.FromResult<DesktopWorkResult<T>>(new DesktopWorkResult<T>.Forbidden());
    public System.Threading.Tasks.Task<DesktopWorkResult<IReadOnlyList<DesktopCatalogItem>>> GetCatalogAsync(CancellationToken cancellationToken = default) => Run(store.Catalog, cancellationToken);
    public System.Threading.Tasks.Task<DesktopWorkResult<IReadOnlyList<DesktopContact>>> GetContactsAsync(CancellationToken cancellationToken = default) => Run(store.Contacts, cancellationToken);
    public System.Threading.Tasks.Task<DesktopWorkResult<IReadOnlyList<DesktopSearchResult>>> SearchAsync(string query, CancellationToken cancellationToken = default) => Run(() => store.SearchPersonal(query), cancellationToken);
    public System.Threading.Tasks.Task<DesktopWorkResult<DesktopCatalogItem>> CreateCatalogItemAsync(string name, string itemType, string? description, CancellationToken cancellationToken = default) => Run(() => store.CreateCatalog(name, itemType, description), cancellationToken);
    public System.Threading.Tasks.Task<DesktopWorkResult<DesktopCatalogItem>> CreateCatalogItemAsync(string name, string itemType, string? description, Guid? parentId, CancellationToken cancellationToken = default) => Run(() => store.CreateCatalog(name, itemType, description, parentId), cancellationToken);
    public System.Threading.Tasks.Task<DesktopWorkResult<DesktopCatalogItem>> MoveCatalogItemAsync(Guid id, long version, Guid? parentId, CancellationToken cancellationToken = default) => Run(() => { store.MoveCatalog(id, version, parentId); return store.Catalog().Single(i => i.Id == id); }, cancellationToken);
    public System.Threading.Tasks.Task<DesktopWorkResult<DesktopContact>> CreateContactAsync(string firstName, string? lastName, string displayName, CancellationToken cancellationToken = default) => Run(() => store.CreateContact(firstName, lastName, displayName), cancellationToken);
    public System.Threading.Tasks.Task<DesktopWorkResult<bool>> AddLocationAsync(Guid itemId, long version, string rawPath, CancellationToken cancellationToken = default) => Run(() => { store.AddPersonalLocation(itemId, version, rawPath); return true; }, cancellationToken);
    public System.Threading.Tasks.Task<DesktopWorkResult<DesktopFileLocation?>> ResolveLocationAsync(Guid itemId, CancellationToken cancellationToken = default) => Run(() =>
    {
        var location = store.PersonalLocation(itemId);
        return location is not null && location.LocationType == "local_path" && !store.WorkspaceSettings().AllowLocalPaths ? location with { CanOpenOnDevice = false } : location;
    }, cancellationToken);
    public System.Threading.Tasks.Task<DesktopWorkResult<IReadOnlyList<DesktopLifecycleItem>>> GetArchiveAsync(CancellationToken cancellationToken = default) => Run(() => store.WorkspaceLifecycle("archived"), cancellationToken);
    public System.Threading.Tasks.Task<DesktopWorkResult<IReadOnlyList<DesktopLifecycleItem>>> GetTrashAsync(CancellationToken cancellationToken = default) => Run(() => store.WorkspaceLifecycle("trashed"), cancellationToken);
    public System.Threading.Tasks.Task<DesktopWorkResult<bool>> RestoreArchiveAsync(Guid objectId, long version, CancellationToken cancellationToken = default) => Run(() => { store.ChangeWorkspaceLifecycle(objectId, version, "unarchive"); return true; }, cancellationToken);
    public System.Threading.Tasks.Task<DesktopWorkResult<bool>> RestoreTrashAsync(Guid objectId, long version, CancellationToken cancellationToken = default) => Run(() => { store.ChangeWorkspaceLifecycle(objectId, version, "restore"); return true; }, cancellationToken);
    public System.Threading.Tasks.Task<DesktopWorkResult<DesktopUserSettings>> GetUserSettingsAsync(CancellationToken cancellationToken = default) => Run(store.WorkspaceSettings, cancellationToken);
    public System.Threading.Tasks.Task<DesktopWorkResult<DesktopUserSettings>> UpdateUserSettingsAsync(DesktopUserSettings settings, CancellationToken cancellationToken = default) => Run(() => store.SaveWorkspaceSettings(settings), cancellationToken);
    public System.Threading.Tasks.Task<DesktopWorkResult<DesktopNotificationPreferences>> GetNotificationPreferencesAsync(CancellationToken cancellationToken = default) => Run(store.WorkspaceNotificationPreferences, cancellationToken);
    public System.Threading.Tasks.Task<DesktopWorkResult<DesktopNotificationPreferences>> UpdateNotificationPreferencesAsync(DesktopNotificationPreferences preferences, CancellationToken cancellationToken = default) => Run(() => store.SaveWorkspaceNotifications(preferences), cancellationToken);
    public System.Threading.Tasks.Task<DesktopWorkResult<DesktopOrganizationSettings>> GetOrganizationSettingsAsync(CancellationToken cancellationToken = default) => Unavailable<DesktopOrganizationSettings>();
    public System.Threading.Tasks.Task<DesktopWorkResult<DesktopOrganizationSettings>> UpdateOrganizationSettingsAsync(DesktopOrganizationSettings settings, CancellationToken cancellationToken = default) => Unavailable<DesktopOrganizationSettings>();
    public System.Threading.Tasks.Task<DesktopWorkResult<IReadOnlyList<DesktopNotification>>> GetNotificationsAsync(CancellationToken cancellationToken = default) => Unavailable<IReadOnlyList<DesktopNotification>>();
    public System.Threading.Tasks.Task<DesktopWorkResult<bool>> MarkNotificationReadAsync(Guid notificationId, CancellationToken cancellationToken = default) => Unavailable<bool>();
    public System.Threading.Tasks.Task<DesktopWorkResult<bool>> MarkAllNotificationsReadAsync(IReadOnlyCollection<Guid> notificationIds, CancellationToken cancellationToken = default) => Unavailable<bool>();
    public System.Threading.Tasks.Task<DesktopWorkResult<DesktopObjectLinks>> GetLinksAsync(Guid source, CancellationToken ct = default) => Run(() => store.PersonalLinks(source), ct);
    public System.Threading.Tasks.Task<DesktopWorkResult<DesktopObjectLinks>> AddLinkAsync(Guid source, long version, Guid target, string type, CancellationToken ct = default) => Run(() => { store.AddPersonalLink(source, version, target, type); return store.PersonalLinks(source); }, ct);
    public System.Threading.Tasks.Task<DesktopWorkResult<DesktopObjectLinks>> RemoveLinkAsync(Guid source, long version, Guid linkId, CancellationToken ct = default) => Run(() => { store.RemovePersonalLink(source, version, linkId); return store.PersonalLinks(source); }, ct);
}
