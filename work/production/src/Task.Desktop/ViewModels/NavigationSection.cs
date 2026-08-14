namespace Task.Desktop.ViewModels;

/// <summary>
/// Describes a navigation section of the application shell.
/// </summary>
public sealed class NavigationSection
{
    public NavigationSection(string route, string title, string placeholderText)
    {
        Route = route;
        Title = title;
        PlaceholderText = placeholderText;
    }

    /// <summary>Canonical shell route of the section.</summary>
    public string Route { get; }

    /// <summary>Display name of the section.</summary>
    public string Title { get; }

    /// <summary>Safe placeholder text shown until the section has real content.</summary>
    public string PlaceholderText { get; }
}
