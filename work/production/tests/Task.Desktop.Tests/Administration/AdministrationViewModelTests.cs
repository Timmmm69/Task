using Task.Desktop.Administration;
using Task.Desktop.ViewModels;
using Task.Desktop.Work;

namespace Task.Desktop.Tests.Administration;

public sealed class AdministrationViewModelTests
{
    [Fact]
    public async global::System.Threading.Tasks.Task AuthorizedTabsLoadOnlyTheSelectedServerProjection()
    {
        var client = new FakeClient();
        using var viewModel = new AdministrationViewModel(client, ["User.Read", "Role.Read", "FileCatalog.Read"]);

        viewModel.Activate();
        await Eventually(() => viewModel.Users.Count == 1);
        Assert.Equal(1, client.UsersReads);
        Assert.Equal(0, client.RolesReads);

        viewModel.SelectedTab = viewModel.Tabs.Single(tab => tab.Area == AdministrationArea.Roles);
        await Eventually(() => viewModel.Roles.Count == 1);
        Assert.Equal("Системный администратор", viewModel.SelectedRole?.Name);
        Assert.Contains("сервер authoritative", viewModel.AccessText);
    }

    [Fact]
    public void MissingCapabilityShowsLimitedRoleWithoutRequestOrCounts()
    {
        var client = new FakeClient();
        using var viewModel = new AdministrationViewModel(client, []);

        viewModel.Activate();

        Assert.True(viewModel.IsLimitedRole);
        Assert.False(viewModel.RefreshCommand.CanExecute(null));
        Assert.Empty(viewModel.Users);
        Assert.Equal(0, client.UsersReads);
        Assert.Contains("не раскрываются", viewModel.AccessText);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task OfflineKeepsConfirmedDataAndBlocksRefresh()
    {
        using var viewModel = new AdministrationViewModel(new FakeClient(), ["User.Read"]);
        viewModel.Activate();
        await Eventually(() => viewModel.Users.Count == 1);

        viewModel.UpdateConnectivity(false);

        Assert.Single(viewModel.Users);
        Assert.True(viewModel.IsOffline);
        Assert.False(viewModel.RefreshCommand.CanExecute(null));
        Assert.Contains("заблокированы", viewModel.Message);
    }

    private static async global::System.Threading.Tasks.Task Eventually(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(2);
        while (!condition() && DateTime.UtcNow < deadline) await global::System.Threading.Tasks.Task.Delay(20);
        Assert.True(condition());
    }

    private sealed class FakeClient : IDesktopAdministrationApiClient
    {
        public int UsersReads { get; private set; }
        public int RolesReads { get; private set; }

        public global::System.Threading.Tasks.Task<DesktopWorkResult<IReadOnlyList<DesktopAdminUser>>> GetUsersAsync(CancellationToken cancellationToken = default)
        {
            UsersReads++;
            return Ok<IReadOnlyList<DesktopAdminUser>>([new(Guid.NewGuid(), 3, "Анна Крылова", "anna.k", "Руководитель", "active")]);
        }

        public global::System.Threading.Tasks.Task<DesktopWorkResult<IReadOnlyList<DesktopAdminRole>>> GetRolesAsync(CancellationToken cancellationToken = default)
        {
            RolesReads++;
            return Ok<IReadOnlyList<DesktopAdminRole>>([new(Guid.NewGuid(), "system-admin", "Системный администратор", true, ["User.Read", "Role.Read"])]);
        }

        public global::System.Threading.Tasks.Task<DesktopWorkResult<IReadOnlyList<DesktopNetworkResource>>> GetResourcesAsync(CancellationToken cancellationToken = default) =>
            Ok<IReadOnlyList<DesktopNetworkResource>>([new(Guid.NewGuid(), 2, "Общие документы", null, null, "active")]);

        private static global::System.Threading.Tasks.Task<DesktopWorkResult<T>> Ok<T>(T value) =>
            global::System.Threading.Tasks.Task.FromResult<DesktopWorkResult<T>>(new DesktopWorkResult<T>.Succeeded(value));
    }
}
