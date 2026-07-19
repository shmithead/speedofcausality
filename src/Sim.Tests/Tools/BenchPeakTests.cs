using Sim.Tools;

namespace Sim.Tests.Tools;

public sealed class BenchPeakTests
{
    private const long Cadence = 10 * 86_400L;

    private static BenchPeak.Config Config(ulong seed) =>
        new(CorrelatedShips: 100, IndependentShips: 100, CadenceSeconds: Cadence, SpanSeconds: 5 * Cadence, Seed: seed);

    [Fact]
    public void Tick_Time_Sequence_Is_Reproducible_For_A_Seed()
    {
        // Non-vacuous: tick times shift with the seeded stagger, so identical output across runs
        // proves the RNG path actually reproduces (§2.3 r4) — not merely that a seed-invariant
        // quantity is stable.
        long[] first = BenchPeak.DecisionTickTimes(Config(42));
        long[] second = BenchPeak.DecisionTickTimes(Config(42));
        Assert.Equal(first, second);
    }

    [Fact]
    public void Different_Seeds_Produce_Different_Tick_Times()
    {
        // Confirms the seed genuinely drives the schedule (the RNG isn't being ignored).
        long[] a = BenchPeak.DecisionTickTimes(Config(1));
        long[] b = BenchPeak.DecisionTickTimes(Config(2));
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Per_Tick_Decision_Counts_Are_Reproducible_For_A_Seed()
    {
        int[] first = BenchPeak.DecisionCountsPerTick(Config(42));
        int[] second = BenchPeak.DecisionCountsPerTick(Config(42));
        Assert.Equal(first, second);
    }

    [Fact]
    public void Correlated_Ships_Produce_A_Peak_Tick()
    {
        // 100 correlated ships all fire together on each trigger tick.
        int[] counts = BenchPeak.DecisionCountsPerTick(Config(42));
        Assert.True(counts.Max() >= 100, $"expected a correlated peak of >=100, got {counts.Max()}");
    }
}
