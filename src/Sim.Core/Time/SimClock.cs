namespace Sim.Core.Time;

/// <summary>
/// The simulation's sense of time: an integer count of seconds since the world epoch. Never wall
/// clock (§2.3 r1) — this is the only "now" Sim.Core knows, and it moves only forward, advanced by
/// the scheduler as events are processed.
/// </summary>
public sealed class SimClock
{
    /// <summary>Current sim-time, in seconds since epoch.</summary>
    public long NowSeconds { get; private set; }

    public SimClock(long startSeconds = 0)
    {
        NowSeconds = startSeconds;
    }

    /// <summary>
    /// Advances to <paramref name="seconds"/>. Time is monotonic: moving backward is a bug (it would
    /// mean an event was scheduled into the past or the queue popped out of order), so it throws.
    /// </summary>
    public void AdvanceTo(long seconds)
    {
        if (seconds < NowSeconds)
        {
            throw new InvalidOperationException(
                $"Clock cannot move backward: now={NowSeconds}s, requested={seconds}s.");
        }

        NowSeconds = seconds;
    }
}
