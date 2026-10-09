using System.IO;
using System.Reflection;
using Task.Desktop.Notifications;
using Task.Desktop.Work;

namespace Task.Desktop.Tests.Work;

public sealed class CorporateNotificationAgentTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "Task-notifications", Guid.NewGuid().ToString("N"));
    private readonly Clock _clock = new();
    private readonly Presenter _presenter = new();
    private readonly IDesktopWorkApiClient _client;
    private readonly ApiProxy _api;
    private bool _allowed = true, _settingsAllowed = true;
    private IReadOnlyList<DesktopNotification> _center = [];
    public CorporateNotificationAgentTests()
    {
        _client = DispatchProxy.Create<IDesktopWorkApiClient, ApiProxy>(); _api = (ApiProxy)_client;
    }
    private CorporateNotificationAgent Agent(NotificationPresentationJournal journal) => new(_client, _presenter, journal,
        () => _allowed, () => _settingsAllowed, items => _center = items, _clock);
    private DesktopNotification Notification(string status = "delivered") => new(Guid.NewGuid(), 1, "task.assigned",
        "Task", "Content", "info", status, Guid.NewGuid(), _clock.Now.AddMinutes(-1));
    private void Set(params DesktopNotification[] notifications) => _api.Response = new DesktopWorkResult<IReadOnlyList<DesktopNotification>>.Succeeded(notifications);
    private async System.Threading.Tasks.Task Seed(CorporateNotificationAgent agent) { Set(); await agent.RunPassAsync(); }

    [Fact]
    public async System.Threading.Tasks.Task AutomaticallyShowsNewItemAndUpdatesUnreadWithoutOpeningCenterOrMarkingRead()
    {
        using var journal = new NotificationPresentationJournal(_directory, "server/org/user"); using var agent = Agent(journal);
        await Seed(agent); var item = Notification(); Set(item); await agent.RunPassAsync();
        Assert.Equal(item.Id, Assert.Single(_presenter.Items).Id);
        Assert.True(Assert.Single(_center).IsUnread); Assert.Equal("delivered", _center[0].Status);
        await agent.RunPassAsync(); Assert.Single(_presenter.Items);
    }
    [Fact]
    public async System.Threading.Tasks.Task FirstLaunchSuppressesHistoryAndRestartRetainsClaimsButShowsNewOfflineEvents()
    {
        var old = Notification(); Set(old);
        using (var journal = new NotificationPresentationJournal(_directory, "one"))
        using (var agent = Agent(journal)) { await agent.RunPassAsync(); Assert.Empty(_presenter.Items); }
        var added = Notification(); Set(old, added);
        using (var journal = new NotificationPresentationJournal(_directory, "one"))
        using (var agent = Agent(journal)) { await agent.RunPassAsync(); Assert.Equal(added.Id, Assert.Single(_presenter.Items).Id); }
        using var reopened = new NotificationPresentationJournal(_directory, "one"); using var again = Agent(reopened);
        await again.RunPassAsync(); Assert.Single(_presenter.Items);
    }
    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async System.Threading.Tasks.Task DisabledDeliveryOrDesktopDefersPopupUntilEnabled(bool enabled, bool desktop)
    {
        using var journal = new NotificationPresentationJournal(_directory, "one"); using var agent = Agent(journal); await Seed(agent);
        _api.Preferences = _api.Preferences with { Enabled = enabled, DesktopEnabled = desktop };
        Set(Notification()); await agent.RunPassAsync(); Assert.Empty(_presenter.Items); Assert.Single(_center);
        _api.Preferences = _api.Preferences with { Enabled = true, DesktopEnabled = true };
        await agent.RunPassAsync(); Assert.Single(_presenter.Items);
    }
    [Fact]
    public async System.Threading.Tasks.Task SoundOffStillSubmitsSilentPopupAndChangedSettingsApplyOnNextPass()
    {
        using var journal = new NotificationPresentationJournal(_directory, "one"); using var agent = Agent(journal); await Seed(agent);
        _api.Preferences = _api.Preferences with { SoundEnabled = false }; Set(Notification()); await agent.RunPassAsync();
        Assert.False(Assert.Single(_presenter.Items).Sound);
        _api.Preferences = _api.Preferences with { SoundEnabled = true }; Set(Notification()); await agent.RunPassAsync();
        Assert.True(_presenter.Items.Last().Sound);
    }
    [Theory]
    [InlineData("2026-10-10T00:00:00Z", true)]
    [InlineData("2026-10-10T10:00:00Z", false)]
    [InlineData("2026-03-08T07:00:00Z", true)]
    [InlineData("2026-11-01T06:00:00Z", true)]
    public void QuietHoursUseTimezoneAcrossMidnightAndDst(string now, bool quiet)
    {
        var preferences = _api.Preferences with { QuietHoursStart = "20:00", QuietHoursEnd = "06:00", QuietHoursTimeZone = "America/New_York" };
        Assert.Equal(!quiet, NotificationPresentationPolicy.Allows(preferences, DateTimeOffset.Parse(now)));
    }
    [Fact]
    public async System.Threading.Tasks.Task DuplicateServerRecordsOfflineAndMalformedResponsesDoNotLoseNewEventsOrRepeatPopups()
    {
        using var journal = new NotificationPresentationJournal(_directory, "one"); using var agent = Agent(journal); await Seed(agent);
        _api.Response = new DesktopWorkResult<IReadOnlyList<DesktopNotification>>.ServerUnavailable(); await agent.RunPassAsync();
        _api.Response = new DesktopWorkResult<IReadOnlyList<DesktopNotification>>.MalformedResponse(); await agent.RunPassAsync();
        var item = Notification(); Set(item, item); await agent.RunPassAsync(); await agent.RunPassAsync();
        Assert.Equal(item.Id, Assert.Single(_presenter.Items).Id); Assert.Single(_center);
    }
    [Fact]
    public async System.Threading.Tasks.Task ExpiredSessionClearsContentAndStopsFurtherRequests()
    {
        using var journal = new NotificationPresentationJournal(_directory, "one"); using var agent = Agent(journal); await Seed(agent);
        _api.Response = new DesktopWorkResult<IReadOnlyList<DesktopNotification>>.AuthenticationFailure(); await agent.RunPassAsync();
        var calls = _api.Calls; Set(Notification()); await agent.RunPassAsync();
        Assert.True(agent.SessionExpired); Assert.Equal(calls, _api.Calls); Assert.Empty(_center); Assert.Empty(_presenter.Items);
    }
    [Fact]
    public async System.Threading.Tasks.Task RevokedNotificationPermissionMakesNoRequestsAndClearsCenter()
    {
        using var journal = new NotificationPresentationJournal(_directory, "one"); using var agent = Agent(journal); await Seed(agent);
        _allowed = false; var calls = _api.Calls; Set(Notification()); await agent.RunPassAsync();
        Assert.Equal(calls, _api.Calls); Assert.Empty(_center); Assert.Empty(_presenter.Items);
    }
    [Fact]
    public async System.Threading.Tasks.Task NoSettingsReadPermissionNeverUsesAssumedDefaultsForPopup()
    {
        using var journal = new NotificationPresentationJournal(_directory, "one"); using var agent = Agent(journal); await Seed(agent);
        _settingsAllowed = false; Set(Notification()); await agent.RunPassAsync(); Assert.Empty(_presenter.Items); Assert.Single(_center);
    }
    [Fact]
    public async System.Threading.Tasks.Task ReadDismissedExpiredAndFutureItemsCannotPopup()
    {
        using var journal = new NotificationPresentationJournal(_directory, "one"); using var agent = Agent(journal); await Seed(agent);
        Set(Notification("read"), Notification("dismissed"), Notification("expired"),
            Notification() with { NotBefore = _clock.Now.AddMinutes(2) }, Notification() with { ExpiresAt = _clock.Now });
        await agent.RunPassAsync(); Assert.Empty(_presenter.Items); Assert.Equal(3, _center.Count);
    }
    [Fact]
    public async System.Threading.Tasks.Task PassCannotOverlapAndDisposeDiscardsAnInflightResponse()
    {
        using var journal = new NotificationPresentationJournal(_directory, "one"); var agent = Agent(journal); await Seed(agent);
        var pending = new TaskCompletionSource<DesktopWorkResult<IReadOnlyList<DesktopNotification>>>();
        _api.Pending = pending.Task;
        var pass = agent.RunPassAsync(); var calls = _api.Calls; await agent.RunPassAsync(); Assert.Equal(calls, _api.Calls);
        agent.Dispose(); pending.SetResult(new DesktopWorkResult<IReadOnlyList<DesktopNotification>>.Succeeded([Notification()]));
        await pass; Assert.Empty(_center); Assert.Empty(_presenter.Items); await agent.RunPassAsync(); Assert.Equal(calls, _api.Calls);
    }
    [Fact]
    public void JournalsAreIsolatedByUserAndServerAndSharedByTwoClientsOfTheSameContext()
    {
        var id = Guid.NewGuid();
        using var a = new NotificationPresentationJournal(_directory, "server/org/userA");
        using var same = new NotificationPresentationJournal(_directory, "server/org/userA");
        using var other = new NotificationPresentationJournal(_directory, "server/org/userB");
        Assert.Empty(a.Claim([])); Assert.Empty(other.Claim([id])); Assert.Contains(id, a.Claim([id])); Assert.Empty(same.Claim([id]));
    }
    [Fact]
    public async System.Threading.Tasks.Task TemporaryWindowsFailureRetriesButAnAcceptedPopupNeverRepeats()
    {
        using var journal = new NotificationPresentationJournal(_directory, "one"); using var agent = Agent(journal); await Seed(agent);
        _presenter.Available = false; var item = Notification(); Set(item); await agent.RunPassAsync(); Assert.Empty(_presenter.Items);
        _presenter.Available = true; await agent.RunPassAsync(); Assert.Equal(item.Id, Assert.Single(_presenter.Items).Id);
        await agent.RunPassAsync(); Assert.Single(_presenter.Items);
    }
    [Fact]
    public async System.Threading.Tasks.Task FirstStartupOfflineDoesNotDiscardEventsThatBecomeDueWhileWaitingForServer()
    {
        using var journal = new NotificationPresentationJournal(_directory, "one"); using var agent = Agent(journal);
        _api.Response = new DesktopWorkResult<IReadOnlyList<DesktopNotification>>.ServerUnavailable(); await agent.RunPassAsync();
        _clock.Now = _clock.Now.AddMinutes(10);
        var current = Notification(); var old = Notification() with { NotBefore = _clock.Now.AddDays(-1) }; Set(old, current);
        await agent.RunPassAsync(); Assert.Equal(current.Id, Assert.Single(_presenter.Items).Id);
    }
    public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }
    private sealed class Presenter : IWindowsNotificationPresenter
    {
        public List<WindowsNotification> Items { get; } = [];
        public bool Available { get; set; } = true;
        public bool Submit(WindowsNotification item) { if (!Available) return false; Items.Add(item); return true; }
        public void Clear() { }
    }
    public class ApiProxy : DispatchProxy
    {
        public int Calls { get; private set; }
        public DesktopNotificationPreferences Preferences { get; set; } = new(1, true, true, true, 15, null, null, null);
        public DesktopWorkResult<IReadOnlyList<DesktopNotification>> Response { get; set; } = new DesktopWorkResult<IReadOnlyList<DesktopNotification>>.Succeeded([]);
        public System.Threading.Tasks.Task<DesktopWorkResult<IReadOnlyList<DesktopNotification>>>? Pending { get; set; }
        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            Calls++;
            return method?.Name switch
            {
                nameof(IDesktopWorkApiClient.GetNotificationPreferencesAsync) => System.Threading.Tasks.Task.FromResult<DesktopWorkResult<DesktopNotificationPreferences>>(new DesktopWorkResult<DesktopNotificationPreferences>.Succeeded(Preferences)),
                nameof(IDesktopWorkApiClient.GetNotificationsAsync) => Pending ?? System.Threading.Tasks.Task.FromResult(Response),
                _ => throw new InvalidOperationException("Unexpected client operation: " + method?.Name),
            };
        }
    }
}
