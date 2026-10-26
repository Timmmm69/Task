using System.Net;
using System.Net.Http;
using System.Text.Json.Nodes;
using Task.Desktop.Work;

namespace Task.Desktop.Tests.Work;

public sealed partial class DesktopWorkApiClientTests
{
    private static HttpResponseMessage CompanyResponse(Guid id, int version = 7, string? tag = "\"v7\"", JsonArray? links = null)
    {
        var response = Json(HttpStatusCode.OK, new JsonObject
        {
            ["id"] = id,
            ["version"] = version,
            ["name"] = "Компания",
            ["legalName"] = "ООО Компания",
            ["industry"] = "ИТ",
            ["website"] = "https://example.test",
            ["taxIdentifier"] = "123",
            ["notes"] = "Заметки",
            ["status"] = "inactive",
            ["lifecycleState"] = "active",
            ["contacts"] = links ?? new JsonArray()
        }.ToJsonString());
        if (tag is not null) response.Headers.TryAddWithoutValidation("ETag", tag);
        return response;
    }

    [Fact]
    public async System.Threading.Tasks.Task CompanyCreatePatchAndCardUseCanonicalFieldsStrongTagsAndStableKey()
    {
        var id = Guid.NewGuid(); var key = Guid.NewGuid().ToString("D");
        await using var fixture = await Fixture.CreateAsync((request, _) => System.Threading.Tasks.Task.FromResult(
            request.Method == HttpMethod.Patch ? CompanyResponse(id, 8, "\"v8\"") : CompanyResponse(id)));
        var original = Assert.IsType<DesktopWorkResult<DesktopCompany>.Succeeded>(await fixture.Client.GetCompanyAsync(id)).Value;
        Assert.Equal("ООО Компания", original.LegalName); Assert.Equal("inactive", original.Status);
        Assert.IsType<DesktopWorkResult<DesktopCompany>.Succeeded>(await fixture.Client.SaveCompanyAsync(original with { Id = Guid.Empty, Version = 0 }, key));
        var saved = Assert.IsType<DesktopWorkResult<DesktopCompany>.Succeeded>(await fixture.Client.SaveCompanyAsync(original, null));
        Assert.Equal(8, saved.Value.Version);
        Assert.Equal("/api/v1/companies", fixture.Requests[1].Uri.AbsolutePath); Assert.Equal(key, fixture.Requests[1].IdempotencyKey); Assert.Null(fixture.Requests[1].IfMatch);
        Assert.Equal($"/api/v1/companies/{id:D}", fixture.Requests[2].Uri.AbsolutePath); Assert.Equal("\"v7\"", fixture.Requests[2].IfMatch); Assert.Null(fixture.Requests[2].IdempotencyKey);
        var body = JsonNode.Parse(fixture.Requests[2].Body)!.AsObject();
        Assert.Equal(["industry", "legalName", "name", "notes", "status", "taxIdentifier", "website"], body.Select(p => p.Key).Order().ToArray());
        Assert.False(body.ContainsKey("lifecycleState"));
    }

    [Fact]
    public async System.Threading.Tasks.Task CompanyAndContactSearchUseServerQAndOpaqueCursor()
    {
        await using var fixture = await Fixture.CreateAsync((_, _) => System.Threading.Tasks.Task.FromResult(Json(HttpStatusCode.OK, "{\"items\":[],\"hasMore\":true,\"nextCursor\":\"next\"}")));
        var list = Assert.IsType<DesktopWorkResult<IReadOnlyList<DesktopCompany>>.Succeeded>(await fixture.Client.GetCompaniesAsync("А & Б", "opaque+/="));
        Assert.Equal("next", list.NextCursor);
        await fixture.Client.FindCompanyContactsAsync("Анна", null);
        Assert.Contains("q=" + Uri.EscapeDataString("А & Б"), fixture.Requests[0].Uri.Query);
        Assert.Contains("cursor=" + Uri.EscapeDataString("opaque+/="), fixture.Requests[0].Uri.Query);
        Assert.Equal("/api/v1/contacts", fixture.Requests[1].Uri.AbsolutePath);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("W/\"v7\"")]
    [InlineData("\"v8\"")]
    [InlineData("\"v07\"")]
    public async System.Threading.Tasks.Task CompanyCardRejectsMissingWeakAndMismatchedTags(string? tag)
    {
        var id = Guid.NewGuid();
        await using var fixture = await Fixture.CreateAsync((_, _) => System.Threading.Tasks.Task.FromResult(CompanyResponse(id, tag: tag)));
        Assert.IsType<DesktopWorkResult<DesktopCompany>.MalformedResponse>(await fixture.Client.GetCompanyAsync(id));
    }

    [Theory]
    [InlineData(403)]
    [InlineData(404)]
    [InlineData(409)]
    [InlineData(412)]
    [InlineData(422)]
    [InlineData(503)]
    public async System.Threading.Tasks.Task CompanyErrorsNeverBecomeSavedRecords(int code)
    {
        await using var fixture = await Fixture.CreateAsync((_, _) => System.Threading.Tasks.Task.FromResult(Json((HttpStatusCode)code, "{\"title\":\"sensitive submitted value\"}")));
        var result = await fixture.Client.SaveCompanyAsync(new(Guid.NewGuid(), 7, "Компания"), null);
        Assert.True(code switch
        {
            403 => result is DesktopWorkResult<DesktopCompany>.Forbidden,
            404 => result is DesktopWorkResult<DesktopCompany>.NotFound,
            409 or 412 => result is DesktopWorkResult<DesktopCompany>.Conflict,
            422 => result is DesktopWorkResult<DesktopCompany>.ValidationFailure,
            _ => result is DesktopWorkResult<DesktopCompany>.ServerUnavailable
        });
        if (result is DesktopWorkResult<DesktopCompany>.ValidationFailure invalid) Assert.DoesNotContain("sensitive", invalid.Message);
    }

    [Fact]
    public async System.Threading.Tasks.Task LinkAndUnlinkUseParentVersionAndRefreshOnlyCompany()
    {
        var id = Guid.NewGuid(); var contact = Guid.NewGuid(); var version = 7;
        await using var fixture = await Fixture.CreateAsync((request, _) =>
        {
            if (request.Method != HttpMethod.Get) version++;
            return System.Threading.Tasks.Task.FromResult(CompanyResponse(id, version, $"\"v{version}\"", version == 8
                ? new JsonArray(new JsonObject { ["companyId"] = id, ["contactId"] = contact, ["jobTitle"] = "Директор", ["departmentName"] = "ИТ", ["isPrimary"] = true, ["validFrom"] = "2026-01-01", ["validTo"] = "2026-12-31" }) : null));
        });
        var linked = Assert.IsType<DesktopWorkResult<DesktopCompany>.Succeeded>(await fixture.Client.LinkCompanyContactAsync(id, 7,
            new(contact, "Директор", "ИТ", true, "2026-01-01", "2026-12-31"), Guid.NewGuid().ToString("D")));
        Assert.Equal("Директор", Assert.Single(linked.Value.Contacts).JobTitle); Assert.Equal(8, linked.Value.Version);
        var unlinked = Assert.IsType<DesktopWorkResult<DesktopCompany>.Succeeded>(await fixture.Client.UnlinkCompanyContactAsync(id, 8, contact));
        Assert.Empty(unlinked.Value.Contacts); Assert.Equal(9, unlinked.Value.Version);
        Assert.Equal(HttpMethod.Post, fixture.Requests[0].Method); Assert.Equal("\"v7\"", fixture.Requests[0].IfMatch); Assert.NotNull(fixture.Requests[0].IdempotencyKey);
        Assert.Equal(HttpMethod.Delete, fixture.Requests[2].Method); Assert.Null(fixture.Requests[2].IdempotencyKey); Assert.Equal("\"v8\"", fixture.Requests[2].IfMatch);
        Assert.Equal($"/api/v1/companies/{id:D}/contacts/{contact:D}", fixture.Requests[2].Uri.AbsolutePath);
        Assert.All(fixture.Requests, r => Assert.StartsWith($"/api/v1/companies/{id:D}", r.Uri.AbsolutePath));
    }

    [Fact]
    public async System.Threading.Tasks.Task MissingVersionAdvancementAndFailedRefetchAreNotConfirmed()
    {
        var id = Guid.NewGuid();
        await using var fixture = await Fixture.CreateAsync((_, _) => System.Threading.Tasks.Task.FromResult(CompanyResponse(id)));
        Assert.IsType<DesktopWorkResult<DesktopCompany>.MalformedResponse>(await fixture.Client.SaveCompanyAsync(new(id, 7, "Компания"), null));
        Assert.IsType<DesktopWorkResult<DesktopCompany>.MalformedResponse>(await fixture.Client.UnlinkCompanyContactAsync(id, 7, Guid.NewGuid()));
    }

    [Theory]
    [InlineData("ftp://example.test")]
    [InlineData("https://user:password@example.test")]
    [InlineData("example.test")]
    public async System.Threading.Tasks.Task CompanyUnsafeWebsiteNeverReachesNetwork(string website)
    {
        await using var fixture = await Fixture.CreateAsync((_, _) => throw new InvalidOperationException("No request expected"));
        Assert.IsType<DesktopWorkResult<DesktopCompany>.ValidationFailure>(await fixture.Client.SaveCompanyAsync(new(Guid.Empty, 0, "Компания", Website: website), Guid.NewGuid().ToString("D")));
        Assert.Empty(fixture.Requests);
    }

    [Fact]
    public async System.Threading.Tasks.Task CompanySessionExpiryAndOfflineDoNotYieldEditableRecords()
    {
        await using var expired = await Fixture.CreateAsync((_, _) => System.Threading.Tasks.Task.FromResult(Json(HttpStatusCode.Unauthorized, "{}")),
            () => Json(HttpStatusCode.Unauthorized, "{\"code\":\"SESSION_REVOKED\",\"title\":\"Session revoked\"}"));
        Assert.IsType<DesktopWorkResult<DesktopCompany>.AuthenticationFailure>(await expired.Client.GetCompanyAsync(Guid.NewGuid()));
        await using var offline = await Fixture.CreateAsync((_, _) => throw new HttpRequestException("offline"));
        Assert.IsType<DesktopWorkResult<DesktopCompany>.ServerUnavailable>(await offline.Client.GetCompanyAsync(Guid.NewGuid()));
    }
}
