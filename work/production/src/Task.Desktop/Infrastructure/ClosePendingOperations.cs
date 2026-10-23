using Task.Desktop.Work;
using Task.Desktop.Calendar;
using Task.Desktop.Projects;

namespace Task.Desktop.Infrastructure;

/// <summary>Per-view-model receipts only; never stores, retries, or cancels a request.</summary>
internal sealed class ClosePendingOperations
{
    private readonly object _gate = new();
    private int _active;
    private bool _unknown;
    internal bool HasPending { get { lock (_gate) return _active > 0 || _unknown; } }

    internal global::System.Threading.Tasks.Task<DesktopWorkResult<T>> TrackAsync<T>(
        Func<global::System.Threading.Tasks.Task<DesktopWorkResult<T>>> send) =>
        TrackAsync(send, result => result is DesktopWorkResult<T>.ServerUnavailable or DesktopWorkResult<T>.MalformedResponse);

    internal global::System.Threading.Tasks.Task<DesktopCalendarResult<T>> TrackAsync<T>(
        Func<global::System.Threading.Tasks.Task<DesktopCalendarResult<T>>> send) =>
        TrackAsync(send, result => result is DesktopCalendarResult<T>.ServerUnavailable or DesktopCalendarResult<T>.MalformedResponse);

    internal global::System.Threading.Tasks.Task<DesktopProjectResult<T>> TrackAsync<T>(
        Func<global::System.Threading.Tasks.Task<DesktopProjectResult<T>>> send) where T : class =>
        TrackAsync(send, result => result is DesktopProjectResult<T>.ServerUnavailable or DesktopProjectResult<T>.MalformedResponse);

    private async global::System.Threading.Tasks.Task<T> TrackAsync<T>(Func<global::System.Threading.Tasks.Task<T>> send, Func<T, bool> unknown)
    {
        lock (_gate) _active++;
        try
        {
            var result = await send();
            if (unknown(result)) { lock (_gate) _unknown = true; }
            return result;
        }
        catch { lock (_gate) _unknown = true; throw; }
        finally { lock (_gate) _active--; }
        // These APIs do not expose an exact retained request identity to the owner.
        // A later successful write cannot prove the outcome of the previous request.
    }
}
