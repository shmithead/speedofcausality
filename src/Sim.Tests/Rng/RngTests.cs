using Sim.Core.Rng;

namespace Sim.Tests.Rng;

public sealed class RngTests
{
    [Fact]
    public void Same_Seed_Reproduces_The_Sequence()
    {
        var a = new RngStream(0xDEADBEEF);
        var b = new RngStream(0xDEADBEEF);
        for (int i = 0; i < 100; i++)
        {
            Assert.Equal(a.NextULong(), b.NextULong());
        }
    }

    [Fact]
    public void Forking_By_Name_Is_Order_Independent()
    {
        // §2.3 r4: a stream depends only on (worldSeed, name) — never on how many others exist
        // or the order they were forked. This is what lets you add a subsystem without perturbing
        // another's rolls.
        var streamsA = new RngStreams(worldSeed: 777);
        RngStream marketFirst = streamsA.Fork("market");

        var streamsB = new RngStreams(worldSeed: 777);
        streamsB.Fork("captains"); // fork something else first
        streamsB.Fork("combat");
        RngStream marketLater = streamsB.Fork("market");

        for (int i = 0; i < 50; i++)
        {
            Assert.Equal(marketFirst.NextULong(), marketLater.NextULong());
        }
    }

    [Fact]
    public void Different_Names_Give_Different_Streams()
    {
        var streams = new RngStreams(worldSeed: 42);
        RngStream a = streams.Fork("orbits");
        RngStream b = streams.Fork("market");
        Assert.NotEqual(a.NextULong(), b.NextULong());
    }

    [Fact]
    public void Different_World_Seeds_Give_Different_Streams()
    {
        RngStream a = new RngStreams(1).Fork("orbits");
        RngStream b = new RngStreams(2).Fork("orbits");
        Assert.NotEqual(a.NextULong(), b.NextULong());
    }

    [Fact]
    public void NextInt_Stays_In_Range()
    {
        var rng = new RngStream(123);
        for (int i = 0; i < 10_000; i++)
        {
            int v = rng.NextInt(-5, 10);
            Assert.InRange(v, -5, 9);
        }
    }

    [Fact]
    public void NextBounded_Is_Below_Bound()
    {
        var rng = new RngStream(99);
        for (int i = 0; i < 10_000; i++)
        {
            Assert.True(rng.NextBounded(7) < 7);
        }
    }

    [Fact]
    public void NextBounded_Covers_Its_Whole_Range()
    {
        var rng = new RngStream(2024);
        var seen = new bool[6];
        for (int i = 0; i < 2_000; i++)
        {
            seen[rng.NextBounded(6)] = true;
        }

        Assert.All(seen, Assert.True); // every face of a d6 appears
    }
}
