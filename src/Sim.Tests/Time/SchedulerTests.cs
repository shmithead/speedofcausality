using Sim.Core.Time;

namespace Sim.Tests.Time;

public sealed class SchedulerTests
{
    private sealed class RecordEvent : ISimEvent
    {
        private readonly List<(long Time, long Ordinal)> _log;

        public RecordEvent(long time, long ordinal, List<(long Time, long Ordinal)> log)
        {
            TimeSeconds = time;
            Ordinal = ordinal;
            _log = log;
        }

        public long TimeSeconds { get; }
        public long Ordinal { get; }
        public void Apply(ISimContext ctx) => _log.Add((TimeSeconds, Ordinal));
    }

    // Fires once, then schedules a RecordEvent `delay` seconds later.
    private sealed class CascadeEvent : ISimEvent
    {
        private readonly long _delay;
        private readonly List<(long Time, long Ordinal)> _log;

        public CascadeEvent(long time, long ordinal, long delay, List<(long Time, long Ordinal)> log)
        {
            TimeSeconds = time;
            Ordinal = ordinal;
            _delay = delay;
            _log = log;
        }

        public long TimeSeconds { get; }
        public long Ordinal { get; }

        public void Apply(ISimContext ctx)
        {
            _log.Add((TimeSeconds, Ordinal));
            ctx.Schedule(new RecordEvent(ctx.NowSeconds + _delay, Ordinal, _log));
        }
    }

    private static readonly (long Time, long Ordinal)[] Specs =
    {
        (100, 2), (100, 1), (50, 5), (200, 0), (50, 1), (150, 9), (100, 3),
    };

    private static List<(long Time, long Ordinal)> RunInOrder(IEnumerable<(long Time, long Ordinal)> order)
    {
        var log = new List<(long Time, long Ordinal)>();
        var sim = new Simulation();
        foreach ((long t, long o) in order)
        {
            sim.Schedule(new RecordEvent(t, o, log));
        }

        sim.RunUntil(long.MaxValue);
        return log;
    }

    [Fact]
    public void Processing_Order_Is_Independent_Of_Scheduling_Order()
    {
        // The load-bearing property (§2.3 r7/r8): shuffle the insertion order, get the same result.
        var forward = RunInOrder(Specs);
        var reversed = RunInOrder(Specs.Reverse());
        Assert.Equal(forward, reversed);
    }

    [Fact]
    public void Events_Fire_Sorted_By_Time_Then_Ordinal()
    {
        var expected = Specs.OrderBy(s => s.Time).ThenBy(s => s.Ordinal).ToList();
        Assert.Equal(expected, RunInOrder(Specs));
    }

    [Fact]
    public void Cascaded_Events_Are_Processed_And_Advance_The_Clock()
    {
        var log = new List<(long Time, long Ordinal)>();
        var sim = new Simulation();
        sim.Schedule(new CascadeEvent(0, 7, delay: 10, log));

        sim.RunUntil(100);

        Assert.Equal(new[] { (0L, 7L), (10L, 7L) }, log);
        Assert.Equal(100, sim.NowSeconds); // clock advanced to the window end
        Assert.Equal(0, sim.PendingCount);
    }

    [Fact]
    public void Step_Processes_Only_The_Earliest_Event()
    {
        var log = new List<(long Time, long Ordinal)>();
        var sim = new Simulation();
        sim.Schedule(new RecordEvent(200, 0, log));
        sim.Schedule(new RecordEvent(50, 0, log));

        Assert.Equal(50, sim.NextEventTime);
        Assert.True(sim.Step());
        Assert.Equal(new[] { (50L, 0L) }, log);
        Assert.Equal(50, sim.NowSeconds);
        Assert.Equal(1, sim.PendingCount);
    }

    [Fact]
    public void Scheduling_Into_The_Past_Throws()
    {
        var sim = new Simulation(startSeconds: 100);
        Assert.Throws<InvalidOperationException>(
            () => sim.Schedule(new RecordEvent(50, 0, new List<(long, long)>())));
    }

    [Fact]
    public void Empty_Queue_Steps_To_False()
    {
        var sim = new Simulation();
        Assert.False(sim.Step());
        Assert.Null(sim.NextEventTime);
    }
}
