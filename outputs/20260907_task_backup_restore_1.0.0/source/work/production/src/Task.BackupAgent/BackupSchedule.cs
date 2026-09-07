using System.Text.Json;

namespace Task.BackupAgent;

public sealed record BackupState(
    DateTimeOffset? LastBackup = null,
    DateTimeOffset? LastCheck = null,
    DateTimeOffset? LastRestoreTest = null,
    DateTimeOffset? LastAttempt = null,
    string? FailedOperation = null);

public sealed class BackupSchedule(BackupOptions options, IBackupCommandRunner runner, TimeProvider clock)
{
    public BackupState State { get; private set; } = new();
    public bool RecoveredInvalidState { get; private set; }

    public void Load()
    {
        Directory.CreateDirectory(options.StateDirectory);
        var path = Path.Combine(options.StateDirectory, "status.json");
        if (File.Exists(path))
        {
            try
            {
                State = JsonSerializer.Deserialize<BackupState>(File.ReadAllText(path))
                    ?? throw new JsonException("Backup status is invalid.");
                var now = clock.GetUtcNow();
                if (State.LastBackup > now || State.LastCheck > now || State.LastRestoreTest > now || State.LastAttempt > now)
                    throw new JsonException("Backup status contains future timestamps.");
            }
            catch (JsonException)
            {
                File.Move(path, Path.Combine(options.StateDirectory, $"invalid-status-{Guid.NewGuid():N}.json"));
                State = new();
                RecoveredInvalidState = true;
            }
        }
    }

    public async global::System.Threading.Tasks.Task TickAsync(CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        if (State.FailedOperation is not null && now - State.LastAttempt < TimeSpan.FromSeconds(options.RetryIntervalSeconds))
        {
            return;
        }

        var due = new DateTimeOffset(now.Year, now.Month, now.Day, options.BackupHourUtc, 0, 0, TimeSpan.Zero);
        if (now < due) due = due.AddDays(-1);
        var operation = State.LastBackup is null || State.LastBackup < due ? "backup"
            : State.LastRestoreTest is null || now - State.LastRestoreTest >= TimeSpan.FromDays(options.RestoreTestDays) ? "verify"
            : State.LastCheck is null || now - State.LastCheck >= TimeSpan.FromSeconds(options.CheckIntervalSeconds) ? "check"
            : null;
        if (operation is null) return;

        try
        {
            await runner.RunAsync(operation, cancellationToken);
            var completed = clock.GetUtcNow();
            State = State with
            {
                LastBackup = operation == "backup" ? completed : State.LastBackup,
                LastRestoreTest = operation == "verify" ? completed : State.LastRestoreTest,
                LastCheck = completed,
                LastAttempt = completed,
                FailedOperation = null
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            State = State with { LastAttempt = clock.GetUtcNow(), FailedOperation = operation };
            Save();
            throw;
        }

        Save();
    }

    private void Save()
    {
        var path = Path.Combine(options.StateDirectory, "status.json");
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(State));
        File.Move(path + ".tmp", path, overwrite: true);
    }
}
