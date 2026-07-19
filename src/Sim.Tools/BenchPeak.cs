using System.Diagnostics;
using System.Globalization;
using Sim.Core.Comms;
using Sim.Core.Numerics;
using Sim.Core.Orbits;
using Sim.Core.Rng;
using Sim.Core.Time;

namespace Sim.Tools;

/// <summary>
/// Peak-tick correlation stress harness (roadmap §2.7 risk, restated). The existing
/// <see cref="Benchmarks"/> measures <b>average</b> throughput — the soak question, which was never
/// the risk. This measures the <b>worst single tick</b> under <b>correlated</b> decisions: 200 ships
/// all deciding on one tick because a single routed report reached a fleet at once. Same average
/// cadence, catastrophically different peak — and a headless average hides it completely.
///
/// <para><b>Honest scope:</b> real correlation is produced by systems not yet built (signal routing,
/// precursor cascades — Phase 3). This harness <i>synthesizes</i> correlation by assumption and
/// measures the <b>engine's ceiling</b> — how many simultaneous decisions before one tick exceeds a
/// frame budget — not whether the game <i>generates</i> that load. A floor of confidence, not a
/// verdict. It applies no horizon/warm-start optimization on the peak path: we want the raw ceiling.</para>
/// </summary>
public static class BenchPeak
{
    private const long SimYearSeconds = 31_557_600L;
    private const long DaySeconds = 86_400L;

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

    public sealed record Config(int CorrelatedShips, int IndependentShips, long CadenceSeconds, long SpanSeconds, ulong Seed);

    public sealed record PeakResult(
        int MaxDecisionsInOneTick,
        double MaxTickMs,
        double PeakTickMsClean,
        double P99TickMs,
        double MeanTickMs,
        long TotalDecisions,
        int TotalTicks,
        double ThroughputSimYearsPerMin);

    public static int Run(string[] args)
    {
        int ships = (int)GetLong(args, "--ships", 200);
        int n = (int)GetLong(args, "--n", 500);
        double rho = GetDouble(args, "--rho", 1.0);
        long cadence = GetLong(args, "--cadence-days", 10) * DaySeconds;
        ulong seed = (ulong)GetLong(args, "--seed", 42);
        double budgetMs = GetDouble(args, "--budget-ms", 16.7);
        string? sweep = GetString(args, "--sweep");

        KeplerianElements el = SampleBody();

        Console.Error.WriteLine("simtool bench-peak — peak-tick correlation stress. Warming up...");
        WarmUp(el);

        // The engine ceiling is scale-free: derive per-decision cost once, report K against budgets.
        double perDecisionMs = PerDecisionMs(el);
        Console.WriteLine($"per-decision cost (pessimistic Kepler + reception solve, no warm-start): {perDecisionMs * 1000:F1} us");
        Console.WriteLine();

        switch (sweep)
        {
            case "n":
                SweepN(el, ships, budgetMs);
                break;
            case "rho":
                SweepRho(el, ships, cadence, seed);
                break;
            case "cadence":
                SweepCadence(el, ships, seed);
                break;
            case null:
                SingleRun(el, ships, n, rho, cadence, seed, budgetMs);
                break;
            default:
                Console.Error.WriteLine($"unknown sweep '{sweep}' (use n|rho|cadence)");
                return 2;
        }

        // The whole point, in one sentence — with the conservatism the spec asked for.
        int k16 = Ceiling(perDecisionMs, 16.7);
        int k8 = Ceiling(perDecisionMs, 8.3);
        Console.WriteLine();
        Console.WriteLine($"VERDICT (WARMED-BATCH FLOOR of peak-tick cost): ~{k16} decisions fit under 16.7 ms (60 fps),");
        Console.WriteLine($"         ~{k8} under 8.3 ms (120 fps). Pessimistic solve, no horizon collapse.");
        Console.WriteLine("But this batch is cache-hot / zero-alloc / steady-state. A LIVE correlated tick is cold, may");
        Console.WriteLine("allocate, and lands mid-GC — the real spike is HIGHER, so this K is a floor, not a ceiling.");
        Console.WriteLine($"=> DESIGN against ~{k16 / 2} simultaneous decisions (2x margin); treat ~{k16} as 'definitely broken past here'.");
        Console.WriteLine("Engine ceiling synthesized by assumption — not a claim the game generates this load.");
        return 0;
    }

    // ---- sweeps ----

    private static void SweepN(KeplerianElements el, int ships, double budgetMs)
    {
        Console.WriteLine("=== N-sweep at rho=1 (all fire on one tick) — the peak-tick curve ===");
        Console.WriteLine($"{"N",8}  {"max-tick ms",12}  {"vs 16.7ms",10}  {"vs 8.3ms",10}");
        foreach (int n in new[] { 50, 100, 200, 500, 1000 })
        {
            double ms = MeasureBatchMs(n, el);
            Console.WriteLine($"{n,8}  {ms,12:F2}  {Verdict(ms, 16.7),10}  {Verdict(ms, 8.3),10}");
        }
    }

    private static void SweepRho(KeplerianElements el, int ships, long cadence, ulong seed)
    {
        Console.WriteLine($"=== rho-sweep at fixed cadence ({cadence / DaySeconds}d, {ships} ships) — average flat, peak explodes ===");
        Console.WriteLine($"{"rho",6}  {"max/tick",9}  {"peak-tick ms",13}  {"throughput yr/min",18}");
        foreach (double rho in new[] { 0.0, 0.25, 0.5, 0.75, 1.0 })
        {
            int correlated = (int)Math.Round(rho * ships);
            var cfg = new Config(correlated, ships - correlated, cadence, 20 * cadence, seed);
            PeakResult r = Measure(cfg, el);
            Console.WriteLine($"{rho,6:F2}  {r.MaxDecisionsInOneTick,9}  {r.PeakTickMsClean,13:F2}  {r.ThroughputSimYearsPerMin,18:F0}");
        }

        Console.WriteLine("Throughput barely moves; peak tick climbs with correlation. Correlation, not frequency, is the risk.");
    }

    private static void SweepCadence(KeplerianElements el, int ships, ulong seed)
    {
        Console.WriteLine($"=== cadence-sweep at rho=0 (decorrelated, {ships} ships) — the average axis is NOT the risk ===");
        Console.WriteLine($"{"cadence",8}  {"max/tick",9}  {"peak-tick ms",13}  {"throughput yr/min",18}");
        foreach (int days in new[] { 40, 10, 4, 1 })
        {
            long cadence = days * DaySeconds;
            var cfg = new Config(0, ships, cadence, 20 * cadence, seed);
            PeakResult r = Measure(cfg, el);
            Console.WriteLine($"{days + "d",8}  {r.MaxDecisionsInOneTick,9}  {r.PeakTickMsClean,13:F2}  {r.ThroughputSimYearsPerMin,18:F0}");
        }

        Console.WriteLine("Decorrelated load degrades smoothly and stays cheap regardless of cadence.");
    }

    private static void SingleRun(KeplerianElements el, int ships, int n, double rho, long cadence, ulong seed, double budgetMs)
    {
        int correlated = n > 0 ? n : (int)Math.Round(rho * ships);
        var cfg = new Config(correlated, ships, cadence, 20 * cadence, seed);
        PeakResult r = Measure(cfg, el);
        Console.WriteLine($"=== single run (correlated={correlated}, independent={ships}, cadence={cadence / DaySeconds}d, seed={seed}) ===");
        Console.WriteLine($"  max decisions in one tick : {r.MaxDecisionsInOneTick}");
        Console.WriteLine($"  max tick wall-time        : {r.MaxTickMs:F2} ms   {Verdict(r.MaxTickMs, budgetMs)} vs {budgetMs} ms budget");
        Console.WriteLine($"  p99 tick wall-time        : {r.P99TickMs:F2} ms");
        Console.WriteLine($"  mean tick wall-time       : {r.MeanTickMs:F3} ms");
        Console.WriteLine($"  total decisions / ticks   : {r.TotalDecisions} / {r.TotalTicks}");
    }

    // ---- measurement core ----

    /// <summary>
    /// Deterministic per-tick decision-count sequence (no timing, no heavy work) — a pure function of
    /// (config, seed). This is the sim <i>work pattern</i>; only wall-time varies by machine.
    /// </summary>
    public static int[] DecisionCountsPerTick(Config cfg)
    {
        var run = Drive(cfg, SampleBody(), doWork: false, measure: false);
        return run.Counts;
    }

    /// <summary>
    /// Deterministic sequence of tick <b>times</b> for (config, seed). Unlike the count sequence
    /// (which is seed-invariant when collisions are near-zero), tick times shift with the seeded
    /// independent stagger — so this is the non-vacuous witness that the RNG genuinely drives the
    /// schedule and reproduces it (§2.3 r4).
    /// </summary>
    public static long[] DecisionTickTimes(Config cfg)
    {
        var run = Drive(cfg, SampleBody(), doWork: false, measure: false);
        return run.TickTimes;
    }

    private static PeakResult Measure(Config cfg, KeplerianElements el)
    {
        DriveResult run = Drive(cfg, el, doWork: true, measure: true);

        double[] times = run.TickMs;
        Array.Sort(times);
        double max = times.Length > 0 ? times[^1] : 0;
        double p99 = Percentile(times, 0.99);
        double mean = times.Length > 0 ? Sum(times) / times.Length : 0;

        long totalDecisions = 0;
        int maxDecisions = 0;
        foreach (int c in run.Counts)
        {
            totalDecisions += c;
            if (c > maxDecisions)
            {
                maxDecisions = c;
            }
        }

        double simYears = cfg.SpanSeconds / (double)SimYearSeconds;
        double totalMinutes = Sum(run.TickMs) / 60_000.0;
        double throughput = totalMinutes > 0 ? simYears / totalMinutes : 0;

        // Clean peak-tick cost: the max-decision count timed as a warmed batch, immune to the
        // sub-ms GC/OS jitter that swamps a single live tick sample.
        double peakClean = maxDecisions > 0 ? MeasureBatchMs(maxDecisions, el) : 0;

        return new PeakResult(maxDecisions, max, peakClean, p99, mean, totalDecisions, run.Counts.Length, throughput);
    }

    private sealed record DriveResult(int[] Counts, long[] TickTimes, double[] TickMs);

    private sealed class Sink
    {
        public long Value;
    }

    // Runs the tick loop, grouping events that share a time into one tick. When measuring, each tick
    // is timed with a Stopwatch OUTSIDE the sim state — it never feeds back into a decision (§2.3 r1).
    private static DriveResult Drive(Config cfg, KeplerianElements el, bool doWork, bool measure)
    {
        var sim = new Simulation();
        RngStream rng = new RngStreams(cfg.Seed).Fork("bench-peak");
        var sink = new Sink();
        long nextId = 0;

        // Correlated ships all start at the first trigger and re-arm at +cadence, so they stay aligned
        // — one shared trigger fans out to all of them on the same tick.
        for (int i = 0; i < cfg.CorrelatedShips; i++)
        {
            sim.Schedule(new Decision(nextId++, cfg.CadenceSeconds, correlated: true, cfg, el, sink, doWork));
        }

        // Independent ships start staggered (seeded) and re-arm at +cadence, so they stay spread out.
        for (int i = 0; i < cfg.IndependentShips; i++)
        {
            long first = 1 + (long)rng.NextBounded((ulong)cfg.CadenceSeconds);
            sim.Schedule(new Decision(nextId++, first, correlated: false, cfg, el, sink, doWork));
        }

        var counts = new List<int>();
        var times = new List<long>();
        var tickMs = measure ? new List<double>() : null;
        var sw = new Stopwatch();

        while (sim.NextEventTime is long t)
        {
            if (measure)
            {
                sw.Restart();
            }

            int decisions = 0;
            while (sim.NextEventTime == t)
            {
                sim.Step();
                decisions++;
            }

            if (measure)
            {
                sw.Stop();
                tickMs!.Add(sw.Elapsed.TotalMilliseconds);
            }

            counts.Add(decisions);
            times.Add(t);
        }

        Consume(sink.Value);
        return new DriveResult(counts.ToArray(), times.ToArray(), tickMs?.ToArray() ?? Array.Empty<double>());
    }

    private sealed class Decision : ISimEvent
    {
        private readonly bool _correlated;
        private readonly Config _cfg;
        private readonly KeplerianElements _el;
        private readonly Sink _sink;
        private readonly bool _doWork;

        public Decision(long id, long time, bool correlated, Config cfg, KeplerianElements el, Sink sink, bool doWork)
        {
            Ordinal = id;
            TimeSeconds = time;
            _correlated = correlated;
            _cfg = cfg;
            _el = el;
            _sink = sink;
            _doWork = doWork;
        }

        public long TimeSeconds { get; }
        public long Ordinal { get; }

        public void Apply(ISimContext ctx)
        {
            long now = ctx.NowSeconds;

            if (_doWork)
            {
                // The pessimistic path: reception root-find whose observerAt is a full Kepler solve
                // (no on-rails lookup collapse, no warm-start). This is the ceiling, deliberately.
                (long X, long Y, long Z) ObserverAt(long t)
                {
                    Vec3 p = Kepler.PositionAt(_el, t);
                    return (EphemerisTable.GmToMm(p.X), EphemerisTable.GmToMm(p.Y), EphemerisTable.GmToMm(p.Z));
                }

                _sink.Value += Reception.Solve(ObserverAt, 0, 0, 0, now);
            }

            long next = now + _cfg.CadenceSeconds;
            if (next <= _cfg.SpanSeconds)
            {
                ctx.Schedule(new Decision(Ordinal, next, _correlated, _cfg, _el, _sink, _doWork));
            }
        }
    }

    // ---- direct peak-tick / ceiling helpers ----

    // One tick of N simultaneous decisions, timed directly (warmed). Equivalent to a single
    // fully-correlated trigger tick.
    private static double MeasureBatchMs(int n, KeplerianElements el)
    {
        (long X, long Y, long Z) ObserverAt(long t)
        {
            Vec3 p = Kepler.PositionAt(el, t);
            return (EphemerisTable.GmToMm(p.X), EphemerisTable.GmToMm(p.Y), EphemerisTable.GmToMm(p.Z));
        }

        long sink = 0;
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < n; i++)
        {
            sink += Reception.Solve(ObserverAt, 0, 0, 0, i * 3600L);
        }

        sw.Stop();
        Consume(sink);
        return sw.Elapsed.TotalMilliseconds;
    }

    private static double PerDecisionMs(KeplerianElements el)
    {
        const int n = 4000;
        return MeasureBatchMs(n, el) / n;
    }

    private static int Ceiling(double perDecisionMs, double budgetMs) => (int)(budgetMs / perDecisionMs);

    private static void WarmUp(KeplerianElements el) => MeasureBatchMs(2000, el);

    // ---- small helpers ----

    private static string Verdict(double ms, double budget) => ms < budget ? "under" : "OVER";

    private static double Sum(double[] values)
    {
        double total = 0;
        foreach (double v in values)
        {
            total += v;
        }

        return total;
    }

    private static double Percentile(double[] sorted, double p)
    {
        if (sorted.Length == 0)
        {
            return 0;
        }

        int index = (int)Math.Ceiling(p * sorted.Length) - 1;
        return sorted[Math.Clamp(index, 0, sorted.Length - 1)];
    }

    private static void Consume(long value)
    {
        if (value == long.MinValue)
        {
            Console.Error.WriteLine(value);
        }
    }

    private static string? GetString(string[] args, string flag)
    {
        int i = Array.IndexOf(args, flag);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    private static long GetLong(string[] args, string flag, long fallback)
    {
        string? raw = GetString(args, flag);
        return raw is null ? fallback : long.Parse(raw, CultureInfo.InvariantCulture);
    }

    private static double GetDouble(string[] args, string flag, double fallback)
    {
        string? raw = GetString(args, flag);
        return raw is null ? fallback : double.Parse(raw, CultureInfo.InvariantCulture);
    }
}
