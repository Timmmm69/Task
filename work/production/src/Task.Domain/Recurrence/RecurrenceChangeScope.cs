namespace Task.Domain.Recurrence;

/// <summary>
/// Explicit scope of a change applied to a recurrence series (SCR-027,
/// BR-042, AC-042). Mirrors the OpenAPI <c>RecurrenceScopedChange.scope</c>
/// enum. The default value zero is deliberately invalid: a caller must always
/// state which occurrences are affected.
/// </summary>
public enum RecurrenceChangeScope
{
    /// <summary>Only the occurrence addressed by the caller (OpenAPI: this_occurrence).</summary>
    ThisOccurrence = 1,

    /// <summary>The addressed occurrence and every later one (OpenAPI: this_and_future).</summary>
    ThisAndFuture = 2,

    /// <summary>Every occurrence of the series (OpenAPI: entire_series).</summary>
    EntireSeries = 3,
}