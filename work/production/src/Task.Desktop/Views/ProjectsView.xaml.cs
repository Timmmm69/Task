using System.Windows;
using System.Windows.Controls;
using Task.Desktop.ViewModels;

namespace Task.Desktop.Views;

public partial class ProjectsView : UserControl
{
    public ProjectsView() => InitializeComponent();
    private void RelatedTasksCollapsed(object sender, RoutedEventArgs args)
    {
        if (ReferenceEquals(sender, args.OriginalSource)) (DataContext as ProjectsViewModel)?.CancelRelatedTasks();
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
    private void ProjectsUnloaded(object sender, RoutedEventArgs args) => (DataContext as ProjectsViewModel)?.CancelDetailSections();
}
