using Microsoft.Extensions.Hosting.WindowsServices;
using Task.Application.Security;
using Task.Infrastructure.Persistence;
using Task.Worker;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddWindowsService(options =>
{
    options.ServiceName = TaskBackgroundWorker.ServiceName;
});

var taskDatabaseConnectionString = builder.Configuration.GetConnectionString("TaskDatabase");
var persistenceRuntime = new TaskPersistenceRuntime(taskDatabaseConnectionString);
builder.Services.AddSingleton(persistenceRuntime);
if (persistenceRuntime.IsConfigured)
{
    builder.Services.AddSingleton<ISessionRepository>(services =>
        services.GetRequiredService<TaskPersistenceRuntime>().CreateSessionRepository());
}

builder.Services.AddHostedService<TaskBackgroundWorker>();
builder.Services.AddHostedService<ExpiredSessionMaintenanceWorker>();

var host = builder.Build();

var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Task.Worker.Program");
logger.LogInformation(
    "Task background worker is starting (environment: {Environment}, windowsService: {IsWindowsService})",
    builder.Environment.EnvironmentName,
    WindowsServiceHelpers.IsWindowsService());

try
{
    await host.RunAsync();
    logger.LogInformation("Task background worker stopped normally");
}
catch (Exception ex)
{
    logger.LogCritical(ex, "Task background worker terminated unexpectedly");
    throw;
}