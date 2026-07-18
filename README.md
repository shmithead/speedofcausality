# Speed of Causality

Sol-system logistics sim — light-lag command, autonomous captains, mortal NPCs, purchasable legality.

The design principle everything else serves: **first light is the floor.** No channel — sensor,
relay, rumor, or market — delivers information about an event faster than light can travel from it.
See [docs/technical-roadmapv3.md](docs/technical-roadmapv3.md) (canonical).

## Status

**Phase 0 — Sim Core Skeleton.** Building the deterministic foundation and proving it is
byte-identical across Windows and Linux before any gameplay is layered on.

Landed so far:

- `Sim.Core` — pure, deterministic sim library. No Godot, no IO, no `System.Math` (enforced, not by discipline).
- `Fixed64` — Q32.32 fixed-point scalar with deterministic integer `Sqrt` (roadmap §2.5).
- `Trig` — deterministic **CORDIC** sin/cos/atan2 (32 iterations), cross-checked vs `System.Math` to <2e-6.
- `Kepler` — closed-form orbital position on the fixed-point solver (mean → eccentric anomaly → inertial frame).
- `EphemerisTable` — one-period integer (mm) table + interpolation; "trig becomes a memory read" (§2.7).
- Determinism-smoke canary (now covering sqrt + trig) + committed golden (§2.6).
- `simtool` — headless CLI (`run` / `dump` / `diff` with first-divergence locator, §4).
- CI on Windows **and** Linux with a cross-OS byte-identical gate (§5).

Next: **validity horizons + invalidation graph** (§2.7, the organizing principle) and the
**Brewer compute benchmark** — plus real Sol-body element data to drive the ephemeris.

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
