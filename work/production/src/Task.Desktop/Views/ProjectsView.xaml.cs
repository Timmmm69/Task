using System.Windows;
using System.Windows.Controls;
using Task.Desktop.Infrastructure;
using Task.Desktop.ViewModels;

namespace Task.Desktop.Views;

public partial class ProjectsView : UserControl
{
    public ProjectsView() => InitializeComponent();
    private async void RelatedTasksExpansionChanged(object sender, RoutedEventArgs args)
    {
        if (!ReferenceEquals(sender, args.OriginalSource) || sender is not Expander { IsLoaded: true } expander) return;
        if (!expander.IsExpanded)
        {
            (DataContext as ProjectsViewModel)?.SetRelatedTasksExpanded(false);
            return;
        }
        // ViewState can restore a collapsed section during the Loaded event.
        if (ViewState.GetStore(expander) is { } store) await store.Ready;
        await Dispatcher.InvokeAsync(() =>
        {
            if (expander.IsLoaded && expander.IsExpanded && DataContext is ProjectsViewModel projects)
                projects.SetRelatedTasksExpanded(true);
        }, System.Windows.Threading.DispatcherPriority.Loaded);
    }
    private void MembersCollapsed(object sender, RoutedEventArgs args)
    {
        if (ReferenceEquals(sender, args.OriginalSource)) (DataContext as ProjectsViewModel)?.CancelMembersSection();
    }
    private async void FilesExpanded(object sender, RoutedEventArgs args)
    {
        if (ReferenceEquals(sender, args.OriginalSource) && DataContext is ProjectsViewModel projects)
            await projects.OpenFilesSectionAsync();
    }
    private void FilesCollapsed(object sender, RoutedEventArgs args)
    {
        if (ReferenceEquals(sender, args.OriginalSource)) (DataContext as ProjectsViewModel)?.CloseFilesSection();
    }
    private async void ContactsExpanded(object sender, RoutedEventArgs args)
    {
        if (ReferenceEquals(sender, args.OriginalSource) && DataContext is ProjectsViewModel projects)
            await projects.OpenContactsSectionAsync();
    }
    private void ContactsCollapsed(object sender, RoutedEventArgs args)
    {
        if (ReferenceEquals(sender, args.OriginalSource)) (DataContext as ProjectsViewModel)?.CloseContactsSection();
    }
    private async void HistoryExpanded(object sender, RoutedEventArgs args)
    {
        if (ReferenceEquals(sender, args.OriginalSource) && DataContext is ProjectsViewModel projects)
            await projects.OpenHistorySectionAsync();
    }
    private void HistoryCollapsed(object sender, RoutedEventArgs args)
    {
        if (ReferenceEquals(sender, args.OriginalSource)) (DataContext as ProjectsViewModel)?.CloseHistorySection();
    }
    private void ProjectsUnloaded(object sender, RoutedEventArgs args)
    {
        if (DataContext is not ProjectsViewModel projects) return;
        projects.SetRelatedTasksExpanded(false);
        projects.CancelDetailSections();
    }
}
