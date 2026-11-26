using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using Task.Desktop.ViewModels;

namespace Task.Desktop.Views;

public partial class WorkdayFooter : UserControl
{
    private CancellationTokenSource? _reaction;

    public WorkdayFooter() => InitializeComponent();

    private async void OnVisible(object sender, RoutedEventArgs e)
    {
        if (DataContext is not WorkdayViewModel model) return;
        model.PropertyChanged -= OnWorkdayChanged;
        model.PropertyChanged += OnWorkdayChanged;
        if (IsVisible) await model.TickAsync();
    }

    private void OnVisibilityChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (IsVisible && IsLoaded) OnVisible(this, new());
        else if (!IsVisible) CloseCards();
    }

    private void OnContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is WorkdayViewModel previous) previous.PropertyChanged -= OnWorkdayChanged;
        if (IsLoaded && IsVisible) OnVisible(this, new());
    }

    private void OnWorkdayChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(WorkdayViewModel.DayHeading) or nameof(WorkdayViewModel.WeekHeading)
            || e.PropertyName == nameof(WorkdayViewModel.IsReady) && sender is WorkdayViewModel { IsReady: false }) CloseCards();
    }

    private async void OnDayOpened(object sender, RoutedEventArgs e)
    {
        if (DataContext is WorkdayViewModel model) await model.OpenDayAsync();
    }

    private async void OnWeekOpened(object sender, RoutedEventArgs e)
    {
        if (DataContext is WorkdayViewModel model) await model.OpenWeekAsync();
    }

    private void OnCheer(object sender, RoutedEventArgs e) => PlayReaction(SystemParameters.ClientAreaAnimation);

    internal async void PlayReaction(bool animate)
    {
        StopReaction();
        using var lifetime = _reaction = new();
        if (animate)
        {
            for (var i = 0; i < 9; i++)
            {
                var dot = new Ellipse { Width = 4, Height = 4, Fill = i % 2 == 0 ? Brushes.DeepSkyBlue : Brushes.MediumSeaGreen };
                Confetti.Children.Add(dot);
                Canvas.SetLeft(dot, 6 + 10 * i);
                var fall = new DoubleAnimation(-5, 28, TimeSpan.FromMilliseconds(650 + i * 36))
                { BeginTime = TimeSpan.FromMilliseconds(i % 3 * 60) };
                dot.BeginAnimation(Canvas.TopProperty, fall);
            }
        }
        else Cheer.SetResourceReference(BorderBrushProperty, SystemColors.HighlightBrushKey);
        try { await System.Threading.Tasks.Task.Delay(1000, lifetime.Token); }
        catch (OperationCanceledException) { return; }
        if (ReferenceEquals(_reaction, lifetime)) StopReaction();
    }

    private void StopReaction()
    {
        _reaction?.Cancel(); _reaction = null;
        Confetti.Children.Clear();
        Cheer.ClearValue(BorderBrushProperty);
    }

    private void CloseCards()
    {
        if (DayCard is null || WeekCard is null || Confetti is null) return;
        StopReaction();
        DayCard.IsExpanded = WeekCard.IsExpanded = false;
        (DataContext as WorkdayViewModel)?.ClearCards();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is WorkdayViewModel model) model.PropertyChanged -= OnWorkdayChanged;
        CloseCards();
    }
}
