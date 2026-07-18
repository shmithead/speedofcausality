using Sim.Core.Time;

namespace Sim.Core.Horizons;

/// <summary>
/// An object with a <b>validity horizon</b> (roadmap §2.7): the time until which its future is
/// closed-form. Nothing is computed inside the horizon — the entity is "on rails." Work happens
/// only when the horizon expires (a scheduled event) or when something invalidates it early.
///
/// <para>This is the event-bounded class: a ship on a committed burn (horizon = next decision
/// point), a market (horizon = next fixing). The infinite-horizon class — celestial bodies — is
/// simply never registered with the <see cref="HorizonManager"/>: an almanac never expires.</para>
/// </summary>
public abstract class HorizonEntity
{
    /// <summary>Stable identity. Also the tiebreak that orders simultaneous expiries (§2.3 r8).</summary>
    public long Id { get; }

    /// <summary>Time (seconds) until which this entity is on rails. Set by the manager.</summary>
    public long Horizon { get; internal set; }

    /// <summary>How many times this entity has actually recomputed — the sparse-tick witness.</summary>
    public int RecomputeCount { get; private set; }

    // Bumped on every (re)schedule so a stale queued expiry can be recognized and ignored
    // (lazy cancellation — the priority queue has no remove).
    internal int Generation { get; set; }

    protected HorizonEntity(long id, long initialHorizon)
    {
        Id = id;
        Horizon = initialHorizon;
    }

    /// <summary>
    /// The expensive work: the entity re-decides now that its horizon has expired, and returns its
    /// <b>new</b> horizon (which must be strictly in the future). May schedule follow-up events via
    /// <paramref name="ctx"/>.
    /// </summary>
    protected abstract long Recompute(ISimContext ctx);

    internal long InvokeRecompute(ISimContext ctx)
    {
        RecomputeCount++;
        return Recompute(ctx);
    }
}
