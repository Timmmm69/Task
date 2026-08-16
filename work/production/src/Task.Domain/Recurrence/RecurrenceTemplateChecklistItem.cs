namespace Task.Domain.Recurrence;

/// <summary>
/// Immutable item of a checklist inside a recurrence task template.
/// Mirrors the OpenAPI <c>RecurrenceTemplateChecklistItem</c> schema.
/// </summary>
public sealed record RecurrenceTemplateChecklistItem
{
    private RecurrenceTemplateChecklistItem(Guid id, string text, int sortOrder)
    {
        Id = id;
        Text = text;
        SortOrder = sortOrder;
    }

    public Guid Id { get; }

    public string Text { get; }

    public int SortOrder { get; }

    /// <summary>Creates an item; the text is trimmed and must not be empty or longer than 1000 characters.</summary>
    public static RecurrenceTemplateChecklistItem Create(Guid id, string text, int sortOrder)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Checklist item identifier must not be empty.", nameof(id));
        }

        var normalizedText = text?.Trim();
        if (string.IsNullOrEmpty(normalizedText))
        {
            throw new ArgumentException("Checklist item text must not be empty.", nameof(text));
        }

        if (normalizedText.Length > 1000)
        {
            throw new ArgumentException("Checklist item text must not exceed 1000 characters.", nameof(text));
        }

        return new RecurrenceTemplateChecklistItem(id, normalizedText, sortOrder);
    }
}