using System.Runtime.InteropServices;
using System.Windows.Threading;
using Microsoft.Toolkit.Uwp.Notifications;
using Windows.UI.Notifications;

namespace Task.Desktop.Notifications;

/// <summary>Toolkit registers the unpackaged application's COM activation with Windows.</summary>
internal sealed class WindowsToastPresenter : IWindowsNotificationPresenter, IDisposable
{
    private readonly string _scope = Guid.NewGuid().ToString("N");
    private readonly Dispatcher _dispatcher;
    private readonly Action<Guid, string> _activate;
    private readonly Dictionary<Guid, string> _tags = [];
    private bool _disposed;
    public WindowsToastPresenter(Dispatcher dispatcher, Action<Guid, string> activate)
    {
        _dispatcher = dispatcher; _activate = activate;
        ToastNotificationManagerCompat.OnActivated += OnActivated;
        // Remove orphaned content after a crash, before a new account or mode can present data.
        try { ToastNotificationManagerCompat.History.Clear(); }
        catch (Exception e) when (e is COMException or InvalidOperationException or UnauthorizedAccessException) { }
    }
    public bool Submit(WindowsNotification notification)
    {
        if (_disposed) return false;
        try
        {
            if (ToastNotificationManagerCompat.CreateToastNotifier().Setting != NotificationSetting.Enabled) return false;
            var builder = new ToastContentBuilder().AddArgument("scope", _scope)
                .AddArgument("id", notification.Id.ToString("D"))
                .AddText(notification.Title).AddText(notification.Body)
                .AddText(notification.EventAt.ToLocalTime().ToString("dd.MM.yyyy HH:mm"))
                .AddAudio(new ToastAudio { Silent = !notification.Sound })
                .AddButton(new ToastButton().SetContent("Открыть").AddArgument("action", "open"))
                .AddButton(new ToastButton().SetContent("Прочитано").AddArgument("action", "read"));
            if (notification.CanSnooze)
                builder.AddButton(new ToastButton().SetContent("Отложить").AddArgument("action", "snooze"));
            var tag = notification.Id.ToString("N")[..16];
            builder.Show(toast => { toast.Tag = tag; toast.Group = _scope[..16]; toast.ExpirationTime = DateTimeOffset.UtcNow.AddHours(1); });
            _tags[notification.Id] = tag; return true;
        }
        catch (Exception e) when (e is COMException or InvalidOperationException or UnauthorizedAccessException or System.IO.IOException or ArgumentException) { return false; }
    }
    private void OnActivated(ToastNotificationActivatedEventArgsCompat args)
    {
        if (_disposed) return;
        try
        {
            var values = ToastArguments.Parse(args.Argument);
            if (!values.TryGetValue("scope", out var scope) || scope != _scope
                || !values.TryGetValue("id", out var id) || !Guid.TryParse(id, out var guid)) return;
            var action = values.TryGetValue("action", out var value) ? value : "open";
            if (action is not ("open" or "read" or "snooze")) return;
            _dispatcher.BeginInvoke(() => { if (!_disposed) _activate(guid, action); });
        }
        catch (Exception e) when (e is ArgumentException or FormatException) { }
    }
    public void Clear()
    {
        Reconcile(new HashSet<Guid>());
    }
    public void Reconcile(IReadOnlySet<Guid> availableUnreadIds)
    {
        var removed = _tags.Where(pair => !availableUnreadIds.Contains(pair.Key)).ToArray();
        try { foreach (var pair in removed) ToastNotificationManagerCompat.History.Remove(pair.Value, _scope[..16]); }
        catch (Exception e) when (e is COMException or InvalidOperationException or UnauthorizedAccessException) { }
        foreach (var pair in removed) _tags.Remove(pair.Key);
    }
    public void Dispose()
    {
        if (_disposed) return; _disposed = true;
        ToastNotificationManagerCompat.OnActivated -= OnActivated; Clear();
    }
}
