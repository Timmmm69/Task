using Task.Application.ProductData;

namespace Task.Tests;

public sealed partial class PostgresProductApiTests
{
    [Fact]
    public void ProjectHistory_CurrentContractPagingVisibilityAndPermissions()
    {
        using var db = Database.Create(); Assert.NotNull(db);
        var project = db.Call("projects", "create", $$"""{"name":"History project","ownerUserId":"{{db.User}}"}""");
        var id = Id(project);
        db.Call("projects", "patch", "{\"name\":\"Renamed\"}", id, 1);
        var first = db.Call("projects", "history", id: id, query: new() { ["limit"] = "1", ["page"] = "1" });
        var second = db.Call("projects", "history", id: id, query: new() { ["limit"] = "1", ["page"] = "2" });
        var row = Assert.Single(first.Body!["items"]!.AsArray())!;
        Assert.NotEqual(row["id"]!.ToString(), Assert.Single(second.Body!["items"]!.AsArray())!["id"]!.ToString());
        Assert.Equal(2, row["version"]!.GetValue<int>()); Assert.Equal(db.User.ToString(), row["actorUserId"]!.ToString());
        Assert.Equal("project.patch", row["action"]!.ToString()); Assert.NotNull(row["occurredAt"]);
        Assert.Contains(row["changedFields"]!.AsArray(), f => f!.ToString() == "name");
        Assert.Null(row["oldValues"]); Assert.Null(row["newValues"]);
        Assert.Empty(db.Call("projects", "history", id: id, query: new() { ["limit"] = "1", ["page"] = "3" }).Body!["items"]!.AsArray());
        Assert.Equal(404, Assert.Throws<ProductApiException>(() => db.Call("projects", "history", id: id, user: db.OtherUser, admin: false)).Status);
        Assert.Equal(422, Assert.Throws<ProductApiException>(() => db.Call("projects", "history", id: id, query: new() { ["page"] = "0" })).Status);
    }

    [Fact]
    public void ProjectComments_RuntimeContractIsMissingDespiteGenericNormativeCommentModel()
    {
        Assert.Contains(ProductApiRoutes.All, r => r.Path == "/api/v1/tasks/{id}/comments" && r.Method == "POST");
        Assert.DoesNotContain(ProductApiRoutes.All, r => r.Path.Contains("comments") && r.Resource != "tasks");
    }
}
