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
                "dump" => Dump(args),
                "diff" => Diff(args),
                "bench" => Benchmarks.Run(),
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

              run    --seed <n> --steps <n> [--out <file>]   deterministic trace
              dump   <file>                                  print a trace
              diff   <a> <b>                                 first divergence between two traces
              bench                                          Brewer compute gate (§2.7)
            """);
        return 0;
    }

    private static int Fail(string message)
    {
        Console.Error.WriteLine($"simtool: {message}");
        return 2;
    }
}
