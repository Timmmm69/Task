using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using Task.Desktop.Security;

namespace Task.Desktop.Work;

public sealed record DesktopInteraction(Guid Id, long Version, Guid CounterpartyObjectId, string InteractionType,
    DateTimeOffset OccurredAt, string Subject, string? Details, string? NextStep, DateTimeOffset? NextStepDueAt,
    IReadOnlyList<Guid> ParticipantObjectIds, string LifecycleState = "active")
{
    public string TypeLabel => InteractionType switch
    { "call" => "Звонок", "meeting" => "Встреча", "email" => "Письмо", "agreement" => "Договорённость", "note" => "Заметка", "next_step" => "Следующий шаг", _ => InteractionType };
    public string DateLabel => OccurredAt.ToLocalTime().ToString("dd.MM.yyyy HH:mm zzz", CultureInfo.InvariantCulture);
    public string? DueLabel => NextStepDueAt?.ToLocalTime().ToString("dd.MM.yyyy HH:mm zzz", CultureInfo.InvariantCulture);
}

public sealed record InteractionCounterparty(Guid Id, string Type, string Name);

public interface IDesktopInteractionsClient
{
    System.Threading.Tasks.Task<DesktopWorkResult<IReadOnlyList<DesktopInteraction>>> GetInteractionsAsync(string? cursor, CancellationToken ct = default);
    System.Threading.Tasks.Task<DesktopWorkResult<DesktopInteraction>> GetInteractionAsync(Guid id, CancellationToken ct = default);
    System.Threading.Tasks.Task<DesktopWorkResult<DesktopInteraction>> SaveInteractionAsync(DesktopInteraction value, string? key, CancellationToken ct = default);
    System.Threading.Tasks.Task<DesktopWorkResult<InteractionCounterparty>> GetInteractionCounterpartyAsync(Guid id, string type, CancellationToken ct = default);
}

public sealed partial class DesktopWorkApiClient : IDesktopInteractionsClient
{
    // The current list contract has no counterparty filter. One page per explicit user action.
    public System.Threading.Tasks.Task<DesktopWorkResult<IReadOnlyList<DesktopInteraction>>> GetInteractionsAsync(string? cursor, CancellationToken ct = default) =>
        GetPageAsync("interactions?limit=50", MapInteraction, ct, cursor);

    public async System.Threading.Tasks.Task<DesktopWorkResult<DesktopInteraction>> GetInteractionAsync(Guid id, CancellationToken ct = default) =>
        ReadInteraction(await _executor.GetAsync(Uri($"interactions/{id:D}"), Correlation(), ct).ConfigureAwait(false), id, false);

    public async System.Threading.Tasks.Task<DesktopWorkResult<InteractionCounterparty>> GetInteractionCounterpartyAsync(Guid id, string type, CancellationToken ct = default)
    {
        if (type == "contact")
        {
            var result = await GetContactAsync(id, ct).ConfigureAwait(false);
            return result switch
            {
                DesktopWorkResult<DesktopContactDetails>.Succeeded { Value.Contact.LifecycleState: "active" } s => new DesktopWorkResult<InteractionCounterparty>.Succeeded(new(id, type, s.Value.Contact.DisplayName)),
                DesktopWorkResult<DesktopContactDetails>.AuthenticationFailure => new DesktopWorkResult<InteractionCounterparty>.AuthenticationFailure(),
                DesktopWorkResult<DesktopContactDetails>.Forbidden => new DesktopWorkResult<InteractionCounterparty>.Forbidden(),
                DesktopWorkResult<DesktopContactDetails>.ServerUnavailable => new DesktopWorkResult<InteractionCounterparty>.ServerUnavailable(),
                _ => new DesktopWorkResult<InteractionCounterparty>.NotFound()
            };
        }
        if (type != "company") return new DesktopWorkResult<InteractionCounterparty>.NotFound();
        var company = await GetCompanyAsync(id, ct).ConfigureAwait(false);
        return company switch
        {
            DesktopWorkResult<DesktopCompany>.Succeeded { Value.LifecycleState: "active" } s => new DesktopWorkResult<InteractionCounterparty>.Succeeded(new(id, type, s.Value.Name)),
            DesktopWorkResult<DesktopCompany>.AuthenticationFailure => new DesktopWorkResult<InteractionCounterparty>.AuthenticationFailure(),
            DesktopWorkResult<DesktopCompany>.Forbidden => new DesktopWorkResult<InteractionCounterparty>.Forbidden(),
            DesktopWorkResult<DesktopCompany>.ServerUnavailable => new DesktopWorkResult<InteractionCounterparty>.ServerUnavailable(),
            _ => new DesktopWorkResult<InteractionCounterparty>.NotFound()
        };
    }

    public static string? ValidateInteraction(DesktopInteraction v) =>
        v.CounterpartyObjectId == Guid.Empty || v.InteractionType is not ("call" or "meeting" or "email" or "agreement" or "note" or "next_step")
        || v.Subject.Trim().Length is < 1 or > 500 || v.Details?.Length > 20000 || v.NextStep?.Length > 5000
        || v.OccurredAt == default || v.ParticipantObjectIds.Count > 500 || v.ParticipantObjectIds.Contains(Guid.Empty)
        || v.ParticipantObjectIds.Distinct().Count() != v.ParticipantObjectIds.Count
        || new[] { v.Subject, v.Details, v.NextStep }.Any(s => s?.Contains('\0') == true)
        ? "Проверьте тип, дату, тему (1–500), детали (до 20000) и следующий шаг (до 5000)." : null;

    public async System.Threading.Tasks.Task<DesktopWorkResult<DesktopInteraction>> SaveInteractionAsync(DesktopInteraction value, string? key, CancellationToken ct = default)
    {
        if (ValidateInteraction(value) is { } error) return new DesktopWorkResult<DesktopInteraction>.ValidationFailure(error);
        var create = value.Id == Guid.Empty;
        if (create ? !Guid.TryParse(key, out _) : value.Version is < 1 or > int.MaxValue)
            return new DesktopWorkResult<DesktopInteraction>.ValidationFailure("Обновите запись или начните создание заново.");
        var body = new JsonObject
        {
            ["interactionType"] = value.InteractionType,
            ["occurredAt"] = Utc(value.OccurredAt),
            ["subject"] = value.Subject.Trim(),
            ["details"] = value.Details,
            ["nextStep"] = value.NextStep,
            ["nextStepDueAt"] = value.NextStepDueAt is { } due ? Utc(due) : null
        };
        if (create)
        {
            body["counterpartyObjectId"] = value.CounterpartyObjectId;
            body["participantObjectIds"] = new JsonArray(value.ParticipantObjectIds.Select(id => (JsonNode?)JsonValue.Create(id)).ToArray());
        }
        // PATCH deliberately omits counterparty and participants, preserving all existing identities and types.
        var result = ReadInteraction(await SendAsync(create ? "interactions" : $"interactions/{value.Id:D}",
            create ? HttpMethod.Post : HttpMethod.Patch, body, create ? null : value.Version, create ? key : null, ct).ConfigureAwait(false),
            create ? null : value.Id, create);
        return result is DesktopWorkResult<DesktopInteraction>.Succeeded saved && saved.Value.Version <= (create ? 0 : value.Version)
            ? new DesktopWorkResult<DesktopInteraction>.MalformedResponse() : result;
    }

    private static string Utc(DateTimeOffset value) => value.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);

    private static DesktopWorkResult<DesktopInteraction> ReadInteraction(AuthenticatedGetResult result, Guid? id, bool create)
    {
        if (result is not AuthenticatedGetResult.Response response || response.StatusCode != (create ? HttpStatusCode.Created : HttpStatusCode.OK))
            return Failure<DesktopInteraction>(result);
        try
        {
            var value = JsonNode.Parse(response.Body) is { } node ? MapInteraction(node) : null;
            return value is not null && (id is null || value.Id == id) && response.EntityTag == $"\"v{value.Version}\""
                ? new DesktopWorkResult<DesktopInteraction>.Succeeded(value, value.Version) : new DesktopWorkResult<DesktopInteraction>.MalformedResponse();
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or FormatException)
        { return new DesktopWorkResult<DesktopInteraction>.MalformedResponse(); }
    }

    private static DesktopInteraction? MapInteraction(JsonNode n)
    {
        if (!Guid.TryParse(Text(n, "id"), out var id) || id == Guid.Empty || n["version"]?.GetValue<long>() is not (> 0 and <= int.MaxValue and var version)
            || !Guid.TryParse(Text(n, "counterpartyObjectId"), out var target) || Date(n, "occurredAt") is not { } at
            || Text(n, "interactionType") is not { } type || Text(n, "subject") is not { } subject
            || n["participantObjectIds"] is not JsonArray participants || participants.Count > 500
            || Text(n, "lifecycleState") is not ("active" or "archived" or "trashed")) return null;
        if (n["nextStepDueAt"] is not null && Date(n, "nextStepDueAt") is null) return null;
        var ids = new List<Guid>();
        foreach (var p in participants)
        { if (p is null || !Guid.TryParse(p.GetValue<string>(), out var participant)) return null; ids.Add(participant); }
        var value = new DesktopInteraction(id, version, target, type, at, subject, Text(n, "details"), Text(n, "nextStep"), Date(n, "nextStepDueAt"), ids, Text(n, "lifecycleState")!);
        return ValidateInteraction(value) is null ? value : null;
    }
}
