using Sim.Core.Events;

namespace Sim.Core.Knowledge;

/// <summary>
/// A payload that declares how far its signal reaches (roadmap §2.2 event scope). Scope must be a
/// pure function of persisted data so that a replay recomputes the same observer set — hence it
/// lives on the payload, not in a side table. A payload that does not implement this defaults to
/// <see cref="EventScope.Broadcast"/> (the ambient world: prices, telemetry).
/// </summary>
public interface IScoped : IEventPayload
{
    /// <summary>How far the signal reaches.</summary>
    EventScope Scope { get; }

    /// <summary>
    /// The addressed recipient — meaningful only for <see cref="EventScope.Direct"/> (a message, a
    /// countermand). Ignored for every other scope.
    /// </summary>
    long Recipient { get; }
}
