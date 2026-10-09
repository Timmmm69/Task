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
    private readonly TimeProvider _clock;
    private readonly DateTimeOffset _startedAt;
    private readonly CancellationTokenSource _lifetime = new();
    private bool _running, _disposed;
    public CorporateNotificationAgent(IDesktopWorkApiClient client, IWindowsNotificationPresenter presenter,
        NotificationPresentationJournal journal, Func<bool> canRead, Func<bool> canReadSettings,
        Action<IReadOnlyList<DesktopNotification>> update, TimeProvider? clock = null, Func<bool>? canSnooze = null)
    {
        _client = client; _presenter = presenter; _journal = journal; _canRead = canRead;
        _canSnooze = canSnooze ?? (() => false);
        _canReadSettings = canReadSettings; _update = update; _clock = clock ?? TimeProvider.System; _startedAt = _clock.GetUtcNow();
    }
    public bool SessionExpired { get; private set; }
    public async System.Threading.Tasks.Task RunPassAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed || _running || SessionExpired) return;
        if (!_canRead()) { _presenter.Clear(); _update([]); return; }
        _running = true;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        var ct = linked.Token;
        try
        {
            DesktopNotificationPreferences? preferences = null;
            if (_canReadSettings())
            {
                var result = await _client.GetNotificationPreferencesAsync(ct);
                if (result is DesktopWorkResult<DesktopNotificationPreferences>.AuthenticationFailure) { Expire(); return; }
                if (result is DesktopWorkResult<DesktopNotificationPreferences>.Succeeded ok) preferences = ok.Value;
                else { _presenter.Clear(); _update([]); return; } // fail closed; never use stale preferences
            }
            var response = await _client.GetNotificationsAsync(ct);
            ct.ThrowIfCancellationRequested();
            if (_disposed || !_canRead()) return;
            if (response is DesktopWorkResult<IReadOnlyList<DesktopNotification>>.AuthenticationFailure) { Expire(); return; }
            if (response is DesktopWorkResult<IReadOnlyList<DesktopNotification>>.Forbidden) { _presenter.Clear(); _update([]); return; }
            if (response is not DesktopWorkResult<IReadOnlyList<DesktopNotification>>.Succeeded success) { _presenter.Clear(); return; }
            var now = _clock.GetUtcNow();
            var items = success.Value.GroupBy(n => n.Id).Select(g => g.OrderByDescending(n => n.Version).First())
                .Where(n => n.NotBefore <= now && (n.ExpiresAt is null || n.ExpiresAt > now)).ToArray();
            _update(items);
            _presenter.Reconcile(items.Where(n => n.IsUnread).Select(n => n.Id).ToHashSet());
            _journal.InitializeBaseline(items.Where(n => n.NotBefore < _startedAt).Select(n => n.Id).ToArray());
            if (preferences is null || !NotificationPresentationPolicy.Allows(preferences, now)) { _presenter.Clear(); return; }
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
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        finally { _running = false; if (_disposed) _lifetime.Dispose(); }
    }
    private void Expire() { SessionExpired = true; _presenter.Clear(); _update([]); }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; _lifetime.Cancel(); _presenter.Clear();
        if (!_running) _lifetime.Dispose();
    }
}
