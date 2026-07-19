using Sim.Core.Comms;
using Sim.Core.Events;
using Sim.Core.Knowledge;
using Sim.Core.Numerics;
using Sim.Core.World;

namespace Sim.Tests.Knowledge;

public sealed class KnowledgeProjectionTests
{
    private const long AuMm = 149_597_870_700_000L;
    private const long C = Reception.SpeedOfLightMmPerSec;

    // 1 AU is 499.005 light-seconds. Solve returns the first *whole* second at which light has fully
    // arrived (the reception floor), so a 1-AU crossing receives at emission + 500, not 499.
    private const long LightSecondsPerAu = 500;

    // A trivial payload standing in for a domain fact (a price fixing, a telemetry ping).
    private sealed record Ping(long Value) : IEventPayload
    {
        public int SchemaVersion => 1;
    }

    private sealed class FixedPoint : ISpatial
    {
        private readonly long _x, _y, _z;
        public FixedPoint(long x, long y, long z) => (_x, _y, _z) = (x, y, z);
        public (long X, long Y, long Z) PositionMmAt(long t) => (_x, _y, _z);
    }

    // Observer drifting along +x at a plausible orbital speed (≪ c).
    private sealed class Drifter : ISpatial
    {
        private readonly long _x0, _y, _vx;
        public Drifter(long x0, long y, long vxMmPerSec) => (_x0, _y, _vx) = (x0, y, vxMmPerSec);
        public (long X, long Y, long Z) PositionMmAt(long t) => (_x0 + (_vx * t), _y, 0);
    }

    private static KnowledgeProjection Build(
        EventLog log,
        IReadOnlyDictionary<long, ISpatial> spatial,
        Func<EventRecord, long, bool>? inScope = null)
        => new(log, id => spatial[id], inScope ?? ((_, _) => true));

    [Fact]
    public void Reception_Solves_The_Light_Cone_From_The_Emission_Position()
    {
        // Origin (entity 1) sits at 1 AU on +x; observer (entity 2) at the Sun.
        var spatial = new Dictionary<long, ISpatial>
        {
            [1] = new FixedPoint(AuMm, 0, 0),
            [2] = new FixedPoint(0, 0, 0),
        };
        var log = new EventLog();
        var ev = new EventRecord(1, 1000, OriginEntity: 1, null, new Ping(7));
        log.Append(ev);

        var k = Build(log, spatial);
        long recv = k.ReceptionTime(observerId: 2, ev);

        Assert.Equal(1000 + LightSecondsPerAu, recv);
    }

    [Fact]
    public void First_Light_Is_The_Floor_No_Signal_Arrives_Before_Emission()
    {
        var spatial = new Dictionary<long, ISpatial>
        {
            [1] = new FixedPoint(AuMm, 3 * AuMm, 0),
            [2] = new Drifter(-AuMm, 0, 25_000_000L), // 25 km/s toward +x
        };
        var log = new EventLog();
        var ev = new EventRecord(1, 5000, OriginEntity: 1, null, new Ping(1));
        log.Append(ev);

        var k = Build(log, spatial);
        long recv = k.ReceptionTime(2, ev);

        Assert.True(recv > ev.TimeSeconds, "a signal can never be received before it was emitted");

        // The light-cone invariant: light has arrived at recv, but not one second earlier.
        Assert.True(LightHasArrived(spatial, ev, 2, recv), "light should have arrived by reception");
        Assert.False(LightHasArrived(spatial, ev, 2, recv - 1), "light should not have arrived a second early");
    }

    [Fact]
    public void HasArrived_Is_False_Before_Reception_And_True_After()
    {
        var spatial = new Dictionary<long, ISpatial>
        {
            [1] = new FixedPoint(AuMm, 0, 0),
            [2] = new FixedPoint(0, 0, 0),
        };
        var log = new EventLog();
        var ev = new EventRecord(1, 0, OriginEntity: 1, null, new Ping(1));
        log.Append(ev);

        var k = Build(log, spatial);
        long recv = k.ReceptionTime(2, ev);

        Assert.False(k.HasArrived(2, ev, recv - 1));
        Assert.True(k.HasArrived(2, ev, recv));
        Assert.True(k.HasArrived(2, ev, recv + 10_000));
    }

    [Fact]
    public void Fold_Includes_Only_Signals_That_Have_Arrived()
    {
        // Two pings from 1 AU out; the observer at the Sun learns them ~499 s after each emission.
        var spatial = new Dictionary<long, ISpatial>
        {
            [1] = new FixedPoint(AuMm, 0, 0),
            [2] = new FixedPoint(0, 0, 0),
        };
        var log = new EventLog();
        log.Append(new EventRecord(1, 0, 1, null, new Ping(10)));
        log.Append(new EventRecord(2, 1000, 1, null, new Ping(20)));

        var k = Build(log, spatial);

        // At T=600: only the first ping (emitted at 0, received ~499) has arrived.
        long early = k.Fold(2, 600, 0L, (acc, ev) => ev.Payload is Ping p ? acc + p.Value : acc);
        Assert.Equal(10, early);

        // At T=2000: both have arrived (second received ~1499).
        long late = k.Fold(2, 2000, 0L, (acc, ev) => ev.Payload is Ping p ? acc + p.Value : acc);
        Assert.Equal(30, late);
    }

    [Fact]
    public void Out_Of_Scope_Events_Never_Enter_The_Fold()
    {
        var spatial = new Dictionary<long, ISpatial>
        {
            [1] = new FixedPoint(AuMm, 0, 0),
            [2] = new FixedPoint(0, 0, 0),
        };
        var log = new EventLog();
        log.Append(new EventRecord(1, 0, 1, null, new Ping(10)));

        // Scope rule: observer 2 is not an interested party for this event.
        var k = Build(log, spatial, inScope: (_, observerId) => observerId != 2);

        long known = k.Fold(2, 1_000_000, 0L, (acc, ev) => ev.Payload is Ping p ? acc + p.Value : acc);
        Assert.Equal(0, known);
    }

    private static bool LightHasArrived(
        IReadOnlyDictionary<long, ISpatial> spatial, EventRecord ev, long observerId, long atT)
    {
        (long sx, long sy, long sz) = spatial[ev.OriginEntity].PositionMmAt(ev.TimeSeconds);
        (long ox, long oy, long oz) = spatial[observerId].PositionMmAt(atT);
        long dist = IntMath.DistanceMm(ox, oy, oz, sx, sy, sz);
        return dist <= C * (atT - ev.TimeSeconds);
    }
}
