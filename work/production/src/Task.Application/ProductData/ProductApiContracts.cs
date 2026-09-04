using System.Text.Json.Nodes;

namespace Task.Application.ProductData;

// HTTP adapters supply only server-authenticated identity and evaluated permissions.
public sealed record ProductApiRequest(
    Guid OrganizationId, Guid UserId, Guid SessionId, Guid CorrelationId,
    ProductApiRoute Route, Guid? Id, Guid? ChildId, JsonObject Body,
    IReadOnlyDictionary<string, string> Query, int? ExpectedVersion,
    string? IdempotencyKey, string RequestHash, IReadOnlySet<string> Permissions,
    CancellationToken CancellationToken = default);

public sealed record ProductApiResponse(JsonNode? Body, int Status = 200, int? Version = null);

public interface IProductApiStore
{
    ProductApiResponse Execute(ProductApiRequest request);
}

public sealed class ProductApiException(int status, string code, string message) : Exception(message)
{
    public int Status { get; } = status;
    public string Code { get; } = code;
}

public sealed record ProductApiRoute(string Method, string Path, string Resource,
    string Operation, string Permission, bool Versioned = false, bool Idempotent = false);

public static class ProductApiRoutes
{
    public static IReadOnlyList<ProductApiRoute> All { get; } = Build();

    private static IReadOnlyList<ProductApiRoute> Build()
    {
        var routes = new List<ProductApiRoute>();
        foreach (var (resource, permission) in new[] {
            ("projects", "Project"), ("contacts", "Contact"),
            ("companies", "Contact"), ("catalog-items", "FileCatalog") })
        {
            Add("GET", resource, "list", permission + ".Read");
            Add("POST", resource, "create", permission + ".Create", key: true);
            Add("GET", resource + "/{id}", "get", permission + ".Read");
            Add("PATCH", resource + "/{id}", "patch", permission + ".Update", version: true);
            Add("DELETE", resource + "/{id}", "trash", permission + ".Delete", version: true);
            Add("POST", resource + "/{id}/archive", "archive",
                permission + (resource == "projects" ? ".Archive" : ".Update"), true, true);
            Add("POST", resource + "/{id}/unarchive", "unarchive",
                permission + (resource == "projects" ? ".Archive" : ".Update"), true);
            Add("POST", resource + "/{id}/restore", "restore", permission + ".Restore", true, true);

            void Add(string method, string path, string operation, string code, bool version = false, bool key = false) =>
                routes.Add(new(method, "/api/v1/" + path, resource, operation, code, version, key));
        }
        Extra("GET", "tasks/options", "tasks", "task-options", "Task.Read");
        Extra("GET", "tasks/{id}/workspace", "tasks", "task-workspace", "Task.Read");
        Extra("GET", "tasks/{id}/history", "tasks", "history", "History.Read");
        Extra("POST", "tasks/{id}/checklist", "tasks", "task-check-add", "Task.Update", true, true);
        Extra("PATCH", "tasks/{id}/checklist/{childId}", "tasks", "task-check-patch", "Task.Update", true, true);
        Extra("DELETE", "tasks/{id}/checklist/{childId}", "tasks", "task-check-remove", "Task.Update", true, true);
        Extra("POST", "tasks/{id}/comments", "tasks", "task-comment-add", "Comment.Create", true, true);
        Extra("POST", "tasks/{id}/dependencies", "tasks", "task-dependency-add", "Task.Update", true, true);
        Extra("DELETE", "tasks/{id}/dependencies/{childId}", "tasks", "task-dependency-remove", "Task.Update", true, true);
        Extra("GET", "projects/{id}/members", "projects", "members", "Project.ManageMembers");
        Extra("POST", "projects/{id}/members", "projects", "member-add", "Project.ManageMembers", true, true);
        Extra("PATCH", "projects/{id}/members/{childId}", "projects", "member-patch", "Project.ManageMembers", true, true);
        Extra("DELETE", "projects/{id}/members/{childId}", "projects", "member-remove", "Project.ManageMembers", true);
        Extra("PUT", "projects/{id}/members/{childId}/overrides", "projects", "member-overrides", "Project.ManageMembers", true, true);
        Extra("POST", "projects/{id}/transfer-ownership", "projects", "transfer", "Project.TransferOwnership", true, true);
        Extra("GET", "projects/{id}/history", "projects", "history", "History.Read");
        Extra("POST", "contacts/{id}/channels", "contacts", "channel-add", "Contact.Update", true, true);
        Extra("PATCH", "contacts/{id}/channels/{childId}", "contacts", "channel-patch", "Contact.Update", true, true);
        Extra("DELETE", "contacts/{id}/channels/{childId}", "contacts", "channel-remove", "Contact.Update", true);
        Extra("POST", "contacts/{id}/addresses", "contacts", "address-add", "Contact.Update", true, true);
        Extra("POST", "companies/{id}/contacts", "companies", "contact-link", "Contact.Update", true, true);
        Extra("DELETE", "companies/{id}/contacts/{childId}", "companies", "contact-unlink", "Contact.Update", true);
        Extra("GET", "catalog/tree", "catalog-items", "tree", "FileCatalog.Read");
        Extra("POST", "catalog-items/{id}/move", "catalog-items", "move", "FileCatalog.Update", true, true);
        Extra("GET", "catalog-items/{id}/locations", "catalog-items", "locations", "FileReference.Open");
        Extra("POST", "catalog-items/{id}/locations", "catalog-items", "location-add", "FileLocation.Update", true, true);
        Extra("PATCH", "catalog-items/{id}/locations/{childId}", "catalog-items", "location-patch", "FileLocation.Update", true, true);
        Extra("DELETE", "catalog-items/{id}/locations/{childId}", "catalog-items", "location-remove", "FileLocation.Update", true);
        Extra("POST", "catalog-items/{id}/resolve-location", "catalog-items", "resolve", "FileReference.Open", false, true);
        Extra("GET", "network-resources", "network-resources", "list", "FileCatalog.Read");
        Extra("POST", "network-resources", "network-resources", "create", "NetworkResource.Manage", false, true);
        Extra("PATCH", "network-resources/{id}", "network-resources", "patch", "NetworkResource.Manage", true, true);
        Extra("GET", "notifications", "notifications", "list", "Notification.ReadOwn");
        Extra("GET", "notifications/{id}", "notifications", "get", "Notification.ReadOwn");
        Extra("POST", "notifications/{id}/read", "notifications", "read", "Notification.ReadOwn");
        Extra("POST", "notifications/{id}/action", "notifications", "action", "Notification.ManageOwn", true, true);
        Extra("POST", "notifications/read-all", "notifications", "read-all", "Notification.ReadOwn", false, true);
        Extra("GET", "notifications/preferences", "preferences", "get", "Settings.ReadOwn");
        Extra("PUT", "notifications/preferences", "preferences", "patch", "Settings.UpdateOwn", true, true);
        Extra("GET", "settings/me", "user-settings", "get", "Settings.ReadOwn");
        Extra("PATCH", "settings/me", "user-settings", "patch", "Settings.UpdateOwn", true, true);
        Extra("GET", "settings/organization", "organization-settings", "get", "Organization.Read");
        Extra("PATCH", "settings/organization", "organization-settings", "patch", "Organization.Update", true, true);
        Extra("GET", "search", "search", "search", "Search.Use");
        Extra("GET", "search/suggestions", "search", "suggestions", "Search.Use");
        Extra("GET", "archive", "archive", "list", "History.Read");
        Extra("POST", "archive/{id}/restore", "archive", "unarchive", "Archive.Restore", true);
        Extra("GET", "trash", "trash", "list", "Trash.Read");
        Extra("POST", "trash/{id}/restore", "trash", "restore", "Trash.Restore", true, true);
        Extra("GET", "interactions", "interactions", "list", "Contact.Read");
        Extra("POST", "interactions", "interactions", "create", "Interaction.Create", false, true);
        Extra("GET", "interactions/{id}", "interactions", "get", "Contact.Read");
        Extra("PATCH", "interactions/{id}", "interactions", "patch", "Interaction.Update", true);
        Extra("DELETE", "interactions/{id}", "interactions", "trash", "Interaction.Update", true);
        Extra("POST", "interactions/{id}/restore", "interactions", "restore", "Interaction.Update", true, true);
        Extra("PUT", "interactions/{id}/participants", "interactions", "participants", "Interaction.Update", true, true);
        Extra("GET", "objects/{id}/links", "objects", "links", "ObjectLink.Read");
        Extra("POST", "objects/{id}/links", "objects", "link-add", "ObjectLink.Create", true, true);
        Extra("DELETE", "objects/{id}/links/{childId}", "objects", "link-remove", "ObjectLink.Delete", true);
        Extra("POST", "file-locations/{id}/check-result", "file-locations", "check", "FileReference.Open", false, true);
        return routes;

        void Extra(string method, string path, string resource, string operation, string permission,
            bool version = false, bool key = false) =>
            routes.Add(new(method, "/api/v1/" + path, resource, operation, permission, version, key));
    }
}
