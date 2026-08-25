namespace Task.Desktop.ViewModels;

/// <summary>
/// Describes a navigation section of the application shell.
/// </summary>
public sealed class NavigationSection
{
    public NavigationSection(
        string route,
        string title,
        string placeholderText,
        string iconKey,
        string supportingText)
    {
        Route = route;
        Title = title;
        PlaceholderText = placeholderText;
        IconKey = iconKey;
        SupportingText = supportingText;
    }

    /// <summary>Canonical shell route of the section.</summary>
    public string Route { get; }

    /// <summary>Display name of the section.</summary>
    public string Title { get; }

    /// <summary>Safe placeholder text shown until the section has real content.</summary>
    public string PlaceholderText { get; }

    /// <summary>Shared resource key for the section's official Fluent icon.</summary>
    public string IconKey { get; }

    /// <summary>Compact factual context shown beside the page title.</summary>
    public string SupportingText { get; }

    public string AutomationId => $"Navigation_{Route}";

    public string NavigationHelpText => $"Открыть раздел «{Title}»";
}
