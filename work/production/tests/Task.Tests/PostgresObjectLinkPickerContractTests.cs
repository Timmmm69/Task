using System.Text.Json.Nodes;
using Task.Application.ProductData;
using Task.Domain;

namespace Task.Tests;

public sealed partial class PostgresProductApiTests
{
    [RequiresPostgresFact]
    public void ObjectLinks_RecheckLifecycleVersionsAndIndependentMutationPermissions()
    {
        using var db = Database.Create()!;
        var source = Id(db.Call("contacts", "create", "{\"firstName\":\"Picker\",\"displayName\":\"Picker source\"}"));
        var target = Id(db.Call("catalog-items", "create", "{\"name\":\"Picker target\",\"itemType\":\"file_reference\"}"));
        ProductApiResponse Add(Guid id, int version, Guid destination, string[]? permissions = null) => db.Call("objects", "link-add",
            new JsonObject { ["sourceObjectId"] = id, ["targetObjectId"] = destination, ["linkType"] = "contact_file" }.ToJsonString(), id, version, permissions: permissions);
        var createOnly = new[] { "ObjectLink.Create", "Contact.Read", "Contact.Update", "FileCatalog.Read", "FileCatalog.Update" };
        var deleteOnly = new[] { "ObjectLink.Delete", "Contact.Read", "Contact.Update", "FileCatalog.Read" };
        var link = Add(source, 1, target, createOnly);
        Assert.Equal(201, link.Status);
        Assert.Equal(409, Assert.Throws<ProductApiException>(() => Add(source, 2, target)).Status);
        Assert.Equal(200, db.Call("objects", "link-remove", id: source, version: 2, child: Id(link), permissions: deleteOnly).Status);
        var next = Add(source, 3, target, createOnly);
        Assert.Equal(201, next.Status);
        Assert.Equal(412, Assert.Throws<ProductApiException>(() => db.Call("objects", "link-remove", id: source, version: 3, child: Id(next), permissions: deleteOnly)).Status);
        Assert.Equal(404, Assert.Throws<ProductApiException>(() => Add(source, 4, Guid.NewGuid())).Status);
        Assert.Equal(404, Assert.Throws<ProductApiException>(() => Add(Guid.NewGuid(), 1, target)).Status);
        db.Call("catalog-items", "archive", id: target, version: 1);
        Assert.Equal(422, Assert.Throws<ProductApiException>(() => Add(source, 4, target)).Status);
        db.Call("archive", "unarchive", id: target, version: 2);
        db.Call("catalog-items", "trash", id: target, version: 3);
        Assert.Equal(422, Assert.Throws<ProductApiException>(() => Add(source, 4, target)).Status);
        db.Call("contacts", "trash", id: source, version: 4);
        Assert.Equal(409, Assert.Throws<ProductApiException>(() => Add(source, 5, target)).Status);
    }

    [RequiresPostgresFact]
    public void ObjectLinks_EnforceThePickerMatrixAndReplayCreateSafely()
    {
        using var db = Database.Create(); if (db is null) return;

        var file = db.Call("catalog-items", "create", "{\"name\":\"Picker file\",\"itemType\":\"file_reference\"}");
        var project = db.Call("projects", "create", new JsonObject { ["name"] = "Picker project", ["ownerUserId"] = db.User }.ToJsonString());
        var contact = db.Call("contacts", "create", "{\"firstName\":\"Picker\",\"displayName\":\"Picker contact\"}");
        var secondContact = db.Call("contacts", "create", "{\"firstName\":\"Target\",\"displayName\":\"Target contact\"}");
        var task = TaskAggregate.Create(Guid.NewGuid(), db.Organization, db.User, "Picker task", DateTimeOffset.UtcNow);
        db.Runtime.CreateTaskStore().Add(task);

        ProductApiResponse Add(Guid source, int version, Guid target, string type, string? key = null) => db.Call("objects", "link-add",
            new JsonObject { ["sourceObjectId"] = source, ["targetObjectId"] = target, ["linkType"] = type }.ToJsonString(), source, version, key: key);

        Assert.Equal(201, Add(task.Metadata.Id, 1, Id(file), "task_file").Status);
        Assert.Equal(201, Add(task.Metadata.Id, 2, Id(contact), "task_contact").Status);
        Assert.Equal(201, Add(task.Metadata.Id, 3, Id(project), "task_project").Status);
        Assert.Equal(201, Add(Id(project), 1, Id(file), "project_file").Status);
        Assert.Equal(201, Add(Id(project), 2, Id(contact), "project_contact").Status);
        Assert.Equal(201, Add(Id(contact), 1, Id(file), "contact_file").Status);

        var first = Add(Id(contact), 2, Id(secondContact), "related", "picker-related-001");
        var replay = Add(Id(contact), 2, Id(secondContact), "related", "picker-related-001");
        Assert.Equal(201, first.Status); Assert.True(JsonNode.DeepEquals(first.Body, replay.Body)); Assert.True(replay.WasReplayed);
        Assert.Equal("IDEMPOTENCY_KEY_REUSED", Assert.Throws<ProductApiException>(() =>
            Add(Id(contact), 2, Id(project), "related", "picker-related-001")).Code);

        Assert.Equal(422, Assert.Throws<ProductApiException>(() => Add(task.Metadata.Id, 4, task.Metadata.Id, "related")).Status);
        Assert.Equal(422, Assert.Throws<ProductApiException>(() => Add(Id(project), 3, Id(file), "parent_reference")).Status);
        Assert.Equal(422, Assert.Throws<ProductApiException>(() => Add(Id(project), 3, Id(contact), "task_contact")).Status);
    }
}
