using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Task.Desktop;

/// <summary>Fits startup windows to their target monitor without limiting later resizing.</summary>
internal static class WindowsUxLayout
{
    internal static Size CalculateEffectiveMinimum(Size minimum, Size workArea)
    {
        Validate(minimum, nameof(minimum), allowZero: true);
        Validate(workArea, nameof(workArea));
        return new Size(Math.Min(minimum.Width, workArea.Width), Math.Min(minimum.Height, workArea.Height));
    }

    internal static Size CalculateStartupSize(Size desired, Size minimum, Size workArea)
    {
        Validate(desired, nameof(desired));
        var effectiveMinimum = CalculateEffectiveMinimum(minimum, workArea);
        return new Size(
            Math.Clamp(desired.Width, effectiveMinimum.Width, workArea.Width),
            Math.Clamp(desired.Height, effectiveMinimum.Height, workArea.Height));
    }

    // Work area, owner and result are physical pixels. Only sizes are converted to/from DIP:
    // scaling absolute virtual-desktop coordinates is incorrect on mixed-DPI desktops.
    internal static Rect CalculateStartupBounds(
        Size desired, Size minimum, Rect workAreaPixels, double dpi, Rect? ownerPixels = null)
    {
        Validate(workAreaPixels.Size, nameof(workAreaPixels));
        if (!double.IsFinite(dpi) || dpi <= 0) throw new ArgumentOutOfRangeException(nameof(dpi));
        var scale = dpi / 96;
        var fitted = CalculateStartupSize(desired, minimum,
            new Size(workAreaPixels.Width / scale, workAreaPixels.Height / scale));
        var width = Math.Min(workAreaPixels.Width, Math.Max(1, Math.Floor(fitted.Width * scale)));
        var height = Math.Min(workAreaPixels.Height, Math.Max(1, Math.Floor(fitted.Height * scale)));
        var center = ownerPixels ?? workAreaPixels;
        return new Rect(
            Math.Clamp(Math.Floor(center.Left + (center.Width - width) / 2), workAreaPixels.Left, workAreaPixels.Right - width),
            Math.Clamp(Math.Floor(center.Top + (center.Height - height) / 2), workAreaPixels.Top, workAreaPixels.Bottom - height),
            width, height);
    }

    // Retain the existing entry point for all callers. SourceInitialized runs after WPF's
    // initial monitor placement, so the HWND has its actual per-monitor DPI at this point.
    internal static void FitStartupWindowToPrimaryWorkArea(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero) throw new InvalidOperationException("Call after SourceInitialized.");
        var minimum = new Size(window.MinWidth, window.MinHeight);
        var desired = new Size(window.Width, window.Height);
        var owner = window.Owner is { } ownerWindow ? new WindowInteropHelper(ownerWindow).Handle : IntPtr.Zero;
        var workArea = GetWorkArea(owner != IntPtr.Zero ? owner : handle);
        Rect? ownerBounds = null;
        if (owner != IntPtr.Zero && !IsIconic(owner))
        {
            if (!GetWindowRect(owner, out var rect)) throw new Win32Exception();
            ownerBounds = rect.ToRect();
        }

        // WPF has already selected the owner's monitor for CenterScreen/CenterOwner.
        // Keep native placement in pixels so negative/nonzero monitor origins remain intact.
        var dpi = GetDpiForWindow(owner != IntPtr.Zero ? owner : handle);
        var bounds = CalculateStartupBounds(desired, minimum, workArea, dpi, ownerBounds);
        ApplyMinimum(workArea, dpi);
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Width = bounds.Width * 96 / dpi;
        window.Height = bounds.Height * 96 / dpi;
        if (!SetWindowPos(handle, IntPtr.Zero, (int)bounds.Left, (int)bounds.Top,
            (int)bounds.Width, (int)bounds.Height, 0x0004 | 0x0010)) // NOZORDER | NOACTIVATE
        {
            throw new Win32Exception();
        }

        // Re-evaluate only minimum constraints when moving or changing DPI. Never pin
        // MaxWidth/MaxHeight to a monitor; WPF/Windows retain their normal resize behavior.
        window.LocationChanged += (_, _) => UpdateMinimum();
        window.DpiChanged += (_, _) => UpdateMinimum();

        void UpdateMinimum() => ApplyMinimum(GetWorkArea(handle), GetDpiForWindow(handle));
        void ApplyMinimum(Rect area, double currentDpi)
        {
            ApplyWorkAreaMinimum(window, minimum,
                new Size(area.Width * 96 / currentDpi, area.Height * 96 / currentDpi));
        }
    }

    internal static void ApplyWorkAreaMinimum(Window window, Size minimum, Size workArea)
    {
        var effective = CalculateEffectiveMinimum(minimum, workArea);
        window.MinWidth = effective.Width;
        window.MinHeight = effective.Height;
    }

    private static Rect GetWorkArea(IntPtr handle)
    {
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(MonitorFromWindow(handle, 2 /* NEAREST */), ref info)) throw new Win32Exception();
        return info.Work.ToRect();
    }

    private static void Validate(Size value, string parameter, bool allowZero = false)
    {
        if (!double.IsFinite(value.Width) || !double.IsFinite(value.Height)
            || (allowZero ? value.Width < 0 || value.Height < 0 : value.Width <= 0 || value.Height <= 0))
        {
            throw new ArgumentOutOfRangeException(parameter);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left, Top, Right, Bottom;
        public readonly Rect ToRect() => new(Left, Top, Right - Left, Bottom - Top);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect Monitor, Work;
        public uint Flags;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr window);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr window, out NativeRect rect);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(IntPtr window);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
}
