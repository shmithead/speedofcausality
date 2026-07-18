using Sim.Core.Comms;
using Sim.Core.Numerics;

namespace Sim.Tests.Comms;

public sealed class ReceptionTests
{
    private const long AuMm = 149_597_870_700_000L; // 1 astronomical unit in millimetres
    private const long C = Reception.SpeedOfLightMmPerSec;

    [Fact]
    public void ClosedForm_Is_Distance_Over_C()
    {
        // Observer one AU from a source at the origin: light takes ~499 s.
        long t = Reception.ClosedForm(AuMm, 0, 0, 0, 0, 0, tEmit: 1000);
        Assert.Equal(1000 + IntMath.DivRound(AuMm, C), t);
        Assert.Equal(1000 + 499, t);
    }

    [Fact]
    public void Solve_With_A_Stationary_Observer_Matches_ClosedForm()
    {
        (long, long, long) Fixed(long _) => (AuMm, 0, 0);
        long solved = Reception.Solve(Fixed, 0, 0, 0, tEmit: 1000);
        long closed = Reception.ClosedForm(AuMm, 0, 0, 0, 0, 0, tEmit: 1000);
        Assert.True(Math.Abs(solved - closed) <= 1, $"solved={solved} closed={closed}");
    }

    [Fact]
    public void Solve_Satisfies_The_Light_Cone_Equation_For_A_Moving_Observer()
    {
        const long tEmit = 5000;
        const long vx = 30_000_000L; // 30 km/s, a plausible orbital speed, ≪ c
        const long y0 = 20_000_000_000_000L; // offset so the observer isn't collinear

        (long X, long Y, long Z) ObserverAt(long t) => (AuMm + (vx * (t - tEmit)), y0, 0);

        long recv = Reception.Solve(ObserverAt, 0, 0, 0, tEmit);

        Assert.True(recv > tEmit); // first light is the floor — never instant

        // Bisection invariant: light has arrived at recv, but not one second earlier.
        Assert.True(F(recv) <= 0, "distance should be within c·Δt at reception");
        Assert.True(F(recv - 1) > 0, "light should not have arrived one second earlier");

        long F(long t)
        {
            (long x, long y, long z) = ObserverAt(t);
            long dist = IntMath.DistanceMm(x, y, z, 0, 0, 0);
            return dist - (C * (t - tEmit));
        }
    }

    [Fact]
    public void Coincident_Observer_And_Source_Receives_At_Emission()
    {
        (long, long, long) Here(long _) => (12_000, -3_000, 500);
        long t = Reception.Solve(Here, 12_000, -3_000, 500, tEmit: 42);
        Assert.Equal(42, t);
    }

    [Fact]
    public void Reception_Is_Deterministic()
    {
        (long X, long Y, long Z) Obs(long t) => (AuMm - (1_000_000L * t), 5_000_000_000L, 0);
        long a = Reception.Solve(Obs, 0, 0, 0, 0);
        long b = Reception.Solve(Obs, 0, 0, 0, 0);
        Assert.Equal(a, b);
    }
}
