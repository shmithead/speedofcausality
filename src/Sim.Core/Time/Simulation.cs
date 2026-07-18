namespace Sim.Core.Time;

/// <summary>
/// The discrete-event driver (roadmap §2.4). Holds a monotonic <see cref="SimClock"/> and a
/// priority queue of future events; each step pops the earliest event, advances the clock to it,
/// and applies it. This is Brewer's "Next Event" mode — the sim jumps to the next scheduled time
/// rather than ticking every minute, which is what makes ticks sparse once horizons drive what gets
/// scheduled.
///
/// <para>Determinism: the queue is ordered by <c>(TimeSeconds, Ordinal)</c>, both content-derived,
/// so the processing order is a function of the event set alone — not of the order events were
/// scheduled in (§2.3 r7/r8). Events must have unique <c>(TimeSeconds, Ordinal)</c> keys.</para>
/// </summary>
public sealed class Simulation : ISimContext
{
    private readonly SimClock _clock;
    private readonly PriorityQueue<ISimEvent, (long Time, long Ordinal)> _queue = new();

    public Simulation(long startSeconds = 0)
    {
        _clock = new SimClock(startSeconds);
    }

    /// <inheritdoc/>
    public long NowSeconds => _clock.NowSeconds;

    /// <summary>Number of events still pending in the queue.</summary>
    public int PendingCount => _queue.Count;

    /// <inheritdoc/>
    public void Schedule(ISimEvent ev)
    {
        if (ev.TimeSeconds < _clock.NowSeconds)
        {
            throw new InvalidOperationException(
                $"Cannot schedule into the past: now={_clock.NowSeconds}s, event={ev.TimeSeconds}s.");
        }

        _queue.Enqueue(ev, (ev.TimeSeconds, ev.Ordinal));
    }

    /// <summary>Time of the next pending event, or null if the queue is empty.</summary>
    public long? NextEventTime => _queue.TryPeek(out _, out (long Time, long Ordinal) key) ? key.Time : null;

    /// <summary>
    /// Processes the single earliest event. Returns false if the queue is empty.
    /// </summary>
    public bool Step()
    {
        if (_queue.Count == 0)
        {
            return false;
        }

        ISimEvent ev = _queue.Dequeue();
        _clock.AdvanceTo(ev.TimeSeconds);
        ev.Apply(this);
        return true;
    }

    /// <summary>
    /// Processes every event with time ≤ <paramref name="untilSeconds"/> in order, then advances the
    /// clock to <paramref name="untilSeconds"/>. Events fired along the way may schedule more events;
    /// those are processed too if they fall within the window.
    /// </summary>
    public void RunUntil(long untilSeconds)
    {
        while (_queue.TryPeek(out _, out (long Time, long Ordinal) key) && key.Time <= untilSeconds)
        {
            ISimEvent ev = _queue.Dequeue();
            _clock.AdvanceTo(ev.TimeSeconds);
            ev.Apply(this);
        }

        if (untilSeconds > _clock.NowSeconds)
        {
            _clock.AdvanceTo(untilSeconds);
        }
    }
}
