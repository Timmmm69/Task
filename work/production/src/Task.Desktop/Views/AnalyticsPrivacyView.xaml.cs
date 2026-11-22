using System.Windows;
using System.Windows.Controls;
using Task.Desktop.Analytics;

namespace Task.Desktop.Views;

public partial class AnalyticsPrivacyView : UserControl
{
    private bool _refreshing;
    private ProductAnalytics? _observed;

    public AnalyticsPrivacyView() => InitializeComponent();

    private static ProductAnalytics? Analytics => (global::System.Windows.Application.Current as App)?.Analytics;

    private async void OnLoaded(object sender, RoutedEventArgs args)
    {
        if (Analytics is not { } analytics) return;
        _observed = analytics;
        analytics.PreferenceChanged += OnPreferenceChanged;
        await analytics.Ready;
        if (IsLoaded) Refresh(analytics);
    }

    private void OnUnloaded(object sender, RoutedEventArgs args)
    {
        if (_observed is not null) _observed.PreferenceChanged -= OnPreferenceChanged;
        _observed = null;
    }

    private void OnPreferenceChanged() => Dispatcher.InvokeAsync(() =>
    {
        if (IsLoaded && _observed is { } analytics) Refresh(analytics);
    });

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
