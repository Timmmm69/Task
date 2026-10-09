using System.IO;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;

namespace Task.Desktop.Notifications;

/// <summary>Only identifiers; never a corporate data cache. Transactional claims precede OS submission.</summary>
public sealed class NotificationPresentationJournal : IDisposable
{
    private readonly SqliteConnection _connection;
    public NotificationPresentationJournal(string directory, string context)
    {
        Directory.CreateDirectory(directory);
        var name = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(context)));
        _connection = new(new SqliteConnectionStringBuilder { DataSource = Path.Combine(directory, name + ".sqlite"), Pooling = false }.ToString());
        _connection.Open();
        using var command = _connection.CreateCommand();
        command.CommandText = "PRAGMA busy_timeout=5000; CREATE TABLE IF NOT EXISTS presentation(id TEXT PRIMARY KEY); CREATE TABLE IF NOT EXISTS initialized(id INTEGER PRIMARY KEY);";
        command.ExecuteNonQuery();
    }
    public IReadOnlySet<Guid> Claim(IReadOnlyCollection<Guid> ids, IReadOnlySet<Guid>? eligibleOnFirstLaunch = null)
    {
        using var tx = _connection.BeginTransaction();
        using var command = _connection.CreateCommand(); command.Transaction = tx;
        command.CommandText = "SELECT count(*) FROM initialized;";
        var first = (long)command.ExecuteScalar()! == 0;
        var claimed = new HashSet<Guid>();
        foreach (var id in ids)
        {
            command.CommandText = "INSERT INTO presentation VALUES($id) ON CONFLICT DO NOTHING;";
            command.Parameters.Clear(); command.Parameters.AddWithValue("$id", id.ToString("D"));
            if (command.ExecuteNonQuery() == 1 && (!first || eligibleOnFirstLaunch?.Contains(id) == true)) claimed.Add(id);
        }
        command.Parameters.Clear(); command.CommandText = "INSERT INTO initialized VALUES(1) ON CONFLICT DO NOTHING;";
        command.ExecuteNonQuery(); tx.Commit(); return claimed;
    }
    public void InitializeBaseline(IReadOnlyCollection<Guid> historicalIds)
    {
        using var tx = _connection.BeginTransaction();
        using var command = _connection.CreateCommand(); command.Transaction = tx;
        command.CommandText = "INSERT INTO initialized VALUES(1) ON CONFLICT DO NOTHING;";
        if (command.ExecuteNonQuery() == 1)
        {
            foreach (var id in historicalIds)
            {
                command.CommandText = "INSERT INTO presentation VALUES($id) ON CONFLICT DO NOTHING;";
                command.Parameters.Clear(); command.Parameters.AddWithValue("$id", id.ToString("D")); command.ExecuteNonQuery();
            }
        }
        tx.Commit();
    }
    public void Release(Guid id)
    {
        using var command = _connection.CreateCommand(); command.CommandText = "DELETE FROM presentation WHERE id=$id;";
        command.Parameters.AddWithValue("$id", id.ToString("D")); command.ExecuteNonQuery();
    }
    public void Dispose() => _connection.Dispose();
}
