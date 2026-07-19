using Sim.Core.Knowledge;

namespace Sim.Core.Economy;

/// <summary>One observer's most recent <i>arrived</i> quote for a commodity at a settlement (§2.2).</summary>
/// <param name="SettlementId">Where the quote came from.</param>
/// <param name="CommodityId">Which commodity.</param>
/// <param name="PriceMinorUnits">The quoted price (minor units).</param>
/// <param name="FixingSeq">The fixing's sequence number, so a later fold never regresses to an older quote.</param>
/// <param name="OccurredAtSeconds">When the fixing <i>happened</i> — subtract from "now" for the quote's age.</param>
public readonly record struct PriceQuote(
    long SettlementId,
    int CommodityId,
    long PriceMinorUnits,
    int FixingSeq,
    long OccurredAtSeconds)
{
    /// <summary>How stale this quote is at <paramref name="nowSeconds"/> — the light-lag made visible (§2.2).</summary>
    public long AgeSeconds(long nowSeconds) => nowSeconds - OccurredAtSeconds;
}

/// <summary>
/// The "last-known prices with age indicators" the Phase 1 map shows (roadmap §5). It is a pure
/// projection of one observer's knowledge fold (§2.2): fold the <see cref="PriceFixed"/> events that
/// have <b>arrived</b> at the observer, keeping the newest per settlement. Nothing here is ground
/// truth — a quote is exactly as old as the geometry between the observer and that settlement, which
/// is the whole point of the render rule (the screen shows only what has arrived).
/// </summary>
public static class PriceBook
{
    /// <summary>
    /// The observer's latest arrived quote per settlement at <paramref name="atSeconds"/>, keyed and
    /// iterated by settlement id (§2.3 r3).
    /// </summary>
    public static IReadOnlyDictionary<long, PriceQuote> Read(
        KnowledgeProjection knowledge,
        long observerId,
        long atSeconds)
    {
        return knowledge.Fold(
            observerId,
            atSeconds,
            new SortedDictionary<long, PriceQuote>(),
            (acc, ev) =>
            {
                if (ev.Payload is PriceFixed pf
                    && (!acc.TryGetValue(pf.SettlementId, out PriceQuote existing) || pf.FixingSeq > existing.FixingSeq))
                {
                    acc[pf.SettlementId] = new PriceQuote(
                        pf.SettlementId, pf.CommodityId, pf.PriceMinorUnits, pf.FixingSeq, ev.TimeSeconds);
                }

                return acc;
            });
    }
}
