using Sim.Core.Numerics;
using Sim.Core.Orbits;
using Sim.Core.Ships;
using Sim.Core.World;

namespace Sim.Tests.Ships;

/// <summary>
/// Stage C: the scripted competitor (roadmap §5) flies a fixed cycle, re-departing on schedule, and
/// broadcasts plans/telemetry the player only ever sees stale (§2.2).
/// </summary>
public sealed class ScriptedRouteTests
{
    private const long Day = 86_400L;
    private const long EarthId = 1, MarsId = 2, CeresId = 3;
    private const long HqId = 10, MarsPortId = 20, CeresPortId = 30;
    private const long CompId = 200;
    private const long Fuel = 5_000_000_000L;

    private static Body Body(long id, string name, double a, double e, double i, double m0, long period)
    {
        var el = new KeplerianElements
        {
            SemiMajorAxisGm = Fixed64.FromDouble(a),
            Eccentricity = Fixed64.FromDouble(e),
            InclinationRad = Fixed64.FromDouble(i),
            AscendingNodeRad = Fixed64.Zero,
            ArgPeriapsisRad = Fixed64.Zero,
            MeanAnomalyEpochRad = Fixed64.FromDouble(m0),
            PeriodSeconds = period,
        };
        return new Body(id, name, EphemerisTable.Build(el));
    }

    private static SimWorld BuildWorld(ulong seed = 42)
    {
        var world = new SimWorld(seed);
        Body earth = Body(EarthId, "Earth", 149.6, 0.0167, 0.0, 6.240, 31_558_150L);
        Body mars = Body(MarsId, "Mars", 227.9, 0.0934, 0.0323, 0.338, 59_355_000L);
        Body ceres = Body(CeresId, "Ceres", 413.8, 0.0758, 0.1857, 1.541, 145_138_000L);
        world.AddEntity(EarthId, earth, isObserver: false);
        world.AddEntity(MarsId, mars, isObserver: false);
        world.AddEntity(CeresId, ceres, isObserver: false);
        world.AddEntity(HqId, new Settlement(HqId, "Earth-HQ", earth), isObserver: true);
        world.AddEntity(MarsPortId, new Settlement(MarsPortId, "Mars-Port", mars), isObserver: true);
        world.AddEntity(CeresPortId, new Settlement(CeresPortId, "Ceres-Port", ceres), isObserver: true);
        return world;
    }

    private static long[] FiledDestinations(SimWorld world)
        => world.Log.Events
            .Select(e => e.Payload)
            .OfType<FlightPlanFiled>()
            .Select(p => p.DestSettlementId)
            .ToArray();

    [Fact]
    public void Competitor_Cycles_Through_Its_Stops_In_Order()
    {
        SimWorld world = BuildWorld();
        long[] stops = { HqId, MarsPortId, CeresPortId };
        ScriptedRoute.Begin(world, CompId, stops, legDurationSeconds: 100 * Day, dwellSeconds: 5 * Day, Fuel);

        world.Sim.RunUntil(320 * Day);

        // Departures at t=0 (Hq→Mars), 105d (Mars→Ceres), 210d (Ceres→Hq), 315d (Hq→Mars).
        long[] dests = FiledDestinations(world);
        Assert.Equal(new[] { MarsPortId, CeresPortId, HqId, MarsPortId }, dests);
    }

    [Fact]
    public void Competitor_Actually_Arrives_Between_Legs()
    {
        SimWorld world = BuildWorld();
        long[] stops = { HqId, MarsPortId, CeresPortId };
        ScriptedRoute route = ScriptedRoute.Begin(world, CompId, stops, 100 * Day, 5 * Day, Fuel);

        world.Sim.RunUntil(320 * Day);

        int arrivals = world.Log.Events
            .Select(e => e.Payload)
            .OfType<Telemetry>()
            .Count(t => t.Cause == TelemetryCause.Arrived);
        Assert.True(arrivals >= 3, $"expected at least 3 arrivals over three legs, saw {arrivals}");
        Assert.True(route.Ship.FuelRemaining < Fuel, "several burns should have spent fuel");
    }

    [Fact]
    public void Scripted_Route_Is_Deterministic()
    {
        long[] Run(ulong seed)
        {
            SimWorld world = BuildWorld(seed);
            long[] stops = { HqId, MarsPortId, CeresPortId };
            ScriptedRoute.Begin(world, CompId, stops, 100 * Day, 5 * Day, Fuel);
            world.Sim.RunUntil(320 * Day);
            return FiledDestinations(world);
        }

        Assert.Equal(Run(3), Run(3));
    }
}
