using System.Text.Json.Nodes;
using Task.Application.ProductData;

namespace Task.Tests;

public sealed partial class PostgresProductApiTests
{
    [Fact]
    public void FileLocationManager_RealAggregateVersionsPatchPrimaryAndDeleteLast()
    {
        using var db = Database.Create(); if (db is null) return;
        var id = Id(db.Call("catalog-items", "create", """{"name":"Manager","itemType":"file_reference"}"""));
        var first = db.Call("catalog-items", "location-add", """{"locationType":"local_path","rawPath":"C:\\Work\\a.txt","isPrimary":true}""", id, 1);
        Assert.Equal(2, first.Version); Assert.Equal(1, first.Body!["version"]!.GetValue<int>());
        var stale = Assert.Throws<ProductApiException>(() => db.Call("catalog-items", "location-add", """{"locationType":"local_path","rawPath":"C:\\Work\\b.txt"}""", id, 1));
        Assert.Equal(412, stale.Status); Assert.Equal("VERSION_CONFLICT", stale.Code);
        var current = db.Call("catalog-items", "get", id: id); Assert.Equal(2, current.Version);
        var second = db.Call("catalog-items", "location-add", """{"locationType":"local_path","rawPath":"C:\\Work\\b.txt","isPrimary":false}""", id, current.Version);
        var edit = db.Call("catalog-items", "location-patch", """{"rawPath":"C:\\Work\\edited.txt","priority":4,"isEnabled":false}""", id, second.Version, child: Id(second));
        Assert.Equal(4, edit.Version);
        var primary = db.Call("catalog-items", "location-patch", """{"isPrimary":true,"isEnabled":true}""", id, edit.Version, child: Id(second));
        var locations = db.Call("catalog-items", "locations", id: id);
        Assert.Equal(primary.Version, locations.Version); var rows = locations.Body!.AsArray(); Assert.Equal(2, rows.Count);
        Assert.Equal(Id(second), Guid.Parse(Assert.Single(rows, n => n!["isPrimary"]!.GetValue<bool>())!["id"]!.ToString()));
        Assert.Equal(@"C:\Work\edited.txt", rows[0]!["rawPath"]!.ToString()); Assert.True(rows[1]!["version"]!.GetValue<int>() > 1);
        var deletion = db.Call("catalog-items", "location-remove", id: id, version: locations.Version, child: Id(first)); Assert.Equal(204, deletion.Status);
        deletion = db.Call("catalog-items", "location-remove", id: id, version: deletion.Version, child: Id(second));
        Assert.Empty(db.Call("catalog-items", "locations", id: id).Body!.AsArray());
        Assert.Equal(deletion.Version, db.Call("catalog-items", "get", id: id).Version);
        Assert.Equal(1, db.Count("files.catalog_items"));
    }

    [Fact]
    public void FileLocationManager_RealUncValidationResourceVisibilityAndLocalSettings()
    {
        using var db = Database.Create(); if (db is null) return;
        var id = Id(db.Call("catalog-items", "create", """{"name":"Manager UNC","itemType":"file_reference"}"""));
        var network = db.Call("network-resources", "create", """{"name":"Docs","rootUncPath":"\\\\server\\docs"}""");
        Assert.Equal(422, Assert.Throws<ProductApiException>(() => db.Call("catalog-items", "location-add", """{"locationType":"unc_path","rawPath":"\\\\server\\docs\\a.txt"}""", id, 1)).Status);
        var outside = new JsonObject { ["locationType"] = "unc_path", ["rawPath"] = @"\\evil\other\a.txt", ["networkResourceId"] = Id(network) };
        Assert.Equal(422, Assert.Throws<ProductApiException>(() => db.Call("catalog-items", "location-add", outside.ToJsonString(), id, 1)).Status);
        outside["rawPath"] = @"\\server\docs\a.txt";
        var added = db.Call("catalog-items", "location-add", outside.ToJsonString(), id, 1); Assert.Equal(2, added.Version);
        db.Call("network-resources", "patch", """{"status":"disabled"}""", Id(network), 1);
        Assert.Equal(422, Assert.Throws<ProductApiException>(() => db.Call("catalog-items", "location-patch", """{"isPrimary":true}""", id, 2, child: Id(added))).Status);
        var resources = db.Call("network-resources", "list", permissions: ["FileCatalog.Read"]); Assert.Null(resources.Body!["items"]![0]!["rootUncPath"]);
        db.Call("user-settings", "patch", """{"allowLocalPaths":false}""", version: 1);
        Assert.Equal(403, Assert.Throws<ProductApiException>(() => db.Call("catalog-items", "location-add", """{"locationType":"local_path","rawPath":"C:\\a.txt"}""", id, 2)).Status);
    }

    [Fact]
    public void FileLocationManager_RealDeviceScopeAndSensitiveRedaction()
    {
        using var db = Database.Create(); if (db is null) return;
        var id = Id(db.Call("catalog-items", "create", """{"name":"Shared manager","itemType":"file_reference"}"""));
        var added = db.Call("catalog-items", "location-add", """{"locationType":"local_path","rawPath":"C:\\Work\\secret.txt"}""", id, 1);
        // Share the object through the existing project membership relation.
        var project = Id(db.Call("projects", "create", new JsonObject { ["name"] = "Shared", ["ownerUserId"] = db.User }.ToJsonString()));
        var role = Guid.NewGuid();
        db.Sql("INSERT INTO iam.roles(id,organization_id,code,display_name) VALUES($1,$2,'location_member','Member');", role, db.Organization);
        db.Call("projects", "member-add", new JsonObject { ["userAccountId"] = db.OtherUser, ["projectRoleId"] = role }.ToJsonString(), project, 1);
        db.Sql("INSERT INTO core.object_links(id,organization_id,source_object_id,target_object_id,link_type,created_by) VALUES($1,$2,$3,$4,'project_file',$5);", Guid.NewGuid(), db.Organization, project, id, db.User);
        var hidden = db.Call("catalog-items", "locations", id: id, user: db.OtherUser, session: db.OtherSession, admin: false, permissions: ["FileReference.Open", "FileCatalog.Read"]);
        Assert.Null(hidden.Body![0]!["rawPath"]); Assert.False(hidden.Body![0]!["canOpenOnDevice"]!.GetValue<bool>());
        var revealed = db.Call("catalog-items", "locations", id: id, user: db.OtherUser, session: db.OtherSession, admin: false, permissions: ["FileReference.Open", "FileCatalog.Read", "FileLocation.ReadSensitivePath"]);
        Assert.Equal(@"C:\Work\secret.txt", revealed.Body![0]!["rawPath"]!.ToString()); Assert.False(revealed.Body![0]!["canOpenOnDevice"]!.GetValue<bool>());
        Assert.Equal(403, Assert.Throws<ProductApiException>(() => db.Call("catalog-items", "location-patch", """{"isEnabled":false}""", id, 2, child: Id(added), user: db.OtherUser, session: db.OtherSession, admin: false)).Status);
        Assert.Equal(403, Assert.Throws<ProductApiException>(() => db.Call("catalog-items", "location-remove", id: id, version: 2, child: Id(added), user: db.OtherUser, session: db.OtherSession, admin: false)).Status);
        Assert.Equal(403, Assert.Throws<ProductApiException>(() => db.Call("catalog-items", "location-patch", new JsonObject { ["deviceId"] = db.DeviceFor(db.OtherSession) }.ToJsonString(), id, 2, child: Id(added))).Status);
        Assert.Equal(2, db.Call("catalog-items", "get", id: id).Version);
    }
}
