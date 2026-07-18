using Sim.Core.Events;

namespace Sim.Tests.Events;

public sealed class EventSourcingTests
{
    // A payload that evolved: V1 had only an amount; V2 added a currency.
    private sealed record DepositV1(long Amount) : IEventPayload
    {
        public int SchemaVersion => 1;
    }

    private sealed record DepositV2(long Amount, string Currency) : IEventPayload
    {
        public int SchemaVersion => 2;
    }

    private static EventUpcaster BuildUpcaster()
    {
        var up = new EventUpcaster();
        up.Register<DepositV1>(v1 => new DepositV2(v1.Amount, "credits"));
        return up;
    }

    private static long Balance(EventLog log, EventUpcaster up) =>
        log.Fold(0L, (bal, ev) => up.Upcast(ev).Payload is DepositV2 d ? bal + d.Amount : bal);

    // ---- log invariants ----

    [Fact]
    public void Fold_Recovers_State_From_Events()
    {
        var log = new EventLog();
        log.Append(new EventRecord(1, 10, OriginEntity: 100, null, new DepositV2(50, "credits")));
        log.Append(new EventRecord(2, 20, OriginEntity: 100, CausalParent: 1, new DepositV2(30, "credits")));

        Assert.Equal(80, Balance(log, BuildUpcaster()));
        Assert.Equal(2, log.Count);
    }

    [Fact]
    public void Duplicate_Id_Throws()
    {
        var log = new EventLog();
        log.Append(new EventRecord(1, 10, 100, null, new DepositV2(1, "credits")));
        Assert.Throws<InvalidOperationException>(
            () => log.Append(new EventRecord(1, 11, 100, null, new DepositV2(1, "credits"))));
    }

    [Fact]
    public void Non_Increasing_Id_Throws()
    {
        var log = new EventLog();
        log.Append(new EventRecord(5, 10, 100, null, new DepositV2(1, "credits")));
        Assert.Throws<InvalidOperationException>(
            () => log.Append(new EventRecord(3, 11, 100, null, new DepositV2(1, "credits"))));
    }

    [Fact]
    public void Backward_Time_Throws()
    {
        var log = new EventLog();
        log.Append(new EventRecord(1, 100, 1, null, new DepositV2(1, "credits")));
        Assert.Throws<InvalidOperationException>(
            () => log.Append(new EventRecord(2, 50, 1, null, new DepositV2(1, "credits"))));
    }

    [Fact]
    public void Dangling_Causal_Parent_Throws()
    {
        var log = new EventLog();
        Assert.Throws<InvalidOperationException>(
            () => log.Append(new EventRecord(1, 10, 1, CausalParent: 999, new DepositV2(1, "credits"))));
    }

    [Fact]
    public void Valid_Causal_Parent_Is_Accepted()
    {
        var log = new EventLog();
        log.Append(new EventRecord(1, 10, 1, null, new DepositV2(1, "credits")));
        log.Append(new EventRecord(2, 10, 1, CausalParent: 1, new DepositV2(1, "credits")));
        Assert.Equal(2, log.Count);
    }

    [Fact]
    public void Filtered_Fold_Applies_Only_Matching_Events()
    {
        var log = new EventLog();
        log.Append(new EventRecord(1, 10, OriginEntity: 1, null, new DepositV2(100, "credits")));
        log.Append(new EventRecord(2, 20, OriginEntity: 2, null, new DepositV2(500, "credits")));

        // Only entity 1's deposits (a stand-in for a per-observer knowledge fold).
        long own = log.Fold(
            0L,
            (bal, ev) => ev.Payload is DepositV2 d ? bal + d.Amount : bal,
            ev => ev.OriginEntity == 1);
        Assert.Equal(100, own);
    }

    // ---- upcasting (§2.6) ----

    [Fact]
    public void Upcaster_Upgrades_Old_Payload_To_Current()
    {
        var up = BuildUpcaster();
        IEventPayload upgraded = up.Upcast(new DepositV1(42));
        var v2 = Assert.IsType<DepositV2>(upgraded);
        Assert.Equal(42, v2.Amount);
        Assert.Equal("credits", v2.Currency);
    }

    [Fact]
    public void Upcaster_Leaves_Current_Payload_Untouched()
    {
        var up = BuildUpcaster();
        var current = new DepositV2(7, "credits");
        Assert.Same(current, up.Upcast(current));
    }

    [Fact]
    public void Old_And_New_Events_Fold_Together_After_Upcasting()
    {
        var log = new EventLog();
        log.Append(new EventRecord(1, 10, 1, null, new DepositV1(100)));          // written pre-migration
        log.Append(new EventRecord(2, 20, 1, null, new DepositV2(25, "credits"))); // written post-migration

        Assert.Equal(125, Balance(log, BuildUpcaster()));
    }
}
