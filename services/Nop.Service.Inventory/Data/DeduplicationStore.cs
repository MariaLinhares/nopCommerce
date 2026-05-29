using Microsoft.Data.Sqlite;

namespace Nop.Service.Inventory.Data;

// Persistent deduplication store backed by SQLite.
// Survives container restarts — prevents reprocessing of already-handled messages
// after a crash or restart (at-least-once delivery guarantee).
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

    // Returns true if this is the first time this eventId is seen (and marks it).
    // Returns false if already processed (duplicate — caller should ack and skip).
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
