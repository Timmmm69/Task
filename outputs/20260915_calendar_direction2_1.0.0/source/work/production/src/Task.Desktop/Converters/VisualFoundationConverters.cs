using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace Task.Desktop.Converters;

/// <summary>Resolves a presentation-only icon key from the shared WPF resource system.</summary>
public sealed class IconKeyToGeometryConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string key || string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        return global::System.Windows.Application.Current?.TryFindResource(key) as Geometry;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Returns the canonical expanded or compact navigation width.</summary>
public sealed class ShellNavigationWidthConverter : IValueConverter
{
    public const double CompactBreakpoint = 1220;
    public const double ExpandedWidth = 212;
    public const double CompactWidth = 178;

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        new GridLength(value is double width && width >= CompactBreakpoint ? ExpandedWidth : CompactWidth);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Compares a window width with a XAML-owned breakpoint.</summary>
public sealed class WindowWidthAtLeastConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not double width
            || parameter is not string text
            || !double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var breakpoint))
        {
            return false;
        }

        return width >= breakpoint;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// Produces a fixed grid column above a breakpoint and a zero-width column below it.
/// Parameter format: <c>breakpoint:visibleWidth</c>.
/// </summary>
public sealed class ResponsiveGridLengthConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not double windowWidth
            || parameter is not string text
            || !TryReadParameter(text, out var breakpoint, out var visibleWidth))
        {
            return new GridLength(0);
        }

        return new GridLength(windowWidth >= breakpoint ? visibleWidth : 0);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    private static bool TryReadParameter(string text, out double breakpoint, out double visibleWidth)
    {
        breakpoint = 0;
        visibleWidth = 0;
        var parts = text.Split(':', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 2
            && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out breakpoint)
            && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out visibleWidth);
    }
}

/// <summary>Places overlapping Today cards into equal-width lanes inside the timeline rail.</summary>
public sealed class TimelineLaneMarginConverter : IMultiValueConverter
{
    private const double LabelRailWidth = 100d;
    private const double RightInset = 14d;
    private const double LaneGap = 6d;

    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Length < 3
            || values[0] is not double canvasWidth
            || values[1] is not int lane
            || values[2] is not int laneCount)
        {
            return new Thickness(LabelRailWidth, 3d, RightInset, 3d);
        }

        laneCount = Math.Max(1, laneCount);
        lane = Math.Clamp(lane, 0, laneCount - 1);
        var usableWidth = Math.Max(0d,
            canvasWidth - LabelRailWidth - RightInset - LaneGap * (laneCount - 1));
        var laneWidth = usableWidth / laneCount;
        var left = LabelRailWidth + lane * (laneWidth + LaneGap);
        var right = RightInset + (laneCount - lane - 1) * (laneWidth + LaneGap);
        return new Thickness(left, 3d, right, 3d);
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Sizes a calendar card so every overlapping interval keeps an equal, readable lane.</summary>
public sealed class CalendarLaneWidthConverter : IMultiValueConverter
{
    private const double EdgeInset = 4d;
    private const double LaneGap = 4d;

    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Length < 2 || values[0] is not double canvasWidth || values[1] is not int laneCount)
            return 0d;
        laneCount = Math.Max(1, laneCount);
        var usableWidth = Math.Max(0d, canvasWidth - EdgeInset * 2d - LaneGap * (laneCount - 1));
        return usableWidth / laneCount;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Places a calendar card in its assigned overlap lane.</summary>
public sealed class CalendarLaneLeftConverter : IMultiValueConverter
{
    private const double EdgeInset = 4d;
    private const double LaneGap = 4d;

    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Length < 3
            || values[0] is not double canvasWidth
            || values[1] is not int lane
            || values[2] is not int laneCount)
            return EdgeInset;
        laneCount = Math.Max(1, laneCount);
        lane = Math.Clamp(lane, 0, laneCount - 1);
        var usableWidth = Math.Max(0d, canvasWidth - EdgeInset * 2d - LaneGap * (laneCount - 1));
        var laneWidth = usableWidth / laneCount;
        return EdgeInset + lane * (laneWidth + LaneGap);
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
