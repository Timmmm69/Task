using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace Task.Desktop.Controls;

/// <summary>Renders literal, case-insensitive query occurrences without changing the result text.</summary>
public sealed class SearchHighlightTextBlock : TextBlock
{
    public static readonly DependencyProperty HighlightTextProperty = DependencyProperty.Register(
        nameof(HighlightText), typeof(string), typeof(SearchHighlightTextBlock),
        new PropertyMetadata(string.Empty, OnTextChanged));
    public static readonly DependencyProperty QueryProperty = DependencyProperty.Register(
        nameof(Query), typeof(string), typeof(SearchHighlightTextBlock),
        new PropertyMetadata(string.Empty, OnTextChanged));

    public string HighlightText { get => (string)GetValue(HighlightTextProperty); set => SetValue(HighlightTextProperty, value); }
    public string Query { get => (string)GetValue(QueryProperty); set => SetValue(QueryProperty, value); }

    private static void OnTextChanged(DependencyObject target, DependencyPropertyChangedEventArgs _) =>
        ((SearchHighlightTextBlock)target).Render();

    private void Render()
    {
        Inlines.Clear();
        var text = HighlightText ?? string.Empty;
        var query = Query?.Trim() ?? string.Empty;
        if (query.Length == 0) { Inlines.Add(new Run(text)); return; }

        var start = 0;
        while (start < text.Length)
        {
            var hit = text.IndexOf(query, start, StringComparison.OrdinalIgnoreCase);
            if (hit < 0) { Inlines.Add(new Run(text[start..])); break; }
            if (hit > start) Inlines.Add(new Run(text[start..hit]));
            var matched = new Run(text.Substring(hit, query.Length))
            {
                Background = SystemParameters.HighContrast
                    ? SystemColors.HighlightBrush
                    : new SolidColorBrush(Color.FromRgb(217, 240, 248)),
                Foreground = SystemParameters.HighContrast
                    ? SystemColors.HighlightTextBrush
                    : new SolidColorBrush(Color.FromRgb(20, 55, 70))
            };
            Inlines.Add(matched);
            start = hit + query.Length;
        }
    }
}
