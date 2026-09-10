namespace Task.Desktop.Security;

public enum DesktopConnectivityStatus
{
    Online,
    Reconnecting,
    ServerUnavailable,
}

/// <summary>
/// Process-local view of API reachability. It never queues writes: consumers use it to
/// disable server mutations while keeping confirmed data and unsaved editors in memory.
/// </summary>
public sealed class DesktopConnectivityService
{
    private readonly SynchronizationContext? _synchronizationContext;
    private readonly object _gate = new();
    private DesktopConnectivityStatus _status = DesktopConnectivityStatus.Online;
    private DateTimeOffset? _lastServerResponseAt = DateTimeOffset.UtcNow;

    public DesktopConnectivityService(SynchronizationContext? synchronizationContext = null)
    {
        _synchronizationContext = synchronizationContext ?? SynchronizationContext.Current;
    }

    public event EventHandler? StatusChanged;

    public DesktopConnectivityStatus Status
    {
        get { lock (_gate) return _status; }
    }

    public DateTimeOffset? LastServerResponseAt
    {
        get { lock (_gate) return _lastServerResponseAt; }
    }

    internal void ReportAttemptStarted()
    {
        lock (_gate)
        {
            if (_status != DesktopConnectivityStatus.ServerUnavailable) return;
            _status = DesktopConnectivityStatus.Reconnecting;
        }

        RaiseStatusChanged();
    }

    internal void ReportServerResponse()
    {
        var changed = false;
        lock (_gate)
        {
            changed = _status != DesktopConnectivityStatus.Online;
            _status = DesktopConnectivityStatus.Online;
            _lastServerResponseAt = DateTimeOffset.UtcNow;
        }

        if (changed) RaiseStatusChanged();
    }

    internal void ReportServerUnavailable()
    {
        lock (_gate)
        {
            if (_status == DesktopConnectivityStatus.ServerUnavailable) return;
            _status = DesktopConnectivityStatus.ServerUnavailable;
        }

        RaiseStatusChanged();
    }

    private void RaiseStatusChanged()
    {
        var handler = StatusChanged;
        if (handler is null) return;
        if (_synchronizationContext is null || ReferenceEquals(SynchronizationContext.Current, _synchronizationContext))
        {
            handler(this, EventArgs.Empty);
            return;
        }

        _synchronizationContext.Post(static state =>
        {
            var (sender, callback) = ((DesktopConnectivityService, EventHandler))state!;
            callback(sender, EventArgs.Empty);
        }, (this, handler));
    }
}
