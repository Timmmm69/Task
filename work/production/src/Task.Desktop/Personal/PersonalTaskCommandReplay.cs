using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Task.Desktop.TaskApi;

namespace Task.Desktop.Personal;

public sealed partial class PersonalTaskStore
{
    private static string TaskCommandFingerprint<T>(T command) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(command))));
    private DesktopTaskDto? ReplayTaskCommand<T>(string key, T command, SqliteTransaction tx)
    {
        using var c = Command("SELECT fingerprint,result FROM personal_task_commands WHERE key=$key;", tx, ("$key", key));
        using var r = c.ExecuteReader();
        if (!r.Read()) return null;
        if (r.GetString(0) != TaskCommandFingerprint(command)) throw new ArgumentException("Ключ команды уже использован для других данных.");
        return JsonSerializer.Deserialize<DesktopTaskDto>(r.GetString(1)) ?? throw new InvalidOperationException("Invalid saved task response.");
    }
    private void CompleteTaskCommand<T>(string key, T command, DesktopTaskDto task, SqliteTransaction tx)
    {
        using var c = Command("INSERT INTO personal_task_commands VALUES($key,$hash,$result);", tx,
            ("$key", key), ("$hash", TaskCommandFingerprint(command)), ("$result", JsonSerializer.Serialize(task)));
        c.ExecuteNonQuery();
    }
}
