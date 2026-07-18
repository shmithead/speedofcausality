namespace Sim.Core.Events;

/// <summary>
/// The domain-specific body of an event (a burn committed, a price fixed, a death). Sim.Core
/// defines only the marker; concrete payloads live with their subsystems. Each carries a
/// <see cref="SchemaVersion"/> so an old persisted payload can be recognized and upgraded on load
/// (roadmap §2.6) rather than silently misread.
/// </summary>
public interface IEventPayload
{
    /// <summary>The schema version this payload was written with.</summary>
    int SchemaVersion { get; }
}
