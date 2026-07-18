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
them required, in order: (1) tick loop + scheduler, (2) validity horizons + invalidation order
(§2.3 r8), (3) reception root-find as a real loop — all now built — after which the benchmark reads
its numbers off a running sim.

## Benchmark completed — the real numbers off a running sim

With the scheduler, horizons, and a real reception root-find in place, `simtool bench` now measures
the §2.7 numbers instead of proxying them. Workload: 200 ships, each re-deciding on a ~40-day
cadence, each decision doing an actual Kepler solve + reception root-find against the Sun.

| Measurement | Value |
|---|---:|
| `Reception.ClosedForm` (on-rails endpoint) | 9.0M/s (111 ns) |
| `Reception.Solve` (moving endpoint, real loop) | 86.7K/s (11.5 µs) |
| Ship-observers per in-scope event @ 60 fps | ~150,000 on-rails · ~1,450 deciding |
| **Fraction of ticks touching a decision point** | **0.35 %** |
| **Measured rate (scheduler + horizon + solve)** | **~2,700 sim-years/min → 50-yr soak ≈ 1.1 s** |

**Read:** decisions are rare (0.35 % of ticks) and the horizon calculation is trivial, so the sim
runs ~200× rails and ~4,300× Brewer — a 50-year soak that costs 79 min naive costs ~1 second. This
is Brewer's "10,000 years/sec," measured. **The gate — does the horizon calc cost less than the
solves it saves? — is answered YES for this workload.**

## Caveats — what the 2,700 does and doesn't cover

- **The decide cadence (~40 days) is assumed, not derived.** Real cadence comes from comms, orders,
  and rumor (Phase 1+). A denser cadence lowers the rate proportionally: at 4-day decisions it is
  ~270 sim-years/min, still far above rails.
- **The reception root-find here is the *pessimistic* 11.5 µs** — its `observerAt` does a full Kepler
  solve every bisection step. §2.7 says a ship inside her horizon is a table lookup, which collapses
  most solves to closed form; that optimization (and warm-starting) is not yet applied, so real
  reception cost is an upper bound.
- **The load-bearing assumption is that the real horizon query stays cheap** — "what is the earliest
  thing that could change her mind?" must remain a scheduler peek, not a solve, once comms/rumor
  drive invalidation. If it becomes expensive, horizons shrink and the rate slides toward Brewer.
  That is the §2.7 failure mode to guard, and it is now the thing to watch in Phase 1+.
- Single machine, single thread. Rule 5 forbids threading *within* a tick; replays parallelize
  *across* runs, so per-replay single-thread is the correct unit.
