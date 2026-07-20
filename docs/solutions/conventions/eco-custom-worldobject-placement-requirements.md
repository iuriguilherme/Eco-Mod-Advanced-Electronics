---
title: Custom Eco WorldObject requirements the stripped reference assemblies hide
date: 2026-07-18
category: conventions
module: EcoServerMod
problem_type: convention
component: tooling
severity: high
applies_when:
  - "Authoring a new placeable WorldObject in an Eco mod (server C# class + client prefab)"
  - "A modded object crafts fine but cannot be placed in the world (no placement ghost, no server error)"
  - "Reasoning about Eco mod object APIs from Eco.ReferenceAssemblies, whose method bodies are stripped"
tags: [eco-modding, worldobject, placement, occupancy, naming-convention, server-mod, modkit]
related_components: [EcoServerMod/AdvancedElectronics, Assets/Art/AdvancedElectronics]
---

# Custom Eco WorldObject requirements the stripped reference assemblies hide

## Context

A hand-written modded WorldObject (a "Drone Dock") crafted correctly — the item appeared
in inventory with its icon — but **could not be placed in the world**: aiming at open
ground produced no placement ghost and no server-side error. Several rounds of fixes based
on reasoning from `Eco.ReferenceAssemblies` (whose method bodies are stripped) and on
diffing against vanilla `Mods/__core__/AutoGen/WorldObject/*.cs` did not resolve it. The
requirements that actually govern placement are invisible in both of those sources — vanilla
objects get them from a code generator, and the reference assemblies carry only signatures.
The definitive answers came from the **game client source** and from **complete working
third-party mods** (Advanced Mixology, Animal Husbandry, Nuclear Reactor, The Orrery).

## Guidance

A placeable custom Eco WorldObject must satisfy a **naming triad** plus a set of
registration requirements. The triad is the one that silently blocks placement:

1. **The item/object/prefab naming triad.** For a feature named `X`:
   - server item class `XItem : WorldObjectItem<XObject>`
   - server object class `XObject : WorldObject`
   - client prefab (and its root GameObject) named exactly `XObject`

   The client derives the placeable object's name from the item by a **hardcoded string
   rule**: strip the trailing `Item`, append `Object`. So `DroneDockItem` resolves to
   `DroneDockObject`. If the object class / prefab is not named `<item-without-Item>Object`,
   the client's lookup returns nothing and the placement interaction is never offered —
   no ghost, no error, just a silent miss.

2. **Register the object's placement footprint in code** via
   `WorldObject.AddOccupancy<XObject>(List<BlockOccupancy>)` in the object's **static
   constructor**. Vanilla objects get this from a generated file
   (`WorldObjectOccupancyAutoGen.cs`), so it never appears in their visible source; a
   hand-written mod object has no generator and must register its own occupancy or it has
   no footprint to validate a placement against.

3. **Namespace `Eco.Mods.TechTree`.** Every vanilla object and every working reference mod
   places its object classes there. (Items register regardless of namespace — crafting
   worked from a custom namespace — but conform the objects to `Eco.Mods.TechTree` to match
   every working example.)

4. **The item declares an occupancy context and the object requires the occupancy
   component:** `XItem` overrides
   `GetOccupancyContext => new SideAttachedContext(DirectionAxisFlags.Down, WorldObject.GetOccupancyInfo(this.WorldObjectType))`,
   and `XObject` carries `[RequireComponent(typeof(OccupancyRequirementComponent))]` and
   implements `IRepresentsItem` (`RepresentedItemType => typeof(XItem)`). Use the single-arg
   `[RequireComponent(typeof(T))]` form for the ordinary case.

   **Correction (2026-07-20):** an earlier version of this doc claimed the two-arg form "is not
   used anywhere". That is wrong. The second argument is a *component name*, used to attach two
   components of the same type to one object and tell them apart — see
   `Server/Mods/__core__/Objects/CivicsObjects.cs:81`:
   `[RequireComponent(typeof(CivicObjectComponent), "Laws"), RequireComponent(typeof(CivicObjectComponent), "Injunctions")]`.
   It pairs with `WorldObjectComponent.Name` ("An optional identifier for specifying between
   components of the same type"). Passing `null` is pointless, which is what the original
   observation was really about; passing a name is a supported pattern.

5. **Every custom `WorldObjectComponent` subclass must carry `[Serialized]` and
   `[NoIcon]` (or `[HasIcon]`).** The base class is marked `[DerivedMustDefineIcon]`,
   and the view encoder only encodes types registered via `[Serialized]`. A component
   missing these loads fine and even runs its `Tick()`, but the moment a player opens
   the owning object's window the server logs
   `Component X has to explicitly define [HasIcon] or [NoIcon]` and
   `Can't encode instance of type 'X'` — and the **entire window renders empty**
   (including sibling parts like the storage grid). `NoIcon` lives in
   `Eco.Core.Controller`. Every vanilla component ships both attributes.

   The general lesson: when conforming a mod class to the working pattern, audit **all**
   custom classes deriving Eco base types in one pass (objects, items, recipes,
   components, commands) — fixing only the class the last live test complained about
   costs one full server restart per omission.

## Why This Matters

None of these requirements are discoverable from the two sources a modder naturally reaches
for. `Eco.ReferenceAssemblies` ships with method bodies stripped, so it reveals signatures
but not the naming rule, the occupancy-registration expectation, or component-attachment
behavior. Vanilla objects in `Mods/__core__/AutoGen/` are **generated** — their occupancy
lives in a separate generated file and their naming is a generator invariant, so reading
them does not reveal that a *hand-written* object must reproduce those pieces itself. A modder
who reasons only from those two sources produces an object that crafts but cannot be placed,
with no error to debug. The naming triad in particular fails as a silent dictionary miss, the
hardest kind of failure to diagnose without the client source.

## When to Apply

- Any new placeable WorldObject in an Eco mod, before the first in-game placement test.
- When reviewing a modded object diff: check the `XItem`/`XObject`/prefab-`XObject` names
  line up, that `AddOccupancy<XObject>` is registered, and that the object is in
  `Eco.Mods.TechTree` — a diff against a *complete working mod* (not the stripped assemblies
  or the generated vanilla source) is the reliable review gate.
- Whenever an Eco mod object "crafts but won't place with no error," suspect the naming triad
  first.

## Examples

The client's hardcoded name derivation (the proven root cause), from the game source at
`Eco/Client/Assets/Scripts/Mods/ItemInfoExtensions.cs`:

```csharp
/// <summary>Strip "Item" off the name and replace with Object</summary>
public static string GetWorldObjectName(this ItemInfoView itemInfo)
{
    var name = ServerName(itemInfo);
    return name.Substring(0, name.Length - 4) + "Object";  // "DroneDockItem" -> "DroneDockObject"
}
```

Before (crafts, silently unplaceable): server class `DroneDock : WorldObject`, item
`DroneDockItem : WorldObjectItem<DroneDock>`, prefab named `DroneDock`. The client looked up
`DroneDockObject`, found nothing, and never offered placement.

After (conformant), in `EcoServerMod/AdvancedElectronics/DroneDock.cs`:

```csharp
namespace Eco.Mods.TechTree
{
    [Serialized]
    [RequireComponent(typeof(PropertyAuthComponent))]
    [RequireComponent(typeof(PublicStorageComponent))]
    [RequireComponent(typeof(OccupancyRequirementComponent))]
    [Tag("Usable")]
    public class DroneDockObject : WorldObject, IRepresentsItem
    {
        public virtual Type RepresentedItemType => typeof(DroneDockItem);

        static DroneDockObject()
        {
            AddOccupancy<DroneDockObject>(new List<BlockOccupancy>
            {
                new BlockOccupancy(new Vector3i(0, 0, 0)),   // 1x1x1 footprint
            });
        }
        // ...
    }

    public class DroneDockItem : WorldObjectItem<DroneDockObject>
    {
        protected override OccupancyContext GetOccupancyContext =>
            new SideAttachedContext(0 | DirectionAxisFlags.Down, WorldObject.GetOccupancyInfo(this.WorldObjectType));
    }
}
```

The client prefab and its root GameObject were renamed to `DroneDockObject` to match (see
`Assets/Art/AdvancedElectronics/DroneDockObject.prefab`).

## Status

The **naming triad is proven** from the game client source above and is the confirmed reason
placement produced no ghost. The full requirement set was derived by diffing against complete
working mods.

After the naming fix, a **second, distinct blocker** surfaced: the placement ghost now
appeared but the object could not be placed — the "Place" action never showed and the place
button did nothing, with no server-side error (the server log for the test window recorded no
placement attempt at all). Root cause, proven from the client source: the client prefab's
`WorldObject.size` was `(0,0,0)`. The client builds its placement-preview occupancy cells by
iterating that field — `WorldObjectPlacementPreviewer.cs` does
`worldObj.size.ConvertI().XYZIter()` — so a zero size produces **zero** cells. The
`SideAttachedContext(Down)` requirement then runs `CheckRequiredAttachedSideRequirements`,
whose `GetFurthestPositions` calls `positions.Min(p => p.y)` on that empty cell list, which
**throws** ("Sequence contains no elements"). The exception aborts placement-preview
evaluation, so the interaction is never produced and nothing is sent to the server — exactly
the "ghost shows, Place never completes, no error" symptom.

`WorldObject.size` is a plain serialized prefab field; nothing sets it at bundle-build or
runtime (neither the ModKit `WorldObjectSetup` tool nor `ModKitTools` does), so a prefab built
by scripted tooling that omits it stays at Unity's default of zero. Fix: set `size` to the
object's block footprint (`(1,1,1)` for a single-block object) in the prefab, and have the
prefab-finishing tool derive it from the encapsulating renderer bounds (ceil to whole blocks,
min 1) so it cannot regress. Add `size` to the review checklist for any custom placeable whose
prefab is produced without the ModKit's own setup tool.

**Placement specifically was confirmed on a live server (2026-07-18):** after setting `size`
to `(1,1,1)` on both prefabs and redeploying the rebuilt bundle, the Drone Dock's "Place"
action appeared, the object placed on the ground, and the placed dock and its spawned Survey
Drone were both interactable (`Take Survey Drone` / `Use`, object UI opens). The two
placement blockers this doc covers — the naming triad and the zero `size` — are the parts
that are proven.

**Scope of that claim:** it covers *placement and interaction only*. The feature these
objects belong to is not complete, and later sessions found further blockers beyond
placement (pathfinding, survey delivery). Do not read this section as evidence that the
surrounding feature works.

## Related

- `docs/solutions/runtime-errors/worldobject-zero-size-blocks-placement.md` — the second, distinct blocker documented on its own: a prefab `WorldObject.size` of `(0,0,0)` throws in the client placement preview so the "Place" action never completes (the "ghost shows but won't place" signature, versus this doc's "no ghost at all").
- `docs/solutions/best-practices/eco-013-server-driven-movement.md` — movement/tick surface for a modded WorldObject (a sibling "the stripped assemblies hide the truth" learning).
- `docs/solutions/best-practices/eco-013-reading-district-civics-data.md` — district/civics reads from a modded object.
- `docs/solutions/conventions/consistent-grid-column-quantization.md` — another convention captured for this mod.
