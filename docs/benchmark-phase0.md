# Phase 0 Compute Benchmark — the Brewer gate (§2.7)

**Date:** 2026-07-18 · **Machine:** Intel i7-12700K · **Config:** .NET 10, Release, single-thread
· **Reproduce:** `dotnet run --project src/Sim.Tools -c Release -- bench`

Brewer's *Slower Than Light* died on throughput — "high-cost trigonometric and radical functions,"
on the *fast* hardware path. This project mandates fixed-point CORDIC, which is **slower but
portable**, so the performance risk is real (§8 risk 1). This is the first measurement of it.

## Measured primitive throughput

| Primitive | Rate | Cost |
|---|---:|---:|
| `Kepler.PositionAt` — decision-point path (full solve) | 1.39M/s | 717 ns |
| `EphemerisTable.PositionMmAt` — on-rails path (lookup + interp) | 30.0M/s | 33 ns |
| `Trig.SinCos` | 9.55M/s | 105 ns |
| `Trig.Atan2` — reception-iteration proxy | 26.5M/s | 38 ns |

The ephemeris lookup is **~21× cheaper** than the Kepler solve. That ratio is the whole §2.7
argument in one number: horizons turn solves into lookups.

## Derived sim-years/minute (60 bodies + 200 ships, 1-minute ticks)

| Scenario | sim-years/min | 50-year soak |
|---|---:|---:|
| **Brewer mode** — every entity a full Kepler solve every tick | 0.61 | ~82 min |
| **Rails mode** — every entity a table lookup every tick | 13.2 | ~3.8 min |

**These are not bounds on the real answer. Both are floors, and the real number is below rails.**

- *Brewer mode (0.61)* is his path reproduced deliberately — a reference point, not our floor. Our
  CORDIC is slower than his `Math.Sin`, but we have rails / horizons / scope he did not.
- *Rails mode (13.2)* is an **optimistic** floor: it assumes zero decision points, zero reception
  solves, zero rumor propagation — nothing is ever deciding.

Real ticks have decision points, and a decision point costs a solve **plus** a reception root-find
(≈1 µs, ~30× a lookup). So the true rate sits **below** rails, and how far below is set entirely by
the fraction of entity-ticks that touch a decision point — which is one of the two §2.7 numbers, and
is itself an **output of the horizon system**. The brackets locate the interval; they do not bound it.

## The real gate — and why horizons *are* the rest of the benchmark

§2.7's two numbers — **ship-observers affordable per in-scope event** and **fraction of ticks that
touch a decision point** — are the horizon system's output. They cannot be proxied, because a proxy
has no horizons. So this is not "benchmark now, horizons later"; the horizon system is the rest of
the benchmark.

The gate question is therefore **not** "lookup vs solve." It is: **does the horizon calculation cost
less than the solves it saves?** If yes, the sim runs near rails. If the horizon calculation is
expensive — §2.7's flagged failure, "if 'what's the earliest thing that could change her mind?' is
costly, you default to short horizons and lose the win" — then decision points multiply and the rate
slides toward Brewer. The **reception root-find** is the piece to watch: it collapses to closed-form
only when both endpoints are horizon-valid, and a real solve is several iterations plus both
endpoints' Kepler positions — not the single-iteration `Atan2` (38 ns) used as a proxy here.

What this benchmark *does* establish: **the primitives are not the wall.** Risk 1's correctness half
is retired (verified cross-OS); the performance half now has endpoints. Finding the point between
them requires, in order:

1. **Tick loop + scheduler**
2. **Validity horizons + invalidation order** (§2.3 r8)
3. **Reception root-find as a real loop**
4. …after which the benchmark completes itself — it reads the two numbers off a running sim.

## Caveats

- Single machine, single thread. Determinism rule 5 forbids threading *within* a tick; replays
  parallelize *across* runs, so per-replay single-thread is the correct unit to measure.
- "Brewer mode" charges a full Kepler solve to bodies too; in reality bodies are always ephemeris
  lookups (infinite horizon), so the realistic no-ship-horizons floor is already better than the
  0.61 shown.
- A real decision point costs a Kepler solve **plus** a reception root-find (several `Atan2`-class
  iterations), ≈1 µs total — but under horizons those are rare per tick, which is the point.
