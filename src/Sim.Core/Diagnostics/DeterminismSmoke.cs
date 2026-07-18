using System.Text;
using Sim.Core.Numerics;
using Sim.Core.Rng;

namespace Sim.Core.Diagnostics;

/// <summary>
/// The determinism canary (roadmap §2.6): a tiny, pure, seeded computation that must produce
/// byte-identical output on every OS and every commit. It exercises the portable-but-fragile
/// path — seeded integer RNG feeding <see cref="Fixed64"/> add/mul/sqrt — so a one-bit
/// regression in the fixed-point library trips this before anything is built on top of it.
///
/// <para>This is deliberately not the real sim. It is the smallest thing whose divergence
/// would prove the numeric foundation is unsound. Keep it stable; regenerate the larger
/// golden corpus at phase boundaries, but this one is the load-bearing constant.</para>
/// </summary>
public static class DeterminismSmoke
{
    /// <summary>Advances a xorshift64 state. Pure integer math — deterministic by construction.</summary>
    private static ulong NextState(ulong x)
    {
        x ^= x << 13;
        x ^= x >> 7;
        x ^= x << 17;
        return x;
    }

    /// <summary>
    /// Runs <paramref name="steps"/> deterministic steps from <paramref name="seed"/>,
    /// returning the running-accumulator raw value after each step.
    /// </summary>
    public static long[] Run(ulong seed, int steps)
    {
        if (steps < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(steps));
        }

        // xorshift64 dies on a zero state; nudge it to a fixed non-zero constant.
        ulong state = seed == 0 ? 0x9E3779B97F4A7C15UL : seed;

        var outputs = new long[steps];
        Fixed64 acc = Fixed64.Zero;

        // A seeded RNG stream folded in too, so a cross-OS divergence in the PRNG trips the canary.
        var rng = new RngStreams(seed).Fork("determinism-smoke");

        for (int i = 0; i < steps; i++)
        {
            state = NextState(state);

            // Map to a whole number in [1, 1000], then walk it through the deterministic ops:
            // sqrt, then CORDIC sin/cos (root exceeds one period, so range reduction runs too),
            // then atan2, plus a bounded RNG draw. If any of those diverge cross-OS, this diverges.
            int n = (int)(state % 1000UL) + 1;
            Fixed64 x = Fixed64.FromInt(n);
            Fixed64 root = Fixed64.Sqrt(x);
            (Fixed64 sin, Fixed64 cos) = Trig.SinCos(root);
            Fixed64 angle = Trig.Atan2(sin, x);
            Fixed64 roll = Fixed64.FromInt((int)rng.NextBounded(360));
            acc += (root * Fixed64.Half) + sin + cos + angle + roll;

            outputs[i] = acc.Raw;
        }

        return outputs;
    }

    /// <summary>
    /// Canonical text rendering of a run: one <c>index=raw</c> pair per line, joined by LF.
    /// Explicit LF (never the platform newline) keeps it byte-identical cross-OS.
    /// </summary>
    public static string ToGolden(ulong seed, int steps)
    {
        long[] values = Run(seed, steps);
        var sb = new StringBuilder();
        for (int i = 0; i < values.Length; i++)
        {
            sb.Append(i).Append('=').Append(values[i]).Append('\n');
        }

        return sb.ToString();
    }
}
