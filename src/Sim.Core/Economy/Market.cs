using Sim.Core.Horizons;
using Sim.Core.Rng;
using Sim.Core.Time;
using Sim.Core.World;

namespace Sim.Core.Economy;

/// <summary>
/// A settlement's market for one commodity (roadmap §3), modelled as the event-bounded horizon class
/// (§2.7): a market never free-runs — it is a step function whose steps are scheduled fixings, so its
/// horizon is always "next fixing." When the horizon expires it fixes a new price, emits a
/// <see cref="PriceFixed"/> from its settlement (which then propagates at c, §2.2), and re-arms.
///
/// <para><b>Phase 1 price driver is a placeholder.</b> The roadmap is explicit that "every price move
/// is caused by an arrival" (§2.7) — real supply/demand from cargo and rumor is Phase 3. Until then
/// the price walks within a band with gentle mean reversion, purely so the map has something moving
/// and stale to render; the walk is driven by this market's own named RNG stream (§2.3 r4), so adding
/// it perturbs no other subsystem's rolls, and it is bounded to keep the economy from exploding
/// (§8 risk 4).</para>
/// </summary>
public sealed class Market : HorizonEntity, IPriceSource
{
    private readonly SimWorld _world;
    private readonly long _settlementId;
    private readonly int _commodityId;
    private readonly long _intervalSeconds;
    private readonly long _floorMinorUnits;
    private readonly long _ceilMinorUnits;
    private readonly int _maxStepMinorUnits;
    private readonly RngStream _rng;

    private long _price;
    private int _fixingSeq;

    /// <param name="id">The market's entity id (its horizon-expiry ordinal, §2.3 r8).</param>
    /// <param name="world">The world it emits fixings into and draws its RNG from.</param>
    /// <param name="settlementId">The settlement this market sits at — the origin of its fixings.</param>
    /// <param name="commodityId">The single commodity it prices.</param>
    /// <param name="startPriceMinorUnits">Opening price.</param>
    /// <param name="intervalSeconds">Sim-seconds between fixings (the fixing cadence / horizon length).</param>
    /// <param name="floorMinorUnits">Lower price bound.</param>
    /// <param name="ceilMinorUnits">Upper price bound.</param>
    /// <param name="maxStepMinorUnits">Largest per-fixing random step (before reversion).</param>
    public Market(
        long id,
        SimWorld world,
        long settlementId,
        int commodityId,
        long startPriceMinorUnits,
        long intervalSeconds,
        long floorMinorUnits,
        long ceilMinorUnits,
        int maxStepMinorUnits)
        : base(id, world.NowSeconds + intervalSeconds)
    {
        _world = world;
        _settlementId = settlementId;
        _commodityId = commodityId;
        _intervalSeconds = intervalSeconds;
        _floorMinorUnits = floorMinorUnits;
        _ceilMinorUnits = ceilMinorUnits;
        _maxStepMinorUnits = maxStepMinorUnits;
        _price = startPriceMinorUnits;
        _rng = world.Rng.Fork($"market:{id}");
    }

    /// <summary>The ground-truth current price (minor units) — <b>not</b> what a distant observer sees.</summary>
    public long PriceMinorUnits => _price;

    /// <summary>The commodity this market prices.</summary>
    public int CommodityId => _commodityId;

    /// <summary>The settlement this market sits at.</summary>
    public long SettlementId => _settlementId;

    /// <summary>Ground-truth count of fixings so far — an observer's known <see cref="PriceQuote.FixingSeq"/> lags this.</summary>
    public int FixingSeq => _fixingSeq;

    protected override long Recompute(ISimContext ctx)
    {
        long mid = (_floorMinorUnits + _ceilMinorUnits) / 2;
        long reversion = (mid - _price) / 8; // gentle pull toward the band's centre
        long noise = _rng.NextInt(-_maxStepMinorUnits, _maxStepMinorUnits + 1);
        _price = Clamp(_price + reversion + noise, _floorMinorUnits, _ceilMinorUnits);
        _fixingSeq++;

        _world.Emit(_settlementId, new PriceFixed(_settlementId, _commodityId, _price, _fixingSeq));

        return ctx.NowSeconds + _intervalSeconds;
    }

    // System.Math is banned in Sim.Core (§2.5), so clamp by hand.
    private static long Clamp(long value, long lo, long hi)
        => value < lo ? lo : value > hi ? hi : value;
}
