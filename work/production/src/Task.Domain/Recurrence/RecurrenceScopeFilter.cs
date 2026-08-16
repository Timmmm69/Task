namespace Task.Domain.Recurrence;

/// <summary>
/// Resolves an explicitly selected change scope to the affected occurrences
/// of a series (SCR-027, BR-042, AC-042). The scope must always be stated:
/// the default enum value is rejected before any selection happens.
/// </summary>
public static class RecurrenceScopeFilter
{
    /// <summary>
    /// Selects the occurrences of <paramref name="occurrences"/> affected by a
    /// change with the given scope, targeting <paramref name="targetKey"/>.
    /// The result is ordered by local date ascending and is deterministic.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="scope"/> is not an explicitly chosen value.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// The target occurrence is not part of the supplied occurrence list.
    /// </exception>
    public static IReadOnlyList<RecurrenceOccurrence> Select(
        RecurrenceChangeScope scope,
        OccurrenceKey targetKey,
        IReadOnlyList<RecurrenceOccurrence> occurrences)
    {
        if (!Enum.IsDefined(scope))
        {
            throw new ArgumentOutOfRangeException(
                nameof(scope),
                "The change scope must be explicitly selected: one occurrence, this and future, or the entire series.");
        }

        ArgumentNullException.ThrowIfNull(targetKey);
        ArgumentNullException.ThrowIfNull(occurrences);

        if (!occurrences.Any(occurrence => occurrence.OccurrenceKey == targetKey))
        {
            throw new ArgumentException(
                "The target occurrence does not belong to the supplied occurrence list.",
                nameof(targetKey));
        }

        return scope switch
        {
            RecurrenceChangeScope.ThisOccurrence => new[]
            {
                occurrences.First(occurrence => occurrence.OccurrenceKey == targetKey),
            },
            RecurrenceChangeScope.ThisAndFuture => occurrences
                .Where(occurrence => occurrence.OccurrenceKey.LocalDate >= targetKey.LocalDate)
                .OrderBy(occurrence => occurrence.OccurrenceKey.LocalDate)
                .ToArray(),
            RecurrenceChangeScope.EntireSeries => occurrences
                .OrderBy(occurrence => occurrence.OccurrenceKey.LocalDate)
                .ToArray(),
            _ => throw new ArgumentOutOfRangeException(nameof(scope)),
        };
    }
}