using Sim.Core.Orbits;

namespace Sim.Core.World;

/// <summary>
/// A celestial body (roadmap §2.7, the infinite-horizon class). Its position is a precomputed
/// <see cref="EphemerisTable"/> lookup — "trig becomes a memory read" — so a body is never
/// registered with the <see cref="Horizons.HorizonManager"/>: an almanac never expires, and nobody
/// transmits Neptune's position because that isn't news. Everyone in the fiction already has the
/// ephemeris, so a body is common knowledge, not something that has to arrive.
/// </summary>
public sealed class Body : ISpatial
{
    private readonly EphemerisTable _ephemeris;

    public Body(long id, string name, EphemerisTable ephemeris)
    {
        Id = id;
        Name = name;
        _ephemeris = ephemeris;
    }

    /// <summary>Stable identity, shared with <see cref="Events.EventRecord.OriginEntity"/>.</summary>
    public long Id { get; }

    /// <summary>Display name (rendering/logging only — never a determinism input).</summary>
    public string Name { get; }

    /// <inheritdoc/>
    public (long X, long Y, long Z) PositionMmAt(long tSeconds) => _ephemeris.PositionMmAt(tSeconds);
}
