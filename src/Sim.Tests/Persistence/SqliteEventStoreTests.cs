using Sim.Core.Events;
using Sim.Persistence;

namespace Sim.Tests.Persistence;

// Public + top-level: MessagePack's dynamic formatter only builds for public types.
public sealed record PriceFixed(long Commodity, long Price) : IEventPayload
{
    public int SchemaVersion => 1;
}

public sealed class SqliteEventStoreTests
{
    private static PayloadCodec Codec() => new PayloadCodec().Register<PriceFixed>("price.fixed.v1");

    private static string TempDb() =>
        $"Data Source={Path.Combine(Path.GetTempPath(), $"soc-{Guid.NewGuid():N}.db")};Pooling=False";

    private static void Delete(string connectionString)
    {
        string path = connectionString.Replace("Data Source=", string.Empty).Replace(";Pooling=False", string.Empty);
        foreach (string p in new[] { path, path + "-wal", path + "-shm" })
        {
            try
            {
                File.Delete(p);
            }
            catch (IOException)
            {
                // best effort
            }
        }
    }

    [Fact]
    public void Events_Survive_Write_Reopen_Read()
    {
        string cs = TempDb();
        try
        {
            using (SqliteEventStore store = SqliteEventStore.Open(cs, Codec()))
            {
                store.Append(new[]
                {
                    new EventRecord(1, 100, 10, null, new PriceFixed(1, 500)),
                    new EventRecord(2, 200, 10, CausalParent: 1, new PriceFixed(1, 520)),
                });
            }

            // Reopen a fresh store on the same file — proves durability, not just in-memory state.
            using (SqliteEventStore store = SqliteEventStore.Open(cs, Codec()))
            {
                IReadOnlyList<EventRecord> events = store.ReadAll();
                Assert.Equal(2, events.Count);

                Assert.Equal(1, events[0].Id);
                Assert.Null(events[0].CausalParent);
                PriceFixed p0 = Assert.IsType<PriceFixed>(events[0].Payload);
                Assert.Equal(500, p0.Price);
                Assert.Equal(1, p0.Commodity);

                Assert.Equal(1, events[1].CausalParent);
                Assert.Equal(520, Assert.IsType<PriceFixed>(events[1].Payload).Price);
            }
        }
        finally
        {
            Delete(cs);
        }
    }

    [Fact]
    public void Persisted_Events_Fold_To_The_Same_State()
    {
        string cs = TempDb();
        try
        {
            var original = new[]
            {
                new EventRecord(1, 10, 1, null, new PriceFixed(7, 100)),
                new EventRecord(2, 20, 1, null, new PriceFixed(7, 150)),
                new EventRecord(3, 30, 1, null, new PriceFixed(7, 90)),
            };

            using (SqliteEventStore store = SqliteEventStore.Open(cs, Codec()))
            {
                store.Append(original);
            }

            using (SqliteEventStore store = SqliteEventStore.Open(cs, Codec()))
            {
                var log = new EventLog();
                foreach (EventRecord ev in store.ReadAll())
                {
                    log.Append(ev);
                }

                // Latest price = fold picking the last PriceFixed.
                long latest = log.Fold(0L, (_, ev) => ((PriceFixed)ev.Payload).Price);
                Assert.Equal(90, latest);
            }
        }
        finally
        {
            Delete(cs);
        }
    }

    [Fact]
    public void Batched_Insert_Persists_All_Rows_In_Order()
    {
        string cs = TempDb();
        try
        {
            const int n = 5000;
            var batch = new List<EventRecord>(n);
            for (int i = 1; i <= n; i++)
            {
                batch.Add(new EventRecord(i, i * 60L, 1, i > 1 ? i - 1 : null, new PriceFixed(1, i)));
            }

            using (SqliteEventStore store = SqliteEventStore.Open(cs, Codec()))
            {
                store.Append(batch);
            }

            using (SqliteEventStore store = SqliteEventStore.Open(cs, Codec()))
            {
                IReadOnlyList<EventRecord> events = store.ReadAll();
                Assert.Equal(n, events.Count);
                Assert.Equal(1, events[0].Id);
                Assert.Equal(n, events[^1].Id);
                Assert.Equal(n, ((PriceFixed)events[^1].Payload).Price);
            }
        }
        finally
        {
            Delete(cs);
        }
    }

    [Fact]
    public void Snapshot_Round_Trips()
    {
        string cs = TempDb();
        try
        {
            byte[] state = { 1, 2, 3, 4, 5 };
            using (SqliteEventStore store = SqliteEventStore.Open(cs, Codec()))
            {
                store.SaveSnapshot(atTime: 3600, state);
                store.SaveSnapshot(atTime: 7200, new byte[] { 9, 9, 9 });
            }

            using (SqliteEventStore store = SqliteEventStore.Open(cs, Codec()))
            {
                (long AtTime, byte[] State)? snap = store.LoadLatestSnapshot();
                Assert.NotNull(snap);
                Assert.Equal(7200, snap!.Value.AtTime);
                Assert.Equal(new byte[] { 9, 9, 9 }, snap.Value.State);
            }
        }
        finally
        {
            Delete(cs);
        }
    }

    [Fact]
    public void No_Snapshot_Returns_Null()
    {
        string cs = TempDb();
        try
        {
            using SqliteEventStore store = SqliteEventStore.Open(cs, Codec());
            Assert.Null(store.LoadLatestSnapshot());
        }
        finally
        {
            Delete(cs);
        }
    }
}
