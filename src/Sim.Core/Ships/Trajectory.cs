namespace Sim.Core.Ships;

/// <summary>
/// A ship's path as a sequence of <b>constant-velocity legs</b> (roadmap §5, "impulse burns, fuel"):
/// an impulse burn changes velocity instantaneously; between burns the ship coasts in a straight
/// line. This is the Phase 1 simplification the roadmap phases in deliberately — the full Keplerian
/// transfer / Lambert solver is Phase 2/3 (§3, "transfers are a precomputed table, not a live
/// solve"). A straight-line coast is enough to exercise what Phase 1 actually tests: filed plans,
/// stale telemetry, and <i>caused</i> deviation.
///
/// <para>This is the event-bounded horizon class realized (§2.7): a coasting leg is closed-form, so
/// a ship on a committed leg is "on rails" — <see cref="PositionMmAt"/> is a table read, and the
/// reception root-find collapses (§2.2). Positions are heliocentric millimetres; velocities are
/// millimetres per second (§2.5). Each leg re-bases position at its own start so
/// <c>velocity · Δt</c> never overflows int64 for realistic leg durations.</para>
/// </summary>
public sealed class Trajectory
{
    /// <summary>One coast leg: from <see cref="StartSeconds"/> the ship is at (X,Y,Z) moving at (Vx,Vy,Vz).</summary>
    public readonly record struct Leg(
        long StartSeconds,
        long X, long Y, long Z,
        long Vx, long Vy, long Vz);

    private readonly List<Leg> _legs = new();

    /// <summary>Starts a trajectory with an initial coast leg (the first burn's outcome, or a drift).</summary>
    public Trajectory(Leg initial) => _legs.Add(initial);

    /// <summary>The legs in time order (rendering / inspection).</summary>
    public IReadOnlyList<Leg> Legs => _legs;

    /// <summary>The velocity the ship is coasting at as of the latest leg (mm/s).</summary>
    public (long Vx, long Vy, long Vz) CurrentVelocity
    {
        get
        {
            Leg last = _legs[^1];
            return (last.Vx, last.Vy, last.Vz);
        }
    }

    /// <summary>
    /// Applies an impulse burn at <paramref name="atSeconds"/>: the ship's position is continuous, its
    /// velocity jumps by <paramref name="dvx"/>/<paramref name="dvy"/>/<paramref name="dvz"/> (mm/s).
    /// A new leg begins here. The burn time must not precede the current leg's start.
    /// </summary>
    public void Burn(long atSeconds, long dvx, long dvy, long dvz)
    {
        Leg current = _legs[^1];
        if (atSeconds < current.StartSeconds)
        {
            throw new InvalidOperationException(
                $"Burn at {atSeconds}s precedes the current leg start {current.StartSeconds}s.");
        }

        (long x, long y, long z) = PositionMmAt(atSeconds);
        _legs.Add(new Leg(atSeconds, x, y, z, current.Vx + dvx, current.Vy + dvy, current.Vz + dvz));
    }

    /// <summary>
    /// Begins a new leg from an <b>explicit</b> position and absolute velocity. Used when a ship leaves
    /// a dock: its start is the port body's current position, not an extrapolation of the arrival coast
    /// (a docked ship rides its body, so the old leg's straight-line continuation has drifted away).
    /// The start time must not precede the current leg's start.
    /// </summary>
    public void DepartFrom(long atSeconds, long x, long y, long z, long vx, long vy, long vz)
    {
        if (atSeconds < _legs[^1].StartSeconds)
        {
            throw new InvalidOperationException(
                $"Departure at {atSeconds}s precedes the current leg start {_legs[^1].StartSeconds}s.");
        }

        _legs.Add(new Leg(atSeconds, x, y, z, vx, vy, vz));
    }

    /// <summary>
    /// Position at <paramref name="tSeconds"/> from the active leg (the last one starting at or before
    /// <paramref name="tSeconds"/>): <c>start + velocity · (t − legStart)</c>. Before the first leg's
    /// start it extrapolates leg 0 backward — callers only ever query a ship at or after it exists.
    /// </summary>
    public (long X, long Y, long Z) PositionMmAt(long tSeconds)
    {
        Leg leg = _legs[0];
        for (int i = 1; i < _legs.Count; i++)
        {
            if (_legs[i].StartSeconds <= tSeconds)
            {
                leg = _legs[i];
            }
            else
            {
                break;
            }
        }

        long dt = tSeconds - leg.StartSeconds;
        return (leg.X + (leg.Vx * dt), leg.Y + (leg.Vy * dt), leg.Z + (leg.Vz * dt));
    }
}
