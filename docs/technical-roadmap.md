# Technical Stack & Roadmap
**Project:** Sol-system logistics sim — light-lag command, autonomous captains, mortal NPCs, purchasable legality.

---

## 1. Stack Summary

| Layer | Choice | Why |
|---|---|---|
| Sim core | **C# class library (.NET 8+), zero engine dependencies** | Deterministic, headless-testable, one language across sim and frontend, huge ecosystem, far gentler than Rust for a first project |
| Frontend | **Godot 4.x (Mono/C#)** | Free, lightweight, excellent 2D, good enough 3D for the inspection view, exports everywhere |
| Sim math | **int64 fixed-point, scale chosen per quantity** (see §2.5) | Basic float ops are portable; **transcendentals are not**. Fixed-point sidesteps auditing every op — but the format is per-quantity, *not* one global Q-format (a single Q32.32 can't span docking-scale to Sol-scale — see §2.5) |
| RNG | **PCG or xoshiro, seeded, one named stream per subsystem** | Reproducibility; adding a system never perturbs another system's rolls |
| Event store | **Events as MessagePack blobs, one row per event in SQLite** (WAL, batched txns); snapshots in separate tables | One store, not two. Compact, queryable, versionable, corruption-resistant; batched inserts survive the 50-year soak |
| Saves | **The event log is the save** (§2.1) | Queryable saves, trivial replay export |
| Testing | **xUnit + property-based (CsCheck, C#-native) + golden-master replays** | Determinism is a test target, not a hope. CsCheck avoids pulling an F# toolchain in for FsCheck |
| CI | **GitHub Actions**, headless sim on Linux + Windows | Cross-platform determinism verified on every commit |
| Version control | Git + Git LFS for art | Standard |

**Explicit rejections:**
- **Unity** — runtime fee history, heavier, no advantage for this game
- **Rust/Bevy** — better determinism story, but the learning-curve cost exceeds the benefit for a solo first project
- **Godot's "exports everywhere"** — true for GDScript, *overstated for C#*: web export is experimental/unsupported and mobile is weaker. Fine here — the target is desktop/Steam — but don't plan a C# web demo on it
- **Custom engine** — the sim *is* custom; the renderer shouldn't be
- **LLM-driven captains** — non-deterministic, non-replayable, kills the architecture. Captains are utility AI + templated prose

---

## 2. Architecture

```
┌─────────────────────────────────────────────┐
│  Godot Frontend (rendering, input, audio)   │
│  - Strategic map    - Inspection view       │
│  - Comms log        - Design bay            │
└──────────────────┬──────────────────────────┘
                   │  Commands in / ViewModels out
                   │  (frontend NEVER reads sim state directly)
┌──────────────────▼──────────────────────────┐
│  Sim.Core (pure C#, no Godot references)    │
│                                              │
│  Clock ─ Scheduler ─ EventLog ─ RNG streams │
│  Systems: Orbits, Comms, Market, Captains,  │
│           Board, Regulator, Crime           │
│  Knowledge: per-entity event subscription   │
└──────────────────┬──────────────────────────┘
┌──────────────────▼──────────────────────────┐
│  Persistence: SQLite (events + snapshots)   │
└─────────────────────────────────────────────┘
```

### 2.1 Event sourcing (non-negotiable)
- **World state = fold(events).** Snapshots every N sim-hours are a cache, never the source of truth.
- Every event: `(id, timestamp, origin_entity, payload, causal_parent)`.
- **Save file = event log.** Replay = load log, re-fold. Bug report = seed + log.

### 2.2 Knowledge model (the light-lag mechanic)
- **Canonical model: analytic reception-time solve.** Events have an **occurrence time** and, per observer, a **reception time**. At occurrence, we compute each relevant observer's reception time and schedule a reception event into the priority queue (§2.4). There is exactly one light-lag model in the sim; the "message as a packet moving at c" in §3 is a **view-layer** concept (something the map can draw), not a second simulation mechanism.
- **Reception time is a root-find, not `occurrence + distance/c`.** That closed form is only exact for a *stationary* observer. A moving receiver requires solving the implicit light-cone equation `|pos_obs(t) − pos_src(t_emit)| = c·(t − t_emit)` for `t`. Because positions are closed-form (Kepler, §3), this is a well-behaved 1-D root-find — but it runs on the **deterministic fixed-point solver** (§2.5), never on `System.Math`.
- `Knowledge(entity, T)` = fold over events where `reception_time(entity) ≤ T`.
- The player is just another entity with a receiver at HQ. The strategic map renders the *player's* knowledge fold — extrapolated positions, stale prices — never ground truth.
- Captains, rival firms, the board, and the regulator all run their decisions against their own folds. One mechanism, every faction.

### 2.3 Determinism rules (enforced by CI, not discipline)
1. No wall-clock, no `DateTime.Now`, no `Environment` reads inside Sim.Core
2. No floats in simulation state — fixed-point or integers only (floats OK in rendering)
3. All collections iterated in defined order (sorted keys, never raw `Dictionary` order)
4. One RNG stream per subsystem, forked from the world seed by name
5. No threading inside a tick (parallelize *across* replays, not within one)
6. Analyzer/lint rule: Sim.Core may not reference Godot, System.IO, or System.Net
7. **Utility scores are fixed-point** (§2.5), and candidate selection uses a stable order: sort by score, break ties by a fixed key (entity id, then action id) — never insertion or hash order. A float utility score or an unstable tie-break silently diverges replays.

### 2.4 Tick model
- Fixed timestep (e.g., 1 sim-minute per tick) with a priority-queue scheduler for future events (order arrivals, burns completing, price fixings)
- Time compression = run more ticks per frame; auto-pause = scheduler flags that halt compression
- Godot interpolates visually between ticks; the sim never knows the frame rate exists

### 2.5 Numerics & determinism foundations

The determinism story is not "avoid floats." Basic IEEE-754 `+ − × ÷ √` are effectively portable in C# on modern x64 (SSE, no implicit FMA contraction). The two real traps are:

1. **Transcendentals.** `System.Math.Sin/Cos/Exp/Pow` are **not** guaranteed bit-identical across Windows and Linux — and Keplerian position, Kepler's-equation solve, the Lambert transfer solver, and the §2.2 reception-time root-find all lean on them. Fixed-point does **not** fix this for free.
   - **Highest-risk Phase-0 item:** port or build a **deterministic fixed-point trig + sqrt library** (CORDIC, or table + polynomial), and *never* call `System.Math` from Sim.Core. This is the single most likely source of a cross-OS replay divergence.
2. **Format mismatch.** There is no one Q-format for this game. A single **Q32.32** integer part maxes at ±2^31 ≈ 2.1×10⁹, so positions in **metres** overflow ~70× at just 1 AU; switch it to AU units and resolution collapses to ~35 m — useless for docking. The fix is **int64 fixed-point with a scale chosen per quantity:**

   | Quantity | Unit / scale | int64 range | Why |
   |---|---|---|---|
   | Position | millimetres (1 = 1 mm) | ±9.2×10¹⁵ mm ≈ ±61,000 AU | mm precision at docking scale, spans well past the Kuiper belt |
   | Velocity | µm/s or mm/s (fixed) | ample | derived from position/time; document the scale, don't infer it |
   | Acceleration | fixed sub-unit of mm/s² | ample | burns integrate cleanly |
   | Mass | grams (or kg·10⁻³) | ample | |
   | Money | minor currency units (integer) | ample | never store money in a float |

   Utility scores (§2.3 rule 7) are their own fixed-point scale. **Rule: every simulated quantity's unit and scale is written down here, not inferred at the call site.**

### 2.6 Event schema evolution (or: how not to break every save)

"Save file = event log" (§2.1) implies event payloads are stable — but a multi-year solo build churns them constantly, and every payload change would otherwise invalidate **all** golden replays and saves. Policy:

- **Versioned event types + upcasting.** Each event type carries a schema version; a load path upgrades old versions to current on read. Never mutate an old event's meaning in place.
- **Pre-1.0 golden masters are milestone-disposable.** Regenerate the golden replay corpus at each phase boundary; do not treat pre-release goldens as sacred. Keep exactly **one** tiny, stable **"determinism smoke" golden** (a few hundred ticks) that must stay byte-identical across commits — that's the canary, not the whole corpus.
- This is a named risk (§7), not an afterthought.

---

## 3. Subsystem Notes

**Orbits** — Keplerian elements per body, closed-form position at time T. No integration, no n-body. Solving Kepler's equation and the transfer **Lambert solver** are iterative root-finds that use trig/sqrt — run them on the deterministic fixed-point solver (§2.5), never `System.Math`. Port a known implementation; don't derive it — but port it *into fixed-point*, which is the non-trivial part.

**Comms** — A message's *reception* is computed analytically and scheduled as an event (§2.2) — that is the simulation. The "packet with a position moving at c" is a **view-layer** rendering the map can draw and (optionally, later) an interception can test against; it is not a second light-lag mechanism. Cancellable only by a faster message (there isn't one — that's the point).

**Market** — Per-settlement order books or a simpler supply/demand curve with elasticity per commodity. Prices fix at intervals; fixings are events; they propagate at c like everything else.

**Agents (DF-style autonomy)** — Captains and crew are need/task agents, Dwarf Fortress style: an order defines the *goal*, the agent plans and executes the steps on its own accord. Architecture:
- **Task decomposition:** an order ("deliver ore to Ceres") expands into a task tree (plot transfer → fuel check → burn → dock → negotiate → unload). The agent owns the tree, not the player.
- **Utility scoring:** at each decision point, candidate actions are scored against doctrine (objective, constraints, fallbacks), traits (initiative, caution, venality, competence), and *personal state* (fatigue, grudges, fear, greed). High initiative reorders the tree; low caution skips verification steps.
- **Interrupts:** new information (received at c, like everything else) can preempt the current task — a contact, a price rumor, a threat. Whether the agent deviates is a trait-weighted roll against doctrine.
- **Decision traces:** every choice logs its inputs → after-action reports are *generated from the actual decision trace*, templated into character voice. Free legibility, zero LLM.
- **Needs layer (scoped small):** DF gets its stories from needs colliding with orders. Ships have a light version — crew morale, fatigue, supply state — that feeds utility scores. A tired crew with an aggressive captain is where stories come from. Keep it to 3–5 needs; DF's full psyche model is a decade-long rabbit hole.

**Combat & the combat log (DF-style)** — Combat resolves at fine granularity *inside the event log*, and the "combat log" is just a prose rendering of those events. No separate narration system.
- **Resolution model:** component- and crew-level, not hit-point bars. A railgun round is an event chain: launch → intercept roll vs. point defense → penetration vs. armor section → component damage (coolant line severed, cargo bay 2 breached) → crew consequences (Technician Ortiz, burns, sickbay). Each step is its own event with causal parent links.
- **Prose templating:** events render through templates keyed on (weapon type, component, severity, actor traits) with variation pools — the DF voice: "*The 40mm slug tears through the aft radiator, showering the drive bay with molten fragments.*" Deterministic sim, varied prose: the template picker draws from its own named RNG stream so combat outcomes never depend on narration.
- **Light-lag applies:** you read the battle *as reports arrive*, minutes late, from ships that may already be dead. The combat log at HQ is a delayed, incomplete document — the full log is only recoverable if the ship survives to transmit or the wreck is boarded. DF's log tells you everything; yours tells you what made it home. That's better.
- **Mortality integration:** crew deaths in combat are the same death events as everywhere else — named characters, lost knowledge, successors promoted mid-battle by the surviving senior officer's own utility roll.

**Board / Regulator / Crime** — Same pattern: entities with knowledge folds, utility functions, and mortality. The regulator's "law" is a data table (prohibited acts, penalties, enforcement budget, attention weights). Lobbying edits the table via events. Criminal capacity is a derived quantity: enforcement gaps × unmet demand × corruption index.

**Mortality** — Any NPC entity can emit a death event. Knowledge attached to that entity (route familiarity, relationships, bribe ledgers) is stored *on the entity* and becomes unreachable, not deleted — successors may partially inherit via handover events.

---

## 4. Repo Structure

```
/src
  Sim.Core/          # pure C#, zero dependencies
  Sim.Tests/         # xUnit + CsCheck (property-based) + golden replays
  Sim.Tools/         # CLI: run headless, diff replays, inspect knowledge at T
  Game/              # Godot project (C#), references Sim.Core
/replays             # golden-master logs (LFS)
/docs                # design docs, this file
```

**Sim.Tools is not optional.** `simtool run --seed 42 --until 300d`, `simtool diff a.log b.log`, `simtool knows --entity capt_7 --at 114d` — you will live in these commands during Phases 2–5.

**When a golden breaks, you must find the *first* divergent event in minutes, not days.** `simtool diff` should locate the first differing event and (behind a flag) dump a per-stream **RNG-draw trace** around it, so a divergence points at the exact subsystem and draw that went wrong. Without this, a one-bit determinism regression is a multi-day hunt; with it, it's a targeted fix. Build it in Phase 0 alongside the determinism gate — that's when you'll first need it.

---

## 5. Technical Roadmap

### Phase −1 — Feel spike *(~2–3 weeks, THROWAWAY)*
§6 says the only question that matters is *is light-lag command fun?* — yet Phase 0 front-loads the hardest infra (deterministic trig, fixed-point) before any gameplay exists. Resolve the tension: **before** Phase 0, build a disposable prototype of just the command → light-lag → countermand loop. Floats allowed, no determinism, no event log — Godot or even a notebook. Its only job is to tell you whether the core loop is tense.
- **Rule: this code is deleted, not grown into Sim.Core.** Label the folder `/spike` and mean it. If the loop isn't fun here, you've spent 3 weeks, not a year.

### Phase 0 — Sim Core Skeleton *(~4–6 weeks, likely more — see §6)*
- .NET solution, analyzer rules, CI on Linux+Windows
- **Deterministic fixed-point trig + sqrt library** (§2.5) — *the highest-risk item; do this early and prove it cross-OS before building on it*
- Clock, scheduler, event log, RNG streams, per-quantity fixed-point math lib (§2.5)
- SQLite event store (MessagePack blobs) + snapshot/restore
- Keplerian orbits for Sol bodies (on the deterministic solver, not `System.Math`)
- `simtool` v0: run, dump, diff **+ first-divergence locator** (§4)

**Gate:** same seed → byte-identical logs on both OSes, 1,000 runs, *including orbital positions from the fixed-point trig lib*. Automated in CI. Do not proceed until green.

### Phase 1 — Prototype *(~4–8 weeks)*
- Knowledge folds + message propagation at c
- Two settlements, one commodity, price fixings
- One ship (impulse burns, fuel), one scripted competitor
- Minimal Godot map: bodies, ship, message packets, last-known prices with age indicators
- Countermand flow

**Gate:** the tension test. Playtest with 3–5 people who aren't you.

### Phase 2 — Doctrine & Agents *(~8–10 weeks)*
- Doctrine data model + editor UI (structured forms, not a scripting language — yet)
- Task-tree decomposition: order → goal → agent-owned plan
- Utility AI, trait system, interrupt handling, decision traces
- Light needs layer (morale, fatigue, supply) feeding utility scores
- Report generator (trace → templated prose in character voice)
- Death events + knowledge loss

**Gate:** replay a captain's controversial decision and see exactly what he knew. If you can't, the knowledge model is leaking ground truth.

### Phase 3 — Market & Rivals *(~8–10 weeks)*
- Full supply/demand sim, 6–10 commodities, production chains
- 3–4 AI firms running the *identical* captain/doctrine stack
- Price propagation stress test: does the market stay stable with 20+ ships trading on stale data?

**Gate:** economic soak test — 50 sim-years headless, no runaway inflation/deflation, no NaN-equivalents, CI-enforced.

### Phase 4 — Ownership *(~6 weeks)*
- Asset entities (mines, refineries, depots), acquisition, insolvency
- Market-share scoring per niche

**Gate:** an AI firm can be legally destroyed by another AI firm with no player involvement.

### Phase 5 — Pressures *(~10–12 weeks)*
Three interacting faction systems — the doc's own most scope-creep-prone build (§7). Do **not** also cram combat in here; combat is its own phase (5.5). Each pressure ships behind a toggle: the game must be playable with any of the three off.
- Board members (horizon/risk/venality/loyalty), quarterly knowledge folds, firing logic
- Regulation table, lobby/starve/distract verbs, designation threshold
- Emergent criminal capacity + piracy/extortion behaviors *(as intent/economic pressure; the shooting itself is Phase 5.5)*

**Gate:** the 30-year test — headless runs where aggressive lobbying measurably produces high-crime systems, without any scripted link.

### Phase 5.5 — Combat & the combat log *(~8–10 weeks)*
Combat is a full system, not a Phase-5 bullet — component/crew resolution *plus* a prose renderer is comparable in size to a whole pressure. Piracy (Phase 5) is its first driver, so it lands right after.
- **Combat resolution:** component/crew-level event chains. *Start with a minimal piracy vertical slice* (one weapon class, one boarding flow); defer richer combat until the slice reads well.
- **Combat log renderer:** event → prose templates, per-observer delayed delivery (light-lag applies — you read the battle as reports arrive).

**Gate:** the combat-log test — a pirate boarding action, read purely as the delayed prose log, is a story someone retells. If the log reads like a damage report instead of DF, iterate templates, not sim.

### Phase 6 — Fidelity *(~8–12 weeks)*
- Design bay (component tradeoffs feeding real sim stats)
- Inspection view (3D or high-detail 2D renders from design output)
- Map polish: vectors, intercept cones, confidence decay shaders
- Audio, comms log UX

### Phase 7 — Campaign & Ship *(~8+ weeks)*
- Scenario definitions, difficulty via starting position
- Endings (fired; designated; dominant; the mob as your board)
- Steam integration, settings, accessibility, save migration

---

## 6. Timeline Reality

Solo, part-time: **2.5–4 years to v1.** The 3-week **feel spike (Phase −1)** answers the only question that matters *first and cheapest* — if the loop isn't tense, you've spent three weeks. Note the honest risk: Phase 0's cross-OS determinism gate now includes the fixed-point trig library, which can easily run past the 4–6 week estimate; that's the price of front-loading the hard numerics, and the feel spike is what buys permission to pay it. Adding Phase 5.5 (combat) nudges the total toward the upper end of the range.

## 7. Top Risks

1. **Deterministic transcendentals** — `System.Math.Sin/Cos/Exp/Pow` differ across OSes; orbits, Lambert, and reception-time all depend on them. This, not "floats in general," is the likeliest divergence source. Mitigation: a fixed-point trig/sqrt library built and proven cross-OS in Phase 0; analyzer rule banning `System.Math` in Sim.Core (§2.5).
2. **Determinism erosion** — one stray float, one unordered dict, one unstable tie-break, and replays silently diverge. Mitigation: CI gate from day one, analyzer rules, cross-OS replay diffing, and `simtool`'s first-divergence locator (§4) so regressions are found in minutes.
3. **Event-schema churn vs. golden masters** — every payload change invalidates goldens and saves; over a multi-year build that's constant. Mitigation: versioned events + upcasting; pre-1.0 goldens are milestone-disposable; one tiny stable "smoke" golden is the canary (§2.6).
4. **Economic instability** — closed-loop economies explode. Mitigation: soak tests in CI from Phase 3; add sinks/faucets deliberately.
5. **Doctrine UI complexity** — the Aurora trap returns through the side door. Mitigation: structured forms with defaults; a full rule language only if playtests demand it.
6. **Scope creep at Phases 5 & 5.5** — three interacting faction systems *plus* a full combat system is the hardest stretch. Mitigation: each pressure ships behind a toggle (playable with any off); combat starts as a minimal piracy vertical slice before richer resolution.
7. **Solo burnout** — mitigated by the phase gates: every 2–3 months produces something playable or a clear kill signal. The feel spike (Phase −1) also front-loads a morale win.
