using Microsoft.Extensions.Hosting.WindowsServices;
using Task.BackupAgent;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddWindowsService(options =>
{
    options.ServiceName = BackupRestoreAgent.ServiceName;
});

var backupOptions = builder.Configuration.GetSection("Backup").Get<BackupOptions>() ?? new BackupOptions();
builder.Services.AddSingleton(backupOptions);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IBackupCommandRunner, BackupCommandRunner>();
builder.Services.AddSingleton<BackupSchedule>();
builder.Services.AddHostedService<BackupRestoreAgent>();

var host = builder.Build();

var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Task.BackupAgent.Program");
logger.LogInformation(
    "Backup/restore agent is starting (environment: {Environment}, windowsService: {IsWindowsService})",
    builder.Environment.EnvironmentName,
    WindowsServiceHelpers.IsWindowsService());

try
{
    await host.RunAsync();
    logger.LogInformation("Backup/restore agent stopped normally");
}
catch (Exception ex)
{
    logger.LogCritical(ex, "Backup/restore agent terminated unexpectedly");
    throw;
}
