# Game — Phase 1 strategic map (Godot 4.7, C#)

The frontend for Speed of Causality. It drives the headless simulation in
[`../Sim.Core`](../Sim.Core) and renders **only the player's knowledge fold** (roadmap §2.2, §5) —
never ground truth. The sim is engine-free and fully tested in `Sim.Tests`; this project only reads
its projections (`KnowledgeProjection`, `PriceBook`, `ShipView`) and draws them.

## Running it

This project is **not** part of `SpeedOfCausality.slnx` — CI builds and tests the sim without a Godot
SDK. Open it separately:

1. Install **Godot 4.7 (.NET/Mono build)** and a .NET SDK.
2. Open `src/Game/project.godot` in the Godot editor (it restores `Sim.Core` via the project
   reference and generates the C# glue on first build).
3. Press **F5** to run `Main.tscn`.

Or headless from the CLI, once Godot is on `PATH`:

```
godot --path src/Game
```

## Controls

- **Space** — pause / resume time
- **+ / −** — speed up / slow down compression
- **C** — order the player's freighter to divert to Ceres (a countermand: it travels at c and only
  takes effect when it reaches the ship — watch the ghost react a lag later)

## What you're looking at (the render rule)

- **Sun** at the heliocentric origin; **planets/Ceres** at their true positions — they are
  common-knowledge almanac (§2.7), not news that must arrive.
- Each ship shows a **green predicted position** (extrapolated from its filed plan — fair to draw
  live, and wrong exactly when the plan is wrong) and a **grey ghost** (its last *arrived* telemetry,
  one light-lag stale). A ghost that has drifted off its filed plan turns **red** — the caused
  deviation (§3).
- **Prices** are last-known quotes with their age in days — as stale as the geometry makes them.

No packet is ever drawn at a true in-flight position and no wavefront is drawn approaching; the screen
contains only what has arrived plus predictions from it (§5, the spike's hard-won rule).

> Note: this scene has not been run in-engine in the environment it was authored in (no Godot binary
> there). The Sim.Core calls it makes are covered by tests; the Godot wiring may need a first-run
> touch-up in the editor.
