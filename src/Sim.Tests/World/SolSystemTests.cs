using Sim.Core.Numerics;
using Sim.Core.World;

namespace Sim.Tests.World;

/// <summary>
/// The real J2000 Sol elements (§3, §5 side-task) must produce physically sane orbits: right
/// distances from the Sun, right periods, and a periodic ephemeris.
/// </summary>
public sealed class SolSystemTests
{
    private const long AuMm = 149_597_870_700_000L;

    private static double AuFromSun(Body body, long t)
    {
        (long x, long y, long z) = body.PositionMmAt(t);
        return (double)IntMath.DistanceMm(x, y, z, 0, 0, 0) / AuMm;
    }

    [Theory]
    [InlineData(SolSystem.MercuryId, 0.31, 0.47)]   // a(1±e) = 0.387(1±0.206)
    [InlineData(SolSystem.VenusId, 0.71, 0.73)]
    [InlineData(SolSystem.EarthId, 0.98, 1.02)]
    [InlineData(SolSystem.MarsId, 1.38, 1.67)]
    [InlineData(SolSystem.JupiterId, 4.95, 5.46)]
    [InlineData(SolSystem.CeresId, 2.55, 2.98)]
    public void Body_Sits_At_Its_Expected_Distance_From_The_Sun(long id, double loAu, double hiAu)
    {
        Body body = SolSystem.BuildBody(SolSystem.Catalog.Single(e => e.Id == id));
        double r = AuFromSun(body, 0);
        Assert.InRange(r, loAu, hiAu);
    }

    [Fact]
    public void Ephemeris_Is_Periodic_Over_One_Orbit()
    {
        SolSystem.Entry earth = SolSystem.Catalog.Single(e => e.Id == SolSystem.EarthId);
        Body body = SolSystem.BuildBody(earth);
        long period = earth.Elements.PeriodSeconds;

        (long x0, long y0, long z0) = body.PositionMmAt(1_000_000);
        (long x1, long y1, long z1) = body.PositionMmAt(1_000_000 + period);

        // One period later the body returns to (essentially) the same point — table wrap is exact.
        Assert.True(IntMath.DistanceMm(x0, y0, z0, x1, y1, z1) < AuMm / 1000, "orbit should close after one period");
    }

    [Fact]
    public void Earth_Period_Is_About_One_Year()
    {
        long earthPeriod = SolSystem.Earth.PeriodSeconds;
        Assert.InRange(earthPeriod, 364L * 86_400, 366L * 86_400);
    }

    [Fact]
    public void Catalog_Is_Ordered_And_Complete()
    {
        Assert.Equal(9, SolSystem.Catalog.Count);
        long[] ids = SolSystem.Catalog.Select(e => e.Id).ToArray();
        Assert.Equal(ids.OrderBy(i => i).ToArray(), ids); // §2.3 r3: defined order
    }
}
