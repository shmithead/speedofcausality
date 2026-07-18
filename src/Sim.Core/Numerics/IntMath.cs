namespace Sim.Core.Numerics;

/// <summary>
/// Deterministic integer math on raw int64 millimetre coordinates — no floats, no <c>System.Math</c>.
/// Distances use a 128-bit intermediate because a squared interplanetary coordinate overflows int64.
/// </summary>
public static class IntMath
{
    /// <summary>Euclidean distance in millimetres between two mm points, floored to the nearest mm.</summary>
    public static long DistanceMm(long ax, long ay, long az, long bx, long by, long bz)
    {
        long dx = ax - bx;
        long dy = ay - by;
        long dz = az - bz;

        // Each squared term is ≤ ~8e31 (dx ≤ ~9e15); the sum fits comfortably in UInt128.
        UInt128 sum = (UInt128)((Int128)dx * dx)
                    + (UInt128)((Int128)dy * dy)
                    + (UInt128)((Int128)dz * dz);
        return (long)ISqrt(sum);
    }

    /// <summary>Floor of the integer square root of a 128-bit value (bit-by-bit; deterministic).</summary>
    public static UInt128 ISqrt(UInt128 n)
    {
        UInt128 result = UInt128.Zero;
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

    /// <summary>Rounded integer division for non-negative operands.</summary>
    public static long DivRound(long value, long divisor) => (value + (divisor / 2)) / divisor;
}
