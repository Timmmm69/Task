using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using Task.Desktop.Security;

namespace Task.Desktop.Work;

public sealed record DesktopCompany(Guid Id, long Version, string Name, string? LegalName = null,
    string? Industry = null, string? Website = null, string? TaxIdentifier = null, string? Notes = null,
    string Status = "active", string LifecycleState = "active")
{
    public IReadOnlyList<DesktopCompanyContact> Contacts { get; init; } = [];
}

public sealed record DesktopCompanyContact(Guid ContactId, string? JobTitle = null, string? DepartmentName = null,
    bool IsPrimary = false, string? ValidFrom = null, string? ValidTo = null)
{
    public string? DisplayName { get; init; }
    public string PrimaryLabel => IsPrimary ? "Основной контакт" : "";
}

public interface IDesktopCompaniesClient : IDesktopContactDetailsClient
{
    System.Threading.Tasks.Task<DesktopWorkResult<IReadOnlyList<DesktopCompany>>> GetCompaniesAsync(string query, string? cursor, CancellationToken ct = default);
    System.Threading.Tasks.Task<DesktopWorkResult<DesktopCompany>> GetCompanyAsync(Guid id, CancellationToken ct = default);
    System.Threading.Tasks.Task<DesktopWorkResult<DesktopCompany>> SaveCompanyAsync(DesktopCompany company, string? idempotencyKey, CancellationToken ct = default);
    System.Threading.Tasks.Task<DesktopWorkResult<IReadOnlyList<DesktopContact>>> FindCompanyContactsAsync(string query, string? cursor, CancellationToken ct = default);
    System.Threading.Tasks.Task<DesktopWorkResult<DesktopCompany>> LinkCompanyContactAsync(Guid id, long version, DesktopCompanyContact link, string key, CancellationToken ct = default);
    System.Threading.Tasks.Task<DesktopWorkResult<DesktopCompany>> UnlinkCompanyContactAsync(Guid id, long version, Guid contactId, CancellationToken ct = default);
}

public sealed partial class DesktopWorkApiClient : IDesktopCompaniesClient
{
    public System.Threading.Tasks.Task<DesktopWorkResult<IReadOnlyList<DesktopCompany>>> GetCompaniesAsync(string query, string? cursor, CancellationToken ct = default) =>
        GetPageAsync("companies?limit=50" + CompanyQuery(query), MapCompany, ct, cursor);

    public System.Threading.Tasks.Task<DesktopWorkResult<IReadOnlyList<DesktopContact>>> FindCompanyContactsAsync(string query, string? cursor, CancellationToken ct = default) =>
        GetPageAsync("contacts?limit=50" + CompanyQuery(query), MapContact, ct, cursor);

    private static string CompanyQuery(string query) => string.IsNullOrWhiteSpace(query) ? "" : "&q=" + System.Uri.EscapeDataString(query.Trim());

    public async System.Threading.Tasks.Task<DesktopWorkResult<DesktopCompany>> GetCompanyAsync(Guid id, CancellationToken ct = default) =>
        ReadCompany(await _executor.GetAsync(Uri($"companies/{id:D}"), Correlation(), ct).ConfigureAwait(false), id);

    public static string? ValidateCompany(DesktopCompany c)
    {
        if (c.Name.Trim().Length is < 1 or > 500 || c.LegalName?.Length > 500 || c.Industry?.Length > 200
            || c.Website?.Length > 2048 || c.TaxIdentifier?.Length > 100 || c.Notes?.Length > 20000
            || c.Status is not ("active" or "inactive")
            || new[] { c.Name, c.LegalName, c.Industry, c.Website, c.TaxIdentifier, c.Notes }.Any(s => s?.Contains('\0') == true))
            return "Проверьте название (1–500), юридическое название (до 500), отрасль (до 200), сайт (до 2048), налоговый идентификатор (до 100), заметки (до 20000) и статус.";
        // Same rule as the product API's ValidateContent: absolute HTTP(S), no credentials.
        return !string.IsNullOrWhiteSpace(c.Website) && (!System.Uri.TryCreate(c.Website.Trim(), UriKind.Absolute, out var url)
            || url.Scheme is not ("http" or "https") || url.UserInfo.Length > 0)
            ? "Укажите абсолютный HTTP(S) адрес сайта без логина и пароля." : null;
    }

    public static string? ValidateCompanyContact(DesktopCompanyContact link)
    {
        static bool Date(string? value) => value is null || DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);
        return link.ContactId == Guid.Empty || link.JobTitle?.Length > 200 || link.DepartmentName?.Length > 200
            || link.JobTitle?.Contains('\0') == true || link.DepartmentName?.Contains('\0') == true
            || !Date(link.ValidFrom) || !Date(link.ValidTo) || link.ValidFrom is not null && link.ValidTo is not null && string.CompareOrdinal(link.ValidFrom, link.ValidTo) > 0
            ? "Выберите контакт; должность и подразделение — до 200 символов. Даты: ГГГГ-ММ-ДД, окончание не раньше начала." : null;
    }

    public async System.Threading.Tasks.Task<DesktopWorkResult<DesktopCompany>> SaveCompanyAsync(DesktopCompany company, string? idempotencyKey, CancellationToken ct = default)
    {
        if (ValidateCompany(company) is { } error) return new DesktopWorkResult<DesktopCompany>.ValidationFailure(error);
        var create = company.Id == Guid.Empty;
        if (create ? !Guid.TryParse(idempotencyKey, out _) : company.Version is < 1 or > int.MaxValue)
            return new DesktopWorkResult<DesktopCompany>.ValidationFailure("Обновите карточку или начните создание заново.");
        var body = new JsonObject
        {
            ["name"] = company.Name.Trim(), ["legalName"] = NullIfWhiteSpace(company.LegalName),
            ["industry"] = NullIfWhiteSpace(company.Industry), ["website"] = NullIfWhiteSpace(company.Website),
            ["taxIdentifier"] = NullIfWhiteSpace(company.TaxIdentifier), ["notes"] = company.Notes, ["status"] = company.Status
        };
        var result = ReadCompany(await SendAsync(create ? "companies" : $"companies/{company.Id:D}",
            create ? HttpMethod.Post : HttpMethod.Patch, body, create ? null : company.Version,
            create ? idempotencyKey : null, ct).ConfigureAwait(false), create ? null : company.Id);
        return result is DesktopWorkResult<DesktopCompany>.Succeeded saved && saved.Value.Version <= (create ? 0 : company.Version)
            ? new DesktopWorkResult<DesktopCompany>.MalformedResponse() : result;
    }

    public System.Threading.Tasks.Task<DesktopWorkResult<DesktopCompany>> LinkCompanyContactAsync(Guid id, long version, DesktopCompanyContact link, string key, CancellationToken ct = default)
    {
        if (ValidateCompanyContact(link) is { } error) return System.Threading.Tasks.Task.FromResult<DesktopWorkResult<DesktopCompany>>(new DesktopWorkResult<DesktopCompany>.ValidationFailure(error));
        return ChangeCompanyContactAsync(id, version, "contacts", HttpMethod.Post, new JsonObject
        {
            ["companyId"] = id, ["contactId"] = link.ContactId, ["jobTitle"] = NullIfWhiteSpace(link.JobTitle),
            ["departmentName"] = NullIfWhiteSpace(link.DepartmentName), ["isPrimary"] = link.IsPrimary,
            ["validFrom"] = link.ValidFrom, ["validTo"] = link.ValidTo
        }, key, ct);
    }

    public System.Threading.Tasks.Task<DesktopWorkResult<DesktopCompany>> UnlinkCompanyContactAsync(Guid id, long version, Guid contactId, CancellationToken ct = default) =>
        ChangeCompanyContactAsync(id, version, $"contacts/{contactId:D}", HttpMethod.Delete, new(), null, ct);

    private async System.Threading.Tasks.Task<DesktopWorkResult<DesktopCompany>> ChangeCompanyContactAsync(Guid id, long version, string path, HttpMethod method, JsonObject body, string? key, CancellationToken ct)
    {
        if (id == Guid.Empty || version is < 1 or > int.MaxValue) return new DesktopWorkResult<DesktopCompany>.ValidationFailure("Обновите компанию.");
        var result = await SendAsync($"companies/{id:D}/{path}", method, body, version, key, ct).ConfigureAwait(false);
        if (result is not AuthenticatedGetResult.Response { StatusCode: HttpStatusCode.OK or HttpStatusCode.Created or HttpStatusCode.NoContent } response)
            return CompanyFailure(result);
        var updated = ReadVersion(response.EntityTag);
        if (updated is null || updated <= version || updated > int.MaxValue || response.EntityTag != $"\"v{updated}\"")
            return new DesktopWorkResult<DesktopCompany>.MalformedResponse();
        var confirmed = await GetCompanyAsync(id, ct).ConfigureAwait(false);
        return confirmed is DesktopWorkResult<DesktopCompany>.Succeeded saved && saved.Value.Version < updated
            ? new DesktopWorkResult<DesktopCompany>.MalformedResponse() : confirmed;
    }

    private static DesktopWorkResult<DesktopCompany> CompanyFailure(AuthenticatedGetResult result) => Failure<DesktopCompany>(result) is DesktopWorkResult<DesktopCompany>.ValidationFailure
        ? new DesktopWorkResult<DesktopCompany>.ValidationFailure("Сервер отклонил данные. Проверьте поля, доступность контакта и возможные дубликаты.") : Failure<DesktopCompany>(result);

    private static DesktopWorkResult<DesktopCompany> ReadCompany(AuthenticatedGetResult result, Guid? id)
    {
        if (result is not AuthenticatedGetResult.Response { StatusCode: HttpStatusCode.OK or HttpStatusCode.Created } response) return CompanyFailure(result);
        try
        {
            if (JsonNode.Parse(response.Body) is not { } node || MapCompany(node) is not { } company
                || id is not null && company.Id != id || response.EntityTag != $"\"v{company.Version}\"")
                return new DesktopWorkResult<DesktopCompany>.MalformedResponse();
            var links = node["contacts"] is null ? [] : node["contacts"]!.AsArray().Select(n =>
            {
                if (n is null || !Guid.TryParse(Text(n, "contactId"), out var contactId) || contactId == Guid.Empty
                    || Text(n, "companyId") != company.Id.ToString("D")) throw new FormatException();
                var link = new DesktopCompanyContact(contactId, Text(n, "jobTitle"), Text(n, "departmentName"),
                    n["isPrimary"]?.GetValue<bool>() ?? false, Text(n, "validFrom"), Text(n, "validTo"));
                return ValidateCompanyContact(link) is null ? link : throw new FormatException();
            }).ToArray();
            return new DesktopWorkResult<DesktopCompany>.Succeeded(company with { Contacts = links }, company.Version);
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or FormatException)
        { return new DesktopWorkResult<DesktopCompany>.MalformedResponse(); }
    }

    private static DesktopCompany? MapCompany(JsonNode n) => Guid.TryParse(Text(n, "id"), out var id) && id != Guid.Empty
        && n["version"]?.GetValue<long>() is > 0 and <= int.MaxValue and var version && Text(n, "name") is { Length: > 0 } name
        && Text(n, "status") is "active" or "inactive" && Text(n, "lifecycleState") is { } lifecycle
        ? new(id, version, name, Text(n, "legalName"), Text(n, "industry"), Text(n, "website"), Text(n, "taxIdentifier"), Text(n, "notes"), Text(n, "status")!, lifecycle) : null;
}
