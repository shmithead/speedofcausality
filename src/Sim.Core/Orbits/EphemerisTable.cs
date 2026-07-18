using Sim.Core.Numerics;

namespace Sim.Core.Orbits;

/// <summary>
/// Precomputed integer ephemeris for one body (roadmap §2.7, the infinite-horizon class):
/// build once at world-gen, then every runtime position query is a table lookup + interpolation
/// instead of a Kepler solve. "Trig becomes a memory read."
///
/// <para>Because a pure Keplerian orbit is <b>exactly periodic</b> (fixed elements, no n-body
/// perturbation), only a single period is tabled — indexed by phase — so the table is compact
/// regardless of how many sim-years elapse. Positions are stored as int64 <b>millimetres</b>,
/// the sim's canonical position unit (§2.5); interpolation is integer-only and deterministic.</para>
/// </summary>
public sealed class EphemerisTable
{
    /// <summary>Millimetres per gigametre (1 Gm = 10⁹ m = 10¹² mm).</summary>
    public const long MmPerGm = 1_000_000_000_000L;

    private readonly long _periodSeconds;
    private readonly int _samples;
    private readonly long[] _x;
    private readonly long[] _y;
    private readonly long[] _z;

    private EphemerisTable(long periodSeconds, int samples, long[] x, long[] y, long[] z)
    {
        _periodSeconds = periodSeconds;
        _samples = samples;
        _x = x;
        _y = y;
        _z = z;
    }

    /// <summary>Number of samples tabled across one orbital period.</summary>
    public int SampleCount => _samples;

    /// <summary>
    /// Precomputes <paramref name="samples"/> positions evenly across one period. More samples =
    /// finer linear-interpolation accuracy; the default balances accuracy against memory for
    /// almanac-grade body positions.
    /// </summary>
    public static EphemerisTable Build(in KeplerianElements el, int samples = 8192)
    {
        if (samples < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(samples), "Need at least two samples.");
        }

        var x = new long[samples];
        var y = new long[samples];
        var z = new long[samples];

        for (int k = 0; k < samples; k++)
        {
            // Sample time = k/samples of one period, computed in 128 bits to stay exact.
            long t = (long)(((Int128)k * el.PeriodSeconds) / samples);
            Vec3 p = Kepler.PositionAt(el, t);
            x[k] = GmToMm(p.X);
            y[k] = GmToMm(p.Y);
            z[k] = GmToMm(p.Z);
        }

        return new EphemerisTable(el.PeriodSeconds, samples, x, y, z);
    }

    /// <summary>
    /// Body position at <paramref name="secondsSinceEpoch"/>, in millimetres, via phase lookup and
    /// linear interpolation between the two bracketing samples (wrapping at the period boundary).
    /// </summary>
    public (long X, long Y, long Z) PositionMmAt(long secondsSinceEpoch)
    {
        long period = _periodSeconds;
        long r = ((secondsSinceEpoch % period) + period) % period; // [0, period)

        // Fractional sample position: (r/period)·samples = index + frac.
        Int128 scaled = (Int128)r * _samples;
        long index = (long)(scaled / period);
        long remainder = (long)(scaled % period); // frac = remainder / period

        int k0 = (int)(index % _samples);
        int k1 = (int)((index + 1) % _samples);

        return (
            Lerp(_x[k0], _x[k1], remainder, period),
            Lerp(_y[k0], _y[k1], remainder, period),
            Lerp(_z[k0], _z[k1], remainder, period));
    }

    /// <summary>Linear interpolation <c>a + (b−a)·num/den</c> in int64, via a 128-bit intermediate.</summary>
    private static long Lerp(long a, long b, long num, long den)
        => a + (long)(((Int128)(b - a) * num) / den);

    /// <summary>Converts a gigametre <see cref="Fixed64"/> to integer millimetres, no floats.</summary>
    public static long GmToMm(Fixed64 gm)
        => (long)(((Int128)gm.Raw * MmPerGm) >> Fixed64.FractionBits);
}
