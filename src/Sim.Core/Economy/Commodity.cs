namespace Sim.Core.Economy;

/// <summary>
/// The traded goods (roadmap §3). Phase 1 has exactly one — <b>Ore</b> — kept as a small id/name
/// table so the fiction reads right on the map without hard-coding strings in the frontend.
/// </summary>
public static class Commodity
{
    /// <summary>The single Phase 1 commodity.</summary>
    public const int Ore = 0;

    /// <summary>Display name for a commodity id (rendering/logging only).</summary>
    public static string Name(int commodityId) => commodityId switch
    {
        Ore => "Ore",
        _ => $"commodity {commodityId}",
    };
}
