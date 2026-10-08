using System.Windows;
using System.Windows.Media.Animation;
using Task.Desktop.ViewModels;

namespace Task.Desktop.Views;

/// <summary>Small opacity pulse on the existing status glyph; does not affect layout or focus.</summary>
public static class CompletionFeedbackBehavior
{
    public static readonly DependencyProperty FeedbackProperty = DependencyProperty.RegisterAttached(
        "Feedback", typeof(TaskCompletionFeedback), typeof(CompletionFeedbackBehavior),
        new PropertyMetadata(null, OnFeedbackChanged));
    public static TaskCompletionFeedback? GetFeedback(DependencyObject element) =>
        (TaskCompletionFeedback?)element.GetValue(FeedbackProperty);
    public static void SetFeedback(DependencyObject element, TaskCompletionFeedback? value) => element.SetValue(FeedbackProperty, value);

    private static void OnFeedbackChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        if (sender is not FrameworkElement element) return;
        Reset(element);
        element.Loaded -= OnLoaded;
        element.Unloaded -= OnUnloaded;
        element.DataContextChanged -= OnDataContextChanged;
        if (args.NewValue is null) return;
        element.Loaded += OnLoaded;
        element.Unloaded += OnUnloaded;
        element.DataContextChanged += OnDataContextChanged;
        if (element.IsLoaded) Play(element);
    }

    private static void OnLoaded(object sender, RoutedEventArgs args) => Play((FrameworkElement)sender);
    private static void OnUnloaded(object sender, RoutedEventArgs args) => Reset((FrameworkElement)sender);
    private static void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs args) => Reset((FrameworkElement)sender);

    private static void Play(FrameworkElement element)
    {
        // Recheck the Windows setting at presentation time, including changes during a request.
        if (!SystemParameters.ClientAreaAnimation || SystemParameters.HighContrast || element.DataContext is not TaskItemViewModel row
            || GetFeedback(element)?.TryPlay(row.Id, row.Source.Version) != true) return;
        var animation = new DoubleAnimation(0.55, 1, TaskCompletionFeedback.Duration)
        {
            FillBehavior = FillBehavior.Stop,
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
        animation.Completed += (_, _) => Reset(element);
        element.BeginAnimation(UIElement.OpacityProperty, animation, HandoffBehavior.SnapshotAndReplace);
    }

    private static void Reset(FrameworkElement element) => element.BeginAnimation(UIElement.OpacityProperty, null);
}
