# Technical Stack & Roadmap
**Project:** Sol-system logistics sim — **information-lag** command, autonomous captains, mortal NPCs, purchasable legality.

> **The speed limit is causality, not light.** This game's `c` is the speed of the fastest possible *signal* — of any kind: a sensor flash, a transmitted alarm, a relayed report, a rumor. Light is the most familiar thing that travels at `c`, but it is not privileged here. Every channel is capped at the same `c` and subject to the same lag, and the core tension — *acting on a signal older than the truth it describes* — is **informational**, not photonic. Where this document says "light" it means "a signal at `c`"; "first light" is kept as a term of art for **the earliest possible arrival of any signal about an event** (the floor no channel beats), not specifically photons. A consequence made explicit in §2.2: **every piece of information a player receives was transmitted by someone or something** — nothing radiates for free, and the screen never shows what nobody sent.

---

## 1. Stack Summary

| Layer | Choice | Why |
|---|---|---|
| Sim core | **C# class library (.NET 8+), zero engine dependencies** | Deterministic, headless-testable, one language across sim and frontend, huge ecosystem, far gentler than Rust for a first project |
| Frontend | **Godot 4.x (Mono/C#)** | Free, lightweight, excellent 2D, good enough 3D for the inspection view, exports everywhere |
| Sim math | **int64 fixed-point, scale chosen per quantity** (see §2.5) | Basic float ops are portable; **transcendentals are not**. Fixed-point sidesteps auditing every op — but the format is per-quantity, *not* one global Q-format (a single Q32.32 can't span docking-scale to Sol-scale — see §2.5). **The CORDIC replacement is slower than the hardware path — see §2.7 for why that's a first-class risk, not a footnote.** |
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

### 2.2 Knowledge model (the information-lag mechanic)
- **Canonical model: analytic reception-time solve.** Events have an **occurrence time** and, per observer, a **reception time**. At occurrence, we compute each relevant observer's reception time and schedule a reception event into the priority queue (§2.4). There is exactly one signal-propagation model in the sim; the "signal as a packet moving at c" in §3 is a **view-layer** concept (something the map can draw), not a second simulation mechanism.
- **Reception time is a root-find, not `occurrence + distance/c`.** That closed form is only exact for a *stationary* observer. A moving receiver requires solving the implicit light-cone equation `|pos_obs(t) − pos_src(t_emit)| = c·(t − t_emit)` for `t`. Because positions are closed-form (Kepler, §3), this is a well-behaved 1-D root-find — but it runs on the **deterministic fixed-point solver** (§2.5), never on `System.Math`.
- **Root-find only when a ship is genuinely deciding.** Bodies are tabled common knowledge, and a ship *inside her validity horizon* is on rails too (§2.7) — so `pos_obs(t)` is a lookup and the light-cone equation collapses to the closed form. Station-to-station comms, market fixings, regulator notices, and coasting ships all reduce to arithmetic. The iterative solve is reserved for decision points with a moving endpoint. This is a compute decision, not a fidelity one (§2.7).
- **Events carry a scope: local / settlement / regional / broadcast.** Only broadcasts get a full observer sweep. This is fiction-first — a hull breach at Ceres was never going to be transmitted to Neptune — and it is the single largest compute win available (§2.7).
- `Knowledge(entity, T)` = fold over events where `reception_time(entity) ≤ T`.
- The player is just another entity with a receiver at HQ. The strategic map renders the *player's* knowledge fold — extrapolated positions, stale prices — never ground truth.
- Captains, rival firms, the board, and the regulator all run their decisions against their own folds. One mechanism, every faction.
- **First light is the floor.** No channel — sensor, relay, rumor, or market — delivers information about an event at A to HQ before `|A−HQ|/c`, where "first light" means the earliest arrival of *any* signal, not photons specifically. The triangle inequality forbids beating it (§3, Precursors & rumor). Any design that appears to beat it is either leaking ground truth or is really about *upstream conditions*, which is a different and legitimate mechanism.
- **Every signal has a sender.** Information is *transmitted*, not radiated for free — there is no universal broadcast that reaches everyone at once (see §3, Signal regulation). Each piece of knowledge an entity holds arrived because some source chose to send it (a monitoring station's alarm, a ship's telemetry, a rival's rumor). This is a fiction *and* a compute property: it is what prevents the "everyone learns at once" tick-spike (§2.7), because there is nothing that everyone learns at once. The screen never shows what nobody sent — the spike's render rule, generalized from light to all information.

### 2.3 Determinism rules (enforced by CI, not discipline)
1. No wall-clock, no `DateTime.Now`, no `Environment` reads inside Sim.Core
2. No floats in simulation state — fixed-point or integers only (floats OK in rendering)
3. All collections iterated in defined order (sorted keys, never raw `Dictionary` order)
4. One RNG stream per subsystem, forked from the world seed by name
5. No threading inside a tick (parallelize *across* replays, not within one)
6. Analyzer/lint rule: Sim.Core may not reference Godot, System.IO, or System.Net
7. **Utility scores are fixed-point** (§2.5), and candidate selection uses a stable order: sort by score, break ties by a fixed key (entity id, then action id) — never insertion or hash order. A float utility score or an unstable tie-break silently diverges replays.
8. **Horizon invalidation order must not affect outcomes** (§2.7). Validity horizons make computation *lazy*, which puts evaluation order on the critical path: two replays that invalidate the same set in different orders must fold to identical state. Invalidation processes in a defined order (sorted by expiry time, then entity id), and a horizon that expires during a tick is resolved before any dependent read. Stale state is worse than slow.

### 2.4 Tick model
- Fixed timestep (e.g., 1 sim-minute per tick) with a priority-queue scheduler for future events (order arrivals, burns completing, price fixings)
- **Ticks are sparse.** A tick touches only objects whose **validity horizon** has expired (§2.7) — typically a handful of captains at decision points, not every ship every tick. The scheduler already holds the reception events that define those horizons, so expiry is known in advance, never discovered.
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
- This is a named risk (§8), not an afterthought.

### 2.7 Compute budget (the Brewer problem)

**The prior art is a warning.** John Brewer's *Slower Than Light* (2014) is the closest thing to this design that anyone has built — hard-sci-fi 4X, light-lag on every channel, C#/.NET, solo. He reported that STL's heaviest load was **not graphics** but "the reasonably extensive amount of high-cost trigonometric and radical functions needed to solve orbital equations," and scoped ~80–100 hours to let players add **LAN compute nodes** to share the load. He was calling `System.Math` — the *fast*, hardware-accelerated path.

This project mandates fixed-point CORDIC trig (§2.5) precisely because `System.Math` isn't portable. **Correct everywhere, and slower than what already melted his machine.** Determinism is therefore a *performance* risk as well as a correctness risk (§8 risk 1), and Phase 3's 50-sim-year soak is where an unaffordable solver would surface — a year in, with three phases built on the assumption. A gate you cannot afford to run is not a gate; it's a wish.

**The organizing principle: validity horizons.**

> **Every object has a validity horizon: the time until which its future is closed-form. Compute nothing inside the horizon. Recompute only when something invalidates it.**

**Terminology, and it matters.** "Deterministic" in this document means *replays byte-identical* (§2.3) — by that meaning **everything in the sim is always deterministic, captains included**. Utility AI on a seeded stream is fully replayable; if captains were non-deterministic in that sense the architecture would be dead. The distinction that drives compute is **predictable**: closed-form, computable ahead without simulating the steps between. Neptune at T+400d is a formula. A captain at T+400d is not — her decision depends on what she will know then. Never use "deterministic" for this axis.

**Three horizon classes:**

| Class | Horizon | Members |
|---|---|---|
| **Infinite** | Never invalidated | Celestial bodies. Nobody transmits Neptune's position — that isn't news, it's an almanac. Everyone in the fiction already has the ephemeris. |
| **Event-bounded** | Next scheduled invalidation | A ship on a committed burn (horizon = next decision point). A market (horizon = next fixing or next arrival that moves supply). |
| **Immediate** | Now | Anything currently deciding. |

**Why this is not a constant-factor win.** Every other cut below saves work *per operation* — a cheaper lookup, a faster convergence. A 10× cheaper operation performed 10× more often is a wash. Horizons cut **the number of operations**: a ship with a 40-day horizon isn't computed cheaply for 57,600 ticks, she isn't computed *at all*. That changes what scales with what.

**Two corrections the framing forces:**

- **Markets never free-run.** They are not "predictable until an event makes them unpredictable" — every price move is *caused* by an arrival (cargo, fixing, rumor). A market is a step function whose steps are all scheduled events. Horizon = next scheduled step. Simpler than it first appears.
- **Captains are not randomly unpredictable — they are unpredictable at scheduled moments.** A captain re-evaluates when information reaches her, which is a **reception event the scheduler already holds** (§2.4). So invalidation is never a surprise: at burn commit, ask *what is the earliest reception that could reach her before arrival?* — that is her horizon. **You know in advance when you will need to think again.** Traits (risk aversion, venality) change *what she decides*, never *when*.

**What horizons subsume.** The reception root-find is what §2.2 calls irreducible — but a ship *inside her horizon is on rails*, so `pos_obs(t)` is a table lookup and the light-cone equation collapses to the closed form. **The root-find is needed only when a ship is genuinely deciding, which is rare.** Most ships, most of the time, are conics. Likewise, ticks become sparse: a tick touches only objects whose horizon has expired — a handful of captains, not 200 ships. That is Brewer's "10,000 years/sec" mode rather than his melting CPU.

**Why this works here and wouldn't in Aurora: information-lag creates horizons for free.** Instant information means anything can change anything at any time, so nothing has a horizon. A captain 40 light-minutes out is *provably unable* to learn anything for 40 minutes — no signal of any kind can reach her sooner. **The mechanic that makes this game expensive is the same mechanic that makes it cheap.**

**Consequences (apply in this order):**

| Cut | What it does | Notes |
|---|---|---|
| **Ephemeris table** | Precompute every body's orbit to an integer position table at world-gen; look up + interpolate. Trig becomes a memory read. | The infinite-horizon class, realized. Integers, so deterministic by construction. Kills most of Brewer's bottleneck outright. |
| **Table committed trajectories** | A ship coasting a committed transfer is a conic — table it at burn commit, invalidate at the horizon. | The event-bounded class, realized. Unpredictability is a property of *captains*, not coasting hulls. |
| **Closed-form for fixed endpoints** | Reception between two tabled/stationary endpoints is `distance/c` — no root-find. Station-to-station comms, market fixings, regulator notices. | Falls out of the two rows above: if both endpoints are inside their horizons, both are lookups. |
| **Event scope** | Events carry a scope: local / settlement / regional / broadcast. Only broadcasts get a full observer sweep. | Fiction-first, not a hack — a hull breach at Ceres was never going to be transmitted to Neptune. Collapses events × observers → events × interested-parties. Orthogonal to horizons; stacks with them. |
| **Horizon check instead of solve** | An arrival that lands after a ship's next decision point needs no root-find — it is already scheduled. Ask "does this land inside her horizon?" before computing anything. | The cheap test that replaces the expensive one. |
| **Warm-start the solver** | Seed each root-find from last tick's answer; cap iterations at tick resolution. | For the residual solves only. Halves iterations. No fidelity loss at tick granularity. |
| **Coarsen distant objects** | Tick far-flung ships less often. | Largely redundant once horizons work. Apply only if the benchmark still demands it. |

**The honest costs.** Horizon bookkeeping is an invalidation graph, and getting it wrong yields **stale state — worse than slow**. It also puts computation *order* on the critical path for determinism: invalidation order must not affect outcomes (this is the deferred-reception risk from earlier drafts, now load-bearing rather than optional — see §8 risk 2). And horizons are only as good as the horizon calculation: if "what is the earliest thing that could change this captain's mind?" is expensive to answer, you will default to short horizons and lose the entire win.

**What survives everything:** the root-find when a ship is genuinely at a decision point with another moving endpoint. Cost scales with **decision points × in-scope events**, not with ships × ticks. Information-lag is what's expensive here, not orbits — cutting orbital fidelity buys back nothing; only horizons do.

**Rule: benchmark before building on it (Phase 0 gate).** The question is not "how fast is CORDIC" but **"how many ship-observers can we afford per in-scope event, and what fraction of ticks touch a decision point?"** Those numbers decide whether Phase 3's 20 ships is 20 or 200. Measured in month two, every mitigation above is cheap; measured in month fourteen, they're architecture-shattering.

---

## 3. Subsystem Notes

**Orbits** — Keplerian elements per body, closed-form position at time T. No integration, no n-body. **Bodies are common knowledge** (§2.7): precompute the ephemeris to an integer table at world-gen and look it up at runtime rather than solving Kepler per query. Solving Kepler's equation and the transfer **Lambert solver** are iterative root-finds that use trig/sqrt — run them on the deterministic fixed-point solver (§2.5), never `System.Math`. Port a known implementation; don't derive it — but port it *into fixed-point*, which is the non-trivial part.

**Transfers are a precomputed table, not a live solve.** Per body-pair: (departure window, arrival time, delta-v cost). Captains consume it via utility scoring (§3 Agents); no human ever picks a trajectory from a menu.

> **The advantage Brewer didn't have.** STL's flight planner presumed Hohmann transfers and listed options per nearby target. Brewer's note on teaching it real orbits: *"it is going to start having huge numbers of maneuver options for each spacecraft. That's good, but I'm going to need to come up with a more sophisticated interface for selecting a destination."* **The physics wasn't his wall — the UI for the physics was.** More correct orbits meant an unusable menu. That wall does not exist here, because the flight planner's output is consumed by a utility function, not a person. This is a genuine architectural advantage of captain autonomy, and it's worth defending.

**Gravity assist is flavour, not physics.** Real assists are a three-body problem; the standard cheat (patched conics — hyperbolic arcs inside spheres of influence, boundary-crossing handoffs) is meaningfully more than "Kepler on rails" and buys nothing this design needs. An assist is simply **a row in the transfer table** with an unusual profile: low delta-v, long duration, narrow window. A captain with the right traits and a marginal fuel state picks it; the decision trace renders it in prose (*"Captain Vasquez elected the Jupiter flyby — fuel state was marginal; she judged the window worth the eleven-day penalty"*). No hyperbolas, no SOIs, no CORDIC tax on every tick.

As flavour it is **better, not merely cheaper**: it becomes a captain's judgment call that reaches you in a delayed report. You gave a goal; weeks later you learn what she decided. Simulating the physics would add nothing the trace doesn't already carry.

**Comms** — A message's *reception* is computed analytically and scheduled as an event (§2.2) — that is the simulation. The "packet with a position moving at c" is a **view-layer** rendering the map can draw and (optionally, later) an interception can test against; it is not a second propagation mechanism. Cancellable only by a faster signal (there isn't one — that's the point).

**Signal regulation — stream, don't broadcast (Phase 3+)** — The governing rule that makes "every signal has a sender" (§2.2) concrete, and the *designed* answer to the "everyone learns at once" tick-spike (§2.7, §8 risk — the Phase 3/5 information storm). **In-fiction:** the Sol government prohibits omnidirectional broadcast — untargeted transmission is treated as signals-intelligence pollution (static that blinds everyone, and free actionable intelligence handed to rivals). Detection at distance is independently weak: a cluttered, satellite-dense system and solar occlusion mean small events are not seen from across the system at all. **So information is *streamed* point-to-point, not *dumped* omnidirectionally.**

Mechanically this splits every event into two channels that were already in the spike (`alarmAt` / `reportAt`):

- **The alarm** (first light, §2.2) — coarse, magnitude-tiered, *type withheld*. For a large event it may be directly detectable at range; for a small one it must itself be transmitted. Cheap to receive — it is not a decision, it is a prompt to wait. **Does not trigger an expensive solve.**
- **The resolved report** — the actual story, *addressed* to a recipient and routed. This is what triggers a real decision (§3 Agents), and because it is streamed one recipient at a time, **decisions stagger naturally instead of landing on one tick.** The compute spike is not patched; it is dissolved — there is no simultaneous universal event to spike on.

**Why it is a game mechanic, not just an optimization:** streamed information is *owned, ordered, and fakeable*. Who transmits, to whom, in what order, and truthfully or not, becomes a strategic surface. The nearest relay learns first; a broker can sell a report late on purpose; a rival can route you a false rumor. A government that banned broadcast to protect signals intelligence has, without meaning to, made **information brokerage** a form of power — and that broker is a player in the Phase 3 economy.

**Discipline this imposes (the render rule, generalized):** every alert on screen must answer *who sent this, and why?* Sometimes boring (own sensors, own telemetry), sometimes the whole plot (a rival's plant). But nothing appears that nobody sent. Additive to the knowledge model, not a rewrite: reports gain a **recipient + route**, not just a reception time. Lands Phase 3; the two channels already exist from Phase −1.

**Do not over-apply the lore.** Detection is *unreliable and range/magnitude-dependent*, not impossible — big events still flash at range (the surge alarm that playtested well must survive), small/occluded ones only ever arrive as streamed reports. Killing detection entirely would amputate the mechanic the spike proved (§ Phase −1). The lore earns a **detection threshold + occlusion**, nothing more. And note the standing caution (§8): this is lore introduced partly to ease compute — legitimate *only because it also deepens the game*; with a ~4,000× compute cushion already measured (§6 benchmark), any further such lore is judged on fun first, cycles a distant second.

**Market** — Per-settlement order books or a simpler supply/demand curve with elasticity per commodity. Prices fix at intervals; fixings are events; they propagate at c like everything else.

**Precursors & rumor (Phase 3+; see §8 risk 7)** — The knowledge model (§2.2) gives *staleness*. This gives *anticipation* — and it is the difference between "how old is my info" and "did I read what I already had?"

The temptation is a faster-than-light alarm channel: magnitude now, content at c. **It does not survive the geometry.** Nothing about an event at A can reach HQ before light from A: for any intermediate observer O, `|A−O| + |O−HQ| ≥ |A−HQ|` by the triangle inequality. A relay path is never shorter than the direct path. First light is the floor, always.

What *can* beat your own reading of first light is someone else's, because **interpretation costs time and a nearer observer pays that cost sooner.** O at 20 light-seconds resolves what HQ at 90 sees as an unresolved smudge; if O sits near the A→HQ line, O's pre-digested claim arrives barely after first light and well before HQ's own integration finishes. That is the only legitimate edge, and it is real.

The genuinely pre-event channel is different, and it doesn't need a signal from A at all: **rumors are inferences from causally upstream facts that already arrived.**

- The refinery ordered replacement coolant three weeks ago — that order passed through your station.
- Their ore shipments went erratic two weeks back — your traders saw it.
- A senior engineer quit and took a berth outbound — the manifest is public.

None of that is about the explosion. All of it is upstream of the explosion, and all of it reached you at c, long before. Nobody is clairvoyant; they're reading upstream. This is how markets price a bankruptcy before the filing.

**Wave structure — every wave obeys c:**

| Wave | Content | Source |
|---|---|---|
| Precursors | Coolant orders, erratic shipments, a departure | Observations, various origins, various lags. Already on your desk. |
| Rumors | "Mars is in trouble" | Inferences from precursors, propagating as *claims* — with a source and a confidence, not an observation |
| First light | Something large happened at A | Light from A. Fastest possible. Magnitude only — unresolved. |
| Reports | What actually happened | Interpretation: O's read, or HQ's own integration |

**Rumors are not low-fidelity truth.** The resolving-fidelity model (each arrival strictly more accurate, nothing ever gets worse) makes the report an anticlimax. A rumor is a *separate object* with its own propagation and no guarantee of correspondence: it can be right, wrong, motivated, or someone talking their book. That makes the report a **judgment** — you bet on the rumor, now you find out. It also gives deniability for free (a price moved; maybe someone saw something, maybe they're repositioning) and makes market manipulation a native verb rather than a bolted-on feature.

**Coarseness rule.** Magnitude tiers must be wide enough that *opposite causes live inside the same tier* — a giant hostile at Mars and Mars' refinery exploding (your cargo just went 100×) are both "a lot of energy released at Mars." The ambiguity is structural, a fact about the world, not noise bolted on; noise averages out and players are very good at averaging. Test: after 20 runs, can the player predict the report from the alarm? If yes, too fine. If the alarm never changes what they do, too coarse. Target the band where it changes *urgency* but not *decision*.

**The architectural cost, stated plainly: major events cannot be die rolls.** Every disaster needs a generative history — a process with observable emissions propagating independently, some of which reached you, some of which you noticed. This is a real commitment and it lands on Phase 3 (market/rumor propagation) and Phase 5 (the pressures that generate precursors), *not* Phase 0.

**Deviation is the same channel, delivered by physics.** A ship off its filed plan (§3 Comms, Phase 1) tells you *something happened* one lag ago; the report explaining it lands later — or never, if the ship died before it could transmit. This only works if deviation is **caused** (a burn to avoid a hazard, a countermand landing) rather than noise: monitoring a random walk is a screensaver with a number on it. Where the spike used `SPEED_WANDER`, the sim uses causes.

**Agents (DF-style autonomy)** — Captains and crew are need/task agents, Dwarf Fortress style: an order defines the *goal*, the agent plans and executes the steps on its own accord. Architecture:
- **Task decomposition:** an order ("deliver ore to Ceres") expands into a task tree (plot transfer → fuel check → burn → dock → negotiate → unload). The agent owns the tree, not the player.
- **Utility scoring:** at each decision point, candidate actions are scored against doctrine (objective, constraints, fallbacks), traits (initiative, caution, venality, competence), and *personal state* (fatigue, grudges, fear, greed). High initiative reorders the tree; low caution skips verification steps.
- **Interrupts:** new information (received at c, like everything else) can preempt the current task — a contact, a price rumor, a threat. Whether the agent deviates is a trait-weighted roll against doctrine.
- **Decision traces:** every choice logs its inputs → after-action reports are *generated from the actual decision trace*, templated into character voice. Free legibility, zero LLM.
- **Needs layer (scoped small):** DF gets its stories from needs colliding with orders. Ships have a light version — crew morale, fatigue, supply state — that feeds utility scores. A tired crew with an aggressive captain is where stories come from. Keep it to 3–5 needs; DF's full psyche model is a decade-long rabbit hole.

**Combat & the combat log (DF-style)** — Combat resolves at fine granularity *inside the event log*, and the "combat log" is just a prose rendering of those events. No separate narration system.
- **Resolution model:** component- and crew-level, not hit-point bars. A railgun round is an event chain: launch → intercept roll vs. point defense → penetration vs. armor section → component damage (coolant line severed, cargo bay 2 breached) → crew consequences (Technician Ortiz, burns, sickbay). Each step is its own event with causal parent links.
- **Prose templating:** events render through templates keyed on (weapon type, component, severity, actor traits) with variation pools — the DF voice: "*The 40mm slug tears through the aft radiator, showering the drive bay with molten fragments.*" Deterministic sim, varied prose: the template picker draws from its own named RNG stream so combat outcomes never depend on narration.
- **Information-lag applies:** you read the battle *as reports arrive*, minutes late, from ships that may already be dead. The combat log at HQ is a delayed, incomplete document — the full log is only recoverable if the ship survives to transmit or the wreck is boarded. DF's log tells you everything; yours tells you what made it home. That's better.
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
§7 says the only question that matters is *is information-lag command fun?* — yet Phase 0 front-loads the hardest infra (deterministic trig, fixed-point) before any gameplay exists. Resolve the tension: **before** Phase 0, build a disposable prototype of just the command → information-lag → countermand loop. Floats allowed, no determinism, no event log — Godot or even a notebook. Its only job is to tell you whether the core loop is tense.
- **Rule: this code is deleted, not grown into Sim.Core.** Label the folder `/spike` and mean it. If the loop isn't fun here, you've spent 3 weeks, not a year.

**Status: run (HTML/canvas, one ship, two verbs).** See `phase-minus-1-final-rework.md`. What it produced was **design, not a verdict** — 24 hours cannot measure fun, and the honest finding is that the spike is a *design generator*. Three things came out of it that the roadmap did not contain:

- **Partial arrival** — revelation staged by fidelity (§3 Precursors & rumor), which is what makes an empty wait live.
- **First light is the floor** — the FTL-alarm idea died on the triangle inequality (§2.2).
- **The render rule** — the player's screen contains only what has *arrived*, plus predictions computed from what has arrived. The spike violated this twice (a packet drawn at its true in-flight position; a wavefront drawn approaching) and both got in via a journal entry reading *"I want to SEE it chasing."* **That is the design pressure, and it won.** Carried into Phase 1 as a standing rule.


### Phase 0 — Sim Core Skeleton *(~4–6 weeks, likely more — see §7)*
- .NET solution, analyzer rules, CI on Linux+Windows
- **Deterministic fixed-point trig + sqrt library** (§2.5) — *the highest-risk item; do this early and prove it cross-OS before building on it*
- Clock, scheduler, event log, RNG streams, per-quantity fixed-point math lib (§2.5)
- SQLite event store (MessagePack blobs) + snapshot/restore
- Keplerian orbits for Sol bodies (on the deterministic solver, not `System.Math`) — **precomputed to an integer ephemeris table** (§2.7)
- **Validity horizons + invalidation graph (§2.7)** — the organizing principle, not a later optimization. Ephemeris table (infinite horizon), horizon bookkeeping, and defined invalidation order (§2.3 rule 8). Retrofitting this at Phase 3 means rewriting the scheduler.
- **Compute benchmark (§2.7)** — the Brewer gate. Measure sim-years/minute at soak-realistic load (~60 bodies, ~200 ships) with CORDIC + ephemeris table + horizons. Two numbers to produce: **ship-observers affordable per in-scope event**, and **fraction of ticks that touch a decision point.**
- `simtool` v0: run, dump, diff **+ first-divergence locator** (§4)

**Gate:** same seed → byte-identical logs on both OSes, 1,000 runs, *including orbital positions from the fixed-point trig lib*. Automated in CI. Do not proceed until green.

**Second gate (performance):** the Phase 3 soak (50 sim-years headless) must be *extrapolated affordable* from the benchmark — minutes, not days. If it isn't, apply §2.7 consequences in order (event scope → horizon checks → warm-start → coarsen distant) and re-measure **before** Phase 1. Every one of these is cheap now and architecture-shattering at Phase 3.

### Phase 1 — Prototype *(~4–8 weeks)*
- Knowledge folds + message propagation at c
- Two settlements, one commodity, price fixings
- One ship (impulse burns, fuel), one scripted competitor
- Minimal Godot map: bodies, ship, message packets, last-known prices with age indicators
- **Filed flight plans + deviation** (§3): ships file a plan on order; the map draws plan, expected-position (a prediction — fair to show live), and the ghost (last actual telemetry, one lag stale). Deviation = ghost vs. plan *at the ghost's own timestamp*. Deviations must be **caused**, never noise.
- Countermand flow

**Render rule (learned the hard way in the spike):** the player's screen contains only what has *arrived*, plus predictions computed from what has arrived. No packet drawn at its true in-flight position, no wavefront drawn approaching. Predicted intercept — where an order lands *if the plan holds* — is legal and is better drama, because it is wrong exactly when the plan is wrong.

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
- **Rumor layer v1** (§3): actors form beliefs from arrived precursors and propagate them as claims (source + confidence, not observation). Claims can be wrong or motivated. Start with one precursor chain and one rumor type — this is the spine of the anticipation game, but it is also unbounded scope if it starts general.
- **Signal regulation v1** (§3): reports are *addressed and routed*, not broadcast. Minimal version — a report has a recipient and arrives per §2.2; the alarm/report split already exists from Phase −1. This is what staggers decisions and dissolves the information-storm spike (§8 risk 7); brokerage/false-routing is later.
- **Fan-out cap (from the Phase 0 peak-tick harness, `bench-peak`).** A single report must not fan out to more than **~700 in-scope decisions on one tick** without batching or staggering, or an interactive frame drops. This is half the ~1,400 warmed-batch *floor* measured on the reference machine (i7-12700K, pessimistic solve, no horizon collapse); the 2× margin carries the unmeasured cold-tick penalty (cache-cold, allocation, GC) that makes the live spike higher than the steady-state batch. Treat ~1,400 as "definitely broken." Re-measure against the real information graph once routing exists — the on-rails-lookup collapse raises the ceiling, a cold-tick measurement lowers the reported floor.
- Price propagation stress test: does the market stay stable with 20+ ships trading on stale data?

**Gate:** economic soak test — 50 sim-years headless, no runaway inflation/deflation, no NaN-equivalents, CI-enforced. *This gate's feasibility was decided at Phase 0 (§2.7) — if the benchmark was skipped, this is where you find out, a year late.*

**Second gate (rumor):** a headless audit proving no rumor ever reaches an entity before `|source−observer|/c` for the precursor it was inferred from. First light is the floor (§2.2); if the audit finds a violation, the rumor layer is leaking, not anticipating.

### Phase 4 — Ownership *(~6 weeks)*
- Asset entities (mines, refineries, depots), acquisition, insolvency
- Market-share scoring per niche

**Gate:** an AI firm can be legally destroyed by another AI firm with no player involvement.

### Phase 5 — Pressures *(~10–12 weeks)*
Three interacting faction systems — the doc's own most scope-creep-prone build (§8). Do **not** also cram combat in here; combat is its own phase (5.5). Each pressure ships behind a toggle: the game must be playable with any of the three off.
- Board members (horizon/risk/venality/loyalty), quarterly knowledge folds, firing logic
- Regulation table, lobby/starve/distract verbs, designation threshold
- Emergent criminal capacity + piracy/extortion behaviors *(as intent/economic pressure; the shooting itself is Phase 5.5)*
- **Precursor emission** (§3): the pressures are the game's main generator of upstream signal — an enforcement-budget cut, a lobbying push, a refinery deferring maintenance. Each pressure emits observable precursors that propagate independently of the event they eventually cause. **Consequence: major events here cannot be die rolls** — they are terminal states of processes with histories. Cheapest honest version is one precursor per pressure, not a full causal web.

**Gate:** the 30-year test — headless runs where aggressive lobbying measurably produces high-crime systems, without any scripted link.

### Phase 5.5 — Combat & the combat log *(~8–10 weeks)*
Combat is a full system, not a Phase-5 bullet — component/crew resolution *plus* a prose renderer is comparable in size to a whole pressure. Piracy (Phase 5) is its first driver, so it lands right after.
- **Combat resolution:** component/crew-level event chains. *Start with a minimal piracy vertical slice* (one weapon class, one boarding flow); defer richer combat until the slice reads well.
- **Combat log renderer:** event → prose templates, per-observer delayed delivery (information-lag applies — you read the battle as reports arrive).

**Gate:** the combat-log test — a pirate boarding action, read purely as the delayed prose log, is a story someone retells. If the log reads like a damage report instead of DF, iterate templates, not sim.

### Phase 6 — Fidelity *(~8–12 weeks)*
- Design bay (component tradeoffs feeding real sim stats)
- Inspection view (3D or high-detail 2D renders from design output) — *inspection only*
- Map polish: vectors, intercept cones, confidence decay shaders
- Audio, comms log UX

> **The strategic map stays 2D.** Brewer tried 3D and reverted: *"ship pathing looks more impressive in 3D, but it actually doesn't provide much useful information from a gameplay perspective; since distance and time are so far removed from each other in orbital mechanics, the intuition a player has looking at an orbital path in 3D actually hurts their ability to judge what is going to happen."* He added numbers over the 3D view and got "a crowded, unsightly mess." 3D here is for the inspection view — looking *at* a ship — never for judging trajectories.

### Phase 7 — Campaign & Ship *(~8+ weeks)*
- Scenario definitions, difficulty via starting position
- Endings (fired; designated; dominant; the mob as your board)
- Steam integration, settings, accessibility, save migration

---

## 6. Prior Art: what happened to *Slower Than Light*

The only serious attempt at this design. John Brewer, 2014, solo, C#/.NET, hard-sci-fi 4X with light-lag on every channel. Worth knowing precisely, because the field is otherwise empty — and empty fields are ambiguous. Either an unclaimed idea or a well-marked grave.

**He was not blocked on engineering.** He had a working demo and shipped two videos on lightspeed communications during the campaign. He'd solved reception-based interrupts the same way §2.2 does — his "Next Event" mode ticked forward at ~10,000 years/sec until an event *"reached the player's in-universe observer."* He understood that light-lag isn't a system you add but a property of the coordinate system: *"I have to store everything as 3+1 spacetime."* Independent convergence on this architecture is a good sign.

**The cause of death was commercial.** The Kickstarter raised $9,290 of $30,000 — 265 backers at $35 average, 30% of goal. What he wrote *during* the campaign, before knowing the result, is the part that matters:

> *"The game price was set at $10 because I thought the novelty value of the mechanics was worth at least that to many people, but after the very exhaustive interaction I've had with a number of potential backers, I think that is less of a factor than I thought it was. It would mean that the game is more in the realm of a Train Simulator, where in order to make back the cost of the game over the potential user base, it has to be priced more in the AAA bracket. I don't think STL can be produced at that quality level and still take anything other than a massive loss."*

**Train Simulator economics**: a mechanic so specific that the people who want it want it *badly*, and there are four hundred of them. The depth that makes it special is the depth that caps the audience. Aurora lives there happily — because Aurora is free and its author has a day job.

**Two honest reads, hold both:**

- *Pessimistic:* the one prior data point says this audience is ~265 people. Phase 7 has Steam integration in it. Steam will not fix that.
- *Optimistic:* 2014 Kickstarter is the worst possible venue for a mechanic whose entire pitch is "you have to feel it" — it demands selling a concept before it exists. Aurora, DF, and RimWorld found their audiences by *existing*, not by being pre-sold. Brewer needed $30k up front from people who'd watched a video. This project needs zero.

**The decision this forces, and it is not a technical one:** *am I building this to ship, or to exist?* Brewer's model required commercial viability at the gate. This one doesn't — **unless Phase 7 says it does**, and right now Phase 7 quietly assumes it does. Decide that deliberately, in writing, rather than discovering it in year three.

---

## 7. Timeline Reality

Solo, part-time: **2.5–4 years to v1.** The 3-week **feel spike (Phase −1)** answers the only question that matters *first and cheapest* — and, as run, produced design rather than a verdict (see Phase −1). Note the honest risk: Phase 0's cross-OS determinism gate now includes *both* the fixed-point trig library *and* the compute benchmark (§2.7), and either can run past the 4–6 week estimate. That's the price of front-loading the hard numerics — and the benchmark is what prevents a year of work resting on an unaffordable solver. Adding Phase 5.5 (combat) nudges the total toward the upper end of the range.

**The estimate assumes the §2.7 cuts land.** If the benchmark comes back bad and event scope + tabled trajectories + warm-starting don't recover it, the honest options are fewer ships (Phase 3 shrinks), a coarser tick (Phase 5.5 suffers), or revisiting the fixed-point mandate itself — which reopens §2.5 and the entire determinism story. That is a Phase 0 decision. It must not become a Phase 3 discovery.

## 8. Top Risks

1. **Deterministic transcendentals — correctness *and* performance** — `System.Math.Sin/Cos/Exp/Pow` differ across OSes; orbits, Lambert, and reception-time all depend on them. This, not "floats in general," is the likeliest divergence source. **But the cost is two-sided:** the fixed-point CORDIC replacement is *slower* than the hardware path that already bottlenecked Brewer's STL (§2.7), so the mitigation for the correctness risk *creates* a performance risk. Mitigation: fixed-point trig/sqrt library built and proven cross-OS in Phase 0; analyzer rule banning `System.Math` in Sim.Core (§2.5); **and the Phase 0 compute benchmark + perf gate (§2.7)**, which decides feasibility before Phases 1–3 are built on top of it.
2. **Determinism erosion — including invalidation order** — one stray float, one unordered dict, one unstable tie-break, and replays silently diverge. **Validity horizons (§2.7) add a second front:** laziness puts computation *order* on the critical path, and a horizon bug yields **stale state, which is worse than slow** — the sim keeps running and quietly answers from an expired cache. Mitigation: CI gate from day one, analyzer rules, cross-OS replay diffing, defined invalidation order (§2.3 rule 8), and `simtool`'s first-divergence locator (§4) so regressions are found in minutes.
3. **Event-schema churn vs. golden masters** — every payload change invalidates goldens and saves; over a multi-year build that's constant. Mitigation: versioned events + upcasting; pre-1.0 goldens are milestone-disposable; one tiny stable "smoke" golden is the canary (§2.6).
4. **Economic instability** — closed-loop economies explode. Mitigation: soak tests in CI from Phase 3; add sinks/faucets deliberately.
5. **Doctrine UI complexity** — the Aurora trap returns through the side door. Mitigation: structured forms with defaults; a full rule language only if playtests demand it.
6. **Scope creep at Phases 5 & 5.5** — three interacting faction systems *plus* a full combat system is the hardest stretch. Mitigation: each pressure ships behind a toggle (playable with any off); combat starts as a minimal piracy vertical slice before richer resolution.
7. **Precursor/rumor scope & FTL leakage** — the anticipation layer (§3) is the strongest idea in the design and the easiest to build wrong in two directions. *Scope:* "every major event needs a generative history" is an unbounded commitment if it starts general — mitigation: one precursor chain, one rumor type, at Phase 3; expand only if it lands. *Leakage:* an alarm channel that beats first light is physically impossible (triangle inequality, §2.2) and any implementation that seems to is leaking ground truth — mitigation: the Phase 3 rumor audit gate. *Decodability:* a magnitude tier fine enough to predict the report is instant information with extra steps — mitigation: the coarseness test in §3. *Information-storm compute spike (previously open):* a single event triggering every entity to decide on one tick — **now addressed by design**, not engineering: signal regulation (§3) means there is no universal broadcast, so streamed reports stagger decisions across ticks. This is unmeasured until Phase 3 builds a real information graph; the interactive-frame cost under a correlated cascade (e.g. a fleet re-deciding on one routed report) is the specific thing to benchmark then — the headless soak is settled (§6), the interactive storm is not.
8. **Good architecture as a scope accelerant** — the subtlest lesson from STL. Mid-Kickstarter, with the game unfinished, Brewer seriously considered adding whole-brain emulation — because *"from a technical coding perspective, implementing this form of transhumanism is just this side of trivial, given the architecture of the game's data structures. This is strictly a design consideration."* He was right: his architecture was good enough that **any** feature looked cheap. That is exactly how a scope-disciplined solo dev talks themselves into a feature that, in his own words, *"could radically alter the gameplay experience."* An event-sourced sim with per-entity knowledge folds will make almost anything look like a weekend. Mitigation: "trivial to implement" is not an argument for inclusion — the phase gates are, and features are judged against the *gate*, never against implementation cost.
9. **Solo burnout** — mitigated by the phase gates: every 2–3 months produces something playable or a clear kill signal. The feel spike (Phase −1) also front-loads a morale win.
