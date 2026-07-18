namespace Sim.Core.Rng;

/// <summary>
/// Forks named PRNG streams from a single world seed (roadmap §1, §2.3 r4). Each subsystem —
/// orbits, market, captains, combat narration — draws from its own stream, derived purely from
/// <c>(worldSeed, name)</c>. The point: <b>adding a subsystem never perturbs another's rolls</b>,
/// because a stream's seed depends only on its own name, not on call order or how many other
/// streams exist.
/// </summary>
public sealed class RngStreams
{
    private readonly ulong _worldSeed;

    public RngStreams(ulong worldSeed)
    {
        _worldSeed = worldSeed;
    }

    /// <summary>Returns the independent stream for <paramref name="name"/>. Same name → same stream.</summary>
    public RngStream Fork(string name)
    {
        ulong subSeed = Combine(_worldSeed, HashName(name));
        return new RngStream(subSeed);
    }

    // Deterministic, culture-independent hash over raw char codes (FNV-1a, 64-bit).
    private static ulong HashName(string name)
    {
        ulong hash = 1469598103934665603UL; // offset basis
        foreach (char c in name)
        {
            hash ^= c;
            hash *= 1099511628211UL; // FNV prime
        }

        return hash;
    }

    private static ulong Combine(ulong worldSeed, ulong nameHash)
        => worldSeed ^ (nameHash * 0x9E3779B97F4A7C15UL);
}
