using Task.Desktop.ViewModels;

namespace Task.Desktop.Personal;

public sealed partial class PersonalShellViewModel
{
    public CommandPaletteViewModel Palette { get; private set; } = null!;
    public event Action? InboxCaptureRequested;
    private IEnumerable<PaletteCommand> PaletteCommands()
    {
        foreach (var section in Sections.Where(s => s.Route is not ("archive" or "trash" or "search")))
            yield return new(section.Route, section.Route == "catalog" ? "Каталог файлов" : section.Title,
                () => SelectedSection = section, () => CanNavigate);
        yield return new("new-task", "Новая задача", () =>
        {
            SelectedSection = Sections.First(s => s.Route == "tasks");
            Tasks?.NewCommand.Execute(null);
        }, () => CanNavigate && Tasks?.NewCommand.CanExecute(null) == true);
        yield return new("capture", "Быстрое добавление во Входящие", () =>
        {
            SelectedSection = Sections.First(s => s.Route == "inbox");
            InboxCaptureRequested?.Invoke();
        }, () => CanNavigate && Tasks is { IsBusy: false });
    }
}
