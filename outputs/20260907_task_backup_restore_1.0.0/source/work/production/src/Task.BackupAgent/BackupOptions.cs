namespace Task.BackupAgent;

public sealed class BackupOptions
{
    public bool Enabled { get; set; }
    public string RunnerPath { get; set; } = "/opt/task-backup/runner.py";
    public string StateDirectory { get; set; } = "/var/lib/task-backup";
    public int BackupHourUtc { get; set; } = 1;
    public int CheckIntervalSeconds { get; set; } = 300;
    public int RetryIntervalSeconds { get; set; } = 300;
    public int RestoreTestDays { get; set; } = 7;
    public int CommandTimeoutSeconds { get; set; } = 14400;

    public void Validate()
    {
        if (!Path.IsPathFullyQualified(RunnerPath) || !Path.IsPathFullyQualified(StateDirectory)
            || BackupHourUtc is < 0 or > 23 || CheckIntervalSeconds is < 1 or > 300
            || RetryIntervalSeconds is < 1 or > 900 || RestoreTestDays is < 1 or > 7
            || CommandTimeoutSeconds is < 1 or > 14400)
        {
            throw new InvalidOperationException("Invalid Backup configuration.");
        }
    }
}
