using System.Windows;

namespace Task.Desktop;

/// <summary> keeps initial WPF windows inside the visible primary work area at high DPI. </summary>
internal static class WindowsUxLayout
{
    internal static Size CalculateStartupSize(Size desired, Size minimum, Size workArea)
    {
        Validate(desired, nameof(desired));
        Validate(minimum, nameof(minimum));
        Validate(workArea, nameof(workArea));

        if (minimum.Width > workArea.Width || minimum.Height > workArea.Height)
        {
            throw new ArgumentOutOfRangeException(
                nameof(workArea),
                "The Windows work area is smaller than the supported minimum window size.");
        }

        return new Size(
            Math.Clamp(desired.Width, minimum.Width, workArea.Width),
            Math.Clamp(desired.Height, minimum.Height, workArea.Height));
    }

    internal static void FitStartupWindowToPrimaryWorkArea(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        var workArea = SystemParameters.WorkArea;
        var fitted = CalculateStartupSize(
            new Size(window.Width, window.Height),
            new Size(window.MinWidth, window.MinHeight),
            workArea.Size);

        window.MaxWidth = workArea.Width;
        window.MaxHeight = workArea.Height;
        window.Width = fitted.Width;
        window.Height = fitted.Height;
    }

    private static void Validate(Size value, string parameter)
    {
        if (!double.IsFinite(value.Width) || !double.IsFinite(value.Height)
            || value.Width <= 0 || value.Height <= 0)
        {
            throw new ArgumentOutOfRangeException(parameter);
        }
    }
}
