using System.Windows;
using System.Windows.Controls;
using Task.Desktop.Analytics;

namespace Task.Desktop.Views;

public partial class AnalyticsPrivacyView : UserControl
{
    private bool _refreshing;

    public AnalyticsPrivacyView() => InitializeComponent();

    private static ProductAnalytics? Analytics => (global::System.Windows.Application.Current as App)?.Analytics;

    private async void OnLoaded(object sender, RoutedEventArgs args)
    {
        if (Analytics is not { } analytics) return;
        await analytics.Ready;
        Refresh(analytics);
    }

    private async void OnConsentChanged(object sender, RoutedEventArgs args)
    {
        if (_refreshing || Analytics is not { } analytics) return;
        Consent.IsEnabled = false;
        var applied = await analytics.SetEnabledAsync(Consent.IsChecked == true);
        Refresh(analytics);
        if (!applied)
            Feedback.Text = "Не удалось изменить настройку. Проверьте доступ к локальным данным Task и повторите попытку.";
    }

    private void Refresh(ProductAnalytics analytics)
    {
        _refreshing = true;
        Consent.IsChecked = analytics.Enabled;
        _refreshing = false;
        Consent.IsEnabled = analytics.StorageAvailable && (analytics.Configured || analytics.Enabled);
        Feedback.Text = !analytics.StorageAvailable ? "Настройка недоступна в этом экземпляре Task."
            : !analytics.Configured ? "Отправка статистики не настроена."
            : "";
    }
}
