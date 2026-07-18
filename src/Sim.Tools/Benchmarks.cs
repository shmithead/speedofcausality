using System.Diagnostics;
using Sim.Core.Numerics;
using Sim.Core.Orbits;

namespace Sim.Tools;

/// <summary>
/// The Brewer compute benchmark (roadmap §2.7, §8 risk 1). Brewer's STL died on throughput, not
/// graphics — "high-cost trigonometric and radical functions." This measures the throughput of the
/// pieces that replaced his hardware path: CORDIC trig, the Kepler solve (the cost of a decision
/// point), and the ephemeris lookup (the cost of a coasting entity on rails).
///
/// <para>From the measured rates it brackets sim-years/minute at soak-realistic load (~60 bodies,
/// ~200 ships, 1-minute ticks). The brackets are honest: <b>Brewer mode</b> (every entity solved
/// every tick — the naive path that melted his CPU) and <b>rails mode</b> (every entity a table
/// lookup — what validity horizons drive most of the sim toward). The true §2.7 number lands
/// between them and needs the horizon system built to measure directly; this establishes the
/// endpoints and confirms the primitives are not themselves the wall.</para>
/// </summary>
internal static class Benchmarks
{
    private const long TicksPerSimYear = 525_600L; // 1 sim-minute per tick
    private const int Bodies = 60;
    private const int Ships = 200;
    private const int Entities = Bodies + Ships;

    private static KeplerianElements SampleBody() => new()
    {
        SemiMajorAxisGm = Fixed64.FromDouble(227.9),
        Eccentricity = Fixed64.FromDouble(0.0934),
        InclinationRad = Fixed64.FromDouble(0.0323),
        AscendingNodeRad = Fixed64.FromDouble(0.865),
        ArgPeriapsisRad = Fixed64.FromDouble(5.000),
        MeanAnomalyEpochRad = Fixed64.FromDouble(0.338),
        PeriodSeconds = 59_355_000L,
    };

    public static int Run()
    {
        Console.Error.WriteLine("simtool bench — Brewer compute gate (§2.7). Warming up...");

        var el = SampleBody();
        var table = EphemerisTable.Build(el, samples: 8192);

        double keplerRate = MeasureKepler(el);
        double ephemRate = MeasureEphemeris(table);
        double sincosRate = MeasureSinCos();
        double atan2Rate = MeasureAtan2();

        Console.WriteLine("=== primitive throughput (this machine) ===");
        Report("Kepler.PositionAt  (decision-point path)", keplerRate);
        Report("EphemerisTable     (on-rails path)      ", ephemRate);
        Report("Trig.SinCos                              ", sincosRate);
        Report("Trig.Atan2         (reception iter proxy)", atan2Rate);

        Console.WriteLine();
        Console.WriteLine($"=== derived sim-years/minute  ({Bodies} bodies + {Ships} ships, 1-min ticks) ===");
        double brewer = SimYearsPerMinute(keplerRate);
        double rails = SimYearsPerMinute(ephemRate);
        Console.WriteLine($"Brewer mode (every entity solved every tick):  {brewer,10:F2} sim-years/min   -> 50-yr soak {SoakMinutes(brewer)}");
        Console.WriteLine($"Rails mode  (every entity a table lookup):     {rails,10:F2} sim-years/min   -> 50-yr soak {SoakMinutes(rails)}");
        Console.WriteLine();
        Console.WriteLine("Both rows are FLOORS, not bounds. The real rate is BELOW rails: real ticks have");
        Console.WriteLine("decision points (~30x a lookup), and their fraction is the horizon system's output.");
        Console.WriteLine("Gate: does the horizon calculation cost less than the solves it saves? Not yet");
        Console.WriteLine("measurable here -- a proxy has no horizons. Horizons are the rest of the benchmark.");
        return 0;
    }

    // sim-years/min = 60 * rate / (entities * ticksPerYear), assuming one op per entity per tick.
    private static double SimYearsPerMinute(double opsPerSec)
        => 60.0 * opsPerSec / (Entities * TicksPerSimYear);

    private static string SoakMinutes(double simYearsPerMin)
    {
        double minutes = 50.0 / simYearsPerMin;
        return minutes < 1.0 ? $"{minutes * 60.0:F1} s" : $"{minutes:F1} min";
    }

    private static void Report(string name, double rate)
        => Console.WriteLine($"  {name}  {rate,12:N0} ops/s   {1e9 / rate,8:F1} ns/op");

    private static double MeasureKepler(KeplerianElements el)
    {
        const int warm = 50_000;
        const int n = 2_000_000;
        long sink = 0;
        for (int i = 0; i < warm; i++)
        {
            sink += Kepler.PositionAt(el, i * 37L).X.Raw;
        }

        var sw = Stopwatch.StartNew();
        for (int i = 0; i < n; i++)
        {
            sink += Kepler.PositionAt(el, i * 37L).X.Raw;
        }

        sw.Stop();
        Consume(sink);
        return n / sw.Elapsed.TotalSeconds;
    }

    private static double MeasureEphemeris(EphemerisTable table)
    {
        const int warm = 100_000;
        const int n = 20_000_000;
        long sink = 0;
        for (int i = 0; i < warm; i++)
        {
            sink += table.PositionMmAt(i * 37L).X;
        }

        var sw = Stopwatch.StartNew();
        for (int i = 0; i < n; i++)
        {
            sink += table.PositionMmAt(i * 37L).X;
        }

        sw.Stop();
        Consume(sink);
        return n / sw.Elapsed.TotalSeconds;
    }

    private static double MeasureSinCos()
    {
        const int warm = 100_000;
        const int n = 10_000_000;
        long sink = 0;
        Fixed64 step = Fixed64.FromDouble(0.001);
        Fixed64 a = Fixed64.Zero;
        for (int i = 0; i < warm; i++)
        {
            a += step;
            sink += Trig.SinCos(a).Sin.Raw;
        }

        a = Fixed64.Zero;
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < n; i++)
        {
            a += step;
            sink += Trig.SinCos(a).Sin.Raw;
        }

        sw.Stop();
        Consume(sink);
        return n / sw.Elapsed.TotalSeconds;
    }

    private static double MeasureAtan2()
    {
        const int warm = 100_000;
        const int n = 10_000_000;
        long sink = 0;
        Fixed64 step = Fixed64.FromDouble(0.0007);
        Fixed64 y = Fixed64.FromInt(1);
        Fixed64 x = Fixed64.FromInt(1);
        for (int i = 0; i < warm; i++)
        {
            y += step;
            sink += Trig.Atan2(y, x).Raw;
        }

        y = Fixed64.FromInt(1);
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < n; i++)
        {
            y += step;
            sink += Trig.Atan2(y, x).Raw;
        }

        sw.Stop();
        Consume(sink);
        return n / sw.Elapsed.TotalSeconds;
    }

    // Defeat dead-code elimination.
    private static void Consume(long value)
    {
        if (value == long.MinValue)
        {
            Console.Error.WriteLine(value);
        }
    }
}
