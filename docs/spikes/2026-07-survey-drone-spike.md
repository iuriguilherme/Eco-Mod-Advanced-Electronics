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
4. Obstacle sub-check: build a 2-block wall between animal and target, re-run, record
   whether the trace routes around it.

| Question half | Verdict (pass/partial/fail) | Evidence |
|---|---|---|
| Pathfinding callable without animal lifecycle | **fail (compile-time)** | Rung (a) evidence above |
| Spawned animal paths to commanded target | _blank_ | |
| Path avoids player-built obstacles | _blank_ | |

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

1. Run `/spike move` — a campfire spawns ~4 blocks away and circles.
2. Observe at walk-speed (default): smooth glide vs. visible stepping/teleporting.
3. Run `/spike stop`, then `/spike move 20` — fast movement; record snap/teleport behavior.
4. Observe from >25m away (SyncPhysics kinematic-fallback distance from client evidence).
5. Record server console errors (world-state complaints about occupancy/collision count
   as evidence, not failure — note them).

| Question half | Verdict (pass/partial/fail) | Evidence |
|---|---|---|
| Motion smoothness at walk speed | _blank_ | |
| Behavior at high speed / >25m distance | _blank_ | |
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
| Mod reads district data (names, membership at position) | _blank_ | |
| District picker on a WorldObject UI | **partial (assembly-evidence only), cap** | No live picker demonstrated; 0.11 attribute gone in 0.13 |

**Implication for survey-drone plan:** data-half pass ⇒ R12's district assignment is
feasible server-side; the picker UI remains a spike-class risk on R12 — a chat-command
assignment fallback works today if the picker mechanism doesn't materialize.

---

## Closing checklist (after the live runs)

- [ ] Fill every blank verdict above with pass/partial/fail + evidence.
- [ ] Update `docs/plans/2026-07-11-001-feat-survey-drone-plan.md` Dependencies /
      Assumptions with these verdicts (R1 rewording per Q1; R16 rendering path per Q2;
      R12 risk note per Q3).
- [ ] The origin plan's spike gate clears when all answerable halves are recorded — the
      two structurally-open items (animation state hooks; live district picker) carry
      into planning as open questions, not gate blockers.
- [ ] Note the game-version context in the origin plan: all evidence is against Eco
      0.13.0.4 (`Eco.ReferenceAssemblies 0.13.0.4-beta-release-1024`).
