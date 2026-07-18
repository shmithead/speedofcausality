using Sim.Core.Numerics;

namespace Sim.Tests.Numerics;

public sealed class TrigTests
{
    // CORDIC at 32 iterations on Q32.32 lands well inside this band; it also guards against
    // regressions in range reduction and the shift-bias accumulation.
    private const double SinCosTolerance = 2e-6;
    private const double Atan2Tolerance = 2e-6;

    [Theory]
    [InlineData(0.0, 0.0, 1.0)]
    [InlineData(Math.PI / 2, 1.0, 0.0)]
    [InlineData(Math.PI, 0.0, -1.0)]
    [InlineData(-Math.PI / 2, -1.0, 0.0)]
    [InlineData(Math.PI / 6, 0.5, 0.8660254037844387)]
    [InlineData(Math.PI / 4, 0.7071067811865476, 0.7071067811865476)]
    public void SinCos_Matches_Known_Angles(double angle, double expectedSin, double expectedCos)
    {
        var (sin, cos) = Trig.SinCos(Fixed64.FromDouble(angle));
        Assert.Equal(expectedSin, sin.ToDouble(), precision: 5);
        Assert.Equal(expectedCos, cos.ToDouble(), precision: 5);
    }

    [Fact]
    public void SinCos_Matches_SystemMath_Across_Full_Range()
    {
        double maxError = 0;
        double worstAngle = 0;
        // Includes several turns to exercise range reduction beyond one period.
        for (double a = -12.5; a <= 12.5; a += 0.0007)
        {
            var (sin, cos) = Trig.SinCos(Fixed64.FromDouble(a));
            double es = Math.Abs(sin.ToDouble() - Math.Sin(a));
            double ec = Math.Abs(cos.ToDouble() - Math.Cos(a));
            double e = Math.Max(es, ec);
            if (e > maxError)
            {
                maxError = e;
                worstAngle = a;
            }
        }

        Assert.True(maxError < SinCosTolerance, $"max sin/cos error {maxError:E3} at {worstAngle:F4} rad");
    }

    [Fact]
    public void SinSquared_Plus_CosSquared_Is_One()
    {
        for (double a = -8.0; a <= 8.0; a += 0.013)
        {
            var (sin, cos) = Trig.SinCos(Fixed64.FromDouble(a));
            double s = sin.ToDouble();
            double c = cos.ToDouble();
            Assert.Equal(1.0, s * s + c * c, precision: 5);
        }
    }

    [Fact]
    public void Sin_Is_Monotonic_Increasing_On_Principal_Range()
    {
        double previous = double.NegativeInfinity;
        for (double a = -Math.PI / 2; a <= Math.PI / 2; a += 0.01)
        {
            double s = Trig.Sin(Fixed64.FromDouble(a)).ToDouble();
            Assert.True(s >= previous - 1e-9, $"sin decreased at {a:F4}");
            previous = s;
        }
    }

    [Fact]
    public void Atan2_Matches_SystemMath_Across_Grid()
    {
        double maxError = 0;
        (double, double) worst = (0, 0);
        for (double y = -10; y <= 10; y += 0.25)
        {
            for (double x = -10; x <= 10; x += 0.25)
            {
                if (x == 0 && y == 0)
                {
                    continue;
                }

                double got = Trig.Atan2(Fixed64.FromDouble(y), Fixed64.FromDouble(x)).ToDouble();
                double expected = Math.Atan2(y, x);
                // Wrap-around at ±π: compare on the circle.
                double e = Math.Abs(got - expected);
                if (e > Math.PI)
                {
                    e = Math.Abs(e - 2 * Math.PI);
                }

                if (e > maxError)
                {
                    maxError = e;
                    worst = (y, x);
                }
            }
        }

        Assert.True(maxError < Atan2Tolerance, $"max atan2 error {maxError:E3} at (y={worst.Item1}, x={worst.Item2})");
    }

    [Fact]
    public void Atan2_Handles_Axes_And_Origin()
    {
        Assert.Equal(0.0, Trig.Atan2(Fixed64.Zero, Fixed64.One).ToDouble(), precision: 6);
        Assert.Equal(Math.PI / 2, Trig.Atan2(Fixed64.One, Fixed64.Zero).ToDouble(), precision: 5);
        Assert.Equal(-Math.PI / 2, Trig.Atan2(-Fixed64.One, Fixed64.Zero).ToDouble(), precision: 5);
        Assert.Equal(Math.PI, Math.Abs(Trig.Atan2(Fixed64.Zero, -Fixed64.One).ToDouble()), precision: 5);
        Assert.Equal(0.0, Trig.Atan2(Fixed64.Zero, Fixed64.Zero).ToDouble()); // origin -> 0 by convention
    }
}
