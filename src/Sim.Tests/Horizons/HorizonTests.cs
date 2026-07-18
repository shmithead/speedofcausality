using Sim.Core.Horizons;
using Sim.Core.Time;

namespace Sim.Tests.Horizons;

public sealed class HorizonTests
{
    // A "coasting" entity: each expiry logs (id, time) and re-arms one period later.
    private sealed class Rails : HorizonEntity
    {
        private readonly long _period;
        private readonly List<(long Id, long Time)> _log;

        public Rails(long id, long firstHorizon, long period, List<(long Id, long Time)> log)
            : base(id, firstHorizon)
        {
            _period = period;
            _log = log;
        }

        protected override long Recompute(ISimContext ctx)
        {
            _log.Add((Id, ctx.NowSeconds));
            return ctx.NowSeconds + _period;
        }
    }

    private sealed class InvalidateEvent : ISimEvent
    {
        private readonly HorizonManager _manager;
        private readonly HorizonEntity _target;
        private readonly long _newHorizon;

        public InvalidateEvent(long time, HorizonManager manager, HorizonEntity target, long newHorizon)
        {
            TimeSeconds = time;
            _manager = manager;
            _target = target;
            _newHorizon = newHorizon;
        }

        public long TimeSeconds { get; }
        public long Ordinal => long.MinValue; // fire before any expiry at the same instant
        public void Apply(ISimContext ctx) => _manager.Invalidate(_target, _newHorizon);
    }

    private sealed class NoOp : ISimEvent
    {
        public NoOp(long time, long ordinal)
        {
            TimeSeconds = time;
            Ordinal = ordinal;
        }

        public long TimeSeconds { get; }
        public long Ordinal { get; }
        public void Apply(ISimContext ctx) { }
    }

    [Fact]
    public void Ticks_Are_Sparse_Entity_Recomputes_Only_At_Its_Horizons()
    {
        var log = new List<(long, long)>();
        var sim = new Simulation();
        var mgr = new HorizonManager(sim);
        var a = new Rails(1, firstHorizon: 100, period: 100, log);
        mgr.Register(a);

        // A storm of unrelated activity must not touch `a`.
        for (long t = 1; t <= 350; t++)
        {
            sim.Schedule(new NoOp(t, ordinal: 9999));
        }

        sim.RunUntil(350);

        // Expiries at 100, 200, 300 only — three recomputes, not 350.
        Assert.Equal(3, a.RecomputeCount);
        Assert.Equal(new[] { (1L, 100L), (1L, 200L), (1L, 300L) }, log);
    }

    [Fact]
    public void Simultaneous_Expiries_Resolve_In_Entity_Id_Order_Regardless_Of_Registration()
    {
        List<(long Id, long Time)> Run(IEnumerable<long> registrationOrder)
        {
            var log = new List<(long Id, long Time)>();
            var sim = new Simulation();
            var mgr = new HorizonManager(sim);
            foreach (long id in registrationOrder)
            {
                mgr.Register(new Rails(id, firstHorizon: 100, period: 1000, log));
            }

            sim.RunUntil(100); // all four expire at t=100
            return log;
        }

        var ascending = Run(new long[] { 1, 2, 3, 4 });
        var descending = Run(new long[] { 4, 3, 2, 1 });

        Assert.Equal(ascending, descending);
        Assert.Equal(new[] { (1L, 100L), (2L, 100L), (3L, 100L), (4L, 100L) }, ascending);
    }

    [Fact]
    public void Invalidation_Pulls_A_Horizon_In_And_Ignores_The_Stale_Expiry()
    {
        var log = new List<(long Id, long Time)>();
        var sim = new Simulation();
        var mgr = new HorizonManager(sim);
        var a = new Rails(1, firstHorizon: 500, period: 1000, log);
        mgr.Register(a);

        // At t=100, information arrives that pulls the horizon in to 200.
        sim.Schedule(new InvalidateEvent(100, mgr, a, newHorizon: 200));

        sim.RunUntil(600);

        // It recomputes at 200 (the pulled-in horizon), not 500 (the stale one), exactly once;
        // the next horizon (200 + 1000) is past the window.
        Assert.Equal(1, a.RecomputeCount);
        Assert.Equal(new[] { (1L, 200L) }, log);
    }

    [Fact]
    public void Invalidation_To_A_Later_Horizon_Is_A_No_Op()
    {
        var log = new List<(long Id, long Time)>();
        var sim = new Simulation();
        var mgr = new HorizonManager(sim);
        var a = new Rails(1, firstHorizon: 300, period: 1000, log);
        mgr.Register(a);

        sim.Schedule(new InvalidateEvent(100, mgr, a, newHorizon: 900)); // later — carries no info

        sim.RunUntil(400);

        // Still expires at its original 300, unaffected by the later "invalidation".
        Assert.Equal(new[] { (1L, 300L) }, log);
    }

    [Fact]
    public void Registering_A_Past_Horizon_Throws()
    {
        var sim = new Simulation(startSeconds: 100);
        var mgr = new HorizonManager(sim);
        Assert.Throws<InvalidOperationException>(
            () => mgr.Register(new Rails(1, firstHorizon: 50, period: 100, new List<(long, long)>())));
    }
}
