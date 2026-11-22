using AsyncTask = global::System.Threading.Tasks.Task;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading.Channels;
using Task.Desktop.Modes;

namespace Task.Desktop.Analytics;

internal sealed class ProductAnalytics : IDisposable
{
    internal const int QueueLimit = 500;
    private const int BatchLimit = 20;
    internal const int MaxAttempts = 24;
    private static readonly TimeSpan Lifetime = TimeSpan.FromDays(7);

    private readonly object _inboxLock = new();
    private readonly List<UsageRecord> _inbox = [];
    private readonly HashSet<ApplicationMode> _opened = [];
    private readonly Channel<bool> _wake = Channel.CreateBounded<bool>(1);
    private readonly SemaphoreSlim _gate = new(1);
    private readonly CancellationTokenSource _shutdown = new();
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Guid _session = Guid.NewGuid();
    private readonly Version _version = typeof(ProductAnalytics).Assembly.GetName().Version ?? new Version(1, 0);
    private readonly TimeProvider _clock;
    private readonly AnalyticsConfiguration? _configuration;
    private readonly HttpClient _http;
    private readonly PostHogCapture? _capture;
    private readonly AsyncTask _worker;

    private AnalyticsStore? _store;
    private AnalyticsState? _state;
    private CancellationTokenSource? _request;
    private ApplicationMode? _readyMode;
    private bool _accepting;
    private bool _disposed;
    private bool _noticeClaimed;
    private volatile bool _optedOut;
    private volatile bool _disableRequested;
    private volatile bool _enabled;
    private volatile bool _storageAvailable;

    public ProductAnalytics(string root, AnalyticsConfiguration? configuration, TimeProvider? clock = null, HttpMessageHandler? transport = null)
    {
        _clock = clock ?? TimeProvider.System;
        _configuration = configuration;
        _http = new HttpClient(transport ?? new HttpClientHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false,
            UseDefaultCredentials = false,
        })
        {
            Timeout = TimeSpan.FromSeconds(8),
            MaxResponseContentBufferSize = 64 * 1024,
        };
        if (configuration is not null) _capture = new(configuration, _http, _clock);
        _worker = AsyncTask.Run(() => RunAsync(root));
    }

    public AsyncTask Ready => _ready.Task;
    public bool Enabled => _enabled;
    public bool StorageAvailable => _storageAvailable;
    public bool Configured => _configuration is not null;
    public bool RequiresConsent => _configuration?.RequiresConsent == true;
    public event Action? PreferenceChanged;

    public bool TryClaimNotice()
    {
        lock (_inboxLock)
        {
            if (_disposed || !_storageAvailable || !Configured || RequiresConsent
                || _enabled || _optedOut || _disableRequested || _noticeClaimed)
                return false;
            _noticeClaimed = true;
            return true;
        }
    }

    public void ReleaseNotice()
    {
        lock (_inboxLock) _noticeClaimed = false;
    }

    public Task<bool> EnableAfterNoticeAsync() => ChangePreferenceAsync(enabled: true, automatic: true);

    public void ModeOpened(ApplicationMode mode)
    {
        try
        {
            lock (_inboxLock)
            {
                if (_disposed) return;
                _readyMode = mode;
                RecordOpened();
            }
        }
        catch (Exception error) { TraceFailure(error); }
    }

    public void Record(UsageEvent usage, ApplicationMode mode)
    {
        lock (_inboxLock)
        {
            if (_accepting) Enqueue(usage, mode);
        }
    }

    private void RecordOpened()
    {
        if (_accepting && _readyMode is { } mode && _opened.Add(mode))
            Enqueue(UsageEvent.AppOpened, mode);
    }

    private void Enqueue(UsageEvent usage, ApplicationMode mode)
    {
        if (_inbox.Count >= QueueLimit) return;
        var now = _clock.GetUtcNow();
        _inbox.Add(new(Guid.CreateVersion7(now), usage, mode, _session, now, _version));
        _wake.Writer.TryWrite(true);
    }

    public Task<bool> SetEnabledAsync(bool enabled) => ChangePreferenceAsync(enabled, automatic: false);

    private Task<bool> ChangePreferenceAsync(bool enabled, bool automatic)
    {
        if (!enabled)
        {
            lock (_inboxLock)
            {
                _disableRequested = true;
                _accepting = false;
                if (_request is not null) _ = _request.CancelAsync();
            }
        }

        return AsyncTask.Run(async () =>
        {
            await Ready.ConfigureAwait(false);
            await _gate.WaitAsync().ConfigureAwait(false);
            try
            {
                if (_disposed || _store is null) return false;
                if (enabled)
                {
                    if (_configuration is null) return false;
                    if (automatic)
                    {
                        lock (_inboxLock)
                        {
                            if (!_noticeClaimed || RequiresConsent || _disableRequested || _optedOut) return false;
                        }
                    }

                    var state = _state ?? new(_configuration.Destination, Guid.NewGuid(), [], EnabledByNotice: automatic);
                    _store.Write(state);
                    if (!automatic) _store.ClearOptOut();
                    _state = state;
                    _enabled = true;
                    lock (_inboxLock)
                    {
                        if (!automatic) _disableRequested = false;
                        _optedOut = false;
                        _accepting = !_disposed && !_disableRequested;
                        RecordOpened();
                    }
                }
                else
                {
                    lock (_inboxLock)
                    {
                        _opened.Clear();
                        _inbox.Clear();
                    }

                    PersistOptOut();
                }
                return true;
            }
            catch (Exception error)
            {
                TraceFailure(error);
                return false;
            }
            finally
            {
                _gate.Release();
                _wake.Writer.TryWrite(true);
                try { PreferenceChanged?.Invoke(); }
                catch (Exception error) { TraceFailure(error); }
            }
        });
    }

    private async AsyncTask RunAsync(string root)
    {
        try
        {
            _store = new(root);
            _optedOut = _store.IsOptedOut();
            if (_optedOut) _store.Delete();
            else
            {
                _state = _store.Read();
                if (_configuration is not null && _state is not null &&
                    (_state.Destination != _configuration.Destination || RequiresConsent && _state.EnabledByNotice))
                {
                    _store.Delete();
                    _state = null;
                }
            }
            _enabled = _state is not null;
            _storageAvailable = true;
            lock (_inboxLock)
            {
                _accepting = !_disposed && _enabled && Configured;
                RecordOpened();
            }
        }
        catch (Exception error)
        {
            TraceFailure(error);
            _store?.Dispose();
            _store = null;
        }
        finally
        {
            _ready.TrySetResult();
        }

        var stop = _shutdown.Token;
        try
        {
            while (_store is not null && !stop.IsCancellationRequested)
            {
                TimeSpan? next;
                try { next = await ProcessAsync(stop).ConfigureAwait(false); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                {
                    TraceFailure(error);
                    await AsyncTask.Delay(TimeSpan.FromMinutes(1), _clock, stop).ConfigureAwait(false);
                    continue;
                }
                if (next <= TimeSpan.Zero) continue;
                using var wait = CancellationTokenSource.CreateLinkedTokenSource(stop);
                var signalled = _wake.Reader.ReadAsync(wait.Token).AsTask();
                if (next is null) await signalled.ConfigureAwait(false);
                else
                {
                    var delay = AsyncTask.Delay(next.Value, _clock, wait.Token);
                    await AsyncTask.WhenAny(signalled, delay).ConfigureAwait(false);
                    await wait.CancelAsync().ConfigureAwait(false);
                    try { await signalled.ConfigureAwait(false); }
                    catch (OperationCanceledException) when (wait.IsCancellationRequested) { }
                }
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        catch (Exception error)
        {
            TraceFailure(error);
            lock (_inboxLock) _accepting = false;
            // Keep consent controls available even if queue processing fails.
            try { await AsyncTask.Delay(Timeout.InfiniteTimeSpan, stop).ConfigureAwait(false); }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        }
        finally
        {
            await _gate.WaitAsync().ConfigureAwait(false);
            try
            {
                if (_store is not null && _disableRequested) PersistOptOut();
                else if (_store is not null && _state is not null) PersistInbox(_clock.GetUtcNow());
            }
            catch (Exception error) { TraceFailure(error); }
            finally
            {
                _store?.Dispose();
                _store = null;
                _storageAvailable = false;
                _gate.Release();
                _http.Dispose();
                _shutdown.Dispose();
            }
        }
    }

    private async Task<TimeSpan?> ProcessAsync(CancellationToken stop)
    {
        Task<CaptureResult> sending;
        CaptureBatch batch;
        AnalyticsState state;
        CancellationTokenSource request;
        await _gate.WaitAsync(stop).ConfigureAwait(false);
        try
        {
            var now = _clock.GetUtcNow();
            PersistInbox(now);
            if (_state is not { } current || current.Events.Count == 0) return null;
            var expires = current.Events.Min(item => item.Timestamp) + Lifetime;
            if (_capture is null) return expires - now;

            var byId = current.Events.ToDictionary(item => item.Id);
            var previous = current.Batch;
            var events = previous?.Events.Where(byId.ContainsKey).Select(id => byId[id]).ToArray();
            if (events is null || events.Length == 0)
            {
                events = current.Events.Take(BatchLimit).ToArray();
                // Briefly coalesce bursts without polling while the queue is empty.
                var until = events[0].Timestamp + TimeSpan.FromSeconds(2) - now;
                if (until > TimeSpan.Zero && events.Length < BatchLimit) return until;
                previous = new(Guid.NewGuid(), 0, now, events.Select(item => item.Id).ToArray());
            }
            else
            {
                if (previous!.NextAttempt > now)
                {
                    return (previous.NextAttempt < expires ? previous.NextAttempt : expires) - now;
                }
                if (previous.Attempt >= MaxAttempts)
                {
                    var exhausted = previous.Events.ToHashSet();
                    Save(current with { Events = current.Events.Where(item => !exhausted.Contains(item.Id)).ToList(), Batch = null });
                    return TimeSpan.Zero;
                }
            }

            lock (_inboxLock)
            {
                if (!_accepting) return null;
            }
            var attempt = previous.Attempt + 1;
            var backoff = TimeSpan.FromSeconds(Math.Min(14400, 5 * Math.Pow(2, attempt - 1)) * (1 + Random.Shared.NextDouble() * 0.2));
            batch = previous with { Attempt = attempt, NextAttempt = now + backoff, Events = events.Select(item => item.Id).ToArray() };
            state = current with { Batch = batch };
            Save(state);
            lock (_inboxLock)
            {
                if (!_accepting) return null;
                request = CancellationTokenSource.CreateLinkedTokenSource(stop);
                _request = request;
            }
            sending = _capture.SendAsync(state.Installation, batch, events, request.Token);
        }
        finally { _gate.Release(); }

        try
        {
            var result = await sending.ConfigureAwait(false);
            await _gate.WaitAsync(stop).ConfigureAwait(false);
            try
            {
                if (!ReferenceEquals(_state, state) || request.IsCancellationRequested) return TimeSpan.Zero;
                var attempted = batch.Events.ToHashSet();
                var retry = batch.Attempt < MaxAttempts ? result.Retry : [];
                var retryAt = _clock.GetUtcNow() + (result.RetryAfter > Lifetime ? Lifetime : result.RetryAfter);
                Save(state with
                {
                    Events = state.Events.Where(item => !attempted.Contains(item.Id) || retry.Contains(item.Id)).ToList(),
                    Batch = retry.Count > 0 ? batch with
                    {
                        Events = batch.Events.Where(retry.Contains).ToArray(),
                        NextAttempt = retryAt > batch.NextAttempt ? retryAt : batch.NextAttempt,
                    } : null,
                });
            }
            finally { _gate.Release(); }
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested) { }
        finally
        {
            await _gate.WaitAsync().ConfigureAwait(false);
            try
            {
                lock (_inboxLock) _request = null;
                request.Dispose();
            }
            finally { _gate.Release(); }
        }

        return TimeSpan.Zero;
    }

    private void PersistOptOut()
    {
        // The persisted preference takes precedence over the queue on restart.
        _store!.RememberOptOut();
        _optedOut = true;
        _enabled = false;
        _state = null;
        _store.Delete();
    }

    private void PersistInbox(DateTimeOffset now)
    {
        if (_state is not { } state) return;
        var oldest = now - Lifetime;
        var events = state.Events.Where(item => item.Timestamp > oldest).ToList();
        int buffered;
        lock (_inboxLock)
        {
            buffered = _inbox.Count;
            events.AddRange(_inbox.Take(QueueLimit - events.Count).Where(item => item.Timestamp > oldest));
        }

        if (!events.SequenceEqual(state.Events)) Save(state with { Events = events });
        lock (_inboxLock) _inbox.RemoveRange(0, buffered);
    }

    private void Save(AnalyticsState state)
    {
        _store!.Write(state);
        _state = state;
    }

    private static void TraceFailure(Exception error) => Trace.TraceWarning("Task analytics unavailable ({0}).", error.GetType().Name);

    public void Dispose()
    {
        lock (_inboxLock)
        {
            if (_disposed) return;
            _disposed = true;
            _accepting = false;
        }
        // Never wait on the network. Cancellation interrupts the request so the worker can persist its inbox.
        if (_worker.IsCompleted) return;
        try
        {
            _ = _shutdown.CancelAsync();
            _worker.Wait(TimeSpan.FromMilliseconds(250));
        }
        catch (ObjectDisposedException) { }
    }
}
