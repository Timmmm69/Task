using Npgsql;
using Task.Infrastructure.Persistence;

using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellation.Cancel();
};

await using var runtime = new TaskPersistenceRuntime(
    Environment.GetEnvironmentVariable("ConnectionStrings__TaskDatabase"));
var exitCode = await DatabaseMigratorCommand.RunAsync(
    args,
    runtime.IsConfigured ? new RuntimeMigrationOperations(runtime.CreateMigrator()) : null,
    Console.Out,
    Console.Error,
    cancellation.Token);
return exitCode;

internal static class DatabaseMigratorCommand
{
    internal const string Usage = "Usage: Task.DatabaseMigrator <status|apply>";

    public static async Task<int> RunAsync(
        string[] args,
        IDatabaseMigrationOperations? operations,
        TextWriter standardOutput,
        TextWriter standardError,
        CancellationToken cancellationToken)
    {
        if (args.Length == 1 && (args[0] == "--help" || args[0] == "-h"))
        {
            await standardOutput.WriteLineAsync(Usage);
            return 0;
        }

        if (args.Length != 1 ||
            (!args[0].Equals("status", StringComparison.OrdinalIgnoreCase) &&
             !args[0].Equals("apply", StringComparison.OrdinalIgnoreCase)))
        {
            await WriteErrorAsync(standardError, "InvalidArguments");
            return 2;
        }

        if (operations is null)
        {
            await WriteErrorAsync(standardError, "NotConfigured");
            return 3;
        }

        try
        {
            var inspection = await operations.InspectAsync(cancellationToken);
            if (args[0].Equals("status", StringComparison.OrdinalIgnoreCase))
            {
                return await WriteStatusAsync(inspection, standardOutput, standardError);
            }

            return await ApplyAsync(operations, inspection, standardOutput, standardError, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await WriteErrorAsync(standardError, "Cancelled");
            return 130;
        }
        catch (TaskPersistenceMigrationException exception)
        {
            var mapping = exception.Error switch
            {
                TaskPersistenceMigrationError.LockUnavailable => (8, "LockUnavailable"),
                TaskPersistenceMigrationError.UnsupportedServerVersion => (5, "UnsupportedServerVersion"),
                TaskPersistenceMigrationError.SchemaIncompatible => (7, "SchemaIncompatible"),
                _ => (9, "ApplyFailed"),
            };
            await WriteErrorAsync(standardError, mapping.Item2);
            return mapping.Item1;
        }
        catch (PostgresException exception)
        {
            var infrastructureFailure = exception.SqlState.StartsWith("08", StringComparison.Ordinal) ||
                exception.SqlState.StartsWith("28", StringComparison.Ordinal) ||
                exception.SqlState.StartsWith("53", StringComparison.Ordinal) ||
                exception.SqlState is "57P01" or "57P02" or "57P03";
            await WriteErrorAsync(
                standardError,
                infrastructureFailure ? "DatabaseUnavailable" : "MigrationExecutionFailed");
            return infrastructureFailure ? 4 : 9;
        }
        catch (Exception exception) when (exception is NpgsqlException or TimeoutException)
        {
            await WriteErrorAsync(standardError, "DatabaseUnavailable");
            return 4;
        }
        catch (Exception)
        {
            await WriteErrorAsync(standardError, "OperationFailed");
            return 9;
        }
    }

    private static async Task<int> WriteStatusAsync(
        TaskPersistenceMigrationInspection inspection,
        TextWriter standardOutput,
        TextWriter standardError)
    {
        return inspection.Status switch
        {
            TaskPersistenceMigrationStatus.Current => await WriteSuccessAsync(
                standardOutput, "Ready", inspection),
            TaskPersistenceMigrationStatus.HistoryMissing or TaskPersistenceMigrationStatus.Pending =>
                await WriteFailureAsync(standardError, 6, "MigrationsRequired", inspection),
            TaskPersistenceMigrationStatus.UnsupportedServerVersion =>
                await WriteFailureAsync(standardError, 5, "UnsupportedServerVersion", inspection),
            TaskPersistenceMigrationStatus.SchemaObjectsMissing =>
                await WriteFailureAsync(standardError, 7, "SchemaObjectsMissing", inspection),
            TaskPersistenceMigrationStatus.HistoryMismatch =>
                await WriteFailureAsync(standardError, 7, "HistoryMismatch", inspection),
            _ => await WriteFailureAsync(standardError, 4, "InspectionFailed", inspection),
        };
    }

    private static async Task<int> ApplyAsync(
        IDatabaseMigrationOperations operations,
        TaskPersistenceMigrationInspection inspection,
        TextWriter standardOutput,
        TextWriter standardError,
        CancellationToken cancellationToken)
    {
        if (inspection.Status == TaskPersistenceMigrationStatus.Current)
        {
            return await WriteSuccessAsync(standardOutput, "AlreadyCurrent", inspection);
        }

        if (inspection.Status is not TaskPersistenceMigrationStatus.HistoryMissing and
            not TaskPersistenceMigrationStatus.Pending)
        {
            return await WriteStatusAsync(inspection, standardOutput, standardError);
        }

        await operations.ApplyPendingAsync(cancellationToken);
        TaskPersistenceMigrationInspection postCheck;
        try
        {
            postCheck = await operations.InspectAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            await WriteErrorAsync(standardError, "PostCheckFailed");
            return 9;
        }

        if (postCheck.Status != TaskPersistenceMigrationStatus.Current)
        {
            return await WriteFailureAsync(standardError, 9, "PostCheckFailed", postCheck);
        }

        return await WriteSuccessAsync(standardOutput, "Applied", postCheck);
    }

    private static async Task<int> WriteSuccessAsync(
        TextWriter writer,
        string code,
        TaskPersistenceMigrationInspection inspection)
    {
        await writer.WriteLineAsync(Format(code, inspection));
        return 0;
    }

    private static async Task<int> WriteFailureAsync(
        TextWriter writer,
        int exitCode,
        string code,
        TaskPersistenceMigrationInspection inspection)
    {
        await writer.WriteLineAsync(Format(code, inspection));
        return exitCode;
    }

    private static global::System.Threading.Tasks.Task WriteErrorAsync(TextWriter writer, string code) =>
        writer.WriteLineAsync($"TASK_DB_MIGRATOR code={code}");

    private static string Format(string code, TaskPersistenceMigrationInspection inspection) =>
        $"TASK_DB_MIGRATOR code={code} expectedVersion={inspection.ExpectedMigrationVersion} " +
        $"actualVersion={inspection.ActualMigrationVersion?.ToString() ?? "unknown"}";
}

internal interface IDatabaseMigrationOperations
{
    Task<TaskPersistenceMigrationInspection> InspectAsync(CancellationToken cancellationToken);

    global::System.Threading.Tasks.Task ApplyPendingAsync(CancellationToken cancellationToken);
}

internal sealed class RuntimeMigrationOperations(TaskPersistenceMigrator migrator)
    : IDatabaseMigrationOperations
{
    public Task<TaskPersistenceMigrationInspection> InspectAsync(CancellationToken cancellationToken) =>
        migrator.InspectAsync(cancellationToken);

    public global::System.Threading.Tasks.Task ApplyPendingAsync(CancellationToken cancellationToken) =>
        migrator.ApplyPendingAsync(cancellationToken);
}
