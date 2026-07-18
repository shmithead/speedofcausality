using System.Diagnostics;
using Sim.Core.Comms;
using Sim.Core.Horizons;
using Sim.Core.Numerics;
using Sim.Core.Orbits;
using Sim.Core.Time;

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
        Console.WriteLine("Both rows are FLOORS, not bounds. The real rate is BELOW rails, but the horizon");
        Console.WriteLine("system decides HOW far below by how rarely entities actually decide. Measured now:");
        Console.WriteLine();

        // --- The real §2.7 numbers, off the actual scheduler + horizon + reception machinery ---
        double recvClosedRate = MeasureReceptionClosedForm();
        double recvSolveRate = MeasureReceptionSolve(el);

        Console.WriteLine("=== reception cost (light-lag core, §2.2) ===");
        Report("Reception.ClosedForm (on-rails endpoint)", recvClosedRate);
        Report("Reception.Solve      (moving endpoint)  ", recvSolveRate);

        const double frameBudgetSec = 1.0 / 60.0; // one 60 fps frame
        Console.WriteLine($"  ship-observers per in-scope event @ 60fps: {recvClosedRate * frameBudgetSec,10:N0} on-rails  |  {recvSolveRate * frameBudgetSec,8:N0} deciding");
        Console.WriteLine();

        // Run a real horizon-driven sim: 200 ships, each re-deciding on a stated cadence, each
        // decision doing an actual Kepler solve + reception root-find. Measure it end to end.
        const long cadenceSeconds = 40L * 24 * 3600;   // a ship re-decides ~every 40 days (assumption)
        const int simYears = 20;
        long span = simYears * SimYearSeconds;
        var sw = Stopwatch.StartNew();
        long decisions = RunDecidingHorizonSim(el, Ships, cadenceSeconds, span);
        sw.Stop();

        double horizonSimYearsPerMin = simYears / sw.Elapsed.TotalMinutes;
        double decisionsPerYear = decisions / (double)simYears;
        double decisionTickFraction = decisionsPerYear / TicksPerSimYear;

        Console.WriteLine($"=== horizon-driven sim ({Ships} ships, ~40-day decide cadence, {simYears} sim-years) ===");
        Console.WriteLine($"  fraction of ticks touching a decision point:  {decisionTickFraction,10:P3}");
        Console.WriteLine($"  measured rate (scheduler+horizon+solve):      {horizonSimYearsPerMin,10:F1} sim-years/min   -> 50-yr soak {SoakMinutes(horizonSimYearsPerMin)}");
        Console.WriteLine();
        Console.WriteLine("Gate answer (this workload): the horizon calc (next = now + cadence) is trivial and");
        Console.WriteLine("decisions are rare, so the sim runs FAR above rails. The load-bearing assumption is");
        Console.WriteLine("the decide cadence + that the real 'earliest thing that could change her mind' query");
        Console.WriteLine("stays cheap (a scheduler peek, not a solve) once comms/rumor drive it. That is Phase 1+.");
        return 0;
    }

    // ---- reception & horizon measurements (the real §2.7 machinery) ----

    private static double MeasureReceptionClosedForm()
    {
        const int warm = 100_000;
        const int n = 5_000_000;
        long sink = 0;
        for (int i = 0; i < warm; i++)
        {
            sink += Reception.ClosedForm(149_597_870_700_000L + i, 20_000_000_000L, 0, 0, 0, 0, i);
        }

        var sw = Stopwatch.StartNew();
        for (int i = 0; i < n; i++)
        {
            sink += Reception.ClosedForm(149_597_870_700_000L + i, 20_000_000_000L, 0, 0, 0, 0, i);
        }

        sw.Stop();
        Consume(sink);
        return n / sw.Elapsed.TotalSeconds;
    }

    private static double MeasureReceptionSolve(KeplerianElements el)
    {
        (long X, long Y, long Z) ObserverAt(long t)
        {
            Vec3 p = Kepler.PositionAt(el, t);
            return (EphemerisTable.GmToMm(p.X), EphemerisTable.GmToMm(p.Y), EphemerisTable.GmToMm(p.Z));
        }

        const int warm = 20_000;
        const int n = 500_000;
        long sink = 0;
        for (int i = 0; i < warm; i++)
        {
            sink += Reception.Solve(ObserverAt, 0, 0, 0, i * 3600L);
        }

        var sw = Stopwatch.StartNew();
        for (int i = 0; i < n; i++)
        {
            sink += Reception.Solve(ObserverAt, 0, 0, 0, i * 3600L);
        }

        sw.Stop();
        Consume(sink);
        return n / sw.Elapsed.TotalSeconds;
    }

    // A ship that, at each horizon expiry, does a real decision (Kepler position + reception
    // root-find against the Sun) and re-arms one cadence later.
    private sealed class DecidingShip : HorizonEntity
    {
        private readonly long _cadence;
        private readonly KeplerianElements _el;
        public long Sink;

        public DecidingShip(long id, long cadence, long firstHorizon, KeplerianElements el)
            : base(id, firstHorizon)
        {
            _cadence = cadence;
            _el = el;
        }

        protected override long Recompute(ISimContext ctx)
        {
            long now = ctx.NowSeconds;
            (long X, long Y, long Z) ObserverAt(long t)
            {
                Vec3 p = Kepler.PositionAt(_el, t);
                return (EphemerisTable.GmToMm(p.X), EphemerisTable.GmToMm(p.Y), EphemerisTable.GmToMm(p.Z));
            }

            Sink += Reception.Solve(ObserverAt, 0, 0, 0, now);
            return now + _cadence;
        }
    }

    private static long RunDecidingHorizonSim(KeplerianElements el, int ships, long cadence, long span)
    {
        var sim = new Simulation();
        var mgr = new HorizonManager(sim);
        var fleet = new DecidingShip[ships];
        for (int i = 0; i < ships; i++)
        {
            long first = 1 + (((long)i * 7919) % cadence); // stagger so they don't all decide at once
            fleet[i] = new DecidingShip(i, cadence, first, el);
            mgr.Register(fleet[i]);
        }

        sim.RunUntil(span);

        long total = 0;
        foreach (DecidingShip s in fleet)
        {
            total += s.RecomputeCount;
            Consume(s.Sink);
        }

        return total;
    }

    private const long SimYearSeconds = 31_557_600L;

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
