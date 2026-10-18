using System.Net;
using System.Net.Http;
using System.Text.Json.Nodes;
using Task.Desktop.Work;

namespace Task.Desktop.Tests.Work;

public sealed partial class DesktopWorkApiClientTests
{
    private static HttpResponseMessage ContactResponse(Guid id, int version = 7, string? tag = "\"v7\"")
    {
        var response = Json(HttpStatusCode.OK, $$"""{"id":"{{id:D}}","version":{{version}},"firstName":"Анна","lastName":"Иванова","middleName":"Петровна","displayName":"Анна Иванова","notes":"Заметки","status":"inactive","lifecycleState":"active"}""");
        if (tag is not null) response.Headers.TryAddWithoutValidation("ETag", tag);
        return response;
    }

    [Fact]
    public async System.Threading.Tasks.Task ContactDetailAndPatchUseExistingContractAndStrongIfMatch()
    {
        var id = Guid.NewGuid();
        await using var fixture = await Fixture.CreateAsync((request, _) => System.Threading.Tasks.Task.FromResult(
            ContactResponse(id, request.Method == HttpMethod.Get ? 7 : 8, request.Method == HttpMethod.Get ? "\"v7\"" : "\"v8\"")));
        var details = Assert.IsType<DesktopWorkResult<DesktopContactDetails>.Succeeded>(await fixture.Client.GetContactAsync(id)).Value;
        Assert.Equal("Петровна", details.MiddleName);
        var saved = Assert.IsType<DesktopWorkResult<DesktopContactDetails>.Succeeded>(await fixture.Client.UpdateContactAsync(details));
        Assert.Equal(8, saved.Value.Contact.Version);
        Assert.All(fixture.Requests, r => Assert.Equal($"/api/v1/contacts/{id:D}", r.Uri.AbsolutePath));
        var patch = fixture.Requests[1]; Assert.Equal(HttpMethod.Patch, patch.Method); Assert.Equal("\"v7\"", patch.IfMatch);
        Assert.Null(patch.IdempotencyKey);
        var body = JsonNode.Parse(patch.Body)!.AsObject();
        Assert.Equal(["displayName", "firstName", "lastName", "middleName", "notes", "status"], body.Select(p => p.Key).Order().ToArray());
        Assert.Equal("inactive", body["status"]!.GetValue<string>());
    }

    [Theory]
    [InlineData(null)][InlineData("W/\"v7\"")][InlineData("\"v6\"")][InlineData("\"v07\"")]
    public async System.Threading.Tasks.Task MissingWeakOrMismatchedVersionCannotBecomeEditableCard(string? tag)
    {
        var id = Guid.NewGuid();
        await using var fixture = await Fixture.CreateAsync((_, _) => System.Threading.Tasks.Task.FromResult(ContactResponse(id, tag: tag)));
        Assert.IsType<DesktopWorkResult<DesktopContactDetails>.MalformedResponse>(await fixture.Client.GetContactAsync(id));
    }

    [Theory]
    [InlineData(403)][InlineData(404)][InlineData(412)][InlineData(422)][InlineData(503)]
    public async System.Threading.Tasks.Task ContactPatchMapsServerErrors(int code)
    {
        var id = Guid.NewGuid();
        await using var fixture = await Fixture.CreateAsync((_, _) => System.Threading.Tasks.Task.FromResult(Json((HttpStatusCode)code, "{\"title\":\"Проверьте данные\"}")));
        var result = await fixture.Client.UpdateContactAsync(new(new(id, 7, "Анна", "Анна", null, null, "active", "active"), null));
        Assert.True(code switch
        {
            403 => result is DesktopWorkResult<DesktopContactDetails>.Forbidden,
            404 => result is DesktopWorkResult<DesktopContactDetails>.NotFound,
            412 => result is DesktopWorkResult<DesktopContactDetails>.Conflict,
            422 => result is DesktopWorkResult<DesktopContactDetails>.ValidationFailure,
            _ => result is DesktopWorkResult<DesktopContactDetails>.ServerUnavailable
        });
    }

    [Fact]
    public async System.Threading.Tasks.Task ExpiredSessionAndUnavailableReadNeverYieldEditableDetails()
    {
        var id = Guid.NewGuid();
        await using var expired = await Fixture.CreateAsync((_, _) => System.Threading.Tasks.Task.FromResult(Json(HttpStatusCode.Unauthorized, "{}")),
            () => Json(HttpStatusCode.Unauthorized, "{\"code\":\"SESSION_REVOKED\",\"title\":\"Session revoked\"}"));
        Assert.IsType<DesktopWorkResult<DesktopContactDetails>.AuthenticationFailure>(await expired.Client.GetContactAsync(id));
        await using var offline = await Fixture.CreateAsync((_, _) => throw new HttpRequestException("offline"));
        Assert.IsType<DesktopWorkResult<DesktopContactDetails>.ServerUnavailable>(await offline.Client.GetContactAsync(id));
    }

    [Fact]
    public async System.Threading.Tasks.Task PatchCannotConfirmAnUnchangedVersion()
    {
        var id = Guid.NewGuid();
        await using var fixture = await Fixture.CreateAsync((_, _) => System.Threading.Tasks.Task.FromResult(ContactResponse(id)));
        Assert.IsType<DesktopWorkResult<DesktopContactDetails>.MalformedResponse>(await fixture.Client.UpdateContactAsync(
            new(new(id, 7, "Анна", "Анна", null, null, "active", "active"), null)));
    }
}
