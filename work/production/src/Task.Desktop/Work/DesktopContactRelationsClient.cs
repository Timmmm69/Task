using System.Net;
using System.Net.Http;
using System.Text.Json.Nodes;
using Task.Desktop.Security;

namespace Task.Desktop.Work;

public sealed record DesktopContactChannel(Guid Id, string ChannelType, string Value, string? Label = null, bool IsPrimary = false)
{
    public string TypeLabel => ContactRelationTypes.Channels.FirstOrDefault(t => t.Value == ChannelType)?.Label ?? "Другой канал";
    public string PrimaryLabel => IsPrimary ? "Основной" : "";
    public override string ToString() => TypeLabel; // Never expose values in diagnostic formatting.
}

public sealed record DesktopContactAddress(Guid Id, string AddressType, string FormattedAddress,
    string? CountryCode = null, string? Region = null, string? City = null, string? Street = null,
    string? PostalCode = null, bool IsPrimary = false)
{
    public string TypeLabel => ContactRelationTypes.Addresses.FirstOrDefault(t => t.Value == AddressType)?.Label ?? "Другой";
    public string Parts => string.Join(", ", new[] { CountryCode, Region, City, Street, PostalCode }.Where(s => !string.IsNullOrWhiteSpace(s)));
    public string PrimaryLabel => IsPrimary ? "Основной" : "";
    public override string ToString() => TypeLabel;
}

public sealed record DesktopContactRelationType(string Value, string Label);

public static class ContactRelationTypes
{
    public static IReadOnlyList<DesktopContactRelationType> Channels { get; } = [new("phone", "Телефон"), new("email", "Электронная почта"),
        new("telegram", "Телеграм"), new("whatsapp", "Ватсап"), new("viber", "Вайбер"), new("other_messenger", "Другой мессенджер"), new("website", "Сайт")];
    public static IReadOnlyList<DesktopContactRelationType> Addresses { get; } = [new("work", "Рабочий"), new("legal", "Юридический"), new("postal", "Почтовый"), new("other", "Другой")];
}

public interface IDesktopContactRelationsClient
{
    System.Threading.Tasks.Task<DesktopWorkResult<DesktopContactDetails>> SaveChannelAsync(Guid contactId, long version, DesktopContactChannel channel, CancellationToken ct = default);
    System.Threading.Tasks.Task<DesktopWorkResult<DesktopContactDetails>> DeleteChannelAsync(Guid contactId, long version, Guid channelId, CancellationToken ct = default);
    System.Threading.Tasks.Task<DesktopWorkResult<DesktopContactDetails>> AddAddressAsync(Guid contactId, long version, DesktopContactAddress address, CancellationToken ct = default);
}

public sealed partial class DesktopWorkApiClient : IDesktopContactRelationsClient
{
    public static string? ValidateChannel(DesktopContactChannel c) =>
        !ContactRelationTypes.Channels.Any(t => t.Value == c.ChannelType) || c.Value.Trim().Length is < 1 or > 1000 || c.Label?.Length > 100
        ? "Выберите тип канала, укажите значение (1–1000 символов) и подпись (до 100)." : null;

    public static string? ValidateAddress(DesktopContactAddress a) =>
        !ContactRelationTypes.Addresses.Any(t => t.Value == a.AddressType) || a.FormattedAddress.Trim().Length is < 1 or > 1000
        || (!string.IsNullOrWhiteSpace(a.CountryCode) && a.CountryCode.Trim().Length != 2) || a.Region?.Length > 200
        || a.City?.Length > 200 || a.Street?.Length > 500 || a.PostalCode?.Length > 40
        ? "Проверьте тип и адрес (1–1000), код страны (2), регион/город (до 200), улицу (до 500), индекс (до 40 символов)." : null;

    public System.Threading.Tasks.Task<DesktopWorkResult<DesktopContactDetails>> SaveChannelAsync(Guid contactId, long version, DesktopContactChannel channel, CancellationToken ct = default)
    {
        if (ValidateChannel(channel) is { } error) return System.Threading.Tasks.Task.FromResult<DesktopWorkResult<DesktopContactDetails>>(new DesktopWorkResult<DesktopContactDetails>.ValidationFailure(error));
        return ChangeRelationAsync(contactId, version, channel.Id == Guid.Empty ? "channels" : $"channels/{channel.Id:D}",
            channel.Id == Guid.Empty ? HttpMethod.Post : HttpMethod.Patch, new JsonObject
            { ["channelType"] = channel.ChannelType, ["value"] = channel.Value.Trim(), ["label"] = NullIfWhiteSpace(channel.Label), ["isPrimary"] = channel.IsPrimary }, ct);
    }

    public System.Threading.Tasks.Task<DesktopWorkResult<DesktopContactDetails>> DeleteChannelAsync(Guid contactId, long version, Guid channelId, CancellationToken ct = default) =>
        ChangeRelationAsync(contactId, version, $"channels/{channelId:D}", HttpMethod.Delete, new JsonObject(), ct);

    public System.Threading.Tasks.Task<DesktopWorkResult<DesktopContactDetails>> AddAddressAsync(Guid contactId, long version, DesktopContactAddress address, CancellationToken ct = default)
    {
        if (ValidateAddress(address) is { } error) return System.Threading.Tasks.Task.FromResult<DesktopWorkResult<DesktopContactDetails>>(new DesktopWorkResult<DesktopContactDetails>.ValidationFailure(error));
        return ChangeRelationAsync(contactId, version, "addresses", HttpMethod.Post, new JsonObject
        {
            ["addressType"] = address.AddressType,
            ["formattedAddress"] = address.FormattedAddress.Trim(),
            ["countryCode"] = NullIfWhiteSpace(address.CountryCode),
            ["region"] = NullIfWhiteSpace(address.Region),
            ["city"] = NullIfWhiteSpace(address.City),
            ["street"] = NullIfWhiteSpace(address.Street),
            ["postalCode"] = NullIfWhiteSpace(address.PostalCode),
            ["isPrimary"] = address.IsPrimary
        }, ct);
    }

    private async System.Threading.Tasks.Task<DesktopWorkResult<DesktopContactDetails>> ChangeRelationAsync(Guid id, long version, string path, HttpMethod method, JsonObject body, CancellationToken ct)
    {
        if (id == Guid.Empty || version is < 1 or > int.MaxValue) return new DesktopWorkResult<DesktopContactDetails>.ValidationFailure("Обновите карточку контакта.");
        var result = await SendAsync($"contacts/{id:D}/{path}", method, body, version, method == HttpMethod.Delete ? null : Key(), ct).ConfigureAwait(false);
        if (result is not AuthenticatedGetResult.Response { StatusCode: HttpStatusCode.OK or HttpStatusCode.Created or HttpStatusCode.NoContent } response)
        {
            var failure = Failure<DesktopContactDetails>(result);
            // Server problem titles may contain submitted values. Keep relation errors local and generic.
            return failure is DesktopWorkResult<DesktopContactDetails>.ValidationFailure
                ? new DesktopWorkResult<DesktopContactDetails>.ValidationFailure("Сервер отклонил данные. Проверьте поля и возможные дубликаты.") : failure;
        }
        var updated = ReadVersion(response.EntityTag);
        if (updated is null || updated <= version || updated > int.MaxValue || response.EntityTag != $"\"v{updated}\"")
            return new DesktopWorkResult<DesktopContactDetails>.MalformedResponse();
        var confirmed = await GetContactAsync(id, ct).ConfigureAwait(false);
        return confirmed is DesktopWorkResult<DesktopContactDetails>.ValidationFailure
            || confirmed is DesktopWorkResult<DesktopContactDetails>.Succeeded saved && saved.Value.Contact.Version < updated
            ? new DesktopWorkResult<DesktopContactDetails>.MalformedResponse() : confirmed;
    }

    private static IReadOnlyList<DesktopContactChannel> ReadChannels(JsonNode? node) => node is null ? [] : node.AsArray().Select(n =>
        new DesktopContactChannel(Guid.Parse(Text(n ?? throw new FormatException(), "id") ?? throw new FormatException()), Text(n!, "channelType") ?? throw new FormatException(),
            Text(n!, "value") ?? throw new FormatException(), Text(n!, "label"), n!["isPrimary"]?.GetValue<bool>() ?? false)).ToArray();

    private static IReadOnlyList<DesktopContactAddress> ReadAddresses(JsonNode? node) => node is null ? [] : node.AsArray().Select(n =>
        new DesktopContactAddress(Guid.Parse(Text(n ?? throw new FormatException(), "id") ?? throw new FormatException()), Text(n!, "addressType") ?? throw new FormatException(),
            Text(n!, "formattedAddress") ?? throw new FormatException(), Text(n!, "countryCode"), Text(n!, "region"),
            Text(n!, "city"), Text(n!, "street"), Text(n!, "postalCode"), n!["isPrimary"]?.GetValue<bool>() ?? false)).ToArray();
}
