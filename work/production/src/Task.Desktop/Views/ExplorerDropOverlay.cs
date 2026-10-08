using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace Task.Desktop.Views;

/// <summary>Non hit-testable feedback: does not change layout or trigger DragLeave.</summary>
internal sealed class ExplorerDropOverlay(UIElement target) : Adorner(target)
{
    public bool Accepted { get; set; }
    public string Message { get; set; } = "";
    protected override void OnRender(DrawingContext context)
    {
        var brush = Accepted ? Brushes.DodgerBlue : Brushes.Firebrick;
        context.DrawRectangle(null, new Pen(brush, 3), new Rect(AdornedElement.RenderSize));
        if (Message.Length == 0) return;
        var text = new FormattedText(Message, System.Globalization.CultureInfo.GetCultureInfo("ru-RU"),
            FlowDirection.LeftToRight, new Typeface("Segoe UI"), 14, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip)
            { MaxTextWidth = Math.Max(40, AdornedElement.RenderSize.Width - 32) };
        context.DrawRectangle(Brushes.White, new Pen(brush, 1), new Rect(8, 8, text.Width + 16, text.Height + 16));
        context.DrawText(text, new Point(16, 16));
    }
}
