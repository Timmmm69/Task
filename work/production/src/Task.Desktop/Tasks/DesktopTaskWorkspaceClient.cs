using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using Task.Desktop.Security;

namespace Task.Desktop.TaskApi;

public sealed record TaskChoice(Guid? Id, string Name);
public sealed record TaskWorkspaceResult(JsonObject? Body, long Version, string? Error)
{
    public bool Succeeded => Error is null && Body is not null;
}

public interface IDesktopTaskWorkspaceClient
{
    System.Threading.Tasks.Task<TaskWorkspaceResult> GetOptionsAsync(string query, CancellationToken token);
    System.Threading.Tasks.Task<TaskWorkspaceResult> GetWorkspaceAsync(Guid id, CancellationToken token);
    System.Threading.Tasks.Task<TaskWorkspaceResult> WriteWorkspaceAsync(Guid id, long version, string path, HttpMethod method, JsonObject body, string key, CancellationToken token);
}

public sealed partial class DesktopTasksApiClient : IDesktopTaskWorkspaceClient
{
    public async System.Threading.Tasks.Task<TaskWorkspaceResult> GetOptionsAsync(string query, CancellationToken token) =>
        ReadWorkspace(await _executor.GetAsync(new Uri($"{_tasksUri}/options?q={Uri.EscapeDataString(query)}"), Guid.NewGuid().ToString("D"), token));

    public async System.Threading.Tasks.Task<TaskWorkspaceResult> GetWorkspaceAsync(Guid id, CancellationToken token) =>
        ReadWorkspace(await _executor.GetAsync(new Uri($"{_tasksUri}/{id:D}/workspace"), Guid.NewGuid().ToString("D"), token));

    public async System.Threading.Tasks.Task<TaskWorkspaceResult> WriteWorkspaceAsync(Guid id, long version, string path, HttpMethod method, JsonObject body, string key, CancellationToken token)
    {
        var uri = path.StartsWith("links", StringComparison.Ordinal)
            ? new Uri($"{_tasksUri.AbsoluteUri[..^5]}objects/{id:D}/{path}") : new Uri($"{_tasksUri}/{id:D}/{path}");
        return ReadWorkspace(await _executor.SendAsync(method, uri, JsonSerializer.SerializeToUtf8Bytes(body),
            Guid.NewGuid().ToString("D"), $"\"v{version}\"", key, token));
    }

    private static TaskWorkspaceResult ReadWorkspace(AuthenticatedGetResult result)
    {
        if (result is AuthenticatedGetResult.Response response)
        {
            if ((int)response.StatusCode is >= 200 and < 300)
            {
                try
                {
                    var version = TryReadEntityTag(response.EntityTag, out var parsed) ? parsed : 0;
                    return new(JsonNode.Parse(response.Body) as JsonObject ?? new(), version, null);
                }
                catch (JsonException) { return new(null, 0, "Сервер вернул некорректные данные."); }
            }
            return new(null, 0, response.StatusCode switch
            {
                HttpStatusCode.PreconditionFailed => "Задача изменена другим пользователем. Обновите карточку перед повтором.",
                HttpStatusCode.Forbidden => "Недостаточно прав для этого действия.",
                HttpStatusCode.NotFound => "Задача или связанный объект больше недоступны.",
                HttpStatusCode.Conflict => "Действие недоступно в текущем состоянии задачи.",
                HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity => "Проверьте данные. Связь должна быть доступна и не создавать цикл.",
                _ => "Сервер недоступен. Введённые данные сохранены; повторите действие позже."
            });
        }
        return new(null, 0, result is AuthenticatedGetResult.AuthenticationFailure ? "Сессия завершена. Войдите снова." : "Нет связи с сервером. Введённые данные сохранены.");
    }
}
