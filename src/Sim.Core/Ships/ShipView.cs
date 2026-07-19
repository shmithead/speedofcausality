using Sim.Core.Events;
using Sim.Core.Knowledge;
using Sim.Core.Numerics;

namespace Sim.Core.Ships;

/// <summary>
/// One observer's knowledge of one ship (roadmap §5): the last <b>filed plan</b> it has heard and the
/// last <b>telemetry ghost</b> that has arrived — both stale by construction (§2.2). Deviation is the
/// ghost measured against the filed plan <i>at the ghost's own timestamp</i> (§5): where the plan said
/// the ship would be when that telemetry was actually emitted, versus where it actually was.
/// </summary>
public sealed class ShipKnowledge
{
    /// <summary>The ship this view is about.</summary>
    public long ShipId { get; internal set; }

    /// <summary>The most recent filed plan the observer has heard, if any.</summary>
    public FlightPlanFiled? Plan { get; internal set; }

    /// <summary>When that plan was filed (its occurrence time).</summary>
    public long PlanOccurredAtSeconds { get; internal set; }

    /// <summary>The most recent telemetry that has arrived (the ghost), if any.</summary>
    public Telemetry? Ghost { get; internal set; }

    /// <summary>When that ghost was actually emitted — the ghost is at least one lag older than "now".</summary>
    public long GhostOccurredAtSeconds { get; internal set; }

    /// <summary>
    /// How far off its filed plan the ghost was, in millimetres, at the ghost's timestamp — or null if
    /// the observer lacks either a plan or a ghost. A departure ghost sits exactly on plan (≈0); a
    /// countermanded ghost is off it, which is the caused deviation the player reads (§3).
    /// </summary>
    public long? DeviationMm()
    {
        if (Plan is null || Ghost is null)
        {
            return null;
        }

        (long px, long py, long pz) = Plan.PredictedPositionMmAt(GhostOccurredAtSeconds);
        return IntMath.DistanceMm(Ghost.X, Ghost.Y, Ghost.Z, px, py, pz);
    }
}

/// <summary>
/// The Phase 1 map's ship layer: fold an observer's arrived <see cref="FlightPlanFiled"/> and
/// <see cref="Telemetry"/> events into a per-ship <see cref="ShipKnowledge"/> (§2.2). Pure projection
/// of the knowledge fold — nothing here is ground truth.
/// </summary>
public static class ShipView
{
    /// <summary>Per-ship knowledge for <paramref name="observerId"/> at <paramref name="atSeconds"/>, keyed/iterated by ship id (§2.3 r3).</summary>
    public static IReadOnlyDictionary<long, ShipKnowledge> Read(
        KnowledgeProjection knowledge,
        long observerId,
        long atSeconds)
    {
        return knowledge.Fold(
            observerId,
            atSeconds,
            new SortedDictionary<long, ShipKnowledge>(),
            (acc, ev) =>
            {
                switch (ev.Payload)
                {
                    case FlightPlanFiled plan:
                        ShipKnowledge kp = Slot(acc, plan.ShipId);
                        kp.Plan = plan;
                        kp.PlanOccurredAtSeconds = ev.TimeSeconds;
                        break;
                    case Telemetry ghost:
                        ShipKnowledge kg = Slot(acc, ghost.ShipId);
                        kg.Ghost = ghost;
                        kg.GhostOccurredAtSeconds = ev.TimeSeconds;
                        break;
                }

                return acc;
            });
    }

    private static ShipKnowledge Slot(SortedDictionary<long, ShipKnowledge> acc, long shipId)
    {
        if (!acc.TryGetValue(shipId, out ShipKnowledge? k))
        {
            k = new ShipKnowledge { ShipId = shipId };
            acc[shipId] = k;
        }

        return k;
    }
}
