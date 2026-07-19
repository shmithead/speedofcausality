using Sim.Core.Time;
using Sim.Core.World;

namespace Sim.Core.Ships;

/// <summary>
/// A scripted competitor (roadmap §5, "one scripted competitor"): a ship that flies a fixed cycle of
/// settlements, dwelling briefly at each and re-departing for the next. It is <b>scripted, not
/// autonomous</b> — no utility scoring, no traits, no reaction to information (that is Phase 2's
/// captain stack, §3 Agents). Because the itinerary is fixed, the whole timeline is <i>predictable</i>
/// (§2.7): every arrival time is known at departure, so the next departure is simply a scheduled
/// event — the competitor never needs to "think."
///
/// <para>Its telemetry and filed plans broadcast like any ship's, so the player sees the competitor
/// on the map only as stale ghosts and announced plans (§2.2) — never its true current position.</para>
/// </summary>
public sealed class ScriptedRoute
{
    private readonly SimWorld _world;
    private readonly long[] _stops;
    private readonly long _legDurationSeconds;
    private readonly long _dwellSeconds;

    private int _nextOriginIndex; // index of the stop the ship will next depart FROM

    private ScriptedRoute(SimWorld world, Ship ship, long[] stops, long legDurationSeconds, long dwellSeconds)
    {
        _world = world;
        Ship = ship;
        _stops = (long[])stops.Clone(); // defensive copy: the itinerary must not change under a caller's mutation
        _legDurationSeconds = legDurationSeconds;
        _dwellSeconds = dwellSeconds;
    }

    /// <summary>The competitor ship being driven (inspection / rendering).</summary>
    public Ship Ship { get; }

    /// <summary>
    /// Departs a new ship on the cycle <paramref name="stops"/> (visited in order, then repeating),
    /// each leg taking <paramref name="legDurationSeconds"/> and each stop held for
    /// <paramref name="dwellSeconds"/>. The world clock must be at the first departure instant.
    /// </summary>
    public static ScriptedRoute Begin(
        SimWorld world,
        long shipId,
        long[] stops,
        long legDurationSeconds,
        long dwellSeconds,
        long fuelMmPerSec,
        long sitrepIntervalSeconds = 0)
    {
        if (stops.Length < 2)
        {
            throw new ArgumentException("A route needs at least two stops.", nameof(stops));
        }

        if (legDurationSeconds <= 0 || dwellSeconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(legDurationSeconds), "Positive leg duration and non-negative dwell required.");
        }

        Ship ship = Ship.Depart(
            world, shipId, stops[0], stops[1], world.NowSeconds + legDurationSeconds, fuelMmPerSec,
            sitrepIntervalSeconds);
        var route = new ScriptedRoute(world, ship, stops, legDurationSeconds, dwellSeconds) { _nextOriginIndex = 1 };
        route.ScheduleNextDeparture(ship.ArriveSeconds);
        return route;
    }

    private void ScheduleNextDeparture(long arrivalSeconds)
        => _world.Sim.Schedule(new DepartEvent(this, arrivalSeconds + _dwellSeconds));

    private void OnDepart(ISimContext ctx)
    {
        int destIndex = (_nextOriginIndex + 1) % _stops.Length;
        long arrive = ctx.NowSeconds + _legDurationSeconds;
        Ship.FileNextLeg(_stops[destIndex], arrive); // origin is the stop it is docked at
        _nextOriginIndex = destIndex;
        ScheduleNextDeparture(arrive);
    }

    private sealed class DepartEvent : ISimEvent
    {
        private readonly ScriptedRoute _route;

        public DepartEvent(ScriptedRoute route, long timeSeconds)
        {
            _route = route;
            TimeSeconds = timeSeconds;
        }

        public long TimeSeconds { get; }

        public long Ordinal => Ordinals.ScriptedDeparture + _route.Ship.Id;

        public void Apply(ISimContext ctx) => _route.OnDepart(ctx);
    }
}
