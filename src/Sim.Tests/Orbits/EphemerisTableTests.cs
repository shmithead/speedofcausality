using Sim.Core.Numerics;
using Sim.Core.Orbits;

namespace Sim.Tests.Orbits;

public sealed class EphemerisTableTests
{
    private static KeplerianElements MarsLike() => new()
    {
        SemiMajorAxisGm = Fixed64.FromDouble(227.9),
        Eccentricity = Fixed64.FromDouble(0.0934),
        InclinationRad = Fixed64.FromDouble(0.0323),
        AscendingNodeRad = Fixed64.FromDouble(0.865),
        ArgPeriapsisRad = Fixed64.FromDouble(5.000),
        MeanAnomalyEpochRad = Fixed64.FromDouble(0.338),
        PeriodSeconds = 59_355_000L,
    };

    private static long MaxInterpErrorMm(KeplerianElements el, int samples)
    {
        var table = EphemerisTable.Build(el, samples);
        long worst = 0;
        long step = el.PeriodSeconds / 997;
        for (long t = 0; t < el.PeriodSeconds; t += step)
        {
            var (tx, ty, tz) = table.PositionMmAt(t);
            Vec3 d = Kepler.PositionAt(el, t);
            long e = Math.Max(
                Math.Abs(tx - EphemerisTable.GmToMm(d.X)),
                Math.Max(Math.Abs(ty - EphemerisTable.GmToMm(d.Y)), Math.Abs(tz - EphemerisTable.GmToMm(d.Z))));
            worst = Math.Max(worst, e);
        }

        return worst;
    }

    [Fact]
    public void More_Samples_Reduce_Interpolation_Error()
    {
        var el = MarsLike();
        // Linear interp error scales ~1/N²; quadrupling samples should cut error by well over half.
        long coarse = MaxInterpErrorMm(el, 2048);
        long fine = MaxInterpErrorMm(el, 8192);
        Assert.True(fine < coarse / 2, $"coarse={coarse}mm fine={fine}mm — refinement did not converge");
    }

    [Fact]
    public void Interpolated_Positions_Track_Direct_Kepler()
    {
        var el = MarsLike();
        var table = EphemerisTable.Build(el, samples: 8192);

        long maxErrMm = 0;
        long worstT = 0;
        // Sample between grid points across more than one period.
        long step = el.PeriodSeconds / 997; // deliberately coprime-ish with the sample grid
        for (long t = 0; t < el.PeriodSeconds * 2; t += step)
        {
            var (tx, ty, tz) = table.PositionMmAt(t);
            Vec3 d = Kepler.PositionAt(el, t);
            long ex = Math.Abs(tx - EphemerisTable.GmToMm(d.X));
            long ey = Math.Abs(ty - EphemerisTable.GmToMm(d.Y));
            long ez = Math.Abs(tz - EphemerisTable.GmToMm(d.Z));
            long e = Math.Max(ex, Math.Max(ey, ez));
            if (e > maxErrMm)
            {
                maxErrMm = e;
                worstT = t;
            }
        }

        // 8192 samples over one period: linear-interp error stays well under 50 km for Mars-scale.
        const long toleranceMm = 50_000_000L; // 50 km
        Assert.True(maxErrMm < toleranceMm, $"max interp error {maxErrMm} mm ({maxErrMm / 1_000_000.0:F1} km) at t={worstT}s");
    }

    [Fact]
    public void Position_Is_Periodic()
    {
        var el = MarsLike();
        var table = EphemerisTable.Build(el, samples: 4096);

        var a = table.PositionMmAt(12_345_678L);
        var b = table.PositionMmAt(12_345_678L + el.PeriodSeconds);
        Assert.Equal(a, b); // exact periodicity — same phase, same lookup
    }

    [Fact]
    public void Lookup_Is_Deterministic()
    {
        var el = MarsLike();
        var table = EphemerisTable.Build(el, samples: 4096);
        Assert.Equal(table.PositionMmAt(9_000_000L), table.PositionMmAt(9_000_000L));
    }
}
