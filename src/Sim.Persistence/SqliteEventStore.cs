using Microsoft.Data.Sqlite;
using Sim.Core.Events;

namespace Sim.Persistence;

/// <summary>
/// The event store (roadmap §1, §2.1): one row per event, payload as a MessagePack blob, in SQLite
/// with WAL journaling and batched transactions so appends survive the 50-year soak. Snapshots live
/// in a separate table and are only a cache — the event rows are the source of truth. "The event log
/// is the save."
/// </summary>
public sealed class SqliteEventStore : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly PayloadCodec _codec;

    public SqliteEventStore(SqliteConnection connection, PayloadCodec codec)
    {
        _connection = connection;
        _codec = codec;
        if (_connection.State != System.Data.ConnectionState.Open)
        {
            _connection.Open();
        }

        Exec("PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL;");
        Exec(
            """
            CREATE TABLE IF NOT EXISTS events (
                id             INTEGER PRIMARY KEY,
                time           INTEGER NOT NULL,
                origin         INTEGER NOT NULL,
                causal_parent  INTEGER,
                type_tag       TEXT    NOT NULL,
                schema_version INTEGER NOT NULL,
                payload        BLOB    NOT NULL
            );
            CREATE TABLE IF NOT EXISTS snapshots (
                at_time INTEGER PRIMARY KEY,
                state   BLOB NOT NULL
            );
            """);
    }

    /// <summary>Opens (or creates) a store at the given connection string.</summary>
    public static SqliteEventStore Open(string connectionString, PayloadCodec codec)
        => new(new SqliteConnection(connectionString), codec);

    /// <summary>Appends events in a single transaction — the whole batch commits or none of it does.</summary>
    public void Append(IEnumerable<EventRecord> events)
    {
        using SqliteTransaction tx = _connection.BeginTransaction();
        using SqliteCommand cmd = _connection.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText =
            """
            INSERT INTO events (id, time, origin, causal_parent, type_tag, schema_version, payload)
            VALUES ($id, $time, $origin, $cp, $tag, $ver, $payload);
            """;

        SqliteParameter id = cmd.Parameters.Add("$id", SqliteType.Integer);
        SqliteParameter time = cmd.Parameters.Add("$time", SqliteType.Integer);
        SqliteParameter origin = cmd.Parameters.Add("$origin", SqliteType.Integer);
        SqliteParameter cp = cmd.Parameters.Add("$cp", SqliteType.Integer);
        SqliteParameter tag = cmd.Parameters.Add("$tag", SqliteType.Text);
        SqliteParameter ver = cmd.Parameters.Add("$ver", SqliteType.Integer);
        SqliteParameter payload = cmd.Parameters.Add("$payload", SqliteType.Blob);

        foreach (EventRecord ev in events)
        {
            id.Value = ev.Id;
            time.Value = ev.TimeSeconds;
            origin.Value = ev.OriginEntity;
            cp.Value = (object?)ev.CausalParent ?? DBNull.Value;
            tag.Value = _codec.TagFor(ev.Payload);
            ver.Value = ev.Payload.SchemaVersion;
            payload.Value = _codec.Serialize(ev.Payload);
            cmd.ExecuteNonQuery();
        }

        tx.Commit();
    }

    /// <summary>Reads every event in id order. Payloads are deserialized to their registered type.</summary>
    public IReadOnlyList<EventRecord> ReadAll()
    {
        var result = new List<EventRecord>();
        using SqliteCommand cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT id, time, origin, causal_parent, type_tag, payload FROM events ORDER BY id;";
        using SqliteDataReader reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            long? causalParent = reader.IsDBNull(3) ? null : reader.GetInt64(3);
            string tag = reader.GetString(4);
            byte[] blob = reader.GetFieldValue<byte[]>(5);
            result.Add(new EventRecord(
                reader.GetInt64(0),
                reader.GetInt64(1),
                reader.GetInt64(2),
                causalParent,
                _codec.Deserialize(tag, blob)));
        }

        return result;
    }

    /// <summary>Stores (or replaces) a snapshot of folded state at a given sim-time — a cache, not truth.</summary>
    public void SaveSnapshot(long atTime, byte[] state)
    {
        using SqliteCommand cmd = _connection.CreateCommand();
        cmd.CommandText =
            """
            INSERT INTO snapshots (at_time, state) VALUES ($t, $s)
            ON CONFLICT(at_time) DO UPDATE SET state = excluded.state;
            """;
        cmd.Parameters.Add("$t", SqliteType.Integer).Value = atTime;
        cmd.Parameters.Add("$s", SqliteType.Blob).Value = state;
        cmd.ExecuteNonQuery();
    }

    /// <summary>The most recent snapshot, or null if none has been written.</summary>
    public (long AtTime, byte[] State)? LoadLatestSnapshot()
    {
        using SqliteCommand cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT at_time, state FROM snapshots ORDER BY at_time DESC LIMIT 1;";
        using SqliteDataReader reader = cmd.ExecuteReader();
        if (!reader.Read())
        {
            return null;
        }

        return (reader.GetInt64(0), reader.GetFieldValue<byte[]>(1));
    }

    private void Exec(string sql)
    {
        using SqliteCommand cmd = _connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    public void Dispose() => _connection.Dispose();
}
