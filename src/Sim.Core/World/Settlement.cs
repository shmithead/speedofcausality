namespace Sim.Core.World;

/// <summary>
/// A populated place with a market and a receiver (roadmap §3), fixed to a host <see cref="Body"/>.
/// Phase 1 co-locates a settlement with its body's centre: the interplanetary distances that drive
/// information-lag dwarf any surface offset, so the position is simply the body's ephemeris lookup.
/// A settlement is an observer (it learns of prices and arrivals) and an origin (its fixings and
/// telemetry emit from here).
/// </summary>
public sealed class Settlement : ISpatial
{
    private readonly Body _host;

    public Settlement(long id, string name, Body host)
    {
        Id = id;
        Name = name;
        _host = host;
    }

    /// <summary>Stable identity, shared with <see cref="Events.EventRecord.OriginEntity"/>.</summary>
    public long Id { get; }

    /// <summary>Display name (rendering/logging only).</summary>
    public string Name { get; }

    /// <summary>The body this settlement sits on.</summary>
    public Body Host => _host;

    /// <inheritdoc/>
    public (long X, long Y, long Z) PositionMmAt(long tSeconds) => _host.PositionMmAt(tSeconds);
}
