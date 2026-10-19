using System.Net;
using System.Text.Json;
using Task.Desktop.Security;

namespace Task.Desktop.Projects;

// Client projections of the existing history response; no server contract changes.
public sealed record DesktopProjectHistoryEvent(Guid Id, string Action, long Version,
    Guid? ActorUserId, DateTimeOffset OccurredAt, IReadOnlyList<string> ChangedFields);
public sealed record DesktopProjectHistoryPage(IReadOnlyList<DesktopProjectHistoryEvent> Items);

public interface IDesktopProjectHistoryApiClient
{
    System.Threading.Tasks.Task<DesktopProjectResult<DesktopProjectHistoryPage>> GetHistoryAsync(
        Guid projectId, int page, int limit = 50, CancellationToken cancellationToken = default);
}

public sealed partial class DesktopProjectsApiClient
{
    public async System.Threading.Tasks.Task<DesktopProjectResult<DesktopProjectHistoryPage>> GetHistoryAsync(
        Guid projectId, int page, int limit = 50, CancellationToken cancellationToken = default)
    {
        RequireId(projectId, nameof(projectId));
        if (page is < 1 or > 10000) throw new ArgumentOutOfRangeException(nameof(page));
        if (limit is < 1 or > 200) throw new ArgumentOutOfRangeException(nameof(limit));
        var uri = new Uri($"{_projectsUri.AbsoluteUri}/{projectId:D}/history?limit={limit}&page={page}");
        var result = await _executor.GetAsync(uri, NewCorrelation(), cancellationToken).ConfigureAwait(false);
        if (result is not AuthenticatedGetResult.Response { StatusCode: HttpStatusCode.OK } response)
            return Failure<DesktopProjectHistoryPage>(result);
        try
        {
            var payload = JsonSerializer.Deserialize<HistoryPayload>(response.Body, JsonOptions);
            if (payload?.Items is null || payload.Items.Count > limit || payload.Items.Any(e =>
                e is null || e.Id == Guid.Empty || string.IsNullOrWhiteSpace(e.Action) || e.Version < 1
                || e.ActorUserId == Guid.Empty || e.OccurredAt == default || e.ChangedFields is null
                || e.ChangedFields.Any(string.IsNullOrWhiteSpace)))
                return new DesktopProjectResult<DesktopProjectHistoryPage>.MalformedResponse();
            return new DesktopProjectResult<DesktopProjectHistoryPage>.Succeeded(new(payload.Items.Select(e =>
                new DesktopProjectHistoryEvent(e.Id, e.Action!, e.Version, e.ActorUserId, e.OccurredAt,
                    Array.AsReadOnly(e.ChangedFields!))).ToArray()));
        }
        catch (JsonException) { return new DesktopProjectResult<DesktopProjectHistoryPage>.MalformedResponse(); }
    }

    private sealed record HistoryPayload(List<HistoryEventPayload>? Items);
    private sealed record HistoryEventPayload(Guid Id, string? Action, long Version, Guid? ActorUserId,
        DateTimeOffset OccurredAt, string[]? ChangedFields);
}
