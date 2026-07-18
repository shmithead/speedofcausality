using Sim.Tools;

namespace Sim.Tests.Tools;

public sealed class BenchPeakTests
{
    private const long Cadence = 10 * 86_400L;

    private static BenchPeak.Config Config(ulong seed) =>
        new(CorrelatedShips: 100, IndependentShips: 100, CadenceSeconds: Cadence, SpanSeconds: 5 * Cadence, Seed: seed);

    [Fact]
    public void Per_Tick_Decision_Counts_Are_Deterministic()
    {
        // The *work pattern* is deterministic (§2.3 r4); only wall-time varies by machine.
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
