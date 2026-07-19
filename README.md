# Speed of Causality

Sol-system logistics sim — **information-lag** command, autonomous captains, mortal NPCs, purchasable legality.

The speed limit is causality, not light: `c` is the speed of the fastest possible *signal* of any kind.
The design principle everything else serves: **first light is the floor** — no channel (sensor, relay,
rumor, or market) delivers information about an event before a signal from it could arrive, and **every
signal has a sender** (nothing radiates for free; the screen never shows what nobody sent).
See [docs/technical-roadmap.md](docs/technical-roadmap.md) (canonical; superseded drafts in `docs/archive/`).

## Status

**Phase 0 — Sim Core Skeleton.** Building the deterministic foundation and proving it is
byte-identical across Windows and Linux before any gameplay is layered on.

Landed so far:

- `Sim.Core` — pure, deterministic sim library. No Godot, no IO, no `System.Math` (enforced, not by discipline).
- `Fixed64` — Q32.32 fixed-point scalar with deterministic integer `Sqrt` (roadmap §2.5).
- `Trig` — deterministic **CORDIC** sin/cos/atan2 (32 iterations), cross-checked vs `System.Math` to <2e-6.
- `Kepler` — closed-form orbital position on the fixed-point solver (mean → eccentric anomaly → inertial frame).
- `EphemerisTable` — one-period integer (mm) table + interpolation; "trig becomes a memory read" (§2.7).
- Discrete-event `Scheduler` + `SimClock`, and **validity horizons** with an invalidation graph (§2.7).
- `Reception` — the light-lag core: closed-form + moving-endpoint root-find (§2.2).
- Seeded per-subsystem **RNG streams** (xoshiro256\*\*, §2.3 r4).
- **Event sourcing**: append-only `EventLog`, causal parents, fold-to-state, schema upcasting (§2.1, §2.6).
- `Sim.Persistence` — SQLite event store, MessagePack payload blobs, snapshots (§2.1).
- Determinism-smoke canary (sqrt + trig + rng) + committed golden (§2.6).
- `simtool` — headless CLI (`run` / `dump` / `diff`, `bench`), first-divergence locator (§4).
- CI on Windows **and** Linux with a cross-OS byte-identical gate (§5).

**The Brewer compute gate is answered** (`simtool bench`, [docs/benchmark-phase0.md](docs/benchmark-phase0.md)):
~2,700 sim-years/min horizon-driven, 0.35% of ticks touch a decision point.

Phase 0 is essentially complete. Next: Phase 1 — knowledge folds + message propagation at c, two
settlements, one ship (plus real Sol-body element data for the ephemeris).

## Layout

```
src/Sim.Core     pure deterministic sim (zero engine deps)
src/Sim.Tests    xUnit + CsCheck property tests + golden replays
src/Sim.Tools    simtool — run headless, dump, diff replays
docs/            design docs (technical-roadmapv3.md is canonical)
replays/         golden-master logs (Git LFS)
```

## Build & test

Requires the .NET 10 SDK.

```sh
dotnet build SpeedOfCausality.slnx
dotnet test  SpeedOfCausality.slnx
```

## simtool

```sh
# deterministic trace to stdout
dotnet run --project src/Sim.Tools -- run --seed 42 --steps 128

# write two traces and locate the first divergence
dotnet run --project src/Sim.Tools -- run --seed 42 --steps 128 --out a.txt
dotnet run --project src/Sim.Tools -- diff a.txt b.txt
```

## Non-negotiables (roadmap §2.3)

1. No wall-clock, no `System.Environment` inside `Sim.Core`.
2. No floats in simulation state — fixed-point or integers only.
3. All collections iterated in a defined order.
4. One seeded RNG stream per subsystem.
5. No threading inside a tick.
6. `Sim.Core` references no Godot, `System.IO`, or `System.Net`.

Rules 1, 2, and 6 are enforced by a banned-API analyzer that fails the build.
