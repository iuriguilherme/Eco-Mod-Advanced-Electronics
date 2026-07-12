---
title: Server-driven movement in Eco 0.13 mods — what works and what doesn't
date: 2026-07-12
category: best-practices
module: EcoServerMod
problem_type: best_practice
component: tooling
severity: high
applies_when:
  - "Building an Eco (Strange Loop Games) server mod that moves entities (vehicles, drones, NPCs, moving machines)"
  - "Deciding between reusing animal AI and writing custom movement for a mod entity"
  - "Registering recurring server-side callbacks from mod code"
tags: [eco-modding, worldobject, syncphysics, tick, animal-ai, server-mod, movement]
---

# Server-driven movement in Eco 0.13 mods — what works and what doesn't

## Context

The Advanced Electronics survey-drone feasibility spike (branch `feat/drone-feasibility-spike`, live-tested 2026-07-12 on an Eco 0.13.0.4 dedicated server against `Eco.ReferenceAssemblies 0.13.0.4-beta-release-1024`) needed a server mod to move an entity around the world. Three approaches were probed in-game; two common assumptions failed and one path works. Full evidence: `docs/spikes/2026-07-survey-drone-spike.md`.

## Guidance

**Works — WorldObject position sync.** Setting `WorldObject.Position` / `.Rotation` and calling `SyncPositionAndRotation()` from server code renders continuous movement on connected clients. Vanilla precedent: `ElevatorComponent`. This is the proven rendering path for any mod-driven mover.

**Does not work — the mod-facing tick manager.** `IWorldObjectManager.AddToTick(ITickOnDemand)` fires the callback **exactly once** regardless of `NextTickTime` strategy (constant `0`, advancing via `TickStartTime`, and explicit re-registration guarded by `IsQueuedForTick` were all tested — one tick each). Do not build recurring behavior on this surface. For real mods, tick from your own `WorldObjectComponent.Tick()` (the vanilla pattern); a `System.Threading.Timer` works for throwaway probes (no thread-affinity exceptions observed driving `Position` + `SyncPositionAndRotation()` from a 50ms timer thread, but treat that as unconfirmed-safe for production).

**Does not work — puppeteering vanilla animals.** A live-brained animal ignores every external navigation command. Tested levers, all ineffective: `GetPathTo(...)`, `RequestPathAndUpdateState(...)`, `DoServerUpdateAnimalData("Wander", ...)`, writing the `Behavior` field, forcing `NextTick = 0`. The brain's own behavior selection overrides external commands — while brain-driven navigation demonstrably works (an activated animal stares at threats and flees pathing around terrain when shot). Also: `EcoSim.AnimalSim.SpawnAnimal(species, pos, herdID, onCreate)` spawns an **inert** animal (no autonomous behavior) until additional activation; even after activation levers it never accepted external path commands.

**Consequence for mod entity design:** to get pathfinding-driven movement, either subclass `AnimalEntity` and put your logic *inside* a custom behavior (navigation comes from the brain machinery, which works), or use a WorldObject mover with self-written navigation (rendering proven; pathing is on you). There is no lifecycle-free pathfinder: `Eco.Simulation.Agents.Animal` is abstract and all navigation members are instance methods requiring `AnimalSpecies`.

## Why This Matters

Movement is the load-bearing capability for any vehicle/drone/NPC mod, and the obvious-looking APIs (`AddToTick`, animal `GetPathTo`) silently do nothing — a mod built on them compiles cleanly and fails only on a live server. Knowing the one proven path (component tick → `Position` → `SyncPositionAndRotation()`) and the two dead ends saves the multi-iteration live-server debugging loop this spike went through (three deploy/test cycles).

## When to Apply

- Any Eco server mod adding moving entities or recurring server-side behavior
- Architecture decisions between "reuse animal AI" and "custom WorldObject mover"
- Debugging a mod callback that fires once and never again

## Examples

Proven movement step (from the spike's `EcoServerMod/AdvancedElectronics.Spike/SpikeMoveCommand.cs`):

```csharp
// Runs from a tick source (real mod: your WorldObjectComponent.Tick()):
obj.Position = nextPos;                       // System.Numerics.Vector3
obj.Rotation = Quaternion.LookRotation(dir);  // Eco.Shared.Math.Quaternion
obj.SyncPositionAndRotation();                // pushes to clients
```

Dead end (fires once, never re-queued — do not use for recurring work):

```csharp
ServiceHolder<IWorldObjectManager>.Obj.AddToTick(myTickOnDemand); // one tick only
```

Version note: Eco 0.13 uses `System.Numerics.Vector3` (Eco ships only extension helpers; there is no `Eco.Shared.Math.Vector3`), reference assemblies target net10.0, and the game-version pin must match the server build (`Eco.ReferenceAssemblies` prerelease versions embed it, e.g. `0.13.0.4-beta-release-1024`).

## Related

- `docs/spikes/2026-07-survey-drone-spike.md` — full probe protocol, verdicts, and evidence
- `docs/plans/2026-07-11-001-feat-survey-drone-plan.md` — the architecture KTD this learning feeds (AnimalEntity subclass vs self-navigated WorldObject)
- `EcoServerMod/README.md` — version matching and net10.0 setup
