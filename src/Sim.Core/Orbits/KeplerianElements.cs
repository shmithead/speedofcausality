using Sim.Core.Numerics;

namespace Sim.Core.Orbits;

/// <summary>
/// Classical Keplerian orbital elements for one body (roadmap §3). Distances in gigametres,
/// angles in radians. The mean anomaly is driven by the orbital <see cref="PeriodSeconds"/>
/// rather than a stored mean motion — <c>n</c> in rad/s is ~1e-7 and far too coarse for Q32.32,
/// whereas a period + a 128-bit time ratio stays exact over the sim's 50-year span.
/// </summary>
public readonly struct KeplerianElements
{
    /// <summary>Semi-major axis <c>a</c>, gigametres.</summary>
    public Fixed64 SemiMajorAxisGm { get; init; }

    /// <summary>Eccentricity <c>e</c> (0 = circular, &lt;1 = elliptical).</summary>
    public Fixed64 Eccentricity { get; init; }

    /// <summary>Inclination <c>i</c>, radians.</summary>
    public Fixed64 InclinationRad { get; init; }

    /// <summary>Longitude of the ascending node <c>Ω</c>, radians.</summary>
    public Fixed64 AscendingNodeRad { get; init; }

    /// <summary>Argument of periapsis <c>ω</c>, radians.</summary>
    public Fixed64 ArgPeriapsisRad { get; init; }

    /// <summary>Mean anomaly at epoch <c>M₀</c>, radians.</summary>
    public Fixed64 MeanAnomalyEpochRad { get; init; }

    /// <summary>Orbital period in seconds. Drives the mean anomaly.</summary>
    public long PeriodSeconds { get; init; }
}
