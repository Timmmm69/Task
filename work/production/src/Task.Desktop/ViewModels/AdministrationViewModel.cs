using Task.Desktop.Administration;
using Task.Desktop.Work;

namespace Task.Desktop.ViewModels;

public enum AdministrationArea { Users, Roles, Resources }

public sealed record AdministrationTab(AdministrationArea Area, string Title, string Capability);

public sealed class AdministrationViewModel : ViewModelBase, IDisposable
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
            new(AdministrationArea.Users, "Управление доступом", "User.ManageRoles"),
        ];
        _selectedTab = Tabs[0];
        RefreshCommand = new(RefreshAsync, _ => _active && _sessionAvailable && _networkAvailable && CanReadCurrentArea);
        RefreshCommand.ExecutionFailed += _ => Message = "Не удалось обновить раздел. Подтверждённые данные не изменены.";
    }

    public IReadOnlyList<AdministrationTab> Tabs { get; }
    public AdministrationTab SelectedTab
    {
        get => _selectedTab;
        set
        {
            if (!SetProperty(ref _selectedTab, value)) return;
            OnPropertyChanged(nameof(IsUsers)); OnPropertyChanged(nameof(IsRoles)); OnPropertyChanged(nameof(IsResources));
            NotifyAccess();
            if (_active) _ = RefreshCommand.ExecuteAsync(cancellationToken: _activation?.Token ?? default);
        }
    }
    public IReadOnlyList<DesktopAdminUser> Users { get => _users; private set => SetProperty(ref _users, value); }
    public IReadOnlyList<DesktopAdminRole> Roles { get => _roles; private set => SetProperty(ref _roles, value); }
    public IReadOnlyList<DesktopNetworkResource> Resources { get => _resources; private set => SetProperty(ref _resources, value); }
    public DesktopAdminUser? SelectedUser { get => _selectedUser; set => SetProperty(ref _selectedUser, value); }
    public DesktopAdminRole? SelectedRole { get => _selectedRole; set => SetProperty(ref _selectedRole, value); }
    public DesktopNetworkResource? SelectedResource { get => _selectedResource; set => SetProperty(ref _selectedResource, value); }
    public string? Message { get => _message; private set { if (SetProperty(ref _message, value)) OnPropertyChanged(nameof(HasMessage)); } }
    public bool HasMessage => !string.IsNullOrWhiteSpace(Message);
    public bool IsLoading { get => _isLoading; private set => SetProperty(ref _isLoading, value); }
    public bool IsUsers => SelectedTab.Area == AdministrationArea.Users;
    public bool IsRoles => SelectedTab.Area == AdministrationArea.Roles;
    public bool IsResources => SelectedTab.Area == AdministrationArea.Resources;
    public bool CanReadCurrentArea => _capabilities.Contains(SelectedTab.Capability);
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
        _activation?.Cancel(); _activation?.Dispose(); _activation = new();
        _active = true;
        NotifyAccess();
        _ = RefreshCommand.ExecuteAsync(cancellationToken: _activation.Token);
    }

    public void Deactivate() { _active = false; _activation?.Cancel(); RefreshCommand.RaiseCanExecuteChanged(); }
    public void UpdateCapabilities(IEnumerable<string>? capabilities) { _capabilities.Clear(); _capabilities.UnionWith(capabilities ?? []); NotifyAccess(); }
    public void UpdateSessionState(bool available) { _sessionAvailable = available; if (!available) Message = "Сессия завершена. Выполните вход снова."; NotifyAccess(); }
    public void UpdateConnectivity(bool available) { _networkAvailable = available; Message = available ? "Подключение восстановлено. Обновите административные данные." : "Сервер недоступен. Действия администратора заблокированы."; NotifyAccess(); }

    private async global::System.Threading.Tasks.Task RefreshAsync(object? _, CancellationToken cancellationToken)
    {
        Message = null; IsLoading = true;
        try
        {
            switch (SelectedTab.Area)
            {
                case AdministrationArea.Users: Apply(await _client.GetUsersAsync(cancellationToken), value => { Users = value; SelectedUser = value.FirstOrDefault(); }); break;
                case AdministrationArea.Roles: Apply(await _client.GetRolesAsync(cancellationToken), value => { Roles = value; SelectedRole = value.FirstOrDefault(); }); break;
                case AdministrationArea.Resources: Apply(await _client.GetResourcesAsync(cancellationToken), value => { Resources = value; SelectedResource = value.FirstOrDefault(); }); break;
            }
        }
        finally { IsLoading = false; }
    }

    private void Apply<T>(DesktopWorkResult<T> result, Action<T> success)
    {
        switch (result)
        {
            case DesktopWorkResult<T>.Succeeded ok: success(ok.Value); _lastRefresh = DateTimeOffset.UtcNow; OnPropertyChanged(nameof(LastSuccessfulRefreshText)); break;
            case DesktopWorkResult<T>.Forbidden: Message = "Область доступа изменилась. Защищённые административные данные не показаны."; break;
            case DesktopWorkResult<T>.AuthenticationFailure: Message = "Сессия завершена. Выполните вход снова."; break;
            case DesktopWorkResult<T>.ServerUnavailable: UpdateConnectivity(false); break;
            default: Message = "Сервер вернул неподтверждённые данные. Они не отображены."; break;
        }
    }

    private void NotifyAccess()
    {
        OnPropertyChanged(nameof(CanReadCurrentArea)); OnPropertyChanged(nameof(IsLimitedRole));
        OnPropertyChanged(nameof(IsOffline)); OnPropertyChanged(nameof(AccessText));
        RefreshCommand.RaiseCanExecuteChanged();
    }

    public void Dispose() { _active = false; _activation?.Cancel(); _activation?.Dispose(); RefreshCommand.Dispose(); }
}
