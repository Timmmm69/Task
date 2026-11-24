using System.Diagnostics;
using System.IO;

namespace Task.Desktop.Work;

public enum FileOpenStatus { Opened, NotFound, AccessDenied, Rejected, Failed }
public sealed record FileOpenResult(FileOpenStatus Status, string Message);

public interface IFileAccessAdapter
{
    FileOpenResult Open(string path);
}

public sealed class WindowsFileAccessAdapter : IFileAccessAdapter
{
    private static readonly HashSet<string> BlockedExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".exe", ".com", ".bat", ".cmd", ".ps1", ".msi", ".scr", ".lnk", ".vbs", ".vbe", ".js", ".jse", ".wsf", ".wsh", ".hta", ".url", ".reg", ".cpl", ".pif", ".psm1", ".msix", ".msp", ".application", ".appref-ms", ".msc", ".chm", ".jar", ".py", ".pyw", ".sh", ".rb", ".pl", ".vb", ".vba", ".dll", ".ocx", ".sys", ".gadget", ".scf", ".sct", ".desktop", ".psd1", ".ps1xml", ".msh", ".msh1", ".msh2", ".msixbundle", ".appx", ".appxbundle" };
    private readonly Action<string> _launch;
    public WindowsFileAccessAdapter(Action<string>? launch = null) => _launch = launch ?? (path => Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }));

    public static bool IsAllowedPath(string? path)
    {
        if (path is null || path.Length is < 3 or > 4096 || path.IndexOfAny(['\r', '\n', '\0', '"', '<', '>', '|', '*', '?']) >= 0
            || !Path.IsPathFullyQualified(path) || path.StartsWith(@"\\.", StringComparison.Ordinal)
            || path.StartsWith(@"\\?", StringComparison.Ordinal) || path.AsSpan(2).Contains(':')) return false;
        if (!(path.Length >= 3 && char.IsAsciiLetter(path[0]) && path[1] == ':' && path[2] == '\\') && !path.StartsWith(@"\\", StringComparison.Ordinal)) return false;
        var parts = path.Split('\\', StringSplitOptions.RemoveEmptyEntries);
        if (path.StartsWith(@"\\", StringComparison.Ordinal) && parts.Length < 2) return false;
        return !BlockedExtensions.Contains(Path.GetExtension(path)) && !parts.Any(p =>
            p.EndsWith(' ') || p.EndsWith('.') && p is not ("." or "..") ||
            System.Text.RegularExpressions.Regex.IsMatch(p, @"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(\.|$)", System.Text.RegularExpressions.RegexOptions.IgnoreCase));
    }


    public FileOpenResult Open(string path) => Access(path, false, true, default);

    internal FileOpenResult CheckFolder(string path, CancellationToken token) => Access(path, true, false, token);

    internal FileOpenResult OpenReference(string path, bool folder, CancellationToken token) => Access(path, folder, true, token);

    private FileOpenResult Access(string path, bool folderOnly, bool launch, CancellationToken token)
    {
        path = path?.Trim() ?? string.Empty;
        if (!IsAllowedPath(path)) return new(FileOpenStatus.Rejected, "Открытие этого пути из каталога запрещено.");
        try
        {
            token.ThrowIfCancellationRequested();
            var attributes = File.GetAttributes(path);
            var isFolder = (attributes & FileAttributes.Directory) != 0;
            if (folderOnly && !isFolder) return new(FileOpenStatus.NotFound, "По связанному пути больше нет папки.");
            FileSystemInfo target = isFolder ? new DirectoryInfo(path) : new FileInfo(path);
            // Reject reparse points, including ancestors, before handing the path to Windows.
            for (FileSystemInfo? current = target; current is not null; current = current is DirectoryInfo dir ? dir.Parent : ((FileInfo)current).Directory)
            {
                token.ThrowIfCancellationRequested();
                if ((current.Attributes & FileAttributes.ReparsePoint) != 0)
                    return new(FileOpenStatus.Rejected, "Ссылки файловой системы из каталога не открываются.");
            }
            if (target is FileInfo file) { using var access = file.Open(FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete); }
            token.ThrowIfCancellationRequested();
            if (launch) _launch(path);
            return new(FileOpenStatus.Opened, !launch ? "" : isFolder ? "Папка открыта в Проводнике." : "Файл передан Windows для открытия.");
        }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException)
        { return new(FileOpenStatus.NotFound, "Файл или папка недоступны на этом компьютере."); }
        catch (UnauthorizedAccessException) { return new(FileOpenStatus.AccessDenied, "Windows запретила доступ к файлу или папке."); }
        catch (Exception e) when (e is IOException or System.ComponentModel.Win32Exception or NotSupportedException or ArgumentException or System.Security.SecurityException)
        { return new(FileOpenStatus.Failed, "Не удалось открыть файл или папку средствами Windows."); }
    }
}
