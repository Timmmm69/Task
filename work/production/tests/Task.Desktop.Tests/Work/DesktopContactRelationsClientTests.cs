using System.Net;
using System.Net.Http;
using System.Text.Json.Nodes;
using Task.Desktop.Work;

namespace Task.Desktop.Tests.Work;

public sealed partial class DesktopWorkApiClientTests
{
    private static HttpResponseMessage RelationContact(Guid id, int version = 8)
    {
        var response = ContactResponse(id, version, $"\"v{version}\"");
        var node = JsonNode.Parse($$"""{"id":"{{id}}","version":{{version}},"firstName":"Анна","displayName":"Анна","status":"active","lifecycleState":"active"}""")!;
        node["channels"] = new JsonArray(new JsonObject { ["id"] = ItemId, ["channelType"] = "email", ["value"] = "saved@example.test", ["isPrimary"] = true });
        node["addresses"] = new JsonArray(new JsonObject { ["id"] = ItemId, ["addressType"] = "work", ["formattedAddress"] = "Минск" });
        response.Content = new StringContent(node.ToJsonString());
        return response;
    }

    [Theory]
    [InlineData("phone")]
    [InlineData("email")]
    [InlineData("telegram")]
    [InlineData("whatsapp")]
    [InlineData("viber")]
    [InlineData("other_messenger")]
    [InlineData("website")]
    public async System.Threading.Tasks.Task EveryChannelTypeUsesContractAndReloadsConfirmedCollections(string type)
    {
        var id = Guid.NewGuid();
        await using var fixture = await Fixture.CreateAsync((request, _) => System.Threading.Tasks.Task.FromResult(RelationContact(id)));
        var result = Assert.IsType<DesktopWorkResult<DesktopContactDetails>.Succeeded>(await fixture.Client.SaveChannelAsync(id, 7, new(Guid.Empty, type, "Value", "Метка", true)));
        Assert.Equal("saved@example.test", Assert.Single(result.Value.Channels).Value);
        Assert.Equal("Минск", Assert.Single(result.Value.Addresses).FormattedAddress);
        Assert.Equal(8, result.Value.Contact.Version);
        var post = fixture.Requests[0]; Assert.Equal(HttpMethod.Post, post.Method);
        Assert.Equal($"/api/v1/contacts/{id:D}/channels", post.Uri.AbsolutePath);
        Assert.Equal("\"v7\"", post.IfMatch); Assert.NotNull(post.IdempotencyKey);
        var body = JsonNode.Parse(post.Body)!;
        Assert.Equal(type, body["channelType"]!.GetValue<string>()); Assert.True(body["isPrimary"]!.GetValue<bool>());
        Assert.Equal("Метка", body["label"]!.GetValue<string>()); Assert.Null(body["isVerified"]);
        Assert.Equal(HttpMethod.Get, fixture.Requests[1].Method);
        Assert.Equal($"/api/v1/contacts/{id:D}", fixture.Requests[1].Uri.AbsolutePath);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async System.Threading.Tasks.Task ChannelPatchAndDeleteUseParentVersionAndReload(bool delete)
    {
        var id = Guid.NewGuid(); var child = Guid.NewGuid();
        await using var fixture = await Fixture.CreateAsync((request, _) =>
        {
            if (request.Method != HttpMethod.Delete) return System.Threading.Tasks.Task.FromResult(RelationContact(id));
            var response = new HttpResponseMessage(HttpStatusCode.NoContent); response.Headers.TryAddWithoutValidation("ETag", "\"v8\"");
            return System.Threading.Tasks.Task.FromResult(response);
        });
        var result = delete ? await fixture.Client.DeleteChannelAsync(id, 7, child) : await fixture.Client.SaveChannelAsync(id, 7, new(child, "email", "changed"));
        Assert.IsType<DesktopWorkResult<DesktopContactDetails>.Succeeded>(result);
        var request = fixture.Requests[0]; Assert.Equal(delete ? HttpMethod.Delete : HttpMethod.Patch, request.Method);
        Assert.Equal($"/api/v1/contacts/{id:D}/channels/{child:D}", request.Uri.AbsolutePath);
        Assert.Equal("\"v7\"", request.IfMatch);
        Assert.Equal(delete, request.IdempotencyKey is null);
        if (delete) Assert.Equal("{}", request.Body);
        else Assert.Null(JsonNode.Parse(request.Body)!["label"]);
        Assert.Equal(2, fixture.Requests.Count);
    }

    [Theory]
    [InlineData("work")]
    [InlineData("legal")]
    [InlineData("postal")]
    [InlineData("other")]
    public async System.Threading.Tasks.Task AddressPostUsesExactFieldsAndNullableParts(string type)
    {
        var id = Guid.NewGuid();
        await using var fixture = await Fixture.CreateAsync((_, _) => System.Threading.Tasks.Task.FromResult(RelationContact(id)));
        Assert.IsType<DesktopWorkResult<DesktopContactDetails>.Succeeded>(await fixture.Client.AddAddressAsync(id, 7,
            new(Guid.Empty, type, "Минск", "BY", null, "Минск", "Улица", "220000", true)));
        var request = fixture.Requests[0]; Assert.Equal(HttpMethod.Post, request.Method);
        Assert.EndsWith("/addresses", request.Uri.AbsolutePath); Assert.Equal("\"v7\"", request.IfMatch); Assert.NotNull(request.IdempotencyKey);
        var body = JsonNode.Parse(request.Body)!.AsObject();
        Assert.Equal(["addressType", "city", "countryCode", "formattedAddress", "isPrimary", "postalCode", "region", "street"], body.Select(p => p.Key).Order().ToArray());
        Assert.Equal(type, body["addressType"]!.GetValue<string>()); Assert.Null(body["region"]);
    }

    [Theory]
    [InlineData(403)]
    [InlineData(404)]
    [InlineData(409)]
    [InlineData(412)]
    [InlineData(422)]
    [InlineData(503)]
    public async System.Threading.Tasks.Task RelationErrorsNeverExposeServerTitlesOrTriggerReload(int status)
    {
        await using var fixture = await Fixture.CreateAsync((_, _) => System.Threading.Tasks.Task.FromResult(Json((HttpStatusCode)status, "{\"title\":\"SECRET_CONTACT_VALUE\"}")));
        var results = new[] { await fixture.Client.SaveChannelAsync(Guid.NewGuid(), 7, new(Guid.Empty, "email", "value")),
            await fixture.Client.DeleteChannelAsync(Guid.NewGuid(), 7, ItemId),
            await fixture.Client.AddAddressAsync(Guid.NewGuid(), 7, new(Guid.Empty, "work", "Минск")) };
        foreach (var result in results)
        {
            Assert.True(status switch
            {
                403 => result is DesktopWorkResult<DesktopContactDetails>.Forbidden,
                404 => result is DesktopWorkResult<DesktopContactDetails>.NotFound,
                409 or 412 => result is DesktopWorkResult<DesktopContactDetails>.Conflict,
                422 => result is DesktopWorkResult<DesktopContactDetails>.ValidationFailure,
                _ => result is DesktopWorkResult<DesktopContactDetails>.ServerUnavailable
            });
            Assert.DoesNotContain("SECRET_CONTACT_VALUE", result.ToString());
        }
        Assert.Equal(3, fixture.Requests.Count);
    }

    [Theory]
    [InlineData(403)]
    [InlineData(404)]
    [InlineData(422)]
    [InlineData(503)]
    public async System.Threading.Tasks.Task AcceptedMutationWithFailedReloadNeverConfirmsLocalData(int status)
    {
        var id = Guid.NewGuid();
        await using var fixture = await Fixture.CreateAsync((request, _) => System.Threading.Tasks.Task.FromResult(
            request.Method == HttpMethod.Get ? Json((HttpStatusCode)status, "{}") : RelationContact(id)));
        var result = await fixture.Client.SaveChannelAsync(id, 7, new(Guid.Empty, "phone", "draft"));
        Assert.False(result is DesktopWorkResult<DesktopContactDetails>.Succeeded);
        Assert.False(result is DesktopWorkResult<DesktopContactDetails>.ValidationFailure); // A retry must first reconcile the committed parent.
        Assert.Equal(2, fixture.Requests.Count);
    }

    [Theory]
    [InlineData(null, 8)]
    [InlineData("W/\"v8\"", 8)]
    [InlineData("\"v7\"", 8)]
    [InlineData("\"v08\"", 8)]
    [InlineData("\"v8\"", 7)]
    public async System.Threading.Tasks.Task ChildOperationNeedsStrongAdvancedParentAndFreshReload(string? tag, int readVersion)
    {
        var id = Guid.NewGuid();
        await using var fixture = await Fixture.CreateAsync((request, _) =>
        {
            var response = RelationContact(id, request.Method == HttpMethod.Get ? readVersion : 8);
            if (request.Method != HttpMethod.Get) { response.Headers.Remove("ETag"); if (tag is not null) response.Headers.TryAddWithoutValidation("ETag", tag); }
            return System.Threading.Tasks.Task.FromResult(response);
        });
        Assert.IsType<DesktopWorkResult<DesktopContactDetails>.MalformedResponse>(await fixture.Client.DeleteChannelAsync(id, 7, ItemId));
    }

    [Fact]
    public async System.Threading.Tasks.Task ParentPatchPreservesRelationsWhenServerReturnsUnenrichedContact()
    {
        var id = Guid.NewGuid();
        await using var fixture = await Fixture.CreateAsync((_, _) => System.Threading.Tasks.Task.FromResult(ContactResponse(id, 8, "\"v8\"")));
        var original = new DesktopContactDetails(new(id, 7, "Анна", "Анна", null, null, "active", "active"), null)
        { Channels = [new(ItemId, "email", "saved")], Addresses = [new(ItemId, "work", "Минск")] };
        var saved = Assert.IsType<DesktopWorkResult<DesktopContactDetails>.Succeeded>(await fixture.Client.UpdateContactAsync(original));
        Assert.Equal(original.Channels, saved.Value.Channels); Assert.Equal(original.Addresses, saved.Value.Addresses);
        Assert.Equal(8, saved.Value.Contact.Version);
    }

    [Fact]
    public async System.Threading.Tasks.Task InvalidRelationDraftsNeverReachHttp()
    {
        await using var fixture = await Fixture.CreateAsync((_, _) => throw new InvalidOperationException());
        foreach (var channel in new[] { new DesktopContactChannel(Guid.Empty, "fax", "x"), new(Guid.Empty, "phone", " "), new(Guid.Empty, "phone", "x", new string('x', 101)), new(Guid.Empty, "phone", new string('x', 1001)) })
            Assert.IsType<DesktopWorkResult<DesktopContactDetails>.ValidationFailure>(await fixture.Client.SaveChannelAsync(ItemId, 7, channel));
        foreach (var address in new[] { new DesktopContactAddress(Guid.Empty, "home", "x"), new(Guid.Empty, "work", " "), new(Guid.Empty, "work", "x", "BYN"), new(Guid.Empty, "work", "x", Region: new string('x', 201)), new(Guid.Empty, "work", "x", Street: new string('x', 501)) })
            Assert.IsType<DesktopWorkResult<DesktopContactDetails>.ValidationFailure>(await fixture.Client.AddAddressAsync(ItemId, 7, address));
        Assert.Empty(fixture.Requests);
    }

    [Fact]
    public async System.Threading.Tasks.Task MissingOptionalAddressPartsAndEmptyRelationsAreReadable()
    {
        var id = Guid.NewGuid();
        await using var fixture = await Fixture.CreateAsync((_, _) => System.Threading.Tasks.Task.FromResult(RelationContact(id)));
        var details = Assert.IsType<DesktopWorkResult<DesktopContactDetails>.Succeeded>(await fixture.Client.GetContactAsync(id)).Value;
        Assert.Empty(Assert.Single(details.Addresses).Parts); Assert.Equal("Рабочий", details.Addresses[0].TypeLabel);
        Assert.Null(details.Channels[0].Label); Assert.True(details.Channels[0].IsPrimary);
        Assert.DoesNotContain("saved@example.test", details.Channels[0].ToString());
    }
}
