using Sim.Core.Horizons;
using Sim.Core.Numerics;
using Sim.Core.Rng;
using Sim.Core.Time;
using Sim.Core.World;

namespace Sim.Core.Economy;

/// <summary>
/// A settlement's market for one commodity (roadmap §3). The price is a <b>continuous, deterministic
/// function of time</b> — <see cref="PriceAt"/> — that a distant observer receives as a feed, light-
/// delayed: HQ sampling <c>PriceAt(now − |HQ−port|/c)</c> sees the market exactly as it was one light-
/// time ago, constantly updating. That is the information-lag mechanic applied to prices (§2.2): the
/// value emitted at the port at time τ cannot reach HQ before τ + the light-time.
///
/// <para><b>Phase 1 price driver is a placeholder.</b> The roadmap wants "every price move caused by an
/// arrival" (§2.7); real supply/demand is Phase 3. Until then the curve is two seeded sinusoids inside
/// a band — smooth, bounded (§8 risk 4), and seed-dependent (its phases are drawn from this market's
/// own named RNG stream, §2.3 r4) so replays with different world seeds differ, but a given seed is
/// exactly reproducible. It still emits periodic <see cref="PriceFixed"/> snapshots for the event log
/// and the discrete knowledge fold; the live continuous feed is the map's view.</para>
/// </summary>
public sealed class Market : HorizonEntity, IPriceSource
{
    private static readonly Fixed64 TwoPi = Fixed64.FromDouble(6.283185307179586);
    private static readonly Fixed64 WeightA = Fixed64.FromDouble(0.6);
    private static readonly Fixed64 WeightB = Fixed64.FromDouble(0.4);

    private readonly SimWorld _world;
    private readonly long _settlementId;
    private readonly int _commodityId;
    private readonly long _intervalSeconds;
    private readonly long _floorMinorUnits;
    private readonly long _ceilMinorUnits;

    private readonly long _center;
    private readonly long _amplitude;
    private readonly long _periodA;
    private readonly long _periodB;
    private readonly long _phaseA;
    private readonly long _phaseB;

    private int _fixingSeq;

    /// <param name="id">The market's entity id (its horizon-expiry ordinal, §2.3 r8).</param>
    /// <param name="world">The world it emits fixings into and draws its seed from.</param>
    /// <param name="settlementId">The settlement this market sits at — the origin of its feed.</param>
    /// <param name="commodityId">The single commodity it prices.</param>
    /// <param name="startPriceMinorUnits">The price the curve oscillates around.</param>
    /// <param name="intervalSeconds">Sim-seconds between recorded <see cref="PriceFixed"/> snapshots.</param>
    /// <param name="floorMinorUnits">Lower price bound.</param>
    /// <param name="ceilMinorUnits">Upper price bound.</param>
    /// <param name="maxStepMinorUnits">Unused since the price became a continuous curve; kept for call-site compatibility.</param>
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
        _ = maxStepMinorUnits;
        _world = world;
        _settlementId = settlementId;
        _commodityId = commodityId;
        _intervalSeconds = intervalSeconds;
        _floorMinorUnits = floorMinorUnits;
        _ceilMinorUnits = ceilMinorUnits;

        _center = startPriceMinorUnits;
        long belowCeil = ceilMinorUnits - startPriceMinorUnits;
        long aboveFloor = startPriceMinorUnits - floorMinorUnits;
        long room = aboveFloor < belowCeil ? aboveFloor : belowCeil; // System.Math is banned (§2.5)
        _amplitude = room > 0 ? room : 0;

        // Seed the curve's periods and phases so different world seeds give different price histories,
        // but a given seed is exactly reproducible (§2.3 r4). Periods ~days.
        RngStream rng = world.Rng.Fork($"market:{id}");
        _periodA = (8 + rng.NextInt(0, 6)) * 86_400L;
        _periodB = (3 + rng.NextInt(0, 3)) * 86_400L;
        _phaseA = rng.NextInt(0, (int)_periodA);
        _phaseB = rng.NextInt(0, (int)_periodB);
    }

    /// <summary>The ground-truth current price at the port (§2.2) — what a docked ship trades against, not what HQ sees.</summary>
    public long PriceMinorUnits => PriceAt(_world.NowSeconds);

    /// <summary>The commodity this market prices.</summary>
    public int CommodityId => _commodityId;

    /// <summary>The settlement this market sits at.</summary>
    public long SettlementId => _settlementId;

    /// <summary>Count of recorded snapshots so far — a discrete observer's known quote lags this.</summary>
    public int FixingSeq => _fixingSeq;

    /// <summary>
    /// The deterministic price (minor units) at sim-time <paramref name="tSeconds"/>. Sample it at
    /// <c>now − lightTime</c> to see the light-delayed feed an observer holds (§2.2). Pure, integer /
    /// fixed-point, no <c>System.Math</c> transcendentals (§2.5), so it is replay-identical cross-OS.
    /// </summary>
    public long PriceAt(long tSeconds)
    {
        if (_amplitude <= 0)
        {
            return _center;
        }

        Fixed64 combined = (Sine(tSeconds + _phaseA, _periodA) * WeightA)
                         + (Sine(tSeconds + _phaseB, _periodB) * WeightB);
        long offset = (long)(((System.Int128)_amplitude * combined.Raw) >> Fixed64.FractionBits);
        return Clamp(_center + offset, _floorMinorUnits, _ceilMinorUnits);
    }

    protected override long Recompute(ISimContext ctx)
    {
        _fixingSeq++;
        _world.Emit(_settlementId, new PriceFixed(_settlementId, _commodityId, PriceAt(ctx.NowSeconds), _fixingSeq));
        return ctx.NowSeconds + _intervalSeconds;
    }

    // sin(2π · frac(t / period)) on the deterministic fixed-point CORDIC — integer phase reduction first
    // so 2π·t never overflows Fixed64.
    private static Fixed64 Sine(long t, long period)
    {
        long r = ((t % period) + period) % period; // [0, period)
        long fracRaw = (long)(((System.Int128)r << Fixed64.FractionBits) / period);
        (Fixed64 sin, _) = Trig.SinCos(Fixed64.FromRaw(fracRaw) * TwoPi);
        return sin;
    }

    private static long Clamp(long value, long lo, long hi)
        => value < lo ? lo : value > hi ? hi : value;
}
