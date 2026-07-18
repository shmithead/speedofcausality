namespace Sim.Core.Time;

/// <summary>
/// What an event can do to the world when it fires: read the current time and schedule follow-up
/// events (a reception scheduled at emission, a next decision scheduled at burn commit). Kept
/// narrow so events cannot reach outside the sim.
/// </summary>
public interface ISimContext
{
    /// <summary>Current sim-time in seconds.</summary>
    long NowSeconds { get; }

    /// <summary>Schedules a future event. Must not be in the past.</summary>
    void Schedule(ISimEvent ev);
}

/// <summary>
/// A scheduled simulation event. Ordering in the queue is <c>(TimeSeconds, Ordinal)</c>, and both
/// come from event <b>content</b> — never insertion order (§2.3 r7). Two replays that schedule the
/// same set of events in different orders must therefore process them identically.
/// </summary>
public interface ISimEvent
{
    /// <summary>When the event fires, in seconds since epoch.</summary>
    long TimeSeconds { get; }

    /// <summary>
    /// Deterministic tiebreak among events at the same time, derived from content (e.g. entity id
    /// folded with event kind). <c>(TimeSeconds, Ordinal)</c> must be unique per event — that is the
    /// contract that makes processing order independent of scheduling order.
    /// </summary>
    long Ordinal { get; }

    /// <summary>Applies the event's effect, optionally scheduling follow-ups via <paramref name="ctx"/>.</summary>
    void Apply(ISimContext ctx);
}
