using Sim.Core.Events;

namespace Sim.Core.Economy;

/// <summary>
/// A settlement's market fixed a new price for a commodity (roadmap §3, "prices fix at intervals;
/// fixings are events; they propagate at c like everything else"). This is the step in the market's
/// step function (§2.7) — and, crucially, it is an <i>occurrence</i> at the settlement, not something
/// the player knows: the price reaches other observers only after the light-lag from that settlement
/// (§2.2), which is what makes a distant price <b>stale</b> on the strategic map.
///
/// <para>Money is integer minor currency units (§2.5) — never a float.</para>
/// </summary>
/// <param name="SettlementId">Where the fixing happened (the signal's origin).</param>
/// <param name="CommodityId">Which commodity (Phase 1 has one).</param>
/// <param name="PriceMinorUnits">The new price, in minor currency units.</param>
/// <param name="FixingSeq">Monotonic per-market fixing counter — lets an observer tell a newer quote from an older one.</param>
public sealed record PriceFixed(
    long SettlementId,
    int CommodityId,
    long PriceMinorUnits,
    int FixingSeq) : IEventPayload
{
    public int SchemaVersion => 1;
}
