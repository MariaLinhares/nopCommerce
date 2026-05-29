using Microsoft.Data.Sqlite;

namespace Nop.Service.Shipping.Data;

public class DeduplicationStore : IDisposable
{
    private readonly SqliteConnection _conn;

    public DeduplicationStore(string dbPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
        _conn = new SqliteConnection($"Data Source={dbPath}");
        _conn.Open();

        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS processed_messages (
                event_id   TEXT PRIMARY KEY,
                processed_at TEXT NOT NULL
            )
            """;
        cmd.ExecuteNonQuery();
    }

    public bool TryMarkProcessed(string eventId)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            INSERT OR IGNORE INTO processed_messages(event_id, processed_at)
            VALUES($id, $ts)
            """;
        cmd.Parameters.AddWithValue("$id", eventId);
        cmd.Parameters.AddWithValue("$ts", DateTime.UtcNow.ToString("O"));
        return cmd.ExecuteNonQuery() == 1;
    }

    public void Dispose() => _conn.Dispose();
}
