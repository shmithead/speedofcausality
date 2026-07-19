namespace Sim.Core.World;

/// <summary>
/// Anything that has a position in Sol-space at a given sim-time (roadmap §2.5): a body, a
/// settlement fixed to a body, a ship on a tabled trajectory. Positions are heliocentric
/// <b>millimetres</b> — the canonical world-space unit — with the Sun at the origin, matching the
/// ephemeris (Kepler about the focus at the origin).
///
/// <para>The whole knowledge model (§2.2) rests on this one method: reception between a source and
/// an observer is a geometry problem, and both endpoints are just <c>PositionMmAt</c> lookups. A
/// body or an on-rails ship answers it from a table (cheap); only a genuinely deciding, moving
/// endpoint forces the root-find (§2.7).</para>
/// </summary>
public interface ISpatial
{
    /// <summary>Heliocentric position in millimetres at <paramref name="tSeconds"/> since epoch.</summary>
    (long X, long Y, long Z) PositionMmAt(long tSeconds);
}
