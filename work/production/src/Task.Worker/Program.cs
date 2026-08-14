using Microsoft.Extensions.Hosting.WindowsServices;
using Task.Worker;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddWindowsService(options =>
{
    options.ServiceName = TaskBackgroundWorker.ServiceName;
});

builder.Services.AddHostedService<TaskBackgroundWorker>();

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