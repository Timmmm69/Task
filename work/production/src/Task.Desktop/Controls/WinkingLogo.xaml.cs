using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;

namespace Task.Desktop.Controls;

public partial class WinkingLogo : UserControl
{
    private long _first;
    private int _clicks;

    public WinkingLogo() => InitializeComponent();

    private void OnPress(object sender, MouseButtonEventArgs e) => RegisterPress(Stopwatch.GetTimestamp(), SystemParameters.ClientAreaAnimation);

    internal void RegisterPress(long timestamp, bool animate)
    {
        if (_clicks == 0 || Stopwatch.GetElapsedTime(_first, timestamp) > TimeSpan.FromMilliseconds(650))
        { _first = timestamp; _clicks = 0; }
        if (++_clicks != 3) return;
        _clicks = 0;
        if (!animate) return;
        var wink = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(420), FillBehavior = FillBehavior.Stop };
        wink.KeyFrames.Add(new LinearDoubleKeyFrame(1, TimeSpan.FromMilliseconds(65)));
        wink.KeyFrames.Add(new LinearDoubleKeyFrame(1, TimeSpan.FromMilliseconds(300)));
        wink.KeyFrames.Add(new LinearDoubleKeyFrame(0, TimeSpan.FromMilliseconds(420)));
        Face.BeginAnimation(OpacityProperty, wink);
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _clicks = 0;
        Face.BeginAnimation(OpacityProperty, null);
    }
}
