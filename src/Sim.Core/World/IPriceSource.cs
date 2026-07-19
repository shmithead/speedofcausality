namespace Sim.Core.World;

/// <summary>
/// A settlement's live (ground-truth) price for its commodity, queried locally when a ship docks and
/// trades (roadmap §3). This is the price <b>at the port</b>, not what a distant observer believes —
/// a docked ship transacts against the real market, while HQ only ever saw a stale quote (§2.2). The
/// interface lives in World so <see cref="SimWorld"/> can hold a market registry without depending on
/// the Economy subsystem's concrete types.
/// </summary>
public interface IPriceSource
{
    /// <summary>The current ground-truth price in minor currency units (§2.5).</summary>
    long PriceMinorUnits { get; }

    /// <summary>Which commodity this market prices.</summary>
    int CommodityId { get; }
}
