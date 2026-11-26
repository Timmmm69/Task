using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Task.Desktop.Analytics;

namespace Task.Desktop.Views;

public partial class AnalyticsNoticeView : UserControl
{
    public AnalyticsNoticeView() => InitializeComponent();

    private static ProductAnalytics? Analytics => (global::System.Windows.Application.Current as App)?.Analytics;

    private async void OnLoaded(object sender, RoutedEventArgs args)
    {
        if (Analytics is not { } analytics) return;
        await analytics.Ready;
        var window = Window.GetWindow(this);
        if (!IsLoaded || window?.IsVisible != true || !analytics.TryClaimNotice()) return;

        Visibility = Visibility.Visible;
        // Let WPF lay out and render the notice before starting automatic collection.
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
        if (!window.IsVisible)
        {
            analytics.ReleaseNotice();
            return;
        }

        await analytics.EnableAfterNoticeAsync();
    }

    private async void OnDisable(object sender, RoutedEventArgs args)
    {
        if (Analytics is not { } analytics) return;
        DisableButton.IsEnabled = false;
        if (await analytics.SetEnabledAsync(false))
        {
            Visibility = Visibility.Collapsed;
            return;
        }

        DisableButton.IsEnabled = true;
        Feedback.Visibility = Visibility.Visible;
        Feedback.Text = "Не удалось сохранить отключение. Проверьте доступ к локальным данным приложения и повторите попытку.";
    }

    private void OnDismiss(object sender, RoutedEventArgs args) => Visibility = Visibility.Collapsed;
}
