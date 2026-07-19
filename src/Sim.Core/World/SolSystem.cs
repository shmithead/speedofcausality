using Sim.Core.Numerics;
using Sim.Core.Orbits;

namespace Sim.Core.World;

/// <summary>
/// Real Keplerian elements for the Sol bodies (roadmap §3 + the §5 side-task), at epoch <b>J2000</b>.
/// Sim.Core does no IO (§2.3 r6), so world-gen data is code: the planetary elements are the JPL/Standish
/// mean elements (valid ~1800–2050), Ceres from JPL small-body data. This replaces the "Earth-like"
/// stand-ins used during bring-up so the ephemeris (§2.7) is driven by the real system — which is what
/// makes the information-lag distances (Earth↔Mars minutes, outer system hours) actually real.
///
/// <para>Elements are converted to the sim's conventions once at world-gen (gigametres, radians,
/// seconds) via <see cref="Fixed64.FromDouble"/> — permitted here because this is config, evaluated
/// once outside any tick, and a plain double multiply is portable (no transcendentals, §2.5).</para>
/// </summary>
public static class SolSystem
{
    private const double DegToRad = 0.017453292519943295; // π/180, as a literal (System.Math is banned, §2.5)
    private const double AuToGm = 149.5978707;            // 1 AU in gigametres
    private const long SecondsPerDay = 86_400L;

    /// <summary>Stable body entity ids (also horizon-expiry ordinals for anything registered, §2.3 r8).</summary>
    public const long MercuryId = 1;
    public const long VenusId = 2;
    public const long EarthId = 3;
    public const long MarsId = 4;
    public const long JupiterId = 5;
    public const long SaturnId = 6;
    public const long UranusId = 7;
    public const long NeptuneId = 8;
    public const long CeresId = 9;

    // a(AU), e, i(°), Ω node(°), ω argPeri(°), M0(°), sidereal period(days). Angles already reduced
    // to [0,360). Planetary values: Standish J2000 mean elements (ω = ϖ−Ω, M0 = L−ϖ). Ceres: JPL J2000.
    public static readonly KeplerianElements Mercury = From(0.38709927, 0.20563593, 7.00497902, 48.33076593, 29.12703035, 174.79252722, 87.9691);
    public static readonly KeplerianElements Venus = From(0.72333566, 0.00677672, 3.39467605, 76.67984255, 54.92262463, 50.37663232, 224.701);
    public static readonly KeplerianElements Earth = From(1.00000261, 0.01671123, 0.00000000, 0.00000000, 102.93768193, 357.52688973, 365.256);
    public static readonly KeplerianElements Mars = From(1.52371034, 0.09339410, 1.84969142, 49.55953891, 286.49683150, 19.39019754, 686.980);
    public static readonly KeplerianElements Jupiter = From(5.20288700, 0.04838624, 1.30439695, 100.47390909, 274.25457074, 19.66796068, 4332.589);
    public static readonly KeplerianElements Saturn = From(9.53667594, 0.05386179, 2.48599187, 113.66242448, 338.93645383, 317.35536592, 10759.22);
    public static readonly KeplerianElements Uranus = From(19.18916464, 0.04725744, 0.77263783, 74.01692503, 96.93735127, 142.28382821, 30685.4);
    public static readonly KeplerianElements Neptune = From(30.06992276, 0.00859048, 1.77004347, 131.78422574, 273.18053653, 259.91520804, 60189.0);
    public static readonly KeplerianElements Ceres = From(2.7691, 0.0760, 10.594, 80.305, 73.597, 77.372, 1681.63);

    /// <summary>One catalog entry: canonical id, display name, and elements.</summary>
    public readonly record struct Entry(long Id, string Name, KeplerianElements Elements);

    /// <summary>The bodies in id order (§2.3 r3) — the world-gen source for building the ephemeris.</summary>
    public static readonly IReadOnlyList<Entry> Catalog = new[]
    {
        new Entry(MercuryId, "Mercury", Mercury),
        new Entry(VenusId, "Venus", Venus),
        new Entry(EarthId, "Earth", Earth),
        new Entry(MarsId, "Mars", Mars),
        new Entry(JupiterId, "Jupiter", Jupiter),
        new Entry(SaturnId, "Saturn", Saturn),
        new Entry(UranusId, "Uranus", Uranus),
        new Entry(NeptuneId, "Neptune", Neptune),
        new Entry(CeresId, "Ceres", Ceres),
    };

    /// <summary>
    /// Builds a <see cref="Body"/> for a catalog entry with a precomputed ephemeris table (§2.7). More
    /// <paramref name="samples"/> is finer interpolation; the default matches <see cref="EphemerisTable.Build"/>.
    /// </summary>
    public static Body BuildBody(in Entry entry, int samples = 8192)
        => new(entry.Id, entry.Name, EphemerisTable.Build(entry.Elements, samples));

    private static KeplerianElements From(
        double aAu, double e, double iDeg, double nodeDeg, double argPeriDeg, double m0Deg, double periodDays)
        => new()
        {
            SemiMajorAxisGm = Fixed64.FromDouble(aAu * AuToGm),
            Eccentricity = Fixed64.FromDouble(e),
            InclinationRad = Fixed64.FromDouble(iDeg * DegToRad),
            AscendingNodeRad = Fixed64.FromDouble(nodeDeg * DegToRad),
            ArgPeriapsisRad = Fixed64.FromDouble(argPeriDeg * DegToRad),
            MeanAnomalyEpochRad = Fixed64.FromDouble(m0Deg * DegToRad),
            PeriodSeconds = (long)(periodDays * SecondsPerDay),
        };
}
