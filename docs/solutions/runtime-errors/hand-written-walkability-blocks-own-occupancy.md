---
title: "Hand-written walkability made a modded entity's own occupancy block impassable, so pathfinding never returned a path"
date: 2026-07-20
last_updated: 2026-08-16
category: runtime-errors
module: EcoServerMod
problem_type: runtime_error
component: tooling
severity: critical
symptoms:
  - "A modded WorldObject with self-written pathfinding never moves; every dispatch immediately reports the no-path/unreachable state"
  - "Failure is independent of distance, biome and terrain -- 20 metres and 2000 metres fail identically, grass and bare desert fail identically"
  - "No exceptions in the server log; the mod loads and ticks cleanly"
root_cause: wrong_api
resolution_type: code_fix
tags: [eco-modding, pathfinding, walkability, block-attributes, occupancy, world-api, server-mod]
related_components: [EcoServerMod/AdvancedElectronics, EcoServerMod/AdvancedElectronics.Navigation]
---

# Hand-written walkability made a modded entity's own occupancy block impassable, so pathfinding never returned a path

## Problem

A survey drone with self-written A* pathfinding never moved. Every dispatch ended in the
`Unreachable` state. The world-sampling predicate behind the pathfinder decided
"is this column blocked?" with a hand-invented rule instead of the engine's own definition
of walkability, and that rule classified the drone's own position as solid terrain.

## Symptoms

- The lifecycle status reports `Unreachable` immediately after every dispatch; the mover
  stays `stationary`.
- Every other layer looks healthy: the object places, the drone spawns, ownership is
  stamped, the district assignment resolves, components tick.
- **The failure is invariant.** Distance does not matter (20m fails exactly like 2000m),
  and terrain does not matter (thick grass fails exactly like bare desert). That
  invariance is the signature: a *routing* problem varies with geometry, a *predicate*
  problem does not.
- Nothing in the server log — no exception, because nothing throws.

**If your `Unreachable` is NOT invariant, this is the wrong doc.** Some destinations working
and others not means the block predicate is fine, and there are three other places the fault
can be:

- **The geometry genuinely blocks that route.** Nothing is broken.
- **The movement model wrongly says it does.** The predicate is right, but a locomotion
  constraint above it — how far the entity may rise, what shape its route takes — is modelled
  for the wrong kind of body. The tell is *directional*: the same trip succeeds outbound and
  fails on the return, or a destination becomes unreachable only after the drone reshapes the
  terrain around it. See
  `docs/solutions/logic-errors/the-pathfinder-modelled-a-flying-drone-as-a-walker.md`.
- **The lifecycle mishandles a legitimate no-path result.** The drone hovers rather than
  sitting still, and its job ledger freezes at zero worked and zero skipped while the lifecycle
  claims to have skipped a plot. See
  `docs/solutions/logic-errors/a-recovery-path-that-cannot-fire-in-the-state-it-exists-for.md`.

## What Didn't Work

Three wrong root causes were pursued before the real one, each costing a live test:

- **"Implement the drone as an animal."** A misread of the instruction to study how animals
  pathfind. A spike proved vanilla animals cannot be externally puppeteered — true, and
  irrelevant. The instruction meant *read the animal pathfinding code as the reference
  implementation*, which is exactly where the answer was.
- **"The destination search radius is too small."** The destination finder did have a
  hard 96-unit cap, and removing it was a real improvement — but the user disproved it
  directly by placing the dock 20 metres from the district and still getting
  `Unreachable`.
- **"Vegetation reads as solid."** Correct as far as it went (grass is a non-empty block,
  and the predicate called every non-empty block solid), but the user disproved it as the
  whole story by testing in open desert where nothing grows.

Each hypothesis was plausible and none was reached by tracing the failing call. The
invariance of the symptom was visible from the first report and pointed at the predicate
the entire time.

## Solution

Replace the invented predicate with the engine's own definition, and separate terrain
blocking from object occupancy.

The engine states its rule in `Eco.Simulation`'s pathfinding (`PackedPathNode.IsPathable`),
including the comment *"Walkable blocks are the first empty block above a solid block,
where there are two empty blocks above it (or plants)"*. It tests `Is<Solid>()` and
`Is<Occupied>()` — never "is not empty":

```csharp
var block = World.GetBlock(pos);
if (block.Is<Solid>() || block.Is<Occupied>()) return false;   // room to stand
var underblock = World.GetBlock(pos.AddY(-1));
if (!underblock.Is<Solid>() && !underblock.Is<UnderWater>()) return false;  // ground beneath
if (underblock.Is<Constructed>()) return false;                // animals stay off player floors
var overBlock = World.GetBlock(pos.AddY(1));
if (overBlock.Is<Solid>() || overBlock.Is<Occupied>()) return false;        // headroom
if (block.Is<UnderWater>() && overBlock.Is<UnderWater>()) return false;     // only the top of water
```

Two of those six tests are **not** mirrored by this mod, deliberately. `EcoWorldSampler` refuses
the `Constructed` rejection — that test exists to keep *animals* off player-built floors, and a
drone is a machine that must be able to cross a road, not least because a dock may stand on one.
The water rule is likewise not carried across. Both divergences are about what kind of entity is
moving, which is the boundary the Prevention section below draws.

Before — a guess, in the mod's own sampler:

```csharp
var above = World.GetBlock(new Vector3i(x, groundY + 1, z));
return above == null || !above.Is<Empty>();   // ANY non-empty block counts as solid
```

After — mirroring the engine, with occupancy split out:

```csharp
// IsSolidAt: terrain geometry only. Plants are walkable, exactly as the engine treats them.
var block = World.GetBlock(stand);
if (block != null && block.Is<Solid>()) return true;                 // no room to stand
var under = World.GetBlock(stand + Vector3i.Down);
if (under == null || (!under.Is<Solid>() && !under.Is<UnderWater>())) return true;
var over = World.GetBlock(stand + Vector3i.Up);
if (over != null && over.Is<Solid>()) return true;                   // no headroom
return false;

// IsObstacleAt: placed objects, read from the Occupied attribute their blocks carry.
```

Splitting them is load-bearing: the pathfinder exempts its start and goal columns from the
*obstacle* predicate (an entity always occupies its own column, and a dock leg must path
into the dock), but never from the *solidity* predicate. Occupancy folded into solidity is
therefore unwaivable.

## Why This Works

Placing a WorldObject writes blocks into its occupancy footprint
(`World.SetBlock(typeof(WorldObjectBlock), worldPos, obj)`), and `WorldObjectBlock` is
declared `[Serialized, Transient, Occupied]` — **not** `Empty`. The drone stands inside its
own occupancy block.

So `!above.Is<Empty>()` returned true for the drone's own column, and the pathfinder's
first guard bailed before expanding a single node:

```csharp
if (_sampler.IsSolidAt(startColumn...) || _sampler.IsSolidAt(goalColumn...))
    return PathResult.NotFound;
```

Every dispatch failed at that line, in any biome, at any distance — which is precisely why
the symptom was invariant. Vegetation was a second instance of the same defective rule
(grass is also non-empty), which is why a grass-only explanation looked convincing and
still failed the desert test.

Testing `Is<Solid>()` fixes both instances at once: neither a plant nor a `WorldObjectBlock`
is `Solid`, so the start column is walkable, while genuine terrain still blocks.

## Prevention

- **When the host engine already implements a predicate, mirror it rather than inventing
  one.** Walkability, reachability, and placement validity are engine semantics, not
  intuitions. Find the engine's own implementation and copy its tests; a plausible-sounding
  reimplementation ("solid means not empty") will diverge on exactly the cases that matter.
- **But mirror it for world facts only, never for locomotion.** `IsPathable` answers two
  different kinds of question in one predicate. *What is this block* — `Is<Solid>`,
  `Is<Occupied>` — is a property of the world and is true for any entity, and copying it is
  what this entry is about. *How may a body move through it* — ground beneath, headroom, and
  in the sibling `CalcMovability`, how far it may rise — is a property of a **biped**, because
  the engine only ever wrote this for animals. Copying that half silently imports a walker's
  locomotion, which is how a flying drone ended up limited to a one-block step and unable to
  re-enter its own excavation. See
  `docs/solutions/logic-errors/the-pathfinder-modelled-a-flying-drone-as-a-walker.md`. Footprint
  and route shape the engine does not model at all, so those were never available to copy.
- **An entity occupies its own position.** Any self-authored spatial query — pathfinding,
  obstacle checks, line of sight — must decide what happens when the query lands on the
  asking entity. Start/goal exemption is one answer; an ignore-set is another; silently
  treating yourself as an obstacle is the bug.
- **Treat symptom invariance as evidence about the layer.** If a failure does not vary with
  distance, terrain, or scale, it is not in the logic that consumes those variables. That
  observation alone excluded routing and pointed at the predicate.
- **State the semantic contract at the interface, not just the implementation.** The
  sampler interface now spells out that vegetation is walkable and that "blocks passage"
  means geometry, so a future implementation cannot quietly reintroduce the old rule. A
  regression test pins it.

## Related

- `docs/solutions/best-practices/eco-013-server-driven-movement.md` — the movement/tick
  surface this pathfinder drives.
- `docs/solutions/workflow-issues/tracing-beats-theorising-on-invariant-failures.md` — the
  diagnosis-process learning from the same three wrong hypotheses.
- `docs/solutions/conventions/eco-custom-worldobject-placement-requirements.md` — the
  occupancy/placement contract; `WorldObjectBlock` and the `Occupied` attribute come from
  the same mechanism.
- `docs/solutions/logic-errors/a-recovery-path-that-cannot-fire-in-the-state-it-exists-for.md` —
  the other way a drone ends up reporting `Unreachable`. There the pathfinder is correct and
  the no-path result is legitimate; the lifecycle simply has no working exit from the state,
  so the drone loops instead of retiring the plot. Separated by invariance: this doc's failure
  never varies with geometry, that one's happens only for particular destinations.
- `docs/solutions/logic-errors/the-pathfinder-modelled-a-flying-drone-as-a-walker.md` — the
  third cause, and the one that scopes this entry's headline rule. There the block predicate is
  correct and the *locomotion model* above it is wrong: a flying drone constrained by a walker's
  one-block step. It is also the reason this entry's mirror-the-engine advice now carries a
  boundary — the engine's predicate is a walking predicate, and only its world half is safe to
  copy.
