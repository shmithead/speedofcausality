namespace Sim.Core.Events;

/// <summary>
/// One event in the log (roadmap §2.1): an immutable envelope around a domain payload. The save
/// file <b>is</b> the sequence of these (§2.1), and world state is <c>fold(events)</c> — so an
/// event, once appended, is never mutated.
/// </summary>
/// <param name="Id">Strictly increasing identity; also the log's ordering key.</param>
/// <param name="TimeSeconds">Occurrence time in sim-seconds (non-decreasing across the log).</param>
/// <param name="OriginEntity">The entity the event originated at.</param>
/// <param name="CausalParent">The event that caused this one, if any — the causal spine (§2.1).</param>
/// <param name="Payload">The domain-specific body.</param>
public sealed record EventRecord(
    long Id,
    long TimeSeconds,
    long OriginEntity,
    long? CausalParent,
    IEventPayload Payload);
