using System.Text.Json.Serialization;
using Microsoft.Extensions.Hosting.WindowsServices;
using Task.Application;
using Task.Api.Security;
using Task.Infrastructure.Persistence;

const string CorrelationIdHeader = "X-Correlation-ID";
const string LogCategory = "Task.Api";

var builder = WebApplication.CreateBuilder(args);

builder.Logging.AddSimpleConsole(options =>
{
    options.SingleLine = true;
    options.IncludeScopes = true;
    options.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ ";
});

if (WindowsServiceHelpers.IsWindowsService())
{
    builder.Host.UseWindowsService(options =>
    {
        options.ServiceName = "Task.Api";
    });
}

builder.Services.AddProblemDetails();
builder.Services.Configure<TaskIdentityFoundationOptions>(
    builder.Configuration.GetSection(TaskIdentityFoundationOptions.SectionName));
builder.Services.AddTaskApiSecurityFoundation();

var taskDatabaseConnectionString = builder.Configuration.GetConnectionString("TaskDatabase");
builder.Services.AddSingleton<TaskPersistenceRuntime>(_ =>
    new TaskPersistenceRuntime(taskDatabaseConnectionString));
if (!string.IsNullOrWhiteSpace(taskDatabaseConnectionString))
{
    builder.Services.AddSingleton<ITaskAggregateStore>(services =>
        services.GetRequiredService<TaskPersistenceRuntime>().CreateTaskStore());
    builder.Services.AddSingleton(services =>
        services.GetRequiredService<TaskPersistenceRuntime>().CreateMigrator());
    builder.Services.AddSingleton<TaskLifecycleService>();
    builder.Services.AddSingleton<TaskQueryService>();
}

var app = builder.Build();

app.UseExceptionHandler();

app.Use(async (context, next) =>
{
    var supplied = context.Request.Headers[CorrelationIdHeader].ToString();
    var correlationId = Guid.TryParseExact(supplied, "D", out var parsed) ? parsed : Guid.NewGuid();
    context.Response.Headers[CorrelationIdHeader] = correlationId.ToString();
    context.Items[TaskApiProblemResponse.CorrelationIdItemName] = correlationId.ToString();

    var logger = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(LogCategory);

    using (logger.BeginScope(new { CorrelationId = correlationId }))
    {
        logger.LogInformation("HTTP {Method} {Path} started with correlation id {CorrelationId}",
            context.Request.Method, context.Request.Path, correlationId);

        await next(context);

        logger.LogInformation("HTTP {Method} {Path} finished with status code {StatusCode}",
            context.Request.Method, context.Request.Path, context.Response.StatusCode);
    }
});

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health/live", () => Results.Ok(new HealthResponse(Status: "Alive"))).AllowAnonymous();

app.MapGet("/health/ready", async (
    TaskPersistenceRuntime persistence,
    CancellationToken cancellationToken) =>
{
    var readiness = await persistence.CheckReadinessAsync(cancellationToken);
    return Results.Json(
        new HealthResponse(
            Status: readiness.Ready ? "Ready" : "NotReady",
            Details: new Dictionary<string, object>
            {
                ["persistence"] = readiness.Message,
                ["persistenceCode"] = readiness.Code.ToString(),
                ["ready"] = readiness.Ready,
                ["expectedMigrationVersion"] = readiness.ExpectedMigrationVersion,
                ["actualMigrationVersion"] = readiness.ActualMigrationVersion?.ToString() ?? "unknown",
                ["postgresVersionNumber"] = readiness.ServerVersionNumber?.ToString() ?? "unknown",
            }),
        statusCode: readiness.Ready
            ? StatusCodes.Status200OK
            : StatusCodes.Status503ServiceUnavailable);
}).AllowAnonymous();

app.Run();

internal sealed record HealthResponse(
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("details")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    Dictionary<string, object>? Details = null);
