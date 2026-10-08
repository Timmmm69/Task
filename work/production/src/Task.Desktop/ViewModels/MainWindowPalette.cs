namespace Task.Desktop.ViewModels;

public sealed partial class MainWindowViewModel
{
    public CommandPaletteViewModel Palette { get; private set; } = null!;
    public event Action? InboxCaptureRequested;

    private IEnumerable<PaletteCommand> PaletteCommands()
    {
        foreach (var section in Sections)
        {
            var capability = section.Route switch
            {
                "today" or "calendar" => "Calendar.Read",
                "inbox" or "tasks" => "Task.Read",
                "projects" => "Project.Read",
                "catalog" => "FileCatalog.Read",
                "contacts" => "Contact.Read",
                "notifications" => "Notification.ReadOwn",
                "settings" => "Settings.ReadOwn",
                _ => null,
            };
            if (capability is null) continue;
            yield return new(section.Route, section.Route == "catalog" ? "Каталог файлов" : section.Title,
                () => SelectedSection = section, () => WorkHub?.HasCapability(capability) == true);
        }
        yield return new("new-task", "Новая задача", () => NewTaskCommand.Execute(null),
            () => WorkHub is { IsOffline: false } && WorkHub.HasCapability("Task.Create") && NewTaskCommand.CanExecute(null));
        yield return new("capture", "Быстрое добавление во Входящие", () =>
        {
            SelectedSection = Sections.First(s => s.Route == "inbox");
            InboxCaptureRequested?.Invoke();
        }, () => IsConnected && WorkHub is { IsOffline: false } && WorkHub.HasCapability("Task.Create") && Inbox?.CanCaptureFromShell == true);
    }
}
