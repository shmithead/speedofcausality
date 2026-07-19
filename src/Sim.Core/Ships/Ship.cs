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
    /// <summary>Sentinel origin for a leg begun mid-flight (a countermand diversion has no departure settlement).</summary>
    public const long InTransit = -1;

    private readonly SimWorld _world;
    private readonly Trajectory _trajectory;
    private readonly long _nominalTransferSeconds;

    private long _departSettlementId;

    /// <summary>Straight-line cruise speed (mm/s) used to size a dispatch's ETA from distance (Phase 1, §3).</summary>
    public const long CruiseMmPerSec = 40_000_000L; // 40 km/s

    private long _fuelMmPerSec;
    private long _destSettlementId;
    private long _departSeconds;
    private long _arriveSeconds;
    private int _generation;
    private bool _arrived;

    private long _sitrepIntervalSeconds;
    private bool _sitrepRunning;
    private long _cargoUnits;
    private long _cargoCapacity;
    private bool _isPlayer;

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

    /// <summary>The settlement the ship is docked at, or -1 if under way.</summary>
    public long DockedAtSettlementId => _arrived ? _destSettlementId : -1;

    /// <summary>Ore units currently aboard.</summary>
    public long CargoUnits => _cargoUnits;

    /// <summary>Maximum ore the ship can carry (0 = a non-trading hull, e.g. the competitor).</summary>
    public long CargoCapacity => _cargoCapacity;

    /// <summary>Whether this ship trades into the player firm's ledger on docking.</summary>
    public bool IsPlayer => _isPlayer;

    /// <summary>Current SITREP cadence in seconds (0 = the ship runs silent between decision points).</summary>
    public long SitrepIntervalSeconds => _sitrepIntervalSeconds;

    /// <inheritdoc/>
    /// <remarks>
    /// While docked, the ship <b>is</b> its port body (§2.7 almanac): it rides the body's orbit, not the
    /// straight-line continuation of the arrival coast — that would drift off as the body curves away.
    /// The <c>t &gt;= _arriveSeconds</c> guard keeps past coast queries on the trajectory, so historical
    /// position reconstruction (for folding old telemetry) stays correct.
    /// </remarks>
    public (long X, long Y, long Z) PositionMmAt(long tSeconds)
        => _arrived && tSeconds >= _arriveSeconds
            ? _world.EntitySpatial(_destSettlementId).PositionMmAt(tSeconds)
            : _trajectory.PositionMmAt(tSeconds);

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
        long fuelMmPerSec,
        long sitrepIntervalSeconds = 0,
        long cargoCapacity = 0,
        bool isPlayer = false)
    {
        long departSeconds = world.NowSeconds;
        if (arriveSeconds <= departSeconds)
        {
            throw new ArgumentOutOfRangeException(nameof(arriveSeconds), "Arrival must be after departure.");
        }

        // Construct docked at rest on the departure settlement; the departure burn is the first commit,
        // so departure, countermand, and scripted re-file all run through one code path (CommitTo).
        (long px, long py, long pz) = world.EntitySpatial(departSettlementId).PositionMmAt(departSeconds);
        var trajectory = new Trajectory(new Trajectory.Leg(departSeconds, px, py, pz, 0, 0, 0));
        var ship = new Ship(
            id, world, trajectory, fuelMmPerSec,
            departSettlementId, destSettlementId, departSeconds, arriveSeconds)
        {
            _sitrepIntervalSeconds = sitrepIntervalSeconds,
            _cargoCapacity = cargoCapacity,
            _isPlayer = isPlayer,
        };

        world.AddEntity(id, ship, isObserver: true);
        ship.CommitTo(departSeconds, departSettlementId, destSettlementId, arriveSeconds, TelemetryCause.Departed, announcePlan: true);
        ship.EnsureSitrep();
        return ship;
    }

    /// <summary>
    /// The player's dispatch, delivered (roadmap §5): re-routes the ship to
    /// <paramref name="targetSettlementId"/> and sets its SITREP cadence. Works whether the ship is
    /// docked (a launch) or under way (a divert). ETA is sized from the straight-line distance at a
    /// fixed cruise speed (§3, no transfer solver yet). Invoked from the scheduled reception callback,
    /// so "now" is the instant the order actually reached the ship (§2.2).
    /// </summary>
    public void ApplyDispatch(ISimContext ctx, long targetSettlementId, long sitrepIntervalSeconds)
    {
        long now = ctx.NowSeconds;
        (long cx, long cy, long cz) = _trajectory.PositionMmAt(now);
        (long tx, long ty, long tz) = _world.EntitySpatial(targetSettlementId).PositionMmAt(now);
        long distance = IntMath.DistanceMm(cx, cy, cz, tx, ty, tz);
        long eta = distance / CruiseMmPerSec;
        long arrive = now + (eta > 0 ? eta : 1); // never same-instant; a co-located target still takes a tick

        long origin = _arrived ? _destSettlementId : InTransit;
        _sitrepIntervalSeconds = sitrepIntervalSeconds;
        CommitTo(now, origin, targetSettlementId, arrive, TelemetryCause.Departed, announcePlan: true);
        EnsureSitrep();
    }

    /// <summary>
    /// Files the next leg of a fixed itinerary: departs the settlement the ship is docked at for
    /// <paramref name="destSettlementId"/>, arriving at <paramref name="arriveSeconds"/>. This is the
    /// scripted competitor's driver (roadmap §5) — a plain re-departure that <i>announces</i> a fresh
    /// plan, unlike a countermand (which goes dark). The world clock must be at the departure instant.
    /// </summary>
    public void FileNextLeg(long destSettlementId, long arriveSeconds)
    {
        long now = _world.NowSeconds;
        if (arriveSeconds <= now)
        {
            throw new ArgumentOutOfRangeException(nameof(arriveSeconds), "Arrival must be after departure.");
        }

        CommitTo(now, _destSettlementId, destSettlementId, arriveSeconds, TelemetryCause.Departed, announcePlan: true);
    }

    /// <summary>
    /// The player's countermand, delivered: recomputes an intercept for
    /// <paramref name="newDestSettlementId"/> from wherever the ship is now and burns onto it. A no-op
    /// if the ship has already arrived — the order came too late (§5). Invoked from the scheduled
    /// reception callback, so "now" is the reception instant, never earlier (§2.2).
    ///
    /// <para>Emits only telemetry — the ship goes dark on its old filed plan. A diverted ship that no
    /// longer matches its announced plan IS the deviation (§3): the physics-delivered signal that
    /// "something happened" reaches the player before any report explaining it. The player renders the
    /// new intent as a live <i>prediction</i> (they ordered it — §5 permits drawing that), not from a
    /// new filed plan the ship never transmitted.</para>
    /// </summary>
    public void ApplyCountermand(ISimContext ctx, long newDestSettlementId)
    {
        long now = ctx.NowSeconds;
        if (_arrived || now >= _arriveSeconds)
        {
            return; // too late — first light was not fast enough
        }

        // Phase 1 simplification: a diversion keeps the ship's original transfer duration (it pushes
        // harder to hold a fixed schedule) rather than re-planning arrival from the new distance — the
        // real transfer solver that would size this is Phase 2/3 (§3, "transfers are a precomputed
        // table, not a live solve").
        CommitTo(now, InTransit, newDestSettlementId, now + _nominalTransferSeconds, TelemetryCause.Countermanded, announcePlan: false);
    }

    // The one code path that puts the ship on a new intercept: burn onto the lead velocity for the
    // destination, spend the fuel, restate the plan, and (re)schedule arrival. Whether it announces a
    // filed plan is what separates a normal departure/re-file from a dark countermand diversion.
    private void CommitTo(
        long now, long originSettlementId, long destSettlementId, long arriveSeconds, TelemetryCause cause, bool announcePlan)
    {
        if (_arrived)
        {
            BuyAtPort(originSettlementId); // fill the hold before leaving a market port
        }

        // Start from the ship's true current position — the port body's position if docked (so a
        // re-dispatch leaves from the planet, not the drifted coast), else the mid-flight coast point.
        (long cx, long cy, long cz) = PositionMmAt(now);
        (long pvx, long pvy, long pvz) = _trajectory.CurrentVelocity;
        (long nvx, long nvy, long nvz) = InterceptVelocity(_world, destSettlementId, cx, cy, cz, now, arriveSeconds);

        _trajectory.DepartFrom(now, cx, cy, cz, nvx, nvy, nvz);

        // Phase 1: fuel is a budget spent per burn, not yet a hard constraint — it may go negative, and
        // a ship never refuses an order for lack of delta-v. Enforcing it (refuse / strand) is Phase 2
        // captain logic (§3 Agents), not the information-lag prototype.
        _fuelMmPerSec -= IntMath.DistanceMm(0, 0, 0, nvx - pvx, nvy - pvy, nvz - pvz);
        _departSettlementId = originSettlementId;
        _destSettlementId = destSettlementId;
        _departSeconds = now;
        _arriveSeconds = arriveSeconds;
        _arrived = false;
        _generation++;

        _world.Emit(Id, new Telemetry(Id, cx, cy, cz, nvx, nvy, nvz, cause));
        if (announcePlan)
        {
            _world.Emit(Id, new FlightPlanFiled(
                Id, originSettlementId, destSettlementId, now, arriveSeconds, cx, cy, cz, nvx, nvy, nvz));
        }

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
        SellAtPort();
    }

    // Arrival trade (roadmap §3): sell the whole hold at the arrival port's ground-truth price. Cargo
    // is bought on departure (see CommitTo) and sold here, so cash realizes the spread between the two
    // ports — and the player only ever saw stale quotes when choosing the route (§2.2). Only player
    // trading hulls touch the firm ledger.
    private void SellAtPort()
    {
        if (!_isPlayer || _cargoUnits <= 0 || !_world.TryGetMarket(_destSettlementId, out IPriceSource market))
        {
            return;
        }

        long price = market.PriceMinorUnits;
        long sold = _cargoUnits;
        _world.Credits += sold * price;
        _cargoUnits = 0;
        _world.Emit(Id, new TradeExecuted(Id, _destSettlementId, market.CommodityId, sold, 0, price, _world.Credits));
    }

    // Departure trade: if a player hull leaves a port that has a market, it fills the hold at that
    // port's local price. Buy cheap here, sell dear on arrival — that is the whole game, played on
    // prices the player could only see stale.
    private void BuyAtPort(long originSettlementId)
    {
        if (!_isPlayer || _cargoCapacity <= _cargoUnits || !_world.TryGetMarket(originSettlementId, out IPriceSource market))
        {
            return;
        }

        long price = market.PriceMinorUnits;
        long bought = _cargoCapacity - _cargoUnits;
        _world.Credits -= bought * price;
        _cargoUnits = _cargoCapacity;
        _world.Emit(Id, new TradeExecuted(Id, originSettlementId, market.CommodityId, 0, bought, price, _world.Credits));
    }

    // Runs the periodic SITREP chain: one self-rescheduling event per ship, emitting a routine position
    // report every interval while under way. The report travels at c like everything (§2.2), so HQ's
    // ghost only advances when a SITREP arrives — the position channel the player set as a mission
    // parameter. Reports pause while docked (position is constant) but the cadence resumes on departure.
    private void EnsureSitrep()
    {
        if (_sitrepIntervalSeconds > 0 && !_sitrepRunning)
        {
            _sitrepRunning = true;
            _world.Sim.Schedule(new SitrepEvent(this, _world.NowSeconds + _sitrepIntervalSeconds));
        }
    }

    private void OnSitrep(ISimContext ctx)
    {
        if (_sitrepIntervalSeconds <= 0)
        {
            _sitrepRunning = false; // SITREPs turned off — let the chain die
            return;
        }

        if (!_arrived)
        {
            (long x, long y, long z) = _trajectory.PositionMmAt(ctx.NowSeconds);
            (long vx, long vy, long vz) = _trajectory.CurrentVelocity;
            _world.Emit(Id, new Telemetry(Id, x, y, z, vx, vy, vz, TelemetryCause.Routine));
        }

        _world.Sim.Schedule(new SitrepEvent(this, ctx.NowSeconds + _sitrepIntervalSeconds));
    }

    private sealed class SitrepEvent : ISimEvent
    {
        private readonly Ship _ship;

        public SitrepEvent(Ship ship, long timeSeconds)
        {
            _ship = ship;
            TimeSeconds = timeSeconds;
        }

        public long TimeSeconds { get; }

        public long Ordinal => Ordinals.Sitrep + _ship.Id;

        public void Apply(ISimContext ctx) => _ship.OnSitrep(ctx);
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
