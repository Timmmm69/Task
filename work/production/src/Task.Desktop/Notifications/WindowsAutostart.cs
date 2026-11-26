using Microsoft.Win32;

namespace Task.Desktop.Notifications;

internal static class WindowsAutostart
{
    private const string StartupName = "Варава Дент";

    public static bool Apply(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
            if (enabled)
            {
                var path = Environment.ProcessPath;
                if (path is null || !System.IO.Path.GetFileName(path).Equals("VoravaDent.exe", StringComparison.OrdinalIgnoreCase))
                    return false;
                key.SetValue(StartupName, "\"" + path + "\" --background");
            }
            else key.DeleteValue(StartupName, false);

            if (key.GetValue("Task") is string legacy && legacy.StartsWith('"') &&
                legacy.EndsWith("\\Task.Desktop.exe\" --background", StringComparison.OrdinalIgnoreCase))
                key.DeleteValue("Task", false);
            return true;
        }
        catch (Exception e) when (e is UnauthorizedAccessException or System.Security.SecurityException or System.IO.IOException) { return false; }
    }
}
