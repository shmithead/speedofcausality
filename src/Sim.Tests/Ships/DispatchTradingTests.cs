using Sim.Core.Economy;
using Sim.Core.Orbits;
using Sim.Core.Ships;
using Sim.Core.World;

namespace Sim.Tests.Ships;

/// <summary>
/// The playable layer (§3, §5): SITREP position reports, dispatch orders that reach a ship at c, and
/// the buy-low/sell-high trade loop on prices the player only ever saw stale.
/// </summary>
public sealed class DispatchTradingTests
{
    private const long Day = 86_400L;
    private const long EarthId = 1, MarsId = 2, CeresId = 3;
    private const long HqId = 10, MarsPortId = 20, CeresPortId = 30;
    private const long MarsMarketId = 200, CeresMarketId = 201;
    private const long ShipId = 100;

    private static Body Body(long id, string name, KeplerianElements el) => new(id, name, EphemerisTable.Build(el));

    private static SimWorld BuildWorld(bool withMarkets, ulong seed = 42)
    {
        var world = new SimWorld(seed);
        Body earth = Body(EarthId, "Earth", SolSystem.Earth);
        Body mars = Body(MarsId, "Mars", SolSystem.Mars);
        Body ceres = Body(CeresId, "Ceres", SolSystem.Ceres);
        world.AddEntity(EarthId, earth, isObserver: false);
        world.AddEntity(MarsId, mars, isObserver: false);
        world.AddEntity(CeresId, ceres, isObserver: false);
        world.AddEntity(HqId, new Settlement(HqId, "HQ", earth), isObserver: true);
        world.AddEntity(MarsPortId, new Settlement(MarsPortId, "Mars-Port", mars), isObserver: true);
        world.AddEntity(CeresPortId, new Settlement(CeresPortId, "Ceres-Port", ceres), isObserver: true);

        if (withMarkets)
        {
            // Fixed prices (floor==ceil, no step) so the trade spread is deterministic.
            Fixed(world, MarsMarketId, MarsPortId, 12_000);
            Fixed(world, CeresMarketId, CeresPortId, 7_000);
        }

        return world;
    }

    private static void Fixed(SimWorld world, long marketId, long settlementId, long price)
    {
        var m = new Market(marketId, world, settlementId, Commodity.Ore, price, 365 * Day, price, price, 0);
        world.Horizons.Register(m);
        world.RegisterMarket(settlementId, m);
    }

    private static int RoutineCount(SimWorld world) => world.Log.Events
        .Select(e => e.Payload).OfType<Telemetry>().Count(t => t.Cause == TelemetryCause.Routine);

    [Fact]
    public void Sitrep_Emits_Periodic_Position_Reports()
    {
        SimWorld world = BuildWorld(withMarkets: false);
        Ship.Depart(world, ShipId, HqId, MarsPortId, 300 * Day, 500_000_000L, sitrepIntervalSeconds: 5 * Day);
        world.Sim.RunUntil(32 * Day);

        // Reports at ~5,10,15,20,25,30 days.
        Assert.True(RoutineCount(world) >= 5, $"expected periodic SITREPs, saw {RoutineCount(world)}");
    }

    [Fact]
    public void Dispatch_Diverts_An_In_Flight_Ship()
    {
        SimWorld world = BuildWorld(withMarkets: false);
        Ship ship = Ship.Depart(world, ShipId, HqId, MarsPortId, 300 * Day, 500_000_000L, sitrepIntervalSeconds: 10 * Day);

        world.Sim.RunUntil(15 * Day);
        ShipCommands.IssueDispatch(world, ship, HqId, CeresPortId, 10 * Day);
        world.Sim.RunUntil(500 * Day);

        Assert.Equal(CeresPortId, ship.DestSettlementId);
        Assert.True(ship.Arrived);
    }

    [Fact]
    public void Dispatch_Launches_A_Docked_Ship_After_The_Light_Lag()
    {
        SimWorld world = BuildWorld(withMarkets: false);
        Ship ship = Ship.Depart(world, ShipId, HqId, MarsPortId, 100 * Day, 500_000_000L);

        world.Sim.RunUntil(101 * Day);
        Assert.True(ship.Arrived); // docked at Mars

        ShipCommands.IssueDispatch(world, ship, HqId, CeresPortId, 10 * Day);
        world.Sim.RunUntil(600 * Day);

        Assert.Equal(CeresPortId, ship.DestSettlementId);
        Assert.True(ship.Arrived); // reached Ceres
    }

    [Fact]
    public void Buying_Cheap_And_Selling_Dear_Profits_The_Spread()
    {
        SimWorld world = BuildWorld(withMarkets: true);
        world.Credits = 5_000_000;

        // Park the ship at Ceres (buy at 7,000), empty — its first arrival sells nothing.
        Ship ship = Ship.Depart(world, ShipId, HqId, CeresPortId, 100 * Day, 500_000_000L, cargoCapacity: 100, isPlayer: true);
        world.Sim.RunUntil(101 * Day);
        Assert.True(ship.Arrived);
        long beforeRun = world.Credits;

        // Send it to Mars (sell at 12,000): buys 100 @ 7,000 on departure, sells 100 @ 12,000 on arrival.
        ShipCommands.IssueDispatch(world, ship, HqId, MarsPortId, 10 * Day);
        world.Sim.RunUntil(600 * Day);
        Assert.True(ship.Arrived);

        Assert.Equal(beforeRun + (100 * (12_000 - 7_000)), world.Credits);
        Assert.Equal(0, ship.CargoUnits); // sold out at Mars
    }

    [Fact]
    public void A_Non_Player_Ship_Never_Touches_The_Ledger()
    {
        SimWorld world = BuildWorld(withMarkets: true);
        world.Credits = 5_000_000;
        Ship.Depart(world, ShipId, HqId, MarsPortId, 100 * Day, 500_000_000L, cargoCapacity: 100, isPlayer: false);
        world.Sim.RunUntil(150 * Day);

        Assert.Equal(5_000_000, world.Credits);
    }
}
