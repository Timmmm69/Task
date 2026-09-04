using System.Text.Json.Serialization;
using Microsoft.Extensions.Hosting.WindowsServices;
using Microsoft.Extensions.Options;
using Task.Application;
using Task.Application.Audit;
using Task.Application.Calendar;
using Task.Application.ProductData;
using Task.Application.Security;
using Task.Api.Audit;
using Task.Api.Auth;
using Task.Api.Capabilities;
using Task.Api.Calendar;
using Task.Api.Security;
using Task.Api.Tasks;
using Task.Api.ProductData;
using Task.Application.Server;
using Task.Infrastructure.Identity;
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
builder.Services.AddOptions<TaskIdentityFoundationOptions>()
    .Bind(builder.Configuration.GetSection(TaskIdentityFoundationOptions.SectionName))
    .ValidateOnStart();
builder.Services.AddTaskApiSecurityFoundation();
builder.Services.AddHostedService<TaskIdentityKeyMaterialStartupValidator>();

var taskDatabaseConnectionString = builder.Configuration.GetConnectionString("TaskDatabase");
builder.Services.AddSingleton<TaskPersistenceRuntime>(_ =>
    new TaskPersistenceRuntime(taskDatabaseConnectionString));
if (!string.IsNullOrWhiteSpace(taskDatabaseConnectionString))
{
    builder.Services.AddSingleton<ITaskAggregateStore>(services =>
        services.GetRequiredService<TaskPersistenceRuntime>().CreateTaskStore());
    builder.Services.AddSingleton<ITaskReadStore>(services =>
        services.GetRequiredService<TaskPersistenceRuntime>().CreateTaskReadStore());
    builder.Services.AddSingleton<ICalendarEventStore>(services =>
        services.GetRequiredService<TaskPersistenceRuntime>().CreateCalendarEventStore());
    builder.Services.AddSingleton<CalendarEventLifecycleService>();
    builder.Services.AddSingleton<CalendarEventQueryService>();
    builder.Services.AddSingleton(services =>
        services.GetRequiredService<TaskPersistenceRuntime>().CreateMigrator());
    builder.Services.AddSingleton<TaskLifecycleService>();
    builder.Services.AddSingleton<ITaskWriteCommandExecutor>(services =>
        services.GetRequiredService<TaskPersistenceRuntime>().CreateTaskWriteCommandExecutor());
    builder.Services.AddSingleton<TaskCreateCommandService>();
    builder.Services.AddSingleton<TaskUpdateCommandService>();
    builder.Services.AddSingleton<TaskQueryService>();
    builder.Services.AddSingleton<IScheduleStore>(services =>
        services.GetRequiredService<TaskPersistenceRuntime>().CreateScheduleStore());
    builder.Services.AddSingleton<ScheduleQueryService>();
    builder.Services.AddSingleton<IRecurrenceStore>(services =>
        services.GetRequiredService<TaskPersistenceRuntime>().CreateRecurrenceStore());
    builder.Services.AddSingleton<IProjectStore>(services =>
        services.GetRequiredService<TaskPersistenceRuntime>().CreateProjectStore());
    builder.Services.AddSingleton<IProductApiStore>(services =>
        services.GetRequiredService<TaskPersistenceRuntime>().CreateProductApiStore());
    builder.Services.AddSingleton<IContactStore>(services =>
        services.GetRequiredService<TaskPersistenceRuntime>().CreateContactStore());
    builder.Services.AddSingleton<ICatalogItemStore>(services =>
        services.GetRequiredService<TaskPersistenceRuntime>().CreateCatalogItemStore());
    builder.Services.AddSingleton<INotificationStore>(services =>
        services.GetRequiredService<TaskPersistenceRuntime>().CreateNotificationStore());
    builder.Services.AddSingleton<IProductSettingsStore>(services =>
        services.GetRequiredService<TaskPersistenceRuntime>().CreateProductSettingsStore());
    builder.Services.AddSingleton<IProductLifecycleStore>(services =>
        services.GetRequiredService<TaskPersistenceRuntime>().CreateProductLifecycleStore());
    builder.Services.AddSingleton<RecurrenceService>();
    builder.Services.AddSingleton<ISessionRepository>(services =>
        services.GetRequiredService<TaskPersistenceRuntime>().CreateSessionRepository());
    builder.Services.AddSingleton<IAccountLookupStore>(services =>
        services.GetRequiredService<TaskPersistenceRuntime>().CreateAccountLookupStore());
    builder.Services.AddSingleton<IDeviceRegistrationStore>(services =>
        services.GetRequiredService<TaskPersistenceRuntime>().CreateDeviceRegistrationStore());
    builder.Services.AddSingleton<IAccountLockoutStore>(services =>
        services.GetRequiredService<TaskPersistenceRuntime>().CreateAccountLockoutStore());
    builder.Services.AddSingleton<IAuditEntryStore>(services =>
        services.GetRequiredService<TaskPersistenceRuntime>().CreateAuditEntryStore());
    builder.Services.AddSingleton<IAccountCredentialStore>(services =>
        services.GetRequiredService<TaskPersistenceRuntime>().CreateAccountCredentialStore());
    builder.Services.AddSingleton<IAuthorizationPolicyStore>(services =>
        services.GetRequiredService<TaskPersistenceRuntime>().CreateAuthorizationPolicyStore());

    builder.Services.AddSingleton<AccountLockoutPolicy>();
    builder.Services.AddSingleton<AccountLockoutService>();
    builder.Services.AddSingleton<IPasswordHasher>(services =>
    {
        var options = services.GetRequiredService<IOptions<TaskIdentityFoundationOptions>>().Value;
        var reference = options.PepperReference;
        if (string.IsNullOrWhiteSpace(reference) || !reference.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Task:Identity:PepperReference must be a file: reference.");
        }

        var path = reference.Substring("file:".Length);
        var pepper = File.ReadAllText(path).Trim();
        return new Argon2idPasswordHasherAdapter(pepper);
    });
    builder.Services.AddSingleton<RefreshTokenRotationService>(services =>
        new RefreshTokenRotationService(services.GetRequiredService<ISessionRepository>()));
    builder.Services.AddSingleton<JwtAccessTokenIssuer>(services =>
    {
        var options = services.GetRequiredService<IOptions<TaskIdentityFoundationOptions>>().Value;
        return new JwtAccessTokenIssuer(
            options.Issuer!,
            options.Audience!,
            options.SigningKeyReference!);
    });
    builder.Services.AddSingleton<LoginService>();
    builder.Services.AddSingleton<RefreshService>();
    builder.Services.AddSingleton<PasswordChangeService>();
    builder.Services.AddSingleton<LoginRateLimiter>();
    builder.Services.AddSingleton<PermissionDecisionService>();
    builder.Services.AddSingleton(new ServerCapabilitiesService(
        schemaVersion: TaskPersistenceRuntime.ExpectedMigrationVersion,
        featureFlags: ["product_api_v1", "projects", "contacts", "file_catalog", "search", "notifications", "archive_trash", "settings"]));
    builder.Services.AddTaskPermissionAuthorization();
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

app.MapAuthEndpoints();
app.MapAuthSessionEndpoints();
app.MapCapabilitiesEndpoints();
app.MapAuditEndpoints();
app.MapTaskEndpoints();
app.MapCalendarEndpoints();
app.MapRecurrenceEndpoints();
app.MapProductEndpoints();

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
