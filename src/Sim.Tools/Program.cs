using System.Globalization;
using Sim.Core.Diagnostics;

namespace Sim.Tools;

/// <summary>
/// simtool v0 — the headless CLI you live in during Phases 2–5 (roadmap §4).
/// v0 surface: <c>run</c> (produce a deterministic trace), <c>dump</c> (print one),
/// <c>diff</c> (compare two and locate the first divergence). The trace content is the
/// determinism-smoke kernel for now; it gets swapped for the real event-log replay as the
/// sim grows, but the command surface and the first-divergence locator stay.
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length == 0)
        {
            return Usage();
        }

        try
        {
            return args[0] switch
            {
                "run" => Run(args),
                "run-phase1" => RunPhase1(args),
                "knows" => Knows(args),
                "dump" => Dump(args),
                "diff" => Diff(args),
                "bench" => Benchmarks.Run(),
                "bench-peak" => BenchPeak.Run(args),
                "-h" or "--help" or "help" => Usage(),
                _ => Fail($"unknown command '{args[0]}'"),
            };
        }
        catch (Exception ex)
        {
            return Fail(ex.Message);
        }
    }

    // simtool run --seed 42 --steps 256 [--out file]
    private static int Run(string[] args)
    {
        ulong seed = GetULong(args, "--seed", 42);
        int steps = (int)GetULong(args, "--steps", 256);
        string? outPath = GetString(args, "--out");

        string golden = DeterminismSmoke.ToGolden(seed, steps);

        if (outPath is null)
        {
            Console.Out.Write(golden);
        }
        else
        {
            // LF-only, no BOM: the file must be byte-identical across OSes.
            File.WriteAllText(outPath, golden, new System.Text.UTF8Encoding(false));
            Console.Error.WriteLine($"wrote {steps} steps (seed {seed}) -> {outPath}");
        }

        return 0;
    }

    // simtool run-phase1 --seed 42 --until 200d [--out file]
    // Replays the canonical Phase 1 sim and emits its event-log golden for cross-OS diffing (§4).
    private static int RunPhase1(string[] args)
    {
        ulong seed = GetULong(args, "--seed", 42);
        long until = GetTime(args, "--until", 200 * 86_400L);
        string? outPath = GetString(args, "--out");

        var world = Sim.Core.Diagnostics.Phase1Scenario.Build(seed);
        world.Sim.RunUntil(until);
        string golden = Sim.Core.Diagnostics.Phase1Scenario.EventLogGolden(world);

        if (outPath is null)
        {
            Console.Out.Write(golden);
        }
        else
        {
            File.WriteAllText(outPath, golden, new System.Text.UTF8Encoding(false));
            Console.Error.WriteLine($"wrote {world.Log.Count} events (seed {seed}, until {until}s) -> {outPath}");
        }

        return 0;
    }

    // simtool knows --entity 100 --at 120d [--seed 42]
    // Prints what one entity knows at time T — the stale fold the map would render (§2.2, §4).
    private static int Knows(string[] args)
    {
        ulong seed = GetULong(args, "--seed", 42);
        long entity = (long)GetULong(args, "--entity", (ulong)Sim.Core.Diagnostics.Phase1Scenario.HqId);
        long at = GetTime(args, "--at", 120 * 86_400L);

        var world = Sim.Core.Diagnostics.Phase1Scenario.Build(seed);
        world.Sim.RunUntil(at);
        Console.Out.Write(Sim.Core.Diagnostics.Phase1Scenario.KnowledgeGolden(world, entity, at));
        return 0;
    }

    // simtool dump <file>
    private static int Dump(string[] args)
    {
        string path = RequirePositional(args, "dump <file>");
        Console.Out.Write(File.ReadAllText(path));
        return 0;
    }

    // simtool diff <a> <b> — reports the first differing line (roadmap §4 first-divergence locator).
    private static int Diff(string[] args)
    {
        if (args.Length < 3)
        {
            return Fail("usage: simtool diff <a> <b>");
        }

        string[] a = File.ReadAllLines(args[1]);
        string[] b = File.ReadAllLines(args[2]);

        int max = Math.Max(a.Length, b.Length);
        for (int i = 0; i < max; i++)
        {
            string? la = i < a.Length ? a[i] : null;
            string? lb = i < b.Length ? b[i] : null;
            if (la != lb)
            {
                Console.Error.WriteLine($"DIVERGENCE at line {i + 1}:");
                Console.Error.WriteLine($"  a: {la ?? "<end of file>"}");
                Console.Error.WriteLine($"  b: {lb ?? "<end of file>"}");
                return 1;
            }
        }

        Console.Error.WriteLine($"identical ({a.Length} lines)");
        return 0;
    }

    // ---- arg helpers ----

    private static string? GetString(string[] args, string flag)
    {
        int i = Array.IndexOf(args, flag);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    private static ulong GetULong(string[] args, string flag, ulong fallback)
    {
        string? raw = GetString(args, flag);
        return raw is null ? fallback : ulong.Parse(raw, CultureInfo.InvariantCulture);
    }

    // A sim-time in seconds, with an optional 'd' (days) or 'h' (hours) suffix: "200d", "36h", "3600".
    private static long GetTime(string[] args, string flag, long fallback)
    {
        string? raw = GetString(args, flag);
        if (raw is null || raw.Length == 0)
        {
            return fallback;
        }

        char suffix = raw[^1];
        long multiplier = suffix switch { 'd' => 86_400L, 'h' => 3_600L, _ => 1L };
        string number = multiplier == 1L ? raw : raw[..^1];
        return long.Parse(number, CultureInfo.InvariantCulture) * multiplier;
    }

    private static string RequirePositional(string[] args, string usage)
    {
        if (args.Length < 2)
        {
            throw new ArgumentException($"usage: simtool {usage}");
        }

        return args[1];
    }

    private static int Usage()
    {
        Console.Error.WriteLine("""
            simtool v0 — Speed of Causality headless CLI

              run        --seed <n> --steps <n> [--out <file>]     determinism-smoke trace
              run-phase1 --seed <n> --until <t> [--out <file>]    Phase 1 event-log golden (§4)
              knows      --entity <id> --at <t> [--seed <n>]      what an entity knows at T (§2.2)
              dump       <file>                                   print a trace
              diff       <a> <b>                                  first divergence between two traces
              bench                                               Brewer compute gate (§2.7)
              bench-peak [--sweep n|rho|cadence] ...              peak-tick correlation stress

            <t> is seconds, or with a 'd'/'h' suffix (e.g. 200d, 36h).
            """);
        return 0;
    }

    private static int Fail(string message)
    {
        Console.Error.WriteLine($"simtool: {message}");
        return 2;
    }
}
