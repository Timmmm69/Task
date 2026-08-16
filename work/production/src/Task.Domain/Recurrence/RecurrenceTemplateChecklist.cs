namespace Task.Domain.Recurrence;

/// <summary>
/// Immutable checklist inside a recurrence task template. Mirrors the
/// OpenAPI <c>RecurrenceTemplateChecklist</c> schema; a checklist holds at
/// most 500 items and carries value equality.
/// </summary>
public sealed class RecurrenceTemplateChecklist
{
    private RecurrenceTemplateChecklist(Guid id, string title, int sortOrder, IReadOnlyList<RecurrenceTemplateChecklistItem> items)
    {
        Id = id;
        Title = title;
        SortOrder = sortOrder;
        Items = items;
    }

    public Guid Id { get; }

    public string Title { get; }

    public int SortOrder { get; }

    public IReadOnlyList<RecurrenceTemplateChecklistItem> Items { get; }

    /// <summary>
    /// Creates a checklist; the title is trimmed and must not be empty or
    /// longer than 300 characters, and the item list must not exceed 500 entries.
    /// </summary>
    public static RecurrenceTemplateChecklist Create(
        Guid id,
        string title,
        int sortOrder,
        IReadOnlyList<RecurrenceTemplateChecklistItem>? items)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Checklist identifier must not be empty.", nameof(id));
        }

        var normalizedTitle = title?.Trim();
        if (string.IsNullOrEmpty(normalizedTitle))
        {
            throw new ArgumentException("Checklist title must not be empty.", nameof(title));
        }

        if (normalizedTitle.Length > 300)
        {
            throw new ArgumentException("Checklist title must not exceed 300 characters.", nameof(title));
        }

        var normalizedItems = items ?? [];
        if (normalizedItems.Count > 500)
        {
            throw new ArgumentException("A checklist must not contain more than 500 items.", nameof(items));
        }

        return new RecurrenceTemplateChecklist(id, normalizedTitle, sortOrder, normalizedItems);
    }

    /// <inheritdoc />
    public bool Equals(RecurrenceTemplateChecklist? other) =>
        other is not null &&
        Id == other.Id &&
        Title == other.Title &&
        SortOrder == other.SortOrder &&
        Items.SequenceEqual(other.Items);

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as RecurrenceTemplateChecklist);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Id);
        hash.Add(Title);
        hash.Add(SortOrder);
        foreach (var item in Items)
        {
            hash.Add(item);
        }

        return hash.ToHashCode();
    }

    public static bool operator ==(RecurrenceTemplateChecklist? left, RecurrenceTemplateChecklist? right) =>
        ReferenceEquals(left, right) || (left is not null && left.Equals(right));

    public static bool operator !=(RecurrenceTemplateChecklist? left, RecurrenceTemplateChecklist? right) => !(left == right);
}