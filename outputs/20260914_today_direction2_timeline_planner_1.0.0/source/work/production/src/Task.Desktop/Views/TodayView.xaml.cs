using System.Windows.Controls;
using System.Windows.Threading;
using Task.Desktop.ViewModels;

namespace Task.Desktop.Views;

public partial class TodayView : UserControl
{
    public TodayView()
    {
        InitializeComponent();
        Loaded += (_, _) => Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            if (DataContext is TodayViewModel viewModel)
                TimelineScrollViewer.ScrollToVerticalOffset(viewModel.InitialTimelineOffset);
        });
    }
}
