# Survey Drone Feasibility Spike — Findings

Answers the three questions blocking `docs/plans/2026-07-11-001-feat-survey-drone-plan.md`.
Probe mod: `EcoServerMod/AdvancedElectronics.Spike/` (build + deploy: `EcoServerMod/README.md`).

**Prerequisites for the manual protocol:** an Eco 0.13.0.4 dedicated server with the spike
DLL in `Mods/UserCode/`, a client connected, and **admin authorization** for your user
(all `/spike` commands are admin-level).

Compile-time findings against `Eco.ReferenceAssemblies 0.13.0.4-beta-release-1024` are
already filled in below — they were established while building the probes. Live-run
verdicts are blank until the protocol is executed.

---

## Q1 — Can a custom server entity invoke animal navigation outside the animal lifecycle?

**Probe:** `/spike path [speciesName] [distance]` (default `Hare`, 15 blocks)

**Compile-time evidence (already established):**

- `Eco.Simulation.Agents.Animal` is abstract; `GetPathTo` / `RequestPathAndUpdateState`
  are instance methods requiring an `AnimalSpecies`; no standalone or static pathfinder
  type exists in the public 0.13.0.4 surface.
- **Rung (a) — lifecycle-free pathfinding: NOT REACHABLE.** The survey-drone plan's
  "custom entity borrowing animal navigation" (its R1) cannot mean "call the pathfinder
  without an animal"; it must mean subclassing/instantiating the animal machinery or
  driving a spawned animal.

**Protocol (rung b):**

1. Stand in open terrain; run `/spike path`.
2. Record the `[Q1 spawn]` line — spawn success/failure is a harness outcome, not a
   pathfinding verdict.
3. Record the `[Q1 path]` line and 60 seconds of `[Q1 trace]` output: does the animal
   move toward the target (distance shrinking)?
4. Obstacle sub-check: the target lies due +X (east) of where you stood when running
   the command — build a 2-block wall across the line between the printed spawn and
   target coordinates, re-run, record whether the trace routes around it.
5. Control run (recommended): spawn the same species without commanding a path
   (vanilla spawn or a second `/spike path` aimed elsewhere) and compare traces —
   "distance shrinking" only counts as commanded pathing if it beats the
   autonomous-wander baseline.

| Question half | Verdict (pass/partial/fail) | Evidence |
|---|---|---|
| Pathfinding callable without animal lifecycle | **fail (compile-time)** | Rung (a) evidence above |
| Spawned animal paths to commanded target | **fail (final, iteration 3)** | All five levers exhausted (GetPathTo, forced NextTick, DoServerUpdateAnimalData, Behavior field write, RequestPathAndUpdateState): animal never walks to the target. Iterations showed the brain itself works — after activation it stares at the player and flees when shot (brain-driven pathfinding + locomotion function) — but behavior selection overrides every external command. Verdict: vanilla animals cannot be puppeteered; navigation is reachable only from inside a brain/behavior. |
| Path avoids player-built obstacles | **moot** | Unreachable — no externally-commanded path ever ran. Brain-driven flee visibly navigates, so obstacle handling exists inside the behavior machinery. |

**Implication for survey-drone plan:** R1's "without being an animal" needs rewording
regardless of the live verdict — the realistic options are (i) subclass `AnimalEntity`
with ecosystem opt-outs, or (ii) a WorldObject mover with its own navigation (see Q2).
If rung (b) shows externally-commanded pathing works, option (i) narrows to a thin
subclass; if not, option (ii) plus simple grid movement becomes the default.

---

## Q2 — Does a server-moved WorldObject render acceptably on the client?

**Probe:** `/spike move [speed] [objectType]` (default 2°/tick, `CampfireObject` — a
plain prefab with no bespoke client movement components), then `/spike stop`.

**Protocol:**

1. Run `/spike move` — a campfire spawns ~4 blocks away and circles. (The probe
   self-terminates after 10 minutes if you forget `/spike stop`.)
2. Observe at walk-speed (default): smooth glide vs. visible stepping/teleporting.
3. Run `/spike stop`, then `/spike move 20` — fast movement; record snap/teleport behavior.
4. Observe from >25m away (SyncPhysics kinematic-fallback distance from client evidence).
5. Record server console errors (world-state complaints about occupancy/collision count
   as evidence, not failure — note them).

| Question half | Verdict (pass/partial/fail) | Evidence |
|---|---|---|
| Motion smoothness at walk speed | **pass (iteration 3, timer strategy)** | With `/spike move ... timer` (50ms timer driving Position + SyncPositionAndRotation) the object moves continuously on the client. Earlier single-step behavior was a tick-surface defect: `IWorldObjectManager.AddToTick`/`NextTickTime` never re-fired our callback (requeue strategy also failed) — the real mod will tick from its own WorldObject component, which is the vanilla pattern. No thread-affinity exceptions were reported by the timer run. |
| Behavior at high speed / >25m distance | not separately recorded | Timer run confirmed continuous movement; snap-distance and >25m kinematic behavior can be characterized during real dock development — no longer gate-relevant. |
| Locomotion animation via state hooks | **not answerable by this probe** (structural) | Needs a custom bundled prefab; carried to origin plan as open item |

**Implication for survey-drone plan:** motion-smoothness pass ⇒ the drone's client half
can be a SyncPhysics WorldObject (its R16 path). The animation half stays open until a
custom prefab exists — cap Q2 at "partial" in the gate decision.

---

## Q3 — Can a server mod read district polygons and offer district selection on an object UI?

**Probe:** `/spike districts`

**Compile-time evidence (already established):**

- Districts are first-class in 0.13: `Eco.Gameplay.Civics.Districts.DistrictMap`
  (registrar-managed, `GetDistrictAtWorldPos`, `Districts` dictionary,
  `DistrictsUpdatedEvent`) with `Eco.Gameplay.LegislationSystem.District` entries —
  reading district data from a mod compiles cleanly.
- The 0.11-era `ClientCanSelectAndAdd` UI attribute was **not found** in 0.13; the
  picker half has no confirmed mechanism yet (see `EcoServerMod/README.md`).

**Protocol:**

1. Draw a district via the in-game map/law interface; stand inside it.
2. Run `/spike districts` — record the `[Q3 districts]` lines: does your district appear
   by name, and does "you are inside" match reality?
3. Step outside the district; re-run; confirm the "no district" line.
4. Record settlement and deed counts for completeness.

| Question half | Verdict (pass/partial/fail) | Evidence |
|---|---|---|
| Mod reads district data (names, membership at position) | pass | I was able to see the district name and that I was inside it, or that I was outside of any district |
| District picker on a WorldObject UI | **partial (assembly-evidence only), cap** | No live picker demonstrated; 0.11 attribute gone in 0.13 |

**Implication for survey-drone plan:** data-half pass ⇒ R12's district assignment is
feasible server-side; the picker UI remains a spike-class risk on R12 — a chat-command
assignment fallback works today if the picker mechanism doesn't materialize.

---

## Closing checklist (after the live runs)

- [x] Fill every blank verdict above with pass/partial/fail + evidence. (Live run 2026-07-12.)
- [x] Update `docs/plans/2026-07-11-001-feat-survey-drone-plan.md` Dependencies /
      Assumptions with these verdicts (R1 rewording per Q1; R16 rendering path per Q2;
      R12 risk note per Q3).
- [x] Gate decision recorded in the origin plan: Q3 data half passes; Q1/Q2 fail
      **as instrumented**, with both failure signatures consistent with probe-harness
      defects rather than settled architecture answers (see interpretation below) —
      the gate stays closed pending a spike iteration 2.
- [x] Game-version context noted in the origin plan: all evidence is against Eco
      0.13.0.4 (`Eco.ReferenceAssemblies 0.13.0.4-beta-release-1024`).

## FINAL VERDICT (2026-07-12, iteration 3 run) — spike gate CLEARS

All three questions answered; the survey-drone plan can proceed to implementation
planning with these resolved inputs:

- **Q1 — external animal puppeteering: FAIL; brain-driven navigation: WORKS.** The
  drone cannot be "a custom entity borrowing animal navigation" as the plan's R1
  worded it. Two viable architectures, to be decided as a planning KTD:
  - **(i) `AnimalEntity` subclass with its own custom behavior** — navigation runs
    inside the brain, which the flee test proved functional; needs ecosystem
    opt-outs (not huntable, no population counting).
  - **(ii) WorldObject mover with self-written navigation** — Q2 proves the
    rendering path; pathing (e.g., simple grid/A*) is on us.
- **Q2 — server-driven WorldObject movement renders: PASS.** Continuous client-side
  movement confirmed (timer strategy). The probe's tick-manager surface
  (`AddToTick`/`NextTickTime`) does not re-fire for mod callbacks — a probe-harness
  dead end, irrelevant to the real mod, which ticks from its own WorldObject
  component like vanilla movers (`ElevatorComponent`). Animation-state hooks remain
  open until a custom prefab exists (client work).
- **Q3 — district data: PASS** (names + positional membership readable). District
  *picker* on object UI unproven — chat-command assignment is the working fallback.

## Iteration 3 — current build, re-run protocol

**Iteration 2 results (2026-07-12, second run):** Q2 unchanged — one teleport then
static, so the `TickStartTime`-based `NextTickTime` advance also failed to re-queue
(the manager appears to consult the schedule only at `AddToTick` time, or uses a
different time base). Q1 progressed — the animal now *stares at the player* and
*flees when shot*: the activation levers woke the brain, and pathfinding/locomotion
demonstrably work **when the brain drives them** — but external `GetPathTo` is
ignored by its own behavior selection. Puppeteering a live-brained vanilla animal is
looking infeasible; the realistic Q1 outcome is "custom `AnimalEntity` subclass with
its own behavior".

Iteration 3 (deployed in this build) — re-run and record:

1. **Q2:** `/spike move` now takes a strategy parameter:
   - `/spike move` (default `requeue`) — explicitly re-registers with the tick
     manager after every tick (`AddToTick`, guarded by `IsQueuedForTick`).
   - `/spike move 2 CampfireObject timer` — a 50ms `System.Threading.Timer` drives
     the same step, bypassing the tick manager entirely. A thread-affinity exception
     in chat is evidence, not failure.
   Record which strategy (if either) produces continuous movement. If `timer` moves
   continuously and smoothly, Q2's rendering answer is effectively PASS (with the
   caveat that the real mod needs a proper tick surface — likely a custom
   WorldObject class with a component `Tick()`, which the real dock will have anyway).
2. **Q1:** `/spike path` adds two final puppeteering levers after the iteration-2
   ones: writes the `Behavior` field to `"Wander"` and calls
   `RequestPathAndUpdateState(...)` (updates behavior state, not just a path).
   Record all `[Q1 activate]` lines and whether the animal finally walks. If it
   still holds position, record Q1 rung (b) as **fail — external puppeteering is
   overridden by the brain**, and the implication below becomes the verdict:
   option (i) custom subclass with its own behavior, informed by the fact that
   brain-driven pathing (flee) visibly works.

## Interpretation of the 2026-07-12 run (spike iteration 2 targets)

- **Q2 — "teleports once, then stays":** the object DID move and sync once, so
  `Position` + `SyncPositionAndRotation()` works; the mover simply never ticked again.
  Prime suspect: `NextTickTime => 0d` — the tick manager most likely schedules each
  `ITickOnDemand` by its `NextTickTime` and re-queues only for a future time, so a
  constant `0` gets scheduled once and never again. Iteration 2: return an advancing
  next-tick time (e.g., `WorldObjectManager.Obj.TickStartTime + TickDeltaTime`-based,
  or wall-clock now + small delta), or move the probe onto a `WorldObjectComponent`
  tick. The Q2 architecture question is therefore still OPEN, not failed.
- **Q1 — animal spawns inert (no wander, no pathing):** `SpawnAnimal(species, pos, 0,
  null)` appears to create a sim agent without activating its brain/behavior loop —
  the animal showed no autonomous behavior at all, which vanilla animals always do. So
  rung (b) never actually tested pathfinding; the spawn harness failed exactly the way
  the probe's own instrumentation warned ("harness failure, NOT a pathfinding
  verdict"). Iteration 2: initialize via the `onCreate` callback / whatever vanilla
  spawn flow uses (compare how the ecosystem sim spawns wild animals), or command a
  naturally-spawned wild animal instead of a mod-spawned one.
- **Q3 — PASS:** district names + positional membership readable from a server mod.
  The survey-drone plan's R12 data half is confirmed feasible on Eco 0.13.
