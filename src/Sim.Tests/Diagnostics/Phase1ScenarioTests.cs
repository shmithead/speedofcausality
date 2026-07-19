using Sim.Core.Diagnostics;
using Sim.Core.World;

namespace Sim.Tests.Diagnostics;

/// <summary>
/// The canonical Phase 1 sim is the determinism fixture (roadmap §2.3, §4): same seed → byte-identical
/// event log, and its knowledge fold is reproducible. This is the growing-sim counterpart to the tiny
/// smoke canary — milestone-disposable (§2.6), regenerated at phase boundaries.
/// </summary>
public sealed class Phase1ScenarioTests
{
    private const long Day = 86_400L;

    private static string RunGolden(ulong seed, long until)
    {
        SimWorld world = Phase1Scenario.Build(seed);
        world.Sim.RunUntil(until);
        return Phase1Scenario.EventLogGolden(world);
    }

    [Fact]
    public void Same_Seed_Produces_Byte_Identical_Event_Log()
    {
        Assert.Equal(RunGolden(42, 200 * Day), RunGolden(42, 200 * Day));
    }

    [Fact]
    public void Different_Seed_Changes_The_Market_And_So_The_Log()
    {
        // Only the market walk is seeded; a different seed must move prices and thus the golden.
        Assert.NotEqual(RunGolden(1, 200 * Day), RunGolden(2, 200 * Day));
    }

    [Fact]
    public void Knowledge_Fold_Is_Reproducible()
    {
        SimWorld a = Phase1Scenario.Build(7);
        SimWorld b = Phase1Scenario.Build(7);
        a.Sim.RunUntil(120 * Day);
        b.Sim.RunUntil(120 * Day);

        Assert.Equal(
            Phase1Scenario.KnowledgeGolden(a, Phase1Scenario.HqId, 120 * Day),
            Phase1Scenario.KnowledgeGolden(b, Phase1Scenario.HqId, 120 * Day));
    }

    [Fact]
    public void Scenario_Actually_Runs_Both_Ships_And_The_Market()
    {
        SimWorld world = Phase1Scenario.Build(42);
        world.Sim.RunUntil(200 * Day);
        string golden = RunGolden(42, 200 * Day);

        Assert.Contains("FlightPlanFiled", golden);
        Assert.Contains("PriceFixed", golden);
        Assert.Contains("Cause = Arrived", golden); // at least one ship reached a port
    }

    [Fact]
    public void Hq_Knows_A_Stale_Mars_Price_By_Day_120()
    {
        SimWorld world = Phase1Scenario.Build(42);
        world.Sim.RunUntil(120 * Day);
        string knowledge = Phase1Scenario.KnowledgeGolden(world, Phase1Scenario.HqId, 120 * Day);

        Assert.Contains("settlement 101:", knowledge); // a Mars-Port quote has arrived
        Assert.DoesNotContain("age 0s", knowledge);    // and it is stale, never fresh
    }
}
