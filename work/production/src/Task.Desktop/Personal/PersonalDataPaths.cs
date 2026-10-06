using System.IO;

namespace Task.Desktop.Personal;

public sealed class PersonalDataPaths
{
    public PersonalDataPaths(string desktopDataRoot)
    {
        if (!Path.IsPathFullyQualified(desktopDataRoot)) throw new ArgumentException("Desktop data root must be absolute.");
        DirectoryPath = Path.Combine(Path.GetFullPath(desktopDataRoot), "Personal");
        DatabasePath = Path.Combine(DirectoryPath, "tasks.db");
    }
    public string DirectoryPath { get; }
    public string DatabasePath { get; }
}
