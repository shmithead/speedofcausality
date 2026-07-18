using Sim.Core.Numerics;

namespace Sim.Core.Orbits;

/// <summary>
/// Closed-form Keplerian position (roadmap §3): mean anomaly → eccentric anomaly (Newton on the
/// deterministic <see cref="Trig"/> solver) → perifocal coordinates → rotation into the inertial
/// (ecliptic) frame. No n-body integration, no <c>System.Math</c>. This is what the ephemeris
/// table (§2.7) is precomputed from — bodies are the infinite-horizon class.
/// </summary>
public static class Kepler
{
    // Planetary eccentricities are small; Newton from E₀ = M converges in a few steps. A fixed
    // count keeps it deterministic — never "iterate until converged", which can vary by platform.
    private const int NewtonIterations = 12;

    /// <summary>
    /// Solves Kepler's equation <c>M = E − e·sin(E)</c> for the eccentric anomaly <c>E</c>,
    /// via a fixed number of Newton steps.
    /// </summary>
    public static Fixed64 SolveEccentricAnomaly(Fixed64 meanAnomaly, Fixed64 eccentricity)
    {
        Fixed64 e = eccentricity;
        Fixed64 m = meanAnomaly;
        Fixed64 eccentric = m; // initial guess

        for (int k = 0; k < NewtonIterations; k++)
        {
            (Fixed64 sinE, Fixed64 cosE) = Trig.SinCos(eccentric);
            Fixed64 f = eccentric - (e * sinE) - m;
            Fixed64 fPrime = Fixed64.One - (e * cosE);
            eccentric -= f / fPrime;
        }

        return eccentric;
    }

    /// <summary>
    /// Position of a body at <paramref name="secondsSinceEpoch"/> in the inertial frame,
    /// in gigametres.
    /// </summary>
    public static Vec3 PositionAt(in KeplerianElements el, long secondsSinceEpoch)
    {
        Fixed64 meanAnomaly = MeanAnomalyAt(el, secondsSinceEpoch);
        Fixed64 e = el.Eccentricity;
        (Fixed64 sinE, Fixed64 cosE) = Trig.SinCos(SolveEccentricAnomaly(meanAnomaly, e));

        // Perifocal coordinates (orbital plane), gigametres.
        Fixed64 a = el.SemiMajorAxisGm;
        Fixed64 semiMinorFactor = Fixed64.Sqrt(Fixed64.One - (e * e)); // √(1−e²)
        Fixed64 xOrb = a * (cosE - e);
        Fixed64 yOrb = a * semiMinorFactor * sinE;

        // Rotate perifocal → inertial: Rz(Ω)·Rx(i)·Rz(ω) (Vallado).
        (Fixed64 sinO, Fixed64 cosO) = Trig.SinCos(el.AscendingNodeRad);
        (Fixed64 sinI, Fixed64 cosI) = Trig.SinCos(el.InclinationRad);
        (Fixed64 sinW, Fixed64 cosW) = Trig.SinCos(el.ArgPeriapsisRad);

        Fixed64 x = (xOrb * ((cosO * cosW) - (sinO * sinW * cosI)))
                  - (yOrb * ((cosO * sinW) + (sinO * cosW * cosI)));
        Fixed64 y = (xOrb * ((sinO * cosW) + (cosO * sinW * cosI)))
                  - (yOrb * ((sinO * sinW) - (cosO * cosW * cosI)));
        Fixed64 z = (xOrb * (sinW * sinI)) + (yOrb * (cosW * sinI));

        return new Vec3(x, y, z);
    }

    /// <summary>
    /// Mean anomaly at a given time, reduced by the orbital period. Uses a 128-bit intermediate
    /// so the time-in-period ratio is exact even for large <paramref name="secondsSinceEpoch"/>.
    /// </summary>
    public static Fixed64 MeanAnomalyAt(in KeplerianElements el, long secondsSinceEpoch)
    {
        long period = el.PeriodSeconds;

        // Remainder in [0, period), correct for negative times.
        long remainder = ((secondsSinceEpoch % period) + period) % period;

        // phase = remainder / period ∈ [0, 1), as a Q32.32 fraction.
        long phaseRaw = (long)(((Int128)remainder << Fixed64.FractionBits) / period);
        Fixed64 phase = Fixed64.FromRaw(phaseRaw);

        return el.MeanAnomalyEpochRad + (Trig.TwoPi * phase);
    }
}
