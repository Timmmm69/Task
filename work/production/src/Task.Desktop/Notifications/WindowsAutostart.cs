using Microsoft.Win32;

namespace Task.Desktop.Notifications;

internal static class WindowsAutostart
{
    public static bool Apply(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
            if (enabled)
            {
                var path = Environment.ProcessPath;
                if (path is null || !System.IO.Path.GetFileName(path).Equals("Task.Desktop.exe", StringComparison.OrdinalIgnoreCase)) return false;
                key.SetValue("Task", "\"" + path + "\" --background");
            }
            else key.DeleteValue("Task", false);
            return true;
        }
        catch (Exception e) when (e is UnauthorizedAccessException or System.Security.SecurityException or System.IO.IOException) { return false; }
    }
}
