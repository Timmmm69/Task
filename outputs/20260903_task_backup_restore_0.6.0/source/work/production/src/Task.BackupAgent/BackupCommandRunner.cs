using System.Diagnostics;

namespace Task.BackupAgent;

public interface IBackupCommandRunner
{
    global::System.Threading.Tasks.Task RunAsync(string operation, CancellationToken cancellationToken);
}

public sealed class BackupCommandRunner(BackupOptions options) : IBackupCommandRunner
{
    public async global::System.Threading.Tasks.Task RunAsync(string operation, CancellationToken cancellationToken)
    {
        if (operation is not ("backup" or "check" or "verify"))
        {
            throw new ArgumentOutOfRangeException(nameof(operation));
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(options.CommandTimeoutSeconds));
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo(options.RunnerPath)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            }
        };
        process.StartInfo.ArgumentList.Add(operation);
        process.Start();
        // Drain without retaining output: child diagnostics can contain paths or secrets.
        var stdout = process.StandardOutput.BaseStream.CopyToAsync(Stream.Null, CancellationToken.None);
        var stderr = process.StandardError.BaseStream.CopyToAsync(Stream.Null, CancellationToken.None);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
            await global::System.Threading.Tasks.Task.WhenAll(stdout, stderr);
            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException($"Backup operation {operation} failed (exit {process.ExitCode}).");
            }
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            await process.WaitForExitAsync(CancellationToken.None);
            await global::System.Threading.Tasks.Task.WhenAll(stdout, stderr);
            cancellationToken.ThrowIfCancellationRequested();
            throw new TimeoutException($"Backup operation {operation} exceeded its deadline.");
        }
    }
}
