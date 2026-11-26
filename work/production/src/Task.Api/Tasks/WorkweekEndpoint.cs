using System.Globalization;
using Task.Application;
using Task.Application.ProductData;
using Task.Application.Security;

namespace Task.Api.Tasks;

internal static partial class TaskEndpoints
{
    private static async Task<IResult> GetWorkweekAsync(HttpContext context, CancellationToken token)
    {
        var user = ReadRequestContext(context);
        if (user is null) return Results.Unauthorized();
        var permissions = context.RequestServices.GetRequiredService<PermissionDecisionService>();
        var permission = ProductApiRoute.GetBackingPermission("History.Read");
        var grants = await permissions.EvaluateManyAsync(user.OrganizationId, user.UserAccountId, [permission], token);
        if (!grants[permission].Allowed) return Results.Forbid();
        var query = context.Request.Query;
        if (query.Count != 2 || !Parse(query["from"], out var from) || !Parse(query["through"], out var through)
            || through < from || through - from > TimeSpan.FromDays(8)) return Results.BadRequest();
        if (context.RequestServices.GetService<ITaskReadStore>() is not IWorkweekReadStore store)
            return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
        return Results.Ok(await store.ReadWeekAsync(user.OrganizationId, user.UserAccountId, from, through, token));
    }

    private static async Task<IResult> GetDayResultAsync(HttpContext context, CancellationToken token)
    {
        var user = ReadRequestContext(context);
        if (user is null) return Results.Unauthorized();
        var query = context.Request.Query;
        if (query.Count != 4 || !DateOnly.TryParseExact(query["date"], "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var date) || !Parse(query["from"], out var from)
            || !Parse(query["to"], out var to) || !Parse(query["through"], out var cutoff)
            || cutoff < from || cutoff > to || !(new TaskTodayWindow(date, from, to)).IsValid) return Results.BadRequest();
        if (context.RequestServices.GetService<ITaskReadStore>() is not IWorkdayReadStore store)
            return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
        var result = await store.ReadDayAsync(user.OrganizationId, user.UserAccountId, new(date, from, to), cutoff, token);
        return result is null ? Results.NoContent() : Results.Ok(result);
    }

    private static bool Parse(string? text, out DateTimeOffset instant) =>
        DateTimeOffset.TryParseExact(text, "O", CultureInfo.InvariantCulture, DateTimeStyles.None, out instant)
            && instant.Offset == TimeSpan.Zero;
}
