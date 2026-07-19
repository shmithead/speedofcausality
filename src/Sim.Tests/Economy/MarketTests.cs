using Sim.Core.Comms;
using Sim.Core.Economy;
using Sim.Core.Numerics;
using Sim.Core.Orbits;
using Sim.Core.World;

namespace Sim.Tests.Economy;

/// <summary>
/// Stage B: a settlement's price fixings propagate at c (§2.2). The player at Earth-HQ never sees a
/// Mars quote sooner than the light-lag allows — "last-known prices with age indicators" (§5) is a
/// stale fold, not ground truth.
/// </summary>
public sealed class MarketTests
{
    private const long AuMm = 149_597_870_700_000L;
    private const long C = Reception.SpeedOfLightMmPerSec;

    private const long EarthId = 1;
    private const long MarsId = 2;
    private const long HqId = 10;      // settlement + observer at Earth
    private const long MarsPortId = 20; // settlement + observer at Mars
    private const long MarsMarketId = 200;
    private const int Ore = 0;

    // Earth-like and Mars-like elements (a in Gm, period in s) — mirrors the Kepler test corpus.
    private static Body Earth() => Body(EarthId, "Earth", a: 149.6, e: 0.0167, i: 0.0, m0: 6.240, period: 31_558_150L);
    private static Body Mars() => Body(MarsId, "Mars", a: 227.9, e: 0.0934, i: 0.0323, m0: 0.338, period: 59_355_000L);

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

    private static (SimWorld World, Market Market) BuildScenario(ulong seed = 42)
    {
        var world = new SimWorld(seed);
        Body earth = Earth(), mars = Mars();
        var hq = new Settlement(HqId, "Earth-HQ", earth);
        var marsPort = new Settlement(MarsPortId, "Mars-Port", mars);

        world.AddEntity(EarthId, earth, isObserver: false);
        world.AddEntity(MarsId, mars, isObserver: false);
        world.AddEntity(HqId, hq, isObserver: true);
        world.AddEntity(MarsPortId, marsPort, isObserver: true);

        // Fixings every 60 s — far shorter than the Earth-Mars light-lag (~3-22 min), so HQ is always
        // several fixings behind ground truth.
        var market = new Market(
            MarsMarketId, world, MarsPortId, Ore,
            startPriceMinorUnits: 10_000, intervalSeconds: 60,
            floorMinorUnits: 5_000, ceilMinorUnits: 15_000, maxStepMinorUnits: 400);
        world.Horizons.Register(market);

        return (world, market);
    }

    [Fact]
    public void Hq_Sees_Mars_Prices_Only_After_The_Light_Lag()
    {
        (SimWorld world, Market market) = BuildScenario();
        const long until = 7200; // two sim-hours
        world.Sim.RunUntil(until);

        IReadOnlyDictionary<long, PriceQuote> book = PriceBook.Read(world.Knowledge, HqId, until);

        Assert.True(book.ContainsKey(MarsPortId), "HQ should have learned some Mars quote by now");
        PriceQuote quote = book[MarsPortId];

        // HQ's newest known fixing is strictly older than ground truth — it is still in flight.
        Assert.True(quote.FixingSeq < market.FixingSeq,
            $"HQ seq {quote.FixingSeq} should lag ground-truth seq {market.FixingSeq}");

        // First light is the floor: the quote HQ holds was emitted at least the Earth-Mars light-time
        // ago. Near-circular coplanar orbits keep them ≥ ~0.37 AU apart (~185 light-s); assert a safe
        // lower bound.
        long minLightSeconds = (long)((37 * AuMm / 100) / C); // 0.37 AU / c
        Assert.True(quote.AgeSeconds(until) >= minLightSeconds,
            $"quote age {quote.AgeSeconds(until)}s must be ≥ light-lag floor {minLightSeconds}s");
    }

    [Fact]
    public void A_Just_Fixed_Price_Is_Not_Yet_Known_At_Hq()
    {
        (SimWorld world, Market market) = BuildScenario();

        // Advance to just after a fixing, then check HQ has NOT yet received that newest fixing.
        world.Sim.RunUntil(600); // ten fixings have occurred at Mars
        int groundTruthSeq = market.FixingSeq;

        IReadOnlyDictionary<long, PriceQuote> book = PriceBook.Read(world.Knowledge, HqId, world.NowSeconds);
        if (book.TryGetValue(MarsPortId, out PriceQuote quote))
        {
            Assert.True(quote.FixingSeq < groundTruthSeq, "the latest Mars fixing cannot have reached Earth yet");
        }
        else
        {
            // Even the first fixing hasn't crossed to Earth yet — also valid this early.
            Assert.True(groundTruthSeq >= 1);
        }
    }

    [Fact]
    public void Run_Is_Deterministic_Same_Seed_Same_Log()
    {
        (SimWorld a, _) = BuildScenario(seed: 7);
        (SimWorld b, _) = BuildScenario(seed: 7);
        a.Sim.RunUntil(7200);
        b.Sim.RunUntil(7200);

        Assert.Equal(a.Log.Count, b.Log.Count);
        for (int i = 0; i < a.Log.Count; i++)
        {
            Assert.Equal(a.Log.Events[i].TimeSeconds, b.Log.Events[i].TimeSeconds);
            Assert.Equal(a.Log.Events[i].Payload, b.Log.Events[i].Payload);
        }
    }
}
