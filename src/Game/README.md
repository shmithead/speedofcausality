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
- **Right-click a body** → a menu lists your freighter(s) with the distance and ETA to that body.
  Click one → type a **SITREP interval (days)** → **Enter** to launch. The dispatch order travels at c
  (instant for a ship at HQ, a light-lag later for one across the system); the "order in flight" banner
  counts down its travel and delivery.
- **Space** pause · **+ / −** time compression.

Once under way a ship **transmits a SITREP every N days**; each report crosses space at c, so the map's
ghost only jumps when a report arrives. Buy **Ore** cheap at one port and sell it dear at another — but
you route on **stale** prices, so a diversion is a bet. Your **Credits** are top-left.

## What you're looking at (the render rule)

- **Sun** at the heliocentric origin; **planets/Ceres** at their true positions — common-knowledge
  almanac (§2.7), not news that must arrive.
- A ship under way shows a **green predicted position** (from its filed plan — fair to draw live, wrong
  exactly when the plan is wrong) and a **grey ghost** (its last arrived SITREP, one light-lag stale);
  a ghost off its plan turns **red** (deviation, §3). A **docked** ship is labeled at its port, because
  HQ heard it arrive.
- **Ore prices** and ghosts are last-known values with their age — as stale as the geometry makes them.
- Markets at **Mars** and **Ceres**; the light-lag to each is shown live in the HUD.

No packet is drawn at a true in-flight position and no wavefront is drawn approaching; the screen
contains only what has arrived plus predictions from it (§5, the spike's hard-won rule).

> Note: this scene has not been run in-engine in the environment it was authored in (no Godot binary
> there). The Sim.Core calls it makes are covered by tests; the Godot wiring may need a first-run
> touch-up in the editor.
