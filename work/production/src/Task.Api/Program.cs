using System.Text.Json.Serialization;
using Microsoft.Extensions.Hosting.WindowsServices;

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

var app = builder.Build();

app.Use(async (context, next) =>
{
    var supplied = context.Request.Headers[CorrelationIdHeader].ToString();
    var correlationId = Guid.TryParseExact(supplied, "D", out var parsed) ? parsed : Guid.NewGuid();
    context.Response.Headers[CorrelationIdHeader] = correlationId.ToString();

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

app.MapGet("/health/live", () => Results.Ok(new HealthResponse(Status: "Alive")));

app.MapGet("/health/ready", () => Results.Json(
    new HealthResponse(
        Status: "NotReady",
        Details: new Dictionary<string, object>
        {
            ["persistence"] = "PostgreSQL and migrations are not implemented yet; persistence readiness is not configured.",
            ["ready"] = false
        }),
    statusCode: StatusCodes.Status503ServiceUnavailable));

app.Run();

internal sealed record HealthResponse(
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("details")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    Dictionary<string, object>? Details = null);