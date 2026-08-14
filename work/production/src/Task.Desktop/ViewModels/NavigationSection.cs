namespace Task.Desktop.ViewModels;

/// <summary>
/// Describes a navigation section of the application shell.
/// </summary>
public sealed class NavigationSection
{
    public NavigationSection(string title, string placeholderText)
    {
        Title = title;
        PlaceholderText = placeholderText;
    }

    /// <summary>Display name of the section.</summary>
    public string Title { get; }

    /// <summary>Safe placeholder text shown until the section has real content.</summary>
    public string PlaceholderText { get; }
}
