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
            new("today", "Сегодня", "Раздел «Сегодня»: сводка задач на текущий день появится после подключения к серверу."),
            new("inbox", "Входящие", "Раздел «Входящие»: новые и назначенные задачи появятся после подключения к серверу."),
            new("calendar", "Календарь", "Раздел «Календарь»: календарная сетка появится после подключения к серверу."),
            new("tasks", "Задачи", "Раздел «Задачи»: список задач появится после подключения к серверу."),
            new("projects", "Проекты", "Раздел «Проекты»: список проектов появится после подключения к серверу."),
            new("catalog", "Каталог", "Раздел «Каталог»: файлы и записи каталога появятся после подключения к серверу."),
            new("contacts", "Контакты", "Раздел «Контакты»: список контактов появится после подключения к серверу."),
            new("notifications", "Уведомления", "Раздел «Уведомления»: уведомления появятся после подключения к серверу."),
            new("archive", "Архив", "Раздел «Архив»: архивные задачи появится после подключения к серверу."),
            new("trash", "Корзина", "Раздел «Корзина»: удалённые записи появятся после подключения к серверу."),
            new("settings", "Настройки", "Раздел «Настройки»: параметры приложения появятся после подключения к серверу."),
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
