using Sim.Core.Numerics;
using Sim.Core.Time;
using Sim.Core.World;

namespace Sim.Core.Ships;

/// <summary>
/// A single freighter under a filed plan (roadmap §5): it departs on a straight-line intercept for a
/// destination settlement, coasts on rails (the event-bounded horizon class, §2.7), and re-decides
/// only when a signal reaches it. Its position is its <see cref="Trajectory"/>; it is an observer
/// (it can receive a countermand) and an origin (its telemetry and filed plans emit from it).
///
/// <para><b>The countermand race is the tension.</b> A <see cref="Countermand"/> travels at c and
/// changes nothing until it arrives (§2.2); the reception is scheduled via
/// <see cref="SimWorld.ScheduleReception"/>, so if it lands before arrival the ship burns and
/// diverts — a <i>caused</i> deviation (§3) the player only learns about a lag later — and if it
/// lands too late, the ship has already docked and the order is moot. Arrival is a generation-guarded
/// self-scheduled ping so a diverted ship's stale arrival is ignored (the sparse-tick staleness
/// pattern, §2.7).</para>
/// </summary>
public sealed class Ship : ISpatial
{
    private readonly SimWorld _world;
    private readonly Trajectory _trajectory;
    private readonly long _departSettlementId;
    private readonly long _nominalTransferSeconds;

    private long _fuelMmPerSec;
    private long _destSettlementId;
    private long _departSeconds;
    private long _arriveSeconds;
    private int _generation;
    private bool _arrived;

    private Ship(
        long id,
        SimWorld world,
        Trajectory trajectory,
        long fuelMmPerSec,
        long departSettlementId,
        long destSettlementId,
        long departSeconds,
        long arriveSeconds)
    {
        Id = id;
        _world = world;
        _trajectory = trajectory;
        _fuelMmPerSec = fuelMmPerSec;
        _departSettlementId = departSettlementId;
        _destSettlementId = destSettlementId;
        _departSeconds = departSeconds;
        _arriveSeconds = arriveSeconds;
        _nominalTransferSeconds = arriveSeconds - departSeconds;
    }

    /// <summary>Stable identity, shared with <see cref="Events.EventRecord.OriginEntity"/>.</summary>
    public long Id { get; }

    /// <summary>Remaining delta-v budget (mm/s). Each burn spends its own magnitude.</summary>
    public long FuelRemaining => _fuelMmPerSec;

    /// <summary>The ship's committed path (rendering / inspection).</summary>
    public Trajectory Trajectory => _trajectory;

    /// <summary>Current declared destination settlement.</summary>
    public long DestSettlementId => _destSettlementId;

    /// <summary>Current declared ETA (sim-seconds).</summary>
    public long ArriveSeconds => _arriveSeconds;

    /// <summary>True once the ship has reached its (current) destination.</summary>
    public bool Arrived => _arrived;

    /// <inheritdoc/>
    public (long X, long Y, long Z) PositionMmAt(long tSeconds) => _trajectory.PositionMmAt(tSeconds);

    /// <summary>
    /// Files a plan and departs <paramref name="departSettlementId"/> now for
    /// <paramref name="destSettlementId"/>, committing to a straight-line intercept of the
    /// destination's <i>future</i> position at <paramref name="arriveSeconds"/>. Emits the filed plan
    /// and a departure telemetry ping (both propagate at c, §2.2), and schedules the arrival. The
    /// caller supplies the arrival time — Phase 1 has no transfer solver (§3); the ship simply leads
    /// the target. The world clock must already be at the departure instant.
    /// </summary>
    public static Ship Depart(
        SimWorld world,
        long id,
        long departSettlementId,
        long destSettlementId,
        long arriveSeconds,
        long fuelMmPerSec)
    {
        long departSeconds = world.NowSeconds;
        if (arriveSeconds <= departSeconds)
        {
            throw new ArgumentOutOfRangeException(nameof(arriveSeconds), "Arrival must be after departure.");
        }

        (long px, long py, long pz) = world.EntitySpatial(departSettlementId).PositionMmAt(departSeconds);
        (long vx, long vy, long vz) = InterceptVelocity(
            world, destSettlementId, px, py, pz, departSeconds, arriveSeconds);

        var trajectory = new Trajectory(new Trajectory.Leg(departSeconds, px, py, pz, vx, vy, vz));
        long departureCost = IntMath.DistanceMm(0, 0, 0, vx, vy, vz); // burn from rest at the settlement
        var ship = new Ship(
            id, world, trajectory, fuelMmPerSec - departureCost,
            departSettlementId, destSettlementId, departSeconds, arriveSeconds);

        world.AddEntity(id, ship, isObserver: true);
        world.Emit(id, new Telemetry(id, px, py, pz, vx, vy, vz, TelemetryCause.Departed));
        world.Emit(id, new FlightPlanFiled(
            id, departSettlementId, destSettlementId, departSeconds, arriveSeconds, px, py, pz, vx, vy, vz));
        ship.ScheduleArrival();
        return ship;
    }

    /// <summary>
    /// The player's countermand, delivered: recomputes an intercept for
    /// <paramref name="newDestSettlementId"/> from wherever the ship is now, burns onto it, and refiles.
    /// A no-op if the ship has already arrived — the order came too late (§5). Invoked from the
    /// scheduled reception callback, so "now" is the reception instant, never earlier (§2.2).
    /// </summary>
    public void ApplyCountermand(ISimContext ctx, long newDestSettlementId)
    {
        long now = ctx.NowSeconds;
        if (_arrived || now >= _arriveSeconds)
        {
            return; // too late — first light was not fast enough
        }

        (long cx, long cy, long cz) = _trajectory.PositionMmAt(now);
        (long pvx, long pvy, long pvz) = _trajectory.CurrentVelocity;

        long newArrive = now + _nominalTransferSeconds;
        (long nvx, long nvy, long nvz) = InterceptVelocity(
            _world, newDestSettlementId, cx, cy, cz, now, newArrive);

        _trajectory.Burn(now, nvx - pvx, nvy - pvy, nvz - pvz);
        _fuelMmPerSec -= IntMath.DistanceMm(0, 0, 0, nvx - pvx, nvy - pvy, nvz - pvz);
        _destSettlementId = newDestSettlementId;
        _departSeconds = now;
        _arriveSeconds = newArrive;
        _generation++;

        // Emit only telemetry — the ship goes dark on its old filed plan. A diverted ship that no
        // longer matches its announced plan IS the deviation (§3): the physics-delivered signal that
        // "something happened" reaches the player before any report explaining it. The player renders
        // the new intent as a live *prediction* (they ordered it — §5 permits drawing that), not from
        // a new filed plan the ship never transmitted.
        _world.Emit(Id, new Telemetry(Id, cx, cy, cz, nvx, nvy, nvz, TelemetryCause.Countermanded));
        ScheduleArrival();
    }

    // Straight-line lead intercept: the velocity that carries the ship from (px,py,pz) at departSeconds
    // to the destination's position at arriveSeconds. Integer division truncates — acceptable for the
    // Phase 1 straight-line model (§3 defers the real transfer solver).
    private static (long Vx, long Vy, long Vz) InterceptVelocity(
        SimWorld world, long destSettlementId, long px, long py, long pz, long departSeconds, long arriveSeconds)
    {
        (long dx, long dy, long dz) = world.EntitySpatial(destSettlementId).PositionMmAt(arriveSeconds);
        long dur = arriveSeconds - departSeconds;
        return ((dx - px) / dur, (dy - py) / dur, (dz - pz) / dur);
    }

    private void ScheduleArrival()
        => _world.Sim.Schedule(new ArrivalEvent(this, _arriveSeconds, _generation));

    private void OnArrival(int generation)
    {
        if (generation != _generation || _arrived)
        {
            return; // stale: the ship was diverted after this arrival was scheduled (§2.7 staleness)
        }

        _arrived = true;
        (long x, long y, long z) = _trajectory.PositionMmAt(_arriveSeconds);
        (long vx, long vy, long vz) = _trajectory.CurrentVelocity;
        _world.Emit(Id, new Telemetry(Id, x, y, z, vx, vy, vz, TelemetryCause.Arrived));
    }

    private sealed class ArrivalEvent : ISimEvent
    {
        private readonly Ship _ship;
        private readonly int _generation;

        public ArrivalEvent(Ship ship, long timeSeconds, int generation)
        {
            _ship = ship;
            TimeSeconds = timeSeconds;
            _generation = generation;
        }

        public long TimeSeconds { get; }

        public long Ordinal => Ordinals.Arrival + _ship.Id;

        public void Apply(ISimContext ctx) => _ship.OnArrival(_generation);
    }
}
