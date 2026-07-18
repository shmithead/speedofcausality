using Sim.Core.Numerics;
using Sim.Core.Orbits;

namespace Sim.Tests.Orbits;

public sealed class KeplerTests
{
    // Independent double-precision reference (System.Math is allowed in the test project).
    public sealed record RefBody(double A, double E, double I, double Node, double ArgP, double M0, long Period);

    private static double RefSolveE(double m, double e)
    {
        double eccentric = m;
        for (int k = 0; k < 12; k++)
        {
            eccentric -= (eccentric - (e * Math.Sin(eccentric)) - m) / (1.0 - (e * Math.Cos(eccentric)));
        }

        return eccentric;
    }

    private static (double X, double Y, double Z) RefPosition(RefBody b, long t)
    {
        long remainder = ((t % b.Period) + b.Period) % b.Period;
        double phase = (double)remainder / b.Period;
        double m = b.M0 + (2.0 * Math.PI * phase);
        double eccentric = RefSolveE(m, b.E);

        double xOrb = b.A * (Math.Cos(eccentric) - b.E);
        double yOrb = b.A * Math.Sqrt(1.0 - (b.E * b.E)) * Math.Sin(eccentric);

        double so = Math.Sin(b.Node), co = Math.Cos(b.Node);
        double si = Math.Sin(b.I), ci = Math.Cos(b.I);
        double sw = Math.Sin(b.ArgP), cw = Math.Cos(b.ArgP);

        double x = (xOrb * ((co * cw) - (so * sw * ci))) - (yOrb * ((co * sw) + (so * cw * ci)));
        double y = (xOrb * ((so * cw) + (co * sw * ci))) - (yOrb * ((so * sw) - (co * cw * ci)));
        double z = (xOrb * (sw * si)) + (yOrb * (cw * si));
        return (x, y, z);
    }

    private static KeplerianElements ToElements(RefBody b) => new()
    {
        SemiMajorAxisGm = Fixed64.FromDouble(b.A),
        Eccentricity = Fixed64.FromDouble(b.E),
        InclinationRad = Fixed64.FromDouble(b.I),
        AscendingNodeRad = Fixed64.FromDouble(b.Node),
        ArgPeriapsisRad = Fixed64.FromDouble(b.ArgP),
        MeanAnomalyEpochRad = Fixed64.FromDouble(b.M0),
        PeriodSeconds = b.Period,
    };

    public static IEnumerable<object[]> Bodies()
    {
        // a(Gm), e, i, Ω, ω, M0, period(s) — Earth-like, Mars-like, and a high-eccentricity stress.
        yield return new object[] { new RefBody(149.6, 0.0167, 0.0, 0.0, 1.993, 6.240, 31_558_150L) };
        yield return new object[] { new RefBody(227.9, 0.0934, 0.0323, 0.865, 5.000, 0.338, 59_355_000L) };
        yield return new object[] { new RefBody(57.9, 0.2056, 0.1222, 0.843, 0.508, 3.050, 7_600_530L) };
    }

    [Theory]
    [MemberData(nameof(Bodies))]
    public void PositionAt_Matches_Double_Reference_Over_An_Orbit(RefBody b)
    {
        double maxError = 0;
        long worstT = 0;
        // Sample across more than a full period, plus a negative time.
        long[] times =
        {
            0, b.Period / 4, b.Period / 3, b.Period / 2,
            (long)(0.9 * b.Period), (long)(3.7 * b.Period), -(long)(0.3 * b.Period),
        };

        foreach (long t in times)
        {
            Vec3 got = Kepler.PositionAt(ToElements(b), t);
            (double rx, double ry, double rz) = RefPosition(b, t);

            double ex = Math.Abs(got.X.ToDouble() - rx);
            double ey = Math.Abs(got.Y.ToDouble() - ry);
            double ez = Math.Abs(got.Z.ToDouble() - rz);
            double e = Math.Max(ex, Math.Max(ey, ez));
            if (e > maxError)
            {
                maxError = e;
                worstT = t;
            }
        }

        // Gigametre units: 1e-5 Gm = 10 km. CORDIC + fixed-point lands far inside this.
        Assert.True(maxError < 1e-5, $"max position error {maxError:E3} Gm at t={worstT}s");
    }

    [Fact]
    public void SolveEccentricAnomaly_Satisfies_Keplers_Equation()
    {
        // For E returned, M = E - e sin E must recover the input M.
        foreach (double e in new[] { 0.0, 0.05, 0.2, 0.4 })
        {
            for (double m = -3.0; m <= 3.0; m += 0.37)
            {
                Fixed64 eccentric = Kepler.SolveEccentricAnomaly(Fixed64.FromDouble(m), Fixed64.FromDouble(e));
                double eDouble = eccentric.ToDouble();
                double recoveredM = eDouble - (e * Math.Sin(eDouble));
                Assert.Equal(m, recoveredM, precision: 4);
            }
        }
    }

    [Fact]
    public void Circular_Equatorial_Orbit_Has_Constant_Radius()
    {
        var el = new KeplerianElements
        {
            SemiMajorAxisGm = Fixed64.FromInt(100),
            Eccentricity = Fixed64.Zero,
            InclinationRad = Fixed64.Zero,
            AscendingNodeRad = Fixed64.Zero,
            ArgPeriapsisRad = Fixed64.Zero,
            MeanAnomalyEpochRad = Fixed64.Zero,
            PeriodSeconds = 10_000_000L,
        };

        for (long t = 0; t <= el.PeriodSeconds; t += el.PeriodSeconds / 16)
        {
            Vec3 p = Kepler.PositionAt(el, t);
            double r = Math.Sqrt((p.X.ToDouble() * p.X.ToDouble()) + (p.Y.ToDouble() * p.Y.ToDouble()));
            Assert.Equal(100.0, r, precision: 4); // circle of radius a, z ≈ 0
            Assert.Equal(0.0, p.Z.ToDouble(), precision: 5);
        }
    }
}
