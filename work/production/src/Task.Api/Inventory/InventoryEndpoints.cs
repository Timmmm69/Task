using Microsoft.AspNetCore.Authorization;
using Task.Api.Security;
using Task.Application.Inventory;
using Task.Application.Security;

namespace Task.Api.Inventory;

internal static class InventoryEndpoints
{
    internal static void MapInventoryEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/material-inventory").RequireAuthorization();
        group.AddEndpointFilter(async (invocation, next) =>
        {
            invocation.HttpContext.Response.Headers.CacheControl = "no-store";
            return await next(invocation);
        });
        group.MapGet("", ReadAsync);
        group.MapPost("", ApplyAsync).WithMetadata(new Microsoft.AspNetCore.Mvc.RequestSizeLimitAttribute(16 * 1024 * 1024));
        group.MapGet("/managers", ManagersAsync).RequireAuthorization(TaskPermissionAuthorization.OrganizationManagePolicyName);
    }

    private static async Task<IResult> ReadAsync(HttpContext context, IAuthorizationService authorization, CancellationToken cancellationToken)
    {
        if (ReadActor(context) is not { } actor) return Results.Unauthorized();
        if (ResolveStore(context) is not { } store) return Results.StatusCode(503);
        var administrator = await IsAdministratorAsync(context, authorization);
        var current = await store.ReadAsync(actor.OrganizationId, cancellationToken);
        return Results.Json(current.Project(actor.UserAccountId, administrator), InventoryJson.Options);
    }

    private static async Task<IResult> ApplyAsync(HttpContext context, InventoryCommand command,
        IAuthorizationService authorization, CancellationToken cancellationToken)
    {
        if (ReadActor(context) is not { } actor) return Results.Unauthorized();
        if (ResolveStore(context) is not { } store) return Results.StatusCode(503);
        var administrator = await IsAdministratorAsync(context, authorization);
        if (command.RequiresAdministrator && !administrator) return Results.Forbid();
        var current = await store.ReadAsync(actor.OrganizationId, cancellationToken);
        if (!administrator && current.ManagerId != actor.UserAccountId) return Results.Forbid();

        if (command is { Action: "manager", ManagerId: { } managerId })
        {
            var users = context.RequestServices.GetService<IUserAccountReadStore>();
            if (users is null) return Results.StatusCode(503);
            var candidate = await users.GetByIdAsync(actor.OrganizationId, managerId, cancellationToken);
            if (candidate?.AccountStatus != UserAccountStatus.Active)
                return Results.Problem(statusCode: 422, detail: "Выберите активного пользователя компании.");
        }

        try
        {
            var clock = context.RequestServices.GetService<TimeProvider>() ?? TimeProvider.System;
            var now = clock.GetUtcNow();
            var next = InventoryAccounting.Apply(current, command, DateOnly.FromDateTime(clock.GetLocalNow().DateTime), now.ToUnixTimeMilliseconds());
            if (!await store.TrySaveAsync(actor.OrganizationId, next, cancellationToken)) throw new InventoryConflictException();
            return Results.Json(next.Project(actor.UserAccountId, administrator), InventoryJson.Options);
        }
        catch (InventoryValidationException error) { return Results.Problem(statusCode: 422, detail: error.Message); }
        catch (InventoryConflictException error) { return Results.Problem(statusCode: 409, detail: error.Message); }
    }

    private static async Task<IResult> ManagersAsync(HttpContext context, CancellationToken cancellationToken)
    {
        if (ReadActor(context) is not { } actor) return Results.Unauthorized();
        var users = context.RequestServices.GetService<IUserAccountReadStore>();
        if (users is null) return Results.StatusCode(503);

        var managers = new List<InventoryManager>();
        var page = 1;
        Guid? cursor = null;
        do
        {
            var result = await users.GetPageAsync(new(actor.OrganizationId, null, page++, cursor), cancellationToken);
            managers.AddRange(result.Items.Where(user => user.AccountStatus == UserAccountStatus.Active)
                .Select(user => new InventoryManager(user.Id, user.DisplayName)));
            cursor = result.NextCursor;
        } while (cursor is not null);
        return Results.Json(managers.OrderBy(user => user.Name).ThenBy(user => user.Id), InventoryJson.Options);
    }

    private static IInventoryStore? ResolveStore(HttpContext context)
        => context.RequestServices.GetService<IInventoryStore>();

    private static AuthenticatedRequestContext? ReadActor(HttpContext context)
        => context.Items[TaskJwtAuthenticationHandler.AuthenticatedRequestContextItemName] as AuthenticatedRequestContext;

    private static async Task<bool> IsAdministratorAsync(HttpContext context, IAuthorizationService authorization)
        => (await authorization.AuthorizeAsync(context.User, context, TaskPermissionAuthorization.OrganizationManagePolicyName)).Succeeded;
}
