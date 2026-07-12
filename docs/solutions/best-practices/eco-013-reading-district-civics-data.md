---
title: Reading district / civics data from an Eco 0.13 server mod
date: 2026-07-12
category: best-practices
module: EcoServerMod
problem_type: best_practice
component: tooling
severity: medium
applies_when:
  - "An Eco 0.13 server mod needs to know which district / settlement / claim a world position belongs to"
  - "Mapping a player-drawn map area (law district) to server-side geometry for mod logic"
  - "Porting a mod that used the 0.11-era districts API to 0.13"
tags: [eco-modding, districts, settlements, civics, deed, server-mod, worldposition]
---

# Reading district / civics data from an Eco 0.13 server mod

## Context

The Advanced Electronics survey-drone spike needed a server mod to resolve "which district is this world position in?" so a drone could be scoped to a player-drawn map area. Planning research (against docs.play.eco) assumed the 0.11-era model where "districts" had folded into `Settlement`; the actual 0.13.0.4 assemblies keep districts as a first-class civics type. This doc records the real 0.13 read surface, verified by reflection dump against `Eco.ReferenceAssemblies 0.13.0.4-beta-release-1024` and by `EcoServerMod/AdvancedElectronics.Spike/SpikeDistrictsCommand.cs` compiling green against it.

## Guidance

**Districts are first-class in 0.13**, under `Eco.Gameplay.Civics.Districts`:

- `DistrictMap` — the registrar-managed container. Key members: `GetDistrictAtWorldPos(WorldPosition2i)` and `GetDistrictAtPlotPos(PlotPos)` for point membership, a `Districts` dictionary, `GetDistrictByID(int)`, and a `DistrictsUpdatedEvent` to react to edits.
- `Eco.Gameplay.LegislationSystem.District` — the individual district entry (`Name`, `Color`, `ContainingMap`).
- `Eco.Gameplay.Civics.Districts.DistrictUtils` — static helpers: `BelongsToDistrict(Vector3i, District)`, `BelongsToDistrict(Vector2i, District)`, `GetAllDeeds(districts, DeedRelationToDistrict)`.

**Enumerate the maps through the registrar**, not a singleton: `Registrars.Get<DistrictMap>()` (`Eco.Core.Systems.Registrars`). The same pattern reads `Settlement` (`Registrars.Get<Settlement>()`, `Eco.Gameplay.Settlements`) and `Deed` (`Registrars.Get<Deed>()`, claim areas).

**Enumerate every area-shaped registry when the goal is "does any area cover P?"** — districts, settlements, and deeds are separate registries; a query against one cannot reveal an artifact in another, and empty output from a Settlement-only query is indistinguishable from "no area exists." (This exact trap was a review finding on the spike.)

## Why This Matters

The planning research (docs.play.eco, ~12 months stale on civics) said districts had been absorbed into settlements — wrong for 0.13. A mod written to that assumption would query `Settlement` and silently find nothing where a district exists. Reading the real registry (`DistrictMap` via `Registrars`) is the difference between district-scoped mod logic working and failing invisibly. It also confirms the survey drone's R12 (district assignment) is feasible server-side — the data half is a solved read.

## When to Apply

- Any Eco 0.13 server mod scoping behavior to a player-drawn area (survey zones, restricted regions, area-triggered effects).
- Whenever an external API doc for Eco civics is more than a few months old — verify class names against the restored reference assemblies, because the civics model has churned across versions.

## Examples

Point membership + full enumeration (from `EcoServerMod/AdvancedElectronics.Spike/SpikeDistrictsCommand.cs`):

```csharp
using Eco.Core.Systems;                       // Registrars
using Eco.Gameplay.Civics.Districts;          // DistrictMap
using Eco.Gameplay.LegislationSystem;         // District
using Eco.Shared.Math;                        // WorldPosition2i

var pos2i = new WorldPosition2i((int)pos.X, (int)pos.Z);
foreach (var map in Registrars.Get<DistrictMap>())
{
    District here = map.GetDistrictAtWorldPos(pos2i);   // null = not in any district on this map
    // here?.Name gives the drawn district's name
}
```

What NOT to assume: there is no live-verified on-object *picker* (choosing a district from a WorldObject's auto-generated UI) — the 0.11-era `ClientCanSelectAndAdd` attribute is gone in 0.13, and no replacement was confirmed. For district *assignment*, a chat command that resolves a `DistrictMap` entry by name is the working fallback; reading is solved, picking is not.

## Related

- `docs/solutions/best-practices/eco-013-server-driven-movement.md` — sibling Eco 0.13 API learning (movement, tick surface, version pin, `Vector3`).
- `docs/spikes/2026-07-survey-drone-spike.md` — Q3 (district read) verdict and the manual protocol that confirmed point membership in-game.
- `EcoServerMod/AdvancedElectronics.Spike/SpikeDistrictsCommand.cs` — the compiling reference implementation.
