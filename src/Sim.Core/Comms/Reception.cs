using Sim.Core.Numerics;

namespace Sim.Core.Comms;

/// <summary>
/// The light-lag mechanic's core computation (roadmap §2.2): when does light emitted at
/// <c>tEmit</c> from a source position reach an observer? "First light is the floor" — nothing
/// beats this time.
///
/// <para>Two paths, and the distinction is the whole §2.7 compute story:</para>
/// <list type="bullet">
/// <item><b>Closed form</b> — when the observer's position over the relevant interval is a fixed
/// lookup (stationary, or on rails inside its horizon), reception is just <c>tEmit + distance/c</c>.
/// This is the common case and costs one distance.</item>
/// <item><b>Root-find</b> — when the observer is genuinely moving during the light-time (a decision
/// point), reception solves the implicit light-cone equation
/// <c>|pos_obs(t) − S| = c·(t − tEmit)</c> by deterministic integer bisection. This is the
/// irreducible cost, and it is reserved for the rare deciding endpoint.</item>
/// </list>
///
/// <para>All millimetres and seconds; no floats, no <c>System.Math</c>. Distance is monotonic
/// against <c>c·Δt</c> for any sub-light observer, so exactly one reception time exists.</para>
/// </summary>
public static class Reception
{
    /// <summary>Speed of light, 299,792,458 m/s expressed in mm/s.</summary>
    public const long SpeedOfLightMmPerSec = 299_792_458_000L;

    /// <summary>
    /// Reception time for a fixed-position observer (stationary, or on-rails so its position is a
    /// constant lookup over the light-time). Closed form: <c>tEmit + round(distance/c)</c>.
    /// </summary>
    public static long ClosedForm(
        long obsX, long obsY, long obsZ,
        long srcX, long srcY, long srcZ,
        long tEmit)
    {
        long distance = IntMath.DistanceMm(obsX, obsY, obsZ, srcX, srcY, srcZ);
        return tEmit + IntMath.DivRound(distance, SpeedOfLightMmPerSec);
    }

    /// <summary>
    /// Reception time for a moving observer whose position at any second is given by
    /// <paramref name="observerAt"/>. Solves the light-cone equation by integer bisection to the
    /// second. The bracket is derived from the emission-instant distance and expanded defensively;
    /// a sub-light observer keeps the root within a hair of <c>tEmit + distance/c</c>.
    /// </summary>
    public static long Solve(
        Func<long, (long X, long Y, long Z)> observerAt,
        long srcX, long srcY, long srcZ,
        long tEmit)
    {
        long F(long t)
        {
            (long x, long y, long z) = observerAt(t);
            long distance = IntMath.DistanceMm(x, y, z, srcX, srcY, srcZ);
            return distance - (SpeedOfLightMmPerSec * (t - tEmit));
        }

        (long ox, long oy, long oz) = observerAt(tEmit);
        long d0 = IntMath.DistanceMm(ox, oy, oz, srcX, srcY, srcZ);
        if (d0 == 0)
        {
            return tEmit;
        }

        // f(tEmit) = d0 ≥ 0 and f decreases to −∞; find the first t where f ≤ 0.
        long lightTime = d0 / SpeedOfLightMmPerSec;
        long lo = tEmit;
        long hi = tEmit + (2 * lightTime) + 2;

        int guard = 0;
        while (F(hi) > 0 && guard++ < 64)
        {
            hi += lightTime + 1; // observer receding faster than expected (v ≪ c makes this rare)
        }

        while (hi - lo > 1)
        {
            long mid = lo + ((hi - lo) / 2);
            if (F(mid) > 0)
            {
                lo = mid;
            }
            else
            {
                hi = mid;
            }
        }

        return hi; // first second at which light has arrived
    }
}
