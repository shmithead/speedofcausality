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

Two honest brackets — the true horizon-optimized number lands between them and needs the horizon
system built to measure directly.

| Scenario | sim-years/min | 50-year soak |
|---|---:|---:|
| **Brewer mode** — every entity a full Kepler solve every tick (his naive path) | 0.61 | ~82 min |
| **Rails mode** — every entity a table lookup every tick | 13.2 | ~3.8 min |

**Read:** Brewer mode reproduces his wall — a 50-year soak in ~1.4 hours is marginal, exactly his
experience. Rails mode clears the soak in minutes. Even the *pessimistic* bracket is runnable
(unlike a hard failure), and the ephemeris table already drags the common case toward rails.

## The two §2.7 numbers — not yet produced

§2.7 asks for **ship-observers affordable per in-scope event** and **fraction of ticks that touch a
decision point.** Both require the built tick loop + validity horizons + the reception root-find as
a full loop (not the single-iteration `Atan2` proxy used here). Those are the next build items.
What this benchmark establishes: **the primitives are not the wall.** The correctness half of
Risk 1 is retired (verified cross-OS); the performance half now has endpoints, and they say the
design is not dead on arrival.

## Caveats

- Single machine, single thread. Determinism rule 5 forbids threading *within* a tick; replays
  parallelize *across* runs, so per-replay single-thread is the correct unit to measure.
- "Brewer mode" charges a full Kepler solve to bodies too; in reality bodies are always ephemeris
  lookups (infinite horizon), so the realistic no-ship-horizons floor is already better than the
  0.61 shown.
- A real decision point costs a Kepler solve **plus** a reception root-find (several `Atan2`-class
  iterations), ≈1 µs total — but under horizons those are rare per tick, which is the point.
