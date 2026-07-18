using CsCheck;
using Sim.Core.Numerics;

namespace Sim.Tests.Numerics;

public sealed class Fixed64Tests
{
    // ---- example-based ----

    [Fact]
    public void One_Is_Whole()
    {
        Assert.Equal(1L << Fixed64.FractionBits, Fixed64.One.Raw);
        Assert.Equal(1.0, Fixed64.One.ToDouble());
    }

    [Theory]
    [InlineData(0, 0.0)]
    [InlineData(1, 1.0)]
    [InlineData(4, 2.0)]
    [InlineData(9, 3.0)]
    [InlineData(144, 12.0)]
    [InlineData(1_000_000, 1000.0)]
    public void Sqrt_Of_Perfect_Squares_Is_Exact(int square, double expectedRoot)
    {
        Fixed64 root = Fixed64.Sqrt(Fixed64.FromInt(square));
        Assert.Equal(expectedRoot, root.ToDouble(), precision: 9);
    }

    [Fact]
    public void Sqrt_Of_Negative_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Fixed64.Sqrt(Fixed64.FromInt(-1)));
    }

    [Fact]
    public void Mul_And_Div_Round_Trip_On_Whole_Numbers()
    {
        Fixed64 a = Fixed64.FromInt(7);
        Fixed64 b = Fixed64.FromInt(3);
        Assert.Equal(Fixed64.FromInt(21), a * b);
        Assert.Equal(a, (a * b) / b);
    }

    // ---- property-based (CsCheck, roadmap §1) ----

    // Whole-number generator kept well inside range so products can't overflow Q32.32.
    private static readonly Gen<int> SmallInt = Gen.Int[-30_000, 30_000];

    [Fact]
    public void Addition_Is_Commutative()
    {
        Gen.Select(SmallInt, SmallInt).Sample((x, y) =>
        {
            Fixed64 a = Fixed64.FromInt(x);
            Fixed64 b = Fixed64.FromInt(y);
            return (a + b) == (b + a);
        });
    }

    [Fact]
    public void Multiplication_Is_Commutative()
    {
        Gen.Select(SmallInt, SmallInt).Sample((x, y) =>
        {
            Fixed64 a = Fixed64.FromInt(x);
            Fixed64 b = Fixed64.FromInt(y);
            return (a * b) == (b * a);
        });
    }

    [Fact]
    public void Sqrt_Squared_Recovers_Input_Within_Epsilon()
    {
        // For n in [0, 1e6], sqrt is floor-accurate to 2^-32; squaring stays within a tiny band.
        Gen.Int[0, 1_000_000].Sample(n =>
        {
            Fixed64 x = Fixed64.FromInt(n);
            Fixed64 root = Fixed64.Sqrt(x);
            double recovered = (root * root).ToDouble();
            // Floor-rounding of a Q32.32 root loses at most ~2*sqrt(n)*2^-32 after squaring.
            double tolerance = 1e-3;
            return Math.Abs(recovered - n) <= tolerance;
        });
    }

    [Fact]
    public void Negation_Is_Involutive()
    {
        SmallInt.Sample(x =>
        {
            Fixed64 a = Fixed64.FromInt(x);
            return -(-a) == a;
        });
    }
}
