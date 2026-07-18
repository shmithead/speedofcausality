using Sim.Core.Time;

namespace Sim.Core.Horizons;

/// <summary>
/// Drives validity horizons over a <see cref="Simulation"/> (roadmap §2.7). Each registered entity
/// has exactly one live expiry event in the scheduler; when it fires, the entity recomputes and
/// re-arms at its new horizon. Because horizon expiry is a scheduled event, <b>ticks are sparse</b>:
/// the sim touches only the entities whose horizon has actually expired, not every entity every
/// tick — Brewer's "10,000 years/sec" mode.
///
/// <para>Determinism (§2.3 r8): expiries are ordered by <c>(time, entityId)</c>, so simultaneous
/// expiries and invalidations resolve in a fixed order independent of registration or invalidation
/// order. Cancellation is lazy — a shortened horizon bumps the entity's generation and schedules a
/// new expiry; the old queued expiry is recognized as stale when it fires and ignored (the priority
/// queue has no remove). Stale state is worse than slow, so a stale expiry is a no-op, never a read
/// of an expired cache.</para>
/// </summary>
public sealed class HorizonManager
{
    private readonly Simulation _sim;

    public HorizonManager(Simulation sim)
    {
        _sim = sim;
    }

    /// <summary>Arms an entity: schedules its first horizon expiry. Infinite-horizon bodies are never registered.</summary>
    public void Register(HorizonEntity entity)
    {
        if (entity.Horizon < _sim.NowSeconds)
        {
            throw new InvalidOperationException(
                $"Entity {entity.Id} registered with a past horizon {entity.Horizon}s (now {_sim.NowSeconds}s).");
        }

        ScheduleExpiry(entity);
    }

    /// <summary>
    /// Shortens an entity's horizon because new information could reach it sooner than its next
    /// planned decision. No-op if <paramref name="newHorizon"/> is not sooner than the current one —
    /// a horizon only ever moves in (§2.7); later "invalidations" carry no information.
    /// </summary>
    public void Invalidate(HorizonEntity entity, long newHorizon)
    {
        if (newHorizon >= entity.Horizon)
        {
            return;
        }

        if (newHorizon < _sim.NowSeconds)
        {
            throw new InvalidOperationException(
                $"Cannot invalidate entity {entity.Id} to a past horizon {newHorizon}s (now {_sim.NowSeconds}s).");
        }

        entity.Horizon = newHorizon;
        ScheduleExpiry(entity);
    }

    private void ScheduleExpiry(HorizonEntity entity)
    {
        entity.Generation++;
        _sim.Schedule(new ExpiryEvent(entity, entity.Horizon, entity.Generation, this));
    }

    private void OnExpiry(HorizonEntity entity, ISimContext ctx)
    {
        long newHorizon = entity.InvokeRecompute(ctx);
        if (newHorizon <= ctx.NowSeconds)
        {
            throw new InvalidOperationException(
                $"Entity {entity.Id} recomputed a non-future horizon {newHorizon}s (now {ctx.NowSeconds}s).");
        }

        entity.Horizon = newHorizon;
        ScheduleExpiry(entity);
    }

    private sealed class ExpiryEvent : ISimEvent
    {
        private readonly HorizonEntity _entity;
        private readonly int _generation;
        private readonly HorizonManager _manager;

        public ExpiryEvent(HorizonEntity entity, long time, int generation, HorizonManager manager)
        {
            _entity = entity;
            TimeSeconds = time;
            _generation = generation;
            _manager = manager;
        }

        public long TimeSeconds { get; }

        // Ordinal = entity id: at equal times, expiries resolve in a fixed order (§2.3 r8).
        public long Ordinal => _entity.Id;

        public void Apply(ISimContext ctx)
        {
            // Stale? The horizon was rescheduled (invalidated or already recomputed) after this
            // event was queued. Lazy cancellation: ignore it.
            if (_generation != _entity.Generation)
            {
                return;
            }

            _manager.OnExpiry(_entity, ctx);
        }
    }
}
