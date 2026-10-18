using Task.Desktop.Administration;
using Task.Desktop.Work;

namespace Task.Desktop.ViewModels;

public enum AdministrationArea { Users, Roles, Resources, Access }

public sealed record AdministrationTab(AdministrationArea Area, string Title, string Capability);

public sealed partial class AdministrationViewModel : ViewModelBase, IDisposable
{
    private readonly IDesktopAdministrationApiClient _client;
    private readonly HashSet<string> _capabilities;
    private CancellationTokenSource? _activation;
    private AdministrationTab _selectedTab;
    private IReadOnlyList<DesktopAdminUser> _users = [];
    private IReadOnlyList<DesktopAdminRole> _roles = [];
    private IReadOnlyList<DesktopNetworkResource> _resources = [];
    private DesktopAdminUser? _selectedUser;
    private DesktopAdminRole? _selectedRole;
    private DesktopNetworkResource? _selectedResource;
    private string? _message;
    private bool _active;
    private bool _disposed;
    private bool _sessionAvailable = true;
    private bool _networkAvailable = true;
    private bool _isLoading;
    private DateTimeOffset? _lastRefresh;

    public AdministrationViewModel(IDesktopAdministrationApiClient client, IEnumerable<string>? capabilities)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _capabilities = new(capabilities ?? [], StringComparer.OrdinalIgnoreCase);
        Tabs =
        [
            new(AdministrationArea.Users, "Пользователи", "User.Read"),
            new(AdministrationArea.Roles, "Роли и права", "Role.Read"),
            new(AdministrationArea.Resources, "Сетевые ресурсы", "FileCatalog.Read"),
            new(AdministrationArea.Access, "Управление доступом", "User.ManageRoles"),
        ];
        _selectedTab = Tabs[0];
        RefreshCommand = new(RefreshAsync, _ => _active && _sessionAvailable && _networkAvailable && CanReadCurrentArea && !IsAccessManagement && !_savingUser && !IsLoading && !IsEditorOpen && !IsTransitionOpen && !_transitionBusy);
        InitializeUserCommands(); InitializeTransitionCommands();
        RefreshCommand.ExecutionFailed += _ => Message = "Не удалось обновить раздел. Подтверждённые данные не изменены.";
    }

    public IReadOnlyList<AdministrationTab> Tabs { get; }
    public AdministrationTab SelectedTab
    {
        get => _selectedTab;
        set
        {
            if (IsEditorOpen || IsTransitionOpen || _transitionBusy || _savingUser || !SetProperty(ref _selectedTab, value)) return;
            OnPropertyChanged(nameof(IsUsers)); OnPropertyChanged(nameof(IsRoles)); OnPropertyChanged(nameof(IsResources)); OnPropertyChanged(nameof(IsAccessManagement));
            _lastRefresh = null; OnPropertyChanged(nameof(LastSuccessfulRefreshText));
            InvalidateUserDetails();
            NotifyAccess();
            if (_active) _ = RefreshCommand.ExecuteAsync(cancellationToken: _activation?.Token ?? default);
        }
    }
    public IReadOnlyList<DesktopAdminUser> Users { get => _users; private set => SetProperty(ref _users, value); }
    public IReadOnlyList<DesktopAdminRole> Roles { get => _roles; private set => SetProperty(ref _roles, value); }
    public IReadOnlyList<DesktopNetworkResource> Resources { get => _resources; private set => SetProperty(ref _resources, value); }
    public DesktopAdminUser? SelectedUser { get => _selectedUser; set { if ((value is null || !_transitionBusy && !IsTransitionOpen) && SetProperty(ref _selectedUser, value)) { InvalidateUserDetails(); NotifyUserCommands(); } } }
    public DesktopAdminRole? SelectedRole { get => _selectedRole; set => SetProperty(ref _selectedRole, value); }
    public DesktopNetworkResource? SelectedResource { get => _selectedResource; set => SetProperty(ref _selectedResource, value); }
    public string? Message { get => _message; private set { if (SetProperty(ref _message, value)) OnPropertyChanged(nameof(HasMessage)); } }
    public bool HasMessage => !string.IsNullOrWhiteSpace(Message);
    public bool IsLoading { get => _isLoading; private set => SetProperty(ref _isLoading, value); }
    public bool IsUsers => SelectedTab.Area == AdministrationArea.Users;
    public bool IsRoles => SelectedTab.Area == AdministrationArea.Roles;
    public bool IsResources => SelectedTab.Area == AdministrationArea.Resources;
    public bool IsAccessManagement => SelectedTab.Area == AdministrationArea.Access;
    public string RoleAssignmentLimitation => "Изменение назначений ролей недоступно: действующий API не позволяет получить полный набор текущих назначений сотрудника. Замена без этих данных может удалить существующие права. Ограничение OQ-ADMIN-ROLES-READ. Каталог определений находится в разделе «Роли и права» при наличии права на его чтение.";
    public bool CanReadCurrentArea => _sessionAvailable && _capabilities.Contains(SelectedTab.Capability);
    public bool IsLimitedRole => !CanReadCurrentArea;
    public bool IsOffline => !_networkAvailable;
    public string AccessText => !_networkAvailable
        ? "Офлайн · административные данные доступны только из подтверждённого состояния"
        : CanReadCurrentArea
            ? $"{SelectedTab.Capability} · сервер authoritative"
            : "Ограниченная роль · поля и количество скрытых объектов не раскрываются";
    public string LastSuccessfulRefreshText => _lastRefresh is null ? "Раздел ещё не обновлялся" : $"Обновлено {_lastRefresh.Value.ToLocalTime():HH:mm}";
    public AsyncCommand RefreshCommand { get; }

    public void Activate()
    {
        if (_disposed) return;
        _activation?.Cancel(); _activation?.Dispose(); _activation = new();
        _active = true;
        NotifyAccess();
        _ = RefreshCommand.ExecuteAsync(cancellationToken: _activation.Token);
    }

    public void Deactivate() { _active = false; ++_accessGeneration; InvalidateUserDetails(); NotifyUserCommands(); _activation?.Cancel(); RefreshCommand.RaiseCanExecuteChanged(); }
    public void UpdateCapabilities(IEnumerable<string>? capabilities)
    {
        var updated = new HashSet<string>(capabilities ?? [], StringComparer.OrdinalIgnoreCase);
        if (_capabilities.SetEquals(updated)) return;
        ++_accessGeneration; _capabilities.Clear(); _capabilities.UnionWith(updated); InvalidateUserDetails();
        if (!_capabilities.Contains("User.Read")) { Users = []; SelectedUser = null; UserEditor = null; _nextUserCursor = null; }
        if (!_capabilities.Contains("Role.Read")) { Roles = []; SelectedRole = null; }
        if (!_capabilities.Contains("FileCatalog.Read")) { Resources = []; SelectedResource = null; }
        NotifyAccess();
    }
    public void UpdateSessionState(bool available)
    {
        _sessionAvailable = available;
        if (!available)
        {
            ++_accessGeneration; InvalidateUserDetails(); Users = []; SelectedUser = null; UserEditor = null; _nextUserCursor = null;
            Roles = []; SelectedRole = null; Resources = []; SelectedResource = null;
            Message = "Сессия завершена. Выполните вход снова.";
        }
        NotifyAccess();
    }
    public void UpdateConnectivity(bool available)
    {
        _networkAvailable = available;
        Message = IsTransitionOpen && _transitionUncertain
            ? available ? "Подключение восстановлено. Результат ещё не подтверждён. Проверьте исходный запрос или отмените действие и обновите запись."
                : "Сервер недоступен. Результат ещё не подтверждён; офлайн-очередь не создаётся. После восстановления связи можно проверить исходный запрос."
            : available ? "Подключение восстановлено. Обновите административные данные." : "Сервер недоступен. Действия администратора заблокированы.";
        NotifyAccess();
    }

    private async global::System.Threading.Tasks.Task RefreshAsync(object? _, CancellationToken cancellationToken)
    {
        Message = null; IsLoading = true;
        try
        {
            // A tab can change while a read is in flight. Discard its receipt, then
            // load the latest selected tab through the same serialized command.
            AdministrationTab area;
            do
            {
                area = SelectedTab;
                var access = _accessGeneration;
                if (!_active || !CanReadCurrentArea || !_networkAvailable || IsAccessManagement) break;
                bool Current() => !cancellationToken.IsCancellationRequested && _active && _sessionAvailable
                    && access == _accessGeneration && SelectedTab == area && CanReadCurrentArea;
                switch (area.Area)
                {
                    case AdministrationArea.Users: await LoadUserPageAsync(false, cancellationToken); break;
                    case AdministrationArea.Roles:
                        var roles = await _client.GetRolesAsync(cancellationToken);
                        if (Current()) Apply(roles, value => { Roles = value; SelectedRole = value.FirstOrDefault(); }, () => { Roles = []; SelectedRole = null; });
                        break;
                    case AdministrationArea.Resources:
                        var resources = await _client.GetResourcesAsync(cancellationToken);
                        if (Current()) Apply(resources, value => { Resources = value; SelectedResource = value.FirstOrDefault(); }, () => { Resources = []; SelectedResource = null; });
                        break;
                }
            }
            while (!cancellationToken.IsCancellationRequested && area != SelectedTab);
        }
        finally { IsLoading = false; NotifyUserCommands(); }
    }

    private void Apply<T>(DesktopWorkResult<T> result, Action<T> success, Action clear)
    {
        switch (result)
        {
            case DesktopWorkResult<T>.Succeeded ok: success(ok.Value); _lastRefresh = DateTimeOffset.UtcNow; OnPropertyChanged(nameof(LastSuccessfulRefreshText)); break;
            case DesktopWorkResult<T>.Forbidden: clear(); Message = "Область доступа изменилась. Защищённые административные данные не показаны."; break;
            case DesktopWorkResult<T>.AuthenticationFailure: UpdateSessionState(false); break;
            case DesktopWorkResult<T>.ServerUnavailable: UpdateConnectivity(false); break;
            default: Message = "Сервер вернул неподтверждённые данные. Они не отображены."; break;
        }
    }

    private void NotifyAccess()
    {
        OnPropertyChanged(nameof(CanReadCurrentArea)); OnPropertyChanged(nameof(IsLimitedRole));
        OnPropertyChanged(nameof(IsOffline)); OnPropertyChanged(nameof(AccessText));
        RefreshCommand.RaiseCanExecuteChanged();
        NotifyUserCommands();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; _active = false; ++_accessGeneration; InvalidateUserDetails();
        BeginUserTransitionCommand.Dispose(); ConfirmUserTransitionCommand.Dispose(); CancelUserTransitionCommand.Dispose();
        CreateUserCommand.Dispose(); EditUserCommand.Dispose(); SaveUserCommand.Dispose(); CancelUserCommand.Dispose();
        MoreUsersCommand.Dispose(); SearchUsersCommand.Dispose(); _activation?.Cancel(); _activation?.Dispose(); RefreshCommand.Dispose();
    }
}
