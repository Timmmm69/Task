using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Task.Desktop.Personal;

/// <summary>Windows Shell notification area; no package, account or network dependency.</summary>
internal sealed class PersonalWindowsNotifications : IDisposable
{
    private const int Callback = 0x800B;
    private readonly Window _window;
    private readonly HwndSource _source;
    private readonly int _taskbarCreated;
    private IconData _data;
    private bool _registered;
    public bool IsAvailable => _registered;
    public PersonalWindowsNotifications(Window window)
    {
        _window = window; var handle = new WindowInteropHelper(window).Handle;
        _source = HwndSource.FromHwnd(handle)!;
        _data = new()
        {
            Size = (uint)Marshal.SizeOf<IconData>(),
            Window = handle,
            Id = 1,
            Flags = 7,
            CallbackMessage = Callback,
            Icon = LoadIcon(IntPtr.Zero, new IntPtr(32512)),
            Tip = "Task · Personal",
            Info = "",
            InfoTitle = ""
        };
        _taskbarCreated = (int)RegisterWindowMessage("TaskbarCreated");
        _source.AddHook(Hook); Register();
    }
    private void Register()
    {
        _data.Flags = 7; _registered = NotifyIcon(0, ref _data);
        if (_registered) { _data.Version = 4; NotifyIcon(4, ref _data); }
        else if (!_window.IsVisible) _window.Show();
    }
    private IntPtr Hook(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == _taskbarCreated) Register();
        if (message == Callback && ((long)lParam & 0xFFFF) is 0x400 or 0x401 or 0x202 or 0x405)
        { _window.Show(); _window.WindowState = WindowState.Normal; _window.Activate(); handled = true; }
        return IntPtr.Zero;
    }
    public bool Submit(PersonalNotification notification, bool sound = true)
    {
        if (!_registered) return false;
        _data.Flags = 0x10; _data.InfoTitle = notification.Title.Length > 63 ? notification.Title[..63] : notification.Title;
        _data.Info = $"Личное напоминание · {notification.DueAt.ToLocalTime():dd.MM.yyyy HH:mm}. Откройте Task, чтобы посмотреть уведомление.";
        _data.InfoFlags = 1 | 0x80 | (sound ? 0u : 0x10u); // information, quiet time, optional no-sound
        return NotifyIcon(1, ref _data);
    }
    public void Dispose()
    {
        _source.RemoveHook(Hook); if (_registered) NotifyIcon(2, ref _data); _registered = false;
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct IconData
    {
        public uint Size; public IntPtr Window; public uint Id; public uint Flags; public uint CallbackMessage; public IntPtr Icon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Tip;
        public uint State; public uint StateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Info;
        public uint Version;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string InfoTitle;
        public uint InfoFlags; public Guid Guid; public IntPtr BalloonIcon;
    }
    [DllImport("shell32.dll", EntryPoint = "Shell_NotifyIconW", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool NotifyIcon(uint operation, ref IconData data);
    [DllImport("user32.dll", EntryPoint = "LoadIconW")] private static extern IntPtr LoadIcon(IntPtr instance, IntPtr name);
    [DllImport("user32.dll", EntryPoint = "RegisterWindowMessageW", CharSet = CharSet.Unicode)] private static extern uint RegisterWindowMessage(string message);
}
