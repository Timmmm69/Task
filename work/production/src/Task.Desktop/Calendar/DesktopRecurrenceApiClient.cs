using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Task.Desktop.Security;

namespace Task.Desktop.Calendar;

public interface IDesktopRecurrenceApiClient
{
    global::System.Threading.Tasks.Task<DesktopCalendarResult<JsonElement>> SendAsync(HttpMethod method, string path,
        string? json, long? version, string? key, CancellationToken cancellationToken);
}

public sealed class DesktopRecurrenceApiClient(HttpClient httpClient, Uri endpoint, SessionService session) : IDesktopRecurrenceApiClient
{
    private readonly DesktopAuthenticatedGetExecutor _executor = new(httpClient, session);
    public async global::System.Threading.Tasks.Task<DesktopCalendarResult<JsonElement>> SendAsync(HttpMethod method,
        string path, string? json, long? version, string? key, CancellationToken cancellationToken)
    {
        var result = await _executor.SendAsync(method, new Uri(endpoint, "api/v1/recurrence-series" + path),
            json is null ? null : Encoding.UTF8.GetBytes(json), Guid.NewGuid().ToString("D"),
            version.HasValue ? $"\"v{version}\"" : null, key, cancellationToken);
        if (result is AuthenticatedGetResult.Response response)
        {
            if (response.StatusCode is HttpStatusCode.OK or HttpStatusCode.Created)
            {
                try { using var document = JsonDocument.Parse(response.Body); return new DesktopCalendarResult<JsonElement>.Succeeded(document.RootElement.Clone()); }
                catch (JsonException) { return new DesktopCalendarResult<JsonElement>.MalformedResponse(); }
            }
            return response.StatusCode switch
            {
                HttpStatusCode.Forbidden => new DesktopCalendarResult<JsonElement>.Forbidden(),
                HttpStatusCode.NotFound => new DesktopCalendarResult<JsonElement>.NotFound(),
                HttpStatusCode.PreconditionFailed => new DesktopCalendarResult<JsonElement>.VersionConflict(),
                HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity or HttpStatusCode.Conflict => new DesktopCalendarResult<JsonElement>.ValidationFailure(ReadMessage(response.Body)),
                _ => new DesktopCalendarResult<JsonElement>.ServerUnavailable(),
            };
        }
        return result is AuthenticatedGetResult.AuthenticationFailure ? new DesktopCalendarResult<JsonElement>.AuthenticationFailure()
            : new DesktopCalendarResult<JsonElement>.ServerUnavailable();
    }
    private static string ReadMessage(string body)
    {
        try { using var document = JsonDocument.Parse(body); return document.RootElement.GetProperty("title").GetString() ?? "Проверьте поля серии."; }
        catch (Exception) { return "Проверьте поля серии."; }
    }
}
