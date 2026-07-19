using Sim.Core.Events;
using Sim.Core.Knowledge;

namespace Sim.Core.Ships;

/// <summary>
/// A ship filed a flight plan on receiving an order (roadmap §5, "filed flight plans"). The plan is
/// what the ship <i>announces</i> it will do — a public declaration, not ground truth — so the
/// payload carries the planned departure leg (position + velocity + times). The map reconstructs the
/// <b>predicted</b> path from this (fair to draw live, §5 render rule); a <see cref="Telemetry"/>
/// ghost that disagrees with it is the deviation.
/// </summary>
/// <param name="ShipId">The ship this plan belongs to.</param>
/// <param name="DepartSettlementId">Origin settlement.</param>
/// <param name="DestSettlementId">Declared destination settlement.</param>
/// <param name="DepartSeconds">When the departure leg begins.</param>
/// <param name="ArriveSeconds">Declared ETA (the straight-line intercept time).</param>
/// <param name="X">Planned departure position X (mm).</param>
/// <param name="Y">Planned departure position Y (mm).</param>
/// <param name="Z">Planned departure position Z (mm).</param>
/// <param name="Vx">Planned coast velocity X (mm/s).</param>
/// <param name="Vy">Planned coast velocity Y (mm/s).</param>
/// <param name="Vz">Planned coast velocity Z (mm/s).</param>
public sealed record FlightPlanFiled(
    long ShipId,
    long DepartSettlementId,
    long DestSettlementId,
    long DepartSeconds,
    long ArriveSeconds,
    long X, long Y, long Z,
    long Vx, long Vy, long Vz) : IEventPayload
{
    public int SchemaVersion => 1;

    /// <summary>Where the plan says the ship should be at <paramref name="tSeconds"/> (straight-line coast).</summary>
    public (long X, long Y, long Z) PredictedPositionMmAt(long tSeconds)
    {
        long dt = tSeconds - DepartSeconds;
        return (X + (Vx * dt), Y + (Vy * dt), Z + (Vz * dt));
    }
}

/// <summary>
/// A ship's actual telemetry ping (roadmap §5, "the ghost — last actual telemetry, one lag stale").
/// It occurs at the ship and reaches the player only after the light-lag (§2.2), so the map's ghost
/// is always at least one lag old. Deviation is the ghost measured against the filed plan <i>at the
/// ghost's own timestamp</i> (§5) — never against where the ship is "really" now, which nobody knows.
/// </summary>
/// <param name="ShipId">The reporting ship.</param>
/// <param name="X">Actual position X (mm) at emission.</param>
/// <param name="Y">Actual position Y (mm) at emission.</param>
/// <param name="Z">Actual position Z (mm) at emission.</param>
/// <param name="Vx">Actual velocity X (mm/s).</param>
/// <param name="Vy">Actual velocity Y (mm/s).</param>
/// <param name="Vz">Actual velocity Z (mm/s).</param>
/// <param name="Cause">Why this ping was sent — the deviation must be caused, never noise (§3).</param>
public sealed record Telemetry(
    long ShipId,
    long X, long Y, long Z,
    long Vx, long Vy, long Vz,
    TelemetryCause Cause) : IEventPayload
{
    public int SchemaVersion => 1;
}

/// <summary>Why a ship emitted telemetry — kept so a deviation always has a stated cause (§3).</summary>
public enum TelemetryCause
{
    /// <summary>Routine position report along a committed leg.</summary>
    Routine,

    /// <summary>Departure: the ship has committed to a filed plan.</summary>
    Departed,

    /// <summary>The ship burned because a countermand reached it — the caused deviation (§3).</summary>
    Countermanded,

    /// <summary>The ship reached its destination.</summary>
    Arrived,
}

/// <summary>
/// The player's order to change a ship's plan mid-flight (roadmap §5, "countermand flow"). It is
/// <see cref="EventScope.Direct"/> — addressed to one ship and routed there — and, like everything,
/// travels at c: it changes nothing until it <i>arrives</i>, and it may arrive after the ship has
/// already passed the decision point, in which case it is too late. That race is the tension.
/// </summary>
/// <param name="ShipId">The addressed ship (also the routing <see cref="Recipient"/>).</param>
/// <param name="NewDestSettlementId">The settlement to divert to.</param>
public sealed record Countermand(long ShipId, long NewDestSettlementId) : IScoped
{
    public int SchemaVersion => 1;

    public EventScope Scope => EventScope.Direct;

    public long Recipient => ShipId;
}
