namespace Sim.Core.Rng;

/// <summary>
/// A deterministic pseudo-random stream (xoshiro256**), seeded from a 64-bit value via splitmix64.
/// Integer-only by design — no <c>NextDouble</c>, because floats are banned from simulation state
/// (§2.3 r2). Every draw is a pure function of the stream's state, so a replay reproduces it
/// exactly.
///
/// <para>You do not construct these directly for the sim; fork them by name from
/// <see cref="RngStreams"/> so each subsystem gets an independent, reproducible stream (§2.3 r4).</para>
/// </summary>
public sealed class RngStream
{
    private ulong _s0;
    private ulong _s1;
    private ulong _s2;
    private ulong _s3;

    /// <summary>Seeds the 256-bit state deterministically from a single 64-bit sub-seed.</summary>
    public RngStream(ulong seed)
    {
        ulong sm = seed;
        _s0 = SplitMix64(ref sm);
        _s1 = SplitMix64(ref sm);
        _s2 = SplitMix64(ref sm);
        _s3 = SplitMix64(ref sm);
    }

    /// <summary>Next 64-bit value.</summary>
    public ulong NextULong()
    {
        ulong result = Rotl(_s1 * 5UL, 7) * 9UL;
        ulong t = _s1 << 17;

        _s2 ^= _s0;
        _s3 ^= _s1;
        _s1 ^= _s2;
        _s0 ^= _s3;
        _s2 ^= t;
        _s3 = Rotl(_s3, 45);

        return result;
    }

    /// <summary>Next value reinterpreted as a signed 64-bit integer.</summary>
    public long NextLong() => unchecked((long)NextULong());

    /// <summary>A fair coin.</summary>
    public bool NextBool() => (NextULong() & 1UL) != 0UL;

    /// <summary>
    /// Uniform value in <c>[0, bound)</c> with no modulo bias (Lemire's method, deterministic).
    /// </summary>
    public ulong NextBounded(ulong bound)
    {
        if (bound == 0UL)
        {
            throw new ArgumentOutOfRangeException(nameof(bound), "bound must be positive.");
        }

        UInt128 product = (UInt128)NextULong() * bound;
        ulong low = (ulong)product;
        if (low < bound)
        {
            ulong threshold = (0UL - bound) % bound;
            while (low < threshold)
            {
                product = (UInt128)NextULong() * bound;
                low = (ulong)product;
            }
        }

        return (ulong)(product >> 64);
    }

    /// <summary>Uniform integer in <c>[minInclusive, maxExclusive)</c>.</summary>
    public int NextInt(int minInclusive, int maxExclusive)
    {
        if (maxExclusive <= minInclusive)
        {
            throw new ArgumentOutOfRangeException(nameof(maxExclusive), "max must exceed min.");
        }

        ulong span = (ulong)((long)maxExclusive - minInclusive);
        return (int)((long)minInclusive + (long)NextBounded(span));
    }

    private static ulong Rotl(ulong x, int k) => (x << k) | (x >> (64 - k));

    private static ulong SplitMix64(ref ulong x)
    {
        x += 0x9E3779B97F4A7C15UL;
        ulong z = x;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }
}
