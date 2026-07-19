using Sim.Core.Events;
using Sim.Core.World;

namespace Sim.Core.Ships;

/// <summary>
/// Player commands to ships (roadmap §5, "countermand flow"). A command is not an instant effect — it
/// is a signal emitted from the player's HQ that travels at c and takes effect only when it
/// <i>arrives</i> at the ship (§2.2). The scheduled reception is the whole mechanic: issue the order
/// now, find out weeks later whether it landed in time.
/// </summary>
public static class ShipCommands
{
    /// <summary>
    /// Orders <paramref name="ship"/> to divert to <paramref name="newDestSettlementId"/>. Emits the
    /// <see cref="Countermand"/> from <paramref name="playerEntityId"/> (the HQ, so it carries HQ's
    /// light-lag) and schedules its arrival at the ship, where <see cref="Ship.ApplyCountermand"/>
    /// runs — or is moot, if the ship already docked (§5).
    /// </summary>
    public static EventRecord IssueCountermand(
        SimWorld world,
        Ship ship,
        long playerEntityId,
        long newDestSettlementId)
    {
        EventRecord ev = world.Emit(playerEntityId, new Countermand(ship.Id, newDestSettlementId));
        world.ScheduleReception(ship.Id, ev, ctx => ship.ApplyCountermand(ctx, newDestSettlementId));
        return ev;
    }

    /// <summary>
    /// Sends <paramref name="ship"/> to <paramref name="targetSettlementId"/> with a SITREP cadence of
    /// <paramref name="sitrepIntervalSeconds"/>. Emits the <see cref="DispatchOrder"/> from
    /// <paramref name="playerEntityId"/> (HQ) and schedules its arrival at the ship, where
    /// <see cref="Ship.ApplyDispatch"/> runs on reception — instantly for a ship at HQ, a light-lag
    /// later for one across the system (§2.2). This is the order a right-click on the map issues.
    /// </summary>
    public static EventRecord IssueDispatch(
        SimWorld world,
        Ship ship,
        long playerEntityId,
        long targetSettlementId,
        long sitrepIntervalSeconds)
    {
        EventRecord ev = world.Emit(playerEntityId, new DispatchOrder(ship.Id, targetSettlementId, sitrepIntervalSeconds));
        world.ScheduleReception(ship.Id, ev, ctx => ship.ApplyDispatch(ctx, targetSettlementId, sitrepIntervalSeconds));
        return ev;
    }
}
