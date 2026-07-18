namespace Sim.Core.Events;

/// <summary>
/// Upgrades old event payloads to the current schema on read (roadmap §2.6). "Save file = event log"
/// means payloads are stable — but a multi-year build churns them, and every change would otherwise
/// invalidate all saves and golden replays. Instead each old payload type registers a one-step
/// upgrade to its successor; <see cref="Upcast"/> chains those steps until it reaches a type with no
/// registered successor (the current version). Old events are never mutated in place.
/// </summary>
public sealed class EventUpcaster
{
    private readonly Dictionary<Type, Func<IEventPayload, IEventPayload>> _steps = new();

    /// <summary>
    /// Registers how to upgrade one version of a payload to the next. Keyed by the old CLR type, so
    /// a payload whose type has no registration is already current.
    /// </summary>
    public void Register<TOld>(Func<TOld, IEventPayload> upgrade)
        where TOld : IEventPayload
    {
        _steps[typeof(TOld)] = old => upgrade((TOld)old);
    }

    /// <summary>Upgrades a payload to the current schema by applying registered steps in sequence.</summary>
    public IEventPayload Upcast(IEventPayload payload)
    {
        IEventPayload current = payload;
        int guard = 0;
        while (_steps.TryGetValue(current.GetType(), out Func<IEventPayload, IEventPayload>? step))
        {
            current = step(current);
            if (++guard > 64)
            {
                throw new InvalidOperationException(
                    $"Upcaster exceeded 64 steps starting from {payload.GetType().Name} — likely a cycle.");
            }
        }

        return current;
    }

    /// <summary>Upcasts an event's payload, returning the event unchanged if already current.</summary>
    public EventRecord Upcast(EventRecord ev)
    {
        IEventPayload upgraded = Upcast(ev.Payload);
        return ReferenceEquals(upgraded, ev.Payload) ? ev : ev with { Payload = upgraded };
    }
}
