using System.Collections.ObjectModel;

namespace Task.Desktop.ViewModels;

/// <summary>
/// View model for the main window shell: navigation sections,
/// the selected section and the connection status.
/// </summary>
public sealed class MainWindowViewModel : ViewModelBase
{
    public MainWindowViewModel()
    {
        Sections = new ObservableCollection<NavigationSection>
        {
            new("Сегодня", "Раздел «Сегодня»: сводка задач на текущий день появится после подключения к серверу."),
            new("Входящие", "Раздел «Входящие»: новые и назначенные задачи появятся после подключения к серверу."),
            new("Задачи", "Раздел «Задачи»: список задач появится после подключения к серверу."),
            new("Календарь", "Раздел «Календарь»: календарная сетка появится после подключения к серверу."),
            new("Проекты", "Раздел «Проекты»: список проектов появится после подключения к серверу."),
            new("Поиск", "Раздел «Поиск»: поиск по задачам будет доступен после подключения к серверу."),
            new("Уведомления", "Раздел «Уведомления»: уведомления появятся после подключения к серверу."),
            new("Настройки", "Раздел «Настройки»: параметры приложения появятся после подключения к серверу."),
        };

        SelectedSection = Sections[0];
    }

    /// <summary>Navigation sections shown in the left panel.</summary>
    public ObservableCollection<NavigationSection> Sections { get; }

    /// <summary>Currently selected navigation section.</summary>
    public NavigationSection? SelectedSection
    {
        get => _selectedSection;
        set => SetProperty(ref _selectedSection, value);
    }

    /// <summary>Visible connection status. No network access is implemented yet.</summary>
    public string ConnectionStatus => "Нет подключения — только просмотр";

    private NavigationSection? _selectedSection;
}
