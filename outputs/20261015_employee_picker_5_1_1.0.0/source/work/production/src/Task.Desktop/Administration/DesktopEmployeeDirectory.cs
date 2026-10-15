using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Task.Desktop.Work;

namespace Task.Desktop.Administration;

/// <summary>One bounded page or one account, using the existing User.Read contract.</summary>
public interface IDesktopEmployeeDirectory
{
    System.Threading.Tasks.Task<DesktopWorkResult<IReadOnlyList<DesktopAdminUser>>> SearchUsersAsync(
        string filter, string? cursor = null, CancellationToken cancellationToken = default);
    System.Threading.Tasks.Task<DesktopWorkResult<DesktopAdminUser>> GetUserAsync(
        Guid userAccountId, CancellationToken cancellationToken = default);
}

public sealed partial class DesktopAdministrationApiClient
{
    public async System.Threading.Tasks.Task<DesktopWorkResult<IReadOnlyList<DesktopAdminUser>>> SearchUsersAsync(
        string filter, string? cursor = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        if (filter.Length > 2000) return new DesktopWorkResult<IReadOnlyList<DesktopAdminUser>>.ValidationFailure("Слишком длинный запрос.");
        var path = "users?sort=id&filter=" + Uri.EscapeDataString(filter.Trim());
        if (cursor is not null) path += "&cursor=" + Uri.EscapeDataString(cursor);
        var result = await _executor.GetAsync(new Uri(_root + path), Guid.NewGuid().ToString("D"), cancellationToken).ConfigureAwait(false);
        if (result is not Security.AuthenticatedGetResult.Response { StatusCode: HttpStatusCode.OK } response)
            return EmployeeFailure<IReadOnlyList<DesktopAdminUser>>(result);
        try
        {
            var root = JsonNode.Parse(response.Body) as JsonObject;
            if (root?["items"] is not JsonArray { Count: <= 100 } items
                || !root.ContainsKey("nextCursor") || root["total"] is not JsonValue total
                || !total.TryGetValue<long>(out var count) || count < items.Count)
                return new DesktopWorkResult<IReadOnlyList<DesktopAdminUser>>.MalformedResponse();
            var next = root["nextCursor"]?.GetValue<string>();
            if (next is not null && (string.IsNullOrWhiteSpace(next) || next.Length > 2000 || next == cursor))
                return new DesktopWorkResult<IReadOnlyList<DesktopAdminUser>>.MalformedResponse();
            var users = items.Select(item => item is null ? null : ReadEmployee(item)).ToArray();
            if (users.Any(user => user is null)) return new DesktopWorkResult<IReadOnlyList<DesktopAdminUser>>.MalformedResponse();
            return new DesktopWorkResult<IReadOnlyList<DesktopAdminUser>>.Succeeded(users.Cast<DesktopAdminUser>().DistinctBy(u => u.Id).ToArray())
            { NextCursor = next };
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or FormatException)
        {
            return new DesktopWorkResult<IReadOnlyList<DesktopAdminUser>>.MalformedResponse();
        }
    }

    public async System.Threading.Tasks.Task<DesktopWorkResult<DesktopAdminUser>> GetUserAsync(
        Guid userAccountId, CancellationToken cancellationToken = default)
    {
        if (userAccountId == Guid.Empty) return new DesktopWorkResult<DesktopAdminUser>.NotFound();
        var result = await _executor.GetAsync(new Uri(_root + "users/" + userAccountId.ToString("D")),
            Guid.NewGuid().ToString("D"), cancellationToken).ConfigureAwait(false);
        if (result is not Security.AuthenticatedGetResult.Response { StatusCode: HttpStatusCode.OK } response)
            return EmployeeFailure<DesktopAdminUser>(result);
        try
        {
            var node = JsonNode.Parse(response.Body);
            return node is not null && ReadEmployee(node) is { } user && user.Id == userAccountId
                ? new DesktopWorkResult<DesktopAdminUser>.Succeeded(user)
                : new DesktopWorkResult<DesktopAdminUser>.MalformedResponse();
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or FormatException)
        {
            return new DesktopWorkResult<DesktopAdminUser>.MalformedResponse();
        }
    }

    private static DesktopAdminUser? ReadEmployee(JsonNode node) => MapUser(node) is { } user
        && user.Id != Guid.Empty && !string.IsNullOrWhiteSpace(user.DisplayName) && !string.IsNullOrWhiteSpace(user.Login)
        && user.AccountStatus is "active" or "pending_activation" or "blocked" or "deactivated" ? user : null;

    private static DesktopWorkResult<T> EmployeeFailure<T>(Security.AuthenticatedGetResult result) => result switch
    {
        Security.AuthenticatedGetResult.Response { StatusCode: HttpStatusCode.NotFound } => new DesktopWorkResult<T>.NotFound(),
        Security.AuthenticatedGetResult.Response { StatusCode: HttpStatusCode.Unauthorized } => new DesktopWorkResult<T>.AuthenticationFailure(),
        _ => Failure<T>(result),
    };
}
