using System.Text;
using Sim.Core.Economy;
using Sim.Core.Knowledge;
using Sim.Core.Ships;
using Sim.Core.World;

namespace Sim.Core.Diagnostics;

/// <summary>
/// The canonical Phase 1 world (roadmap §5): Earth-HQ, a Mars port with a market, a Ceres port, the
/// player's freighter bound for Mars, and one scripted competitor cycling the three ports. It is the
/// shared fixture behind <c>simtool knows</c> (inspect an entity's knowledge fold at time T, §4) and
/// the Phase 1 determinism check — one seeded, reproducible sim so a cross-OS or cross-commit
/// divergence has something real to bite on. Pure Sim.Core: no IO, no wall-clock (§2.3).
/// </summary>
public static class Phase1Scenario
{
    private const long Day = 86_400L;

    /// <summary>Earth-HQ — also the player's own receiver (the map renders <b>this</b> fold, §2.2).</summary>
    public const long HqId = 100;
    public const long MarsPortId = 101;
    public const long CeresPortId = 102;
    public const long MarsMarketId = 200;
    public const long CeresMarketId = 201;
    public const long PlayerShipId = 300;
    public const long CompetitorId = 301;

    /// <summary>The player firm's opening cash (minor units).</summary>
    public const long StartingCredits = 1_000_000L;

    /// <summary>Builds the seeded world with everything registered and both ships departed.</summary>
    public static SimWorld Build(ulong seed)
    {
        var world = new SimWorld(seed);

        Body earth = SolSystem.BuildBody(Entry(SolSystem.EarthId));
        Body mars = SolSystem.BuildBody(Entry(SolSystem.MarsId));
        Body ceres = SolSystem.BuildBody(Entry(SolSystem.CeresId));
        world.AddEntity(earth.Id, earth, isObserver: false);
        world.AddEntity(mars.Id, mars, isObserver: false);
        world.AddEntity(ceres.Id, ceres, isObserver: false);

        world.AddEntity(HqId, new Settlement(HqId, "Earth-HQ", earth), isObserver: true);
        world.AddEntity(MarsPortId, new Settlement(MarsPortId, "Mars-Port", mars), isObserver: true);
        world.AddEntity(CeresPortId, new Settlement(CeresPortId, "Ceres-Port", ceres), isObserver: true);

        world.Credits = StartingCredits;

        // Two Ore markets with different opening prices, so there is a spread to trade on (buy cheap at
        // Ceres, sell dear at Mars — if the stale quote you routed on still holds when you arrive).
        var marsMarket = new Market(
            MarsMarketId, world, MarsPortId, Commodity.Ore,
            startPriceMinorUnits: 12_000, intervalSeconds: 7 * Day,
            floorMinorUnits: 8_000, ceilMinorUnits: 16_000, maxStepMinorUnits: 800);
        var ceresMarket = new Market(
            CeresMarketId, world, CeresPortId, Commodity.Ore,
            startPriceMinorUnits: 7_000, intervalSeconds: 7 * Day,
            floorMinorUnits: 4_000, ceilMinorUnits: 11_000, maxStepMinorUnits: 700);
        foreach (Market m in new[] { marsMarket, ceresMarket })
        {
            world.Horizons.Register(m);
            world.RegisterMarket(m.SettlementId, m);
        }

        // The player freighter: reports every 10 days, carries 100 units of Ore, trades on the ledger.
        Ship.Depart(
            world, PlayerShipId, HqId, MarsPortId, arriveSeconds: 200 * Day, fuelMmPerSec: 500_000_000L,
            sitrepIntervalSeconds: 10 * Day, cargoCapacity: 100, isPlayer: true);

        ScriptedRoute.Begin(
            world, CompetitorId, new[] { HqId, MarsPortId, CeresPortId },
            legDurationSeconds: 150 * Day, dwellSeconds: 10 * Day, fuelMmPerSec: 5_000_000_000L,
            sitrepIntervalSeconds: 20 * Day);

        return world;
    }

    private static SolSystem.Entry Entry(long id) => SolSystem.Catalog.Single(e => e.Id == id);

    /// <summary>
    /// The whole event log rendered as stable text — one event per line — for cross-OS diffing via
    /// <c>simtool diff</c> and the first-divergence locator (§4). LF-joined, invariant formatting.
    /// </summary>
    public static string EventLogGolden(SimWorld world)
    {
        var sb = new StringBuilder();
        foreach (Events.EventRecord ev in world.Log.Events)
        {
            sb.Append(ev.Id).Append('|')
              .Append(ev.TimeSeconds).Append('|')
              .Append(ev.OriginEntity).Append('|')
              .Append(ev.CausalParent?.ToString() ?? "-").Append('|')
              .Append(ev.Payload.GetType().Name).Append('|')
              .Append(ev.Payload).Append('\n');
        }

        return sb.ToString();
    }

    /// <summary>
    /// What entity <paramref name="observerId"/> knows at <paramref name="atSeconds"/> — the same
    /// stale fold the strategic map would render (§2.2): last-known prices with age, and each ship's
    /// filed plan, ghost, and deviation. This is the body of <c>simtool knows</c>.
    /// </summary>
    public static string KnowledgeGolden(SimWorld world, long observerId, long atSeconds)
    {
        var sb = new StringBuilder();
        sb.Append("knowledge of entity ").Append(observerId).Append(" at ").Append(atSeconds).Append("s\n");

        sb.Append("prices:\n");
        foreach ((long settlementId, PriceQuote q) in PriceBook.Read(world.Knowledge, observerId, atSeconds))
        {
            sb.Append("  settlement ").Append(settlementId)
              .Append(": ").Append(q.PriceMinorUnits)
              .Append(" (seq ").Append(q.FixingSeq)
              .Append(", age ").Append(q.AgeSeconds(atSeconds)).Append("s)\n");
        }

        sb.Append("ships:\n");
        foreach ((long shipId, ShipKnowledge k) in ShipView.Read(world.Knowledge, observerId, atSeconds))
        {
            sb.Append("  ship ").Append(shipId).Append(':');
            sb.Append(k.Plan is { } p
                ? $" plan->{p.DestSettlementId} eta {p.ArriveSeconds}s"
                : " plan->none");
            sb.Append(k.Ghost is { } g
                ? $"; ghost {g.Cause} @({g.X},{g.Y},{g.Z}) t={k.GhostOccurredAtSeconds}s"
                : "; ghost none");
            long? dev = k.DeviationMm();
            sb.Append(dev is { } d ? $"; deviation {d}mm" : "; deviation n/a");
            sb.Append('\n');
        }

        return sb.ToString();
    }
}
