using Task.Desktop.Work;

namespace Task.Desktop.Notifications;

/// <summary>One cancellable pass at a time. API remains the authority for access and delivery.</summary>
public sealed class CorporateNotificationAgent : IDisposable
{
    private readonly IDesktopWorkApiClient _client;
    private readonly IWindowsNotificationPresenter _presenter;
    private readonly NotificationPresentationJournal _journal;
    private readonly Func<bool> _canRead, _canReadSettings;
    private readonly Func<bool> _canSnooze;
    private readonly Action<IReadOnlyList<DesktopNotification>> _update;
    private readonly Action<bool>? _applyAutostart;
    private readonly TimeProvider _clock;
    private readonly DateTimeOffset _startedAt;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly SortedDictionary<Guid, DesktopNotification> _items = [];
    private Guid? _revision;
    private bool _running, _disposed, _legacyPoll;
    public CorporateNotificationAgent(IDesktopWorkApiClient client, IWindowsNotificationPresenter presenter,
        NotificationPresentationJournal journal, Func<bool> canRead, Func<bool> canReadSettings,
        Action<IReadOnlyList<DesktopNotification>> update, TimeProvider? clock = null, Func<bool>? canSnooze = null,
        Action<bool>? applyAutostart = null)
    {
        _client = client; _presenter = presenter; _journal = journal; _canRead = canRead;
        _canSnooze = canSnooze ?? (() => false);
        _canReadSettings = canReadSettings; _update = update; _clock = clock ?? TimeProvider.System; _startedAt = _clock.GetUtcNow();
        _applyAutostart = applyAutostart;
    }
    public bool SessionExpired { get; private set; }
    public async System.Threading.Tasks.Task RunPassAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed || _running || SessionExpired) return;
        var readNotifications = _canRead();
        var readSettings = _canReadSettings();
        if (!readNotifications) ClearNotifications();
        if (!readNotifications && !readSettings) return;
        _running = true;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        var ct = linked.Token;
        try
        {
            if (!_legacyPoll && _client is IDesktopNotificationPollClient polling)
            {
                var response = await polling.PollNotificationsAsync(_revision, readNotifications, readSettings, ct);
                ct.ThrowIfCancellationRequested();
                if (response is DesktopWorkResult<DesktopNotificationPoll>.NotFound) _legacyPoll = true;
                else
                {
                    await ApplyPollAsync(response, ct);
                    return;
                }
            }
            if (_canRead()) await ReadLegacyNotificationsAsync(ct);
            if (!SessionExpired && _canReadSettings() && _applyAutostart is not null)
            {
                var settings = await _client.GetUserSettingsAsync(ct);
                ct.ThrowIfCancellationRequested();
                if (settings is DesktopWorkResult<DesktopUserSettings>.AuthenticationFailure) Expire();
                if (_canReadSettings() && settings is DesktopWorkResult<DesktopUserSettings>.Succeeded ok)
                    _applyAutostart(ok.Value.AutostartEnabled);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        finally { _running = false; if (_disposed) _lifetime.Dispose(); }
    }

    private async System.Threading.Tasks.Task ApplyPollAsync(DesktopWorkResult<DesktopNotificationPoll> response, CancellationToken ct)
    {
        if (response is DesktopWorkResult<DesktopNotificationPoll>.AuthenticationFailure) { Expire(); return; }
        if (response is DesktopWorkResult<DesktopNotificationPoll>.Forbidden) { ClearNotifications(); return; }
        if (response is not DesktopWorkResult<DesktopNotificationPoll>.Succeeded success) { _presenter.Clear(); return; }
        var poll = success.Value;
        if (_canReadSettings() && poll.AutostartEnabled is { } autostart) _applyAutostart?.Invoke(autostart);
        if (!poll.NotificationsIncluded || !_canRead()) { ClearNotifications(); return; }
        if (poll.HistoryRequired)
        {
            _revision = null;
            _items.Clear();
            await ReadHistoryAsync(poll.Preferences, ct);
            return;
        }

        if (!poll.Reset && _revision is null) { ClearNotifications(); return; }
        if (poll.Reset) _items.Clear();
        foreach (var id in poll.RemovedIds) _items.Remove(id);
        foreach (var item in poll.Items) _items[item.Id] = item;
        _revision = poll.Revision;
        Publish(_items.Values, poll.Preferences, ct);
    }

    private async System.Threading.Tasks.Task ReadLegacyNotificationsAsync(CancellationToken ct)
    {
        DesktopNotificationPreferences? preferences = null;
        if (_canReadSettings())
        {
            var result = await _client.GetNotificationPreferencesAsync(ct);
            ct.ThrowIfCancellationRequested();
            if (result is DesktopWorkResult<DesktopNotificationPreferences>.AuthenticationFailure) { Expire(); return; }
            if (result is DesktopWorkResult<DesktopNotificationPreferences>.Succeeded ok) preferences = ok.Value;
            else { ClearNotifications(); return; }
        }
        await ReadHistoryAsync(preferences, ct);
    }

    private async System.Threading.Tasks.Task ReadHistoryAsync(DesktopNotificationPreferences? preferences, CancellationToken ct)
    {
        var response = await _client.GetNotificationsAsync(ct);
        ct.ThrowIfCancellationRequested();
        if (!_canRead()) { ClearNotifications(); return; }
        if (response is DesktopWorkResult<IReadOnlyList<DesktopNotification>>.AuthenticationFailure) { Expire(); return; }
        if (response is DesktopWorkResult<IReadOnlyList<DesktopNotification>>.Forbidden) { ClearNotifications(); return; }
        if (response is not DesktopWorkResult<IReadOnlyList<DesktopNotification>>.Succeeded success) { _presenter.Clear(); return; }
        Publish(success.Value.GroupBy(n => n.Id).Select(g => g.OrderByDescending(n => n.Version).First()), preferences, ct);
    }

    private void Publish(IEnumerable<DesktopNotification> notifications, DesktopNotificationPreferences? preferences, CancellationToken ct)
    {
        var now = _clock.GetUtcNow();
        var items = notifications.Where(n => n.NotBefore <= now && (n.ExpiresAt is null || n.ExpiresAt > now)).ToArray();
        _update(items);
        _presenter.Reconcile(items.Where(n => n.IsUnread).Select(n => n.Id).ToHashSet());
        _journal.InitializeBaseline(items.Where(n => (n.DeliveredAt ?? n.NotBefore) < _startedAt).Select(n => n.Id).ToArray());
        if (!_canReadSettings() || preferences is null || !NotificationPresentationPolicy.Allows(preferences, now)) { _presenter.Clear(); return; }
        var presented = 0;
        foreach (var item in items.Where(n => n.Status is "pending" or "delivered")
            .OrderByDescending(n => n.Severity == "critical").ThenByDescending(n => n.Severity == "warning").ThenBy(n => n.NotBefore))
        {
            ct.ThrowIfCancellationRequested();
            if (_disposed || !_canRead() || !_canReadSettings()) return;
            if (!_journal.Claim([item.Id]).Contains(item.Id)) continue;
            if (!_presenter.Submit(new(item.Id, item.Title, item.Body, item.NotBefore, preferences.SoundEnabled, item.ReminderId is not null && _canSnooze()))) _journal.Release(item.Id);
            else if (++presented == 3) break; // bound Windows popup bursts; remaining events stay eligible
        }
    }

    private void ClearNotifications()
    {
        _items.Clear();
        _revision = null;
        _presenter.Clear();
        _update([]);
    }

    private void Expire() { SessionExpired = true; ClearNotifications(); }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; _lifetime.Cancel(); _presenter.Clear();
        if (!_running) _lifetime.Dispose();
    }
}
