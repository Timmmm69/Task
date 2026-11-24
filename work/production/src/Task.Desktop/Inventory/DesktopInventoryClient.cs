using System.Net;
using System.Net.Http;
using System.Text.Json;
using Task.Application.Inventory;
using Task.Desktop.Security;
using Async = System.Threading.Tasks;

namespace Task.Desktop.Inventory;

internal sealed class InventoryAccessException(string message) : Exception(message);

public interface IDesktopInventoryClient
{
    bool IsCorporate { get; }
    Async.Task<InventorySnapshot> ReadAsync(CancellationToken token);
    Async.Task<InventorySnapshot> ApplyAsync(InventoryCommand command, CancellationToken token);
    Async.Task<InventoryManager[]> ManagersAsync(CancellationToken token);
}

internal sealed class CorporateInventoryClient(HttpClient http, Uri endpoint, SessionService session,
    DesktopConnectivityService connectivity) : IDesktopInventoryClient
{
    private readonly DesktopAuthenticatedGetExecutor _executor = new(http, session, connectivity);
    private readonly Uri _uri = new(endpoint, "/api/v1/material-inventory");
    public bool IsCorporate => true;

    public Async.Task<InventorySnapshot> ReadAsync(CancellationToken token)
        => SendAsync<InventorySnapshot>(HttpMethod.Get, _uri, null, token);

    public Async.Task<InventorySnapshot> ApplyAsync(InventoryCommand command, CancellationToken token)
        => SendAsync<InventorySnapshot>(HttpMethod.Post, _uri, command, token);

    public Async.Task<InventoryManager[]> ManagersAsync(CancellationToken token)
        => SendAsync<InventoryManager[]>(HttpMethod.Get, new(_uri.AbsoluteUri + "/managers"), null, token);

    private async Async.Task<T> SendAsync<T>(HttpMethod method, Uri uri, InventoryCommand? command, CancellationToken token)
        where T : class
    {
        var correlation = Guid.NewGuid().ToString("D");
        var bytes = command is null ? null : JsonSerializer.SerializeToUtf8Bytes(command, InventoryJson.Options);
        var result = await _executor.SendAsync(method, uri, bytes, correlation, null, null, token).ConfigureAwait(false);
        switch (result)
        {
            case AuthenticatedGetResult.Response { StatusCode: HttpStatusCode.OK } ok:
                return JsonSerializer.Deserialize<T>(ok.Body, InventoryJson.Options)
                    ?? throw new InventoryValidationException("Сервер вернул пустой ответ.");
            case AuthenticatedGetResult.Response { StatusCode: HttpStatusCode.Conflict }:
                throw new InventoryConflictException();
            case AuthenticatedGetResult.Response { StatusCode: HttpStatusCode.Forbidden }:
                throw new InventoryAccessException("У вас нет прав на эту операцию склада. Обновите данные.");
            case AuthenticatedGetResult.Response { StatusCode: HttpStatusCode.UnprocessableEntity } invalid:
                using (var document = JsonDocument.Parse(invalid.Body))
                {
                    var message = document.RootElement.TryGetProperty("detail", out var detail) && detail.ValueKind == JsonValueKind.String
                        ? detail.GetString() : null;
                    throw new InventoryValidationException(message ?? "Проверьте данные операции.");
                }
            case AuthenticatedGetResult.AuthenticationFailure:
                throw new InventoryAccessException("Войдите в пространство компании заново.");
            case AuthenticatedGetResult.Response { StatusCode: HttpStatusCode.NotFound }:
                throw new InventoryValidationException("Вкладка требует обновления сервера компании.");
            default:
                throw new InventoryValidationException("Склад недоступен. Обновите данные перед повторной попыткой.");
        }
    }
}
