namespace Sim.Core.Knowledge;

/// <summary>
/// How far an event's signal reaches (roadmap §2.2): "Events carry a scope... Only broadcasts get a
/// full observer sweep." This is the single largest compute win available (§2.7) — a hull breach at
/// Ceres was never going to be transmitted to Neptune — and it is fiction-first, not a hack. Scope
/// collapses <c>events × observers</c> down to <c>events × interested-parties</c>.
///
/// <para><b>Phase 1 note.</b> The knowledge model is exercised at <see cref="Broadcast"/> for the
/// ambient world (prices, telemetry) and <see cref="Direct"/> for addressed messages (a countermand
/// to one ship). The full point-to-point routing that "every signal has a sender" implies — the
/// signal-regulation mechanic — lands in Phase 3 (§3); the scope field is threaded now so that
/// restricting reach later is a data change, not a rewrite.</para>
/// </summary>
public enum EventScope
{
    /// <summary>Reaches only the origin entity itself (an internal state change nobody transmits).</summary>
    Local,

    /// <summary>Addressed to a single named recipient and routed there (a message, a countermand).</summary>
    Direct,

    /// <summary>Reaches the parties interested in one settlement (its market, ships docked there).</summary>
    Settlement,

    /// <summary>Reaches a region of nearby entities (reserved; Phase 3 signal regulation refines it).</summary>
    Regional,

    /// <summary>Swept to every observer with a receiver — the only scope that pays the full sweep.</summary>
    Broadcast,
}
