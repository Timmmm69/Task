using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Task.Desktop.Security;

namespace Task.Desktop.Work;

// Corporate detail projection only. Personal contacts keep their existing model.
public sealed record DesktopContactDetails(DesktopContact Contact, string? MiddleName);

public interface IDesktopContactDetailsClient
{
    System.Threading.Tasks.Task<DesktopWorkResult<DesktopContactDetails>> GetContactAsync(Guid id, CancellationToken cancellationToken = default);
    System.Threading.Tasks.Task<DesktopWorkResult<DesktopContactDetails>> UpdateContactAsync(DesktopContactDetails details, CancellationToken cancellationToken = default);
}

public sealed partial class DesktopWorkApiClient : IDesktopContactDetailsClient
{
    public async System.Threading.Tasks.Task<DesktopWorkResult<DesktopContactDetails>> GetContactAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var result = await _executor.GetAsync(Uri($"contacts/{id:D}"), Correlation(), cancellationToken).ConfigureAwait(false);
        return ReadContactDetails(result, id);
    }

    public async System.Threading.Tasks.Task<DesktopWorkResult<DesktopContactDetails>> UpdateContactAsync(DesktopContactDetails details, CancellationToken cancellationToken = default)
    {
        if (ValidateContact(details) is { } error) return new DesktopWorkResult<DesktopContactDetails>.ValidationFailure(error);
        var contact = details.Contact;
        var body = new JsonObject
        {
            ["firstName"] = contact.FirstName.Trim(), ["lastName"] = NullIfWhiteSpace(contact.LastName),
            ["middleName"] = NullIfWhiteSpace(details.MiddleName), ["displayName"] = contact.DisplayName.Trim(),
            ["notes"] = contact.Notes, ["status"] = contact.Status
        };
        var result = await SendAsync($"contacts/{contact.Id:D}", System.Net.Http.HttpMethod.Patch, body,
            contact.Version, null, cancellationToken).ConfigureAwait(false);
        var confirmed = ReadContactDetails(result, contact.Id);
        return confirmed is DesktopWorkResult<DesktopContactDetails>.Succeeded saved && saved.Value.Contact.Version <= contact.Version
            ? new DesktopWorkResult<DesktopContactDetails>.MalformedResponse() : confirmed;
    }

    public static string? ValidateContact(DesktopContactDetails details)
    {
        var c = details.Contact;
        return c.Id == Guid.Empty || c.Version is < 1 or > int.MaxValue
            || c.FirstName.Trim().Length is < 1 or > 100 || c.DisplayName.Trim().Length is < 1 or > 300
            || c.LastName?.Length > 100 || details.MiddleName?.Length > 100 || c.Notes?.Length > 20000
            || c.Status is not ("active" or "inactive")
            ? "Проверьте имя (1–100), фамилию и отчество (до 100), отображаемое имя (1–300), заметки (до 20000) и статус." : null;
    }

    private static DesktopWorkResult<DesktopContactDetails> ReadContactDetails(AuthenticatedGetResult result, Guid id)
    {
        if (result is not AuthenticatedGetResult.Response { StatusCode: HttpStatusCode.OK } response)
            return Failure<DesktopContactDetails>(result);
        try
        {
            if (JsonNode.Parse(response.Body) is not { } node || MapContact(node) is not { } contact
                || contact.Id != id || contact.Version > int.MaxValue || response.EntityTag != $"\"v{contact.Version}\""
                || Text(node, "status") is not ("active" or "inactive") || Text(node, "lifecycleState") is null)
                return new DesktopWorkResult<DesktopContactDetails>.MalformedResponse();
            return new DesktopWorkResult<DesktopContactDetails>.Succeeded(new(contact, Text(node, "middleName")), contact.Version);
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or FormatException)
        { return new DesktopWorkResult<DesktopContactDetails>.MalformedResponse(); }
    }
}
