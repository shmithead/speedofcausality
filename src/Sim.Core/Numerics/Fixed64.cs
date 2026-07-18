namespace Sim.Core.Numerics;

/// <summary>
/// Deterministic signed Q32.32 fixed-point number backed by a 64-bit integer.
///
/// <para>This is the general-purpose scalar for sim math and utility scores (roadmap §2.5).
/// It is <b>not</b> the storage type for world-space position — positions are raw int64
/// millimetres, because a single Q-format cannot span docking-scale to Sol-scale (§2.5).</para>
///
/// <para>Range ≈ ±2.1e9 (integer part), resolution 2^-32 ≈ 2.3e-10. Every operation is
/// integer arithmetic with a fixed rounding rule, so results are byte-identical across
/// Windows and Linux — unlike <c>System.Math</c>, which is banned in this project (§2.5).</para>
/// </summary>
public readonly struct Fixed64 : IEquatable<Fixed64>, IComparable<Fixed64>
{
    /// <summary>Number of fractional bits (the "32" in Q32.32).</summary>
    public const int FractionBits = 32;

    private const long OneRaw = 1L << FractionBits;

    /// <summary>The underlying Q32.32 integer. Public for serialization and cross-OS diffing.</summary>
    public long Raw { get; }

    private Fixed64(long raw) => Raw = raw;

    /// <summary>Wraps an existing Q32.32 integer without conversion.</summary>
    public static Fixed64 FromRaw(long raw) => new(raw);

    /// <summary>Exact conversion from a whole number.</summary>
    public static Fixed64 FromInt(int value) => new((long)value << FractionBits);

    public static readonly Fixed64 Zero = new(0);
    public static readonly Fixed64 One = new(OneRaw);
    public static readonly Fixed64 Half = new(OneRaw >> 1);

    // ---- Arithmetic. Overflow wraps (deterministic); callers keep quantities in range (§2.5). ----

    public static Fixed64 operator +(Fixed64 a, Fixed64 b) => new(unchecked(a.Raw + b.Raw));

    public static Fixed64 operator -(Fixed64 a, Fixed64 b) => new(unchecked(a.Raw - b.Raw));

    public static Fixed64 operator -(Fixed64 a) => new(unchecked(-a.Raw));

    /// <summary>
    /// Multiply via a 128-bit intermediate so no precision is lost before the shift.
    /// The arithmetic right shift truncates toward negative infinity — a fixed, portable rule.
    /// </summary>
    public static Fixed64 operator *(Fixed64 a, Fixed64 b)
        => new((long)(((Int128)a.Raw * b.Raw) >> FractionBits));

    /// <summary>Divide via a 128-bit intermediate; integer division truncates toward zero.</summary>
    public static Fixed64 operator /(Fixed64 a, Fixed64 b)
        => new((long)(((Int128)a.Raw << FractionBits) / b.Raw));

    // ---- Comparison ----

    public static bool operator ==(Fixed64 a, Fixed64 b) => a.Raw == b.Raw;
    public static bool operator !=(Fixed64 a, Fixed64 b) => a.Raw != b.Raw;
    public static bool operator <(Fixed64 a, Fixed64 b) => a.Raw < b.Raw;
    public static bool operator >(Fixed64 a, Fixed64 b) => a.Raw > b.Raw;
    public static bool operator <=(Fixed64 a, Fixed64 b) => a.Raw <= b.Raw;
    public static bool operator >=(Fixed64 a, Fixed64 b) => a.Raw >= b.Raw;

    public static Fixed64 Abs(Fixed64 a) => new(a.Raw < 0 ? unchecked(-a.Raw) : a.Raw);

    /// <summary>
    /// Deterministic floor square root. Computes <c>isqrt(raw · 2^32)</c> with a 128-bit
    /// intermediate and a bit-by-bit integer root — no <c>System.Math</c>, no transcendentals.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">If <paramref name="value"/> is negative.</exception>
    public static Fixed64 Sqrt(Fixed64 value)
    {
        if (value.Raw < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "Sqrt of a negative Fixed64.");
        }

        if (value.Raw == 0)
        {
            return Zero;
        }

        // r = sqrt(X/2^32)·2^32 = sqrt(X·2^32). X·2^32 can reach ~2^95, so it needs 128 bits.
        UInt128 n = (UInt128)(ulong)value.Raw << FractionBits;
        return new((long)ISqrt(n));
    }

    /// <summary>Floor of the integer square root of a 128-bit value (classic bit-by-bit method).</summary>
    private static UInt128 ISqrt(UInt128 n)
    {
        UInt128 result = UInt128.Zero;
        // Largest power of four not exceeding n.
        UInt128 bit = (UInt128)1 << 126;
        while (bit > n)
        {
            bit >>= 2;
        }

        while (bit != UInt128.Zero)
        {
            if (n >= result + bit)
            {
                n -= result + bit;
                result = (result >> 1) + bit;
            }
            else
            {
                result >>= 1;
            }

            bit >>= 2;
        }

        return result;
    }

    // ---- Conversions for rendering / config / tests ONLY. Never store a double in sim state (§2.3 r2). ----

    /// <summary>Approximate value as a double — for rendering, logging, and test assertions only.</summary>
    public double ToDouble() => Raw / (double)OneRaw;

    /// <summary>Construct from a double — for config and tests only, never inside a tick.</summary>
    public static Fixed64 FromDouble(double value) => new((long)(value * OneRaw));

    public int CompareTo(Fixed64 other) => Raw.CompareTo(other.Raw);

    public bool Equals(Fixed64 other) => Raw == other.Raw;

    public override bool Equals(object? obj) => obj is Fixed64 other && Equals(other);

    public override int GetHashCode() => Raw.GetHashCode();

    /// <summary>Invariant, human-readable form. Not a serialization format — use <see cref="Raw"/> for that.</summary>
    public override string ToString() => ToDouble().ToString("0.##########");
}
