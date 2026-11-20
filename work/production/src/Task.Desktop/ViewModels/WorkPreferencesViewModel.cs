using Task.Desktop.Infrastructure;

namespace Task.Desktop.ViewModels;

public sealed class WorkPreferencesViewModel : ViewModelBase, IDisposable
{
    private readonly ViewStateStore _store;
    private readonly IEnumerable<NavigationSection> _sections;
    private readonly Func<NavigationSection, bool> _canOpen;
    private bool _ready;
    private bool _dismissed;

    internal WorkPreferencesViewModel(ViewStateStore store, IEnumerable<NavigationSection> sections,
        Func<NavigationSection, bool> canOpen)
    {
        _store = store;
        _sections = sections;
        _canOpen = canOpen;
        DismissTodayHelpCommand = new AsyncCommand((_, _) =>
        {
            _dismissed = true;
            _store.SetExpanded(ViewStateKeys.TodayHelpDismissed, true);
            OnPropertyChanged(nameof(ShowTodayHelp));
            return System.Threading.Tasks.Task.CompletedTask;
        }, _ => ShowTodayHelp);
    }

    public IReadOnlyList<NavigationSection> StartupSections => _sections
        .Where(section => ViewStateKeys.StartupRoutes.Contains(section.Route) && _canOpen(section)).ToArray();

    public NavigationSection? StartupSection
    {
        get => StartupSections.FirstOrDefault(section => section.Route == _store.StartupRoute)
            ?? StartupSections.FirstOrDefault();
        set
        {
            if (!_ready || value is null || !StartupSections.Contains(value)) return;
            _store.SetStartupRoute(value.Route);
            OnPropertyChanged();
        }
    }

    public bool IsReady => _ready;
    public bool ShowTodayHelp => _ready && !_dismissed;
    public AsyncCommand DismissTodayHelpCommand { get; }

    internal void Restore(CalendarViewModel? calendar)
    {
        _dismissed = _store.Expanded(ViewStateKeys.TodayHelpDismissed) == true;
        calendar?.RestoreViewPreference(_store);
        _ready = true;
        RefreshStartupSections();
        OnPropertyChanged(nameof(IsReady));
        OnPropertyChanged(nameof(ShowTodayHelp));
        DismissTodayHelpCommand.RaiseCanExecuteChanged();
    }

    internal void RefreshStartupSections()
    {
        OnPropertyChanged(nameof(StartupSections));
        OnPropertyChanged(nameof(StartupSection));
    }

    public void Dispose() => DismissTodayHelpCommand.Dispose();
}
