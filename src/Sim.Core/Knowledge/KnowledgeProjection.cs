using Sim.Core.Comms;
using Sim.Core.Events;
using Sim.Core.World;

namespace Sim.Core.Knowledge;

/// <summary>
/// The information-lag mechanic, as a query (roadmap §2.2): <c>Knowledge(entity, T)</c> is the fold
/// over exactly the events whose signal has <b>reached</b> that entity by time <c>T</c> and that were
/// in scope for it. The player's strategic map is this fold for the player's receiver — extrapolated
/// positions, stale prices — never ground truth. Captains, rivals, the board and the regulator each
/// read their own fold through the same one mechanism.
///
/// <para><b>Reception is geometry, not <c>occurrence + distance/c</c>.</b> A signal leaves the origin
/// from wherever the origin <i>was</i> at emission (<c>ev.TimeSeconds</c>); the observer, still
/// moving, intercepts the expanding sphere. That is the light-cone equation, solved by
/// <see cref="Reception.Solve"/> on lookups (§2.5) — exact for a moving observer, and cheap because
/// both endpoints are table reads unless the observer is a deciding, moving endpoint (§2.7).</para>
///
/// <para>This type computes the <b>filter</b> (has it arrived, and was it in scope); the reducer that
/// turns arrived events into a knowledge state — last-known prices, last telemetry — is
/// domain-specific and lives with each subsystem.</para>
/// </summary>
public sealed class KnowledgeProjection
{
    private readonly EventLog _log;
    private readonly Func<long, ISpatial> _spatialOf;
    private readonly Func<EventRecord, long, bool> _inScope;

    /// <param name="log">The authoritative domain-event log.</param>
    /// <param name="spatialOf">Resolves an entity id to its position provider (body, settlement, ship).</param>
    /// <param name="inScope">Whether an event was in scope for a given observer (§2.2 event scope).</param>
    public KnowledgeProjection(
        EventLog log,
        Func<long, ISpatial> spatialOf,
        Func<EventRecord, long, bool> inScope)
    {
        _log = log;
        _spatialOf = spatialOf;
        _inScope = inScope;
    }

    /// <summary>
    /// The sim-second at which <paramref name="observerId"/>'s signal of <paramref name="ev"/> arrives.
    /// Solves the light-cone equation from the origin's emission-instant position; "first light is the
    /// floor" (§2.2) — no channel beats this.
    /// </summary>
    public long ReceptionTime(long observerId, EventRecord ev)
    {
        (long sx, long sy, long sz) = _spatialOf(ev.OriginEntity).PositionMmAt(ev.TimeSeconds);
        ISpatial observer = _spatialOf(observerId);
        return Reception.Solve(observer.PositionMmAt, sx, sy, sz, ev.TimeSeconds);
    }

    /// <summary>True if <paramref name="ev"/> was in scope for <paramref name="observerId"/> and its
    /// signal has arrived by <paramref name="atSeconds"/>.</summary>
    public bool HasArrived(long observerId, EventRecord ev, long atSeconds)
        => _inScope(ev, observerId) && ReceptionTime(observerId, ev) <= atSeconds;

    /// <summary>
    /// <c>Knowledge(observer, T)</c>: fold the reducer over every in-scope event whose signal has
    /// arrived by <paramref name="atSeconds"/>, in log (occurrence) order. This is the observer's view
    /// of the world at time <c>T</c> — as stale as the geometry makes it.
    /// </summary>
    public TState Fold<TState>(
        long observerId,
        long atSeconds,
        TState seed,
        Func<TState, EventRecord, TState> reducer)
        => _log.Fold(seed, reducer, ev => HasArrived(observerId, ev, atSeconds));
}
