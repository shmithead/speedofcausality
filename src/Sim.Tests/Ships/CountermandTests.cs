using Sim.Core.Numerics;
using Sim.Core.Orbits;
using Sim.Core.Ships;
using Sim.Core.World;

namespace Sim.Tests.Ships;

/// <summary>
/// Stage C: filed plans, the countermand race, and caused deviation (roadmap §5). A countermand only
/// takes effect when its signal reaches the ship (§2.2); land it in time and the ship diverts (a
/// caused deviation the player reads late), land it late and the order is moot.
/// </summary>
public sealed class CountermandTests
{
    private const long AuMm = 149_597_870_700_000L;
    private const long Day = 86_400L;

    private const long EarthId = 1, MarsId = 2, CeresId = 3;
    private const long HqId = 10, MarsPortId = 20, CeresPortId = 30;
    private const long ShipId = 100;
    private const long Fuel = 200_000_000L; // 200 km/s delta-v budget

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

    [Fact]
    public void Countermand_That_Arrives_In_Time_Diverts_The_Ship()
    {
        SimWorld world = BuildWorld();
        Ship ship = Ship.Depart(world, ShipId, HqId, MarsPortId, arriveSeconds: 180 * Day, fuelMmPerSec: Fuel);
        long fuelAfterDeparture = ship.FuelRemaining;

        // 15 days out, well clear of Earth, order a diversion to Ceres.
        world.Sim.RunUntil(15 * Day);
        SimClockAssertBurnNotYetSpent(ship, fuelAfterDeparture);
        ShipCommands.IssueCountermand(world, ship, HqId, CeresPortId);

        world.Sim.RunUntil(400 * Day);

        Assert.Equal(CeresPortId, ship.DestSettlementId);
        Assert.True(ship.Arrived, "the diverted ship should reach Ceres");
        Assert.True(ship.FuelRemaining < fuelAfterDeparture, "the diversion burn should have spent fuel");

        // The player at HQ eventually reads the ghost off its filed (Mars) plan — the caused deviation.
        IReadOnlyDictionary<long, ShipKnowledge> view = ShipView.Read(world.Knowledge, HqId, world.NowSeconds);
        ShipKnowledge k = view[ShipId];
        long? deviation = k.DeviationMm();
        Assert.NotNull(deviation);
        Assert.True(deviation!.Value > AuMm / 10, $"ghost should be well off the Mars plan, was {deviation} mm");
    }

    [Fact]
    public void Countermand_That_Arrives_Too_Late_Is_Moot()
    {
        SimWorld world = BuildWorld();
        Ship ship = Ship.Depart(world, ShipId, HqId, MarsPortId, arriveSeconds: 180 * Day, fuelMmPerSec: Fuel);
        long fuelAfterDeparture = ship.FuelRemaining;

        // Let the ship dock at Mars first, THEN order the diversion — first light was not fast enough.
        world.Sim.RunUntil(181 * Day);
        Assert.True(ship.Arrived);

        ShipCommands.IssueCountermand(world, ship, HqId, CeresPortId);
        world.Sim.RunUntil(400 * Day);

        Assert.Equal(MarsPortId, ship.DestSettlementId);
        Assert.Equal(fuelAfterDeparture, ship.FuelRemaining); // no second burn
    }

    [Fact]
    public void Departure_Ghost_Sits_On_The_Filed_Plan()
    {
        SimWorld world = BuildWorld();
        Ship.Depart(world, ShipId, HqId, MarsPortId, arriveSeconds: 180 * Day, fuelMmPerSec: Fuel);

        // Run far enough for the departure plan + ghost to reach HQ, but before any decision.
        world.Sim.RunUntil(2 * Day);
        IReadOnlyDictionary<long, ShipKnowledge> view = ShipView.Read(world.Knowledge, HqId, world.NowSeconds);

        Assert.True(view.TryGetValue(ShipId, out ShipKnowledge? k));
        long? deviation = k!.DeviationMm();
        Assert.NotNull(deviation);
        Assert.True(deviation!.Value <= 2, $"a departure ghost must sit on its own plan, was {deviation} mm");
    }

    [Fact]
    public void Diversion_Is_Deterministic()
    {
        long DivertAndHash(ulong seed)
        {
            SimWorld world = BuildWorld(seed);
            Ship ship = Ship.Depart(world, ShipId, HqId, MarsPortId, 180 * Day, Fuel);
            world.Sim.RunUntil(15 * Day);
            ShipCommands.IssueCountermand(world, ship, HqId, CeresPortId);
            world.Sim.RunUntil(400 * Day);
            (long x, long y, long z) = ship.PositionMmAt(world.NowSeconds);
            return x ^ (y * 31) ^ (z * 131) ^ ship.FuelRemaining;
        }

        Assert.Equal(DivertAndHash(1), DivertAndHash(1));
    }

    // The diversion burn must not have been charged yet at the moment we issue the order.
    private static void SimClockAssertBurnNotYetSpent(Ship ship, long fuelAfterDeparture)
        => Assert.Equal(fuelAfterDeparture, ship.FuelRemaining);
}
