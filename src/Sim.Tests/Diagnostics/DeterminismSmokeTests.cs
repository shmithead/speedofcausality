using System.Text;
using Sim.Core.Diagnostics;

namespace Sim.Tests.Diagnostics;

public sealed class DeterminismSmokeTests
{
    private const ulong GoldenSeed = 42;
    private const int GoldenSteps = 128;

    private static string GoldenPath =>
        Path.Combine(AppContext.BaseDirectory, "Goldens", "determinism-smoke.txt");

    /// <summary>
    /// The canary (roadmap §2.6): output must stay byte-identical to the committed golden.
    /// A single differing byte here means the fixed-point foundation regressed — fail loudly.
    /// </summary>
    [Fact]
    public void Smoke_Output_Matches_Committed_Golden()
    {
        string expected = File.ReadAllText(GoldenPath, new UTF8Encoding(false));
        string actual = DeterminismSmoke.ToGolden(GoldenSeed, GoldenSteps);
        Assert.Equal(expected, actual);
    }

    /// <summary>Same seed, same output — the most basic replay guarantee.</summary>
    [Fact]
    public void Run_Is_Repeatable()
    {
        long[] first = DeterminismSmoke.Run(GoldenSeed, GoldenSteps);
        long[] second = DeterminismSmoke.Run(GoldenSeed, GoldenSteps);
        Assert.Equal(first, second);
    }

    /// <summary>Different seeds should not collapse to the same trace.</summary>
    [Fact]
    public void Different_Seeds_Diverge()
    {
        long[] a = DeterminismSmoke.Run(1, GoldenSteps);
        long[] b = DeterminismSmoke.Run(2, GoldenSteps);
        Assert.NotEqual(a, b);
    }
}
