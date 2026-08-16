namespace Task.Domain.Recurrence;

/// <summary>
/// Calendar cadence of a recurrence rule. Mirrors the OpenAPI
/// <c>RecurrenceSeries.frequency</c> enum (daily, weekly, monthly, yearly).
/// </summary>
public enum RecurrenceFrequency
{
    Daily = 0,
    Weekly = 1,
    Monthly = 2,
    Yearly = 3,
}