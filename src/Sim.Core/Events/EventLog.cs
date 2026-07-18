namespace Sim.Core.Events;

/// <summary>
/// The append-only event log (roadmap §2.1): the single source of truth. World state is recovered
/// by folding events in order; snapshots (elsewhere) are only a cache. Appends are validated so the
/// log cannot become internally inconsistent — ids strictly increase, time never runs backward, and
/// a causal parent must be an event already in the log.
/// </summary>
public sealed class EventLog
{
    private readonly List<EventRecord> _events = new();
    private readonly HashSet<long> _ids = new();

    /// <summary>The events, in append order.</summary>
    public IReadOnlyList<EventRecord> Events => _events;

    public int Count => _events.Count;

    /// <summary>Appends an event after checking the log's invariants.</summary>
    public void Append(EventRecord ev)
    {
        if (_ids.Contains(ev.Id))
        {
            throw new InvalidOperationException($"Duplicate event id {ev.Id}.");
        }

        if (_events.Count > 0)
        {
            EventRecord last = _events[^1];
            if (ev.Id <= last.Id)
            {
                throw new InvalidOperationException(
                    $"Event ids must strictly increase: {ev.Id} after {last.Id}.");
            }

            if (ev.TimeSeconds < last.TimeSeconds)
            {
                throw new InvalidOperationException(
                    $"Event time must not go backward: {ev.TimeSeconds}s after {last.TimeSeconds}s.");
            }
        }

        if (ev.CausalParent is long parent && !_ids.Contains(parent))
        {
            throw new InvalidOperationException(
                $"Causal parent {parent} of event {ev.Id} is not an earlier event in the log.");
        }

        _events.Add(ev);
        _ids.Add(ev.Id);
    }

    /// <summary>World state = fold(events). Applies <paramref name="reducer"/> in order from <paramref name="seed"/>.</summary>
    public TState Fold<TState>(TState seed, Func<TState, EventRecord, TState> reducer)
    {
        TState state = seed;
        foreach (EventRecord ev in _events)
        {
            state = reducer(state, ev);
        }

        return state;
    }

    /// <summary>
    /// Folds only the events matching <paramref name="filter"/> — the shape a per-observer knowledge
    /// fold takes (events whose reception time has arrived, §2.2). Phase 0 provides the mechanism;
    /// the knowledge model wires the predicate in Phase 1.
    /// </summary>
    public TState Fold<TState>(TState seed, Func<TState, EventRecord, TState> reducer, Func<EventRecord, bool> filter)
    {
        TState state = seed;
        foreach (EventRecord ev in _events)
        {
            if (filter(ev))
            {
                state = reducer(state, ev);
            }
        }

        return state;
    }
}
