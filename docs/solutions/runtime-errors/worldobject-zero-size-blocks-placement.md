---
title: "A WorldObject prefab with size (0,0,0) throws in the client placement preview and blocks placement"
date: 2026-07-18
category: runtime-errors
module: AdvancedElectronics
problem_type: runtime_error
component: tooling
severity: high
symptoms:
  - "Crafted modded WorldObject shows a placement ghost but the Place action never appears and pressing place does nothing"
  - "No server-side error and no placement attempt in the server log — the failure is entirely client-side"
root_cause: incomplete_setup
resolution_type: code_fix
tags: [eco-modding, worldobject, placement, occupancy, prefab, modkit, client-exception, unity]
related_components: [Assets/Art/AdvancedElectronics, Assets/Art/AdvancedElectronics/Editor]
---

# A WorldObject prefab with size (0,0,0) throws in the client placement preview and blocks placement

## Problem

A custom Eco modded WorldObject (a "Drone Dock") crafted fine and showed a placement ghost,
but could not be placed: the "Place" action never appeared and the place button did nothing,
with no server-side error. The blocker was a single zero-valued field on the client prefab —
`WorldObject.size` was `(0,0,0)` — which makes the client's placement-preview code throw.

## Symptoms

- The placement ghost renders (so the client found the prefab and the naming was correct), but
  the "Place" interaction is never offered and right-clicking to place does nothing.
- No error is shown to the player, and the **server log records no placement attempt** for the
  test window — proof the `Place` RPC never left the client.
- Free-placement float and shift-to-snap behave normally (that part is not the bug — vanilla
  storage-type objects behave the same because none of their components carry
  `[MustBeGridAligned]`).

## What Didn't Work

- **Assuming it was a server-side C# defect.** The object/item C# was compared line-for-line
  against vanilla `StorageChest` (`.references/__core__/AutoGen/WorldObject/StorageChest.cs`)
  and the working reference mods (Animal Husbandry, Mixology, greenleaf Gift Machine) and found
  correct: same components, `IRepresentsItem`, `SideAttachedContext(Down)`, `AddOccupancy` in a
  static constructor. Nothing in the C# explained a silent client-side miss.
- **Suspecting occupancy was unregistered.** `WorldObject.GetOccupancyInfo` falls back to
  `defaultOccupancyInfo`, a single 1×1×1 block (`Server/Eco.Gameplay/Objects/WorldObject.cs`
  in the game source), so occupancy is never empty even if the static constructor never ran —
  ruling this out as the blocker.
- **Suspecting a server-side occupancy rejection.** `SideAttachedContext.CanPlaceObject` calls
  `player.ErrorLoc(...)` on failure (game source `Server/Eco.Gameplay/Occupancy/OccupancyContext.cs`),
  so a server rejection would have printed a message. The player saw none, and the server log showed
  no attempt — the failure was upstream, on the client.

## Solution

Set the prefab's `WorldObject.size` to the object's block footprint (`(1,1,1)` for a
single-block object), and harden the prefab-finishing tool so it can never leave `size` at
zero again.

Prefab change (`Assets/Art/AdvancedElectronics/Prefabs/DroneDockObject.prefab` and
`SurveyDroneObject.prefab`), on the `WorldObject` component:

```yaml
# before
size: {x: 0, y: 0, z: 0}
# after
size: {x: 1, y: 1, z: 1}
```

Tool change (`Assets/Art/AdvancedElectronics/Editor/AdvancedElectronicsBuildTools.cs`), so the
scripted keyboard workflow derives `size` from the encapsulating renderer bounds whenever it is
zero (ceil to whole blocks, minimum 1):

```csharp
if (worldObject.size == Vector3.zero)
{
    var sizeBounds = new Bounds(go.transform.position, Vector3.zero);
    foreach (var renderer in go.GetComponentsInChildren<Renderer>())
        sizeBounds.Encapsulate(renderer.bounds);
    worldObject.size = new Vector3(
        Mathf.Max(1, Mathf.CeilToInt(sizeBounds.size.x)),
        Mathf.Max(1, Mathf.CeilToInt(sizeBounds.size.y)),
        Mathf.Max(1, Mathf.CeilToInt(sizeBounds.size.z)));
}
```

Then reimport the prefabs, rebuild the asset bundle, and redeploy it (client bundle only — no
server DLL change was needed). Confirmed on a live server: the "Place" action appeared, the
dock placed on the ground, and both the placed dock and its spawned drone were interactable.

## Why This Works

`WorldObject.size` is a plain serialized prefab field (`Assets/EcoModKit/Scripts/WorldObject.cs`
declares `public Vector3 size;`) with no runtime or build-time derivation — neither the ModKit's
own `WorldObjectSetup` editor tool, the `ModKitTools` bundle builder, nor any client `Awake`/
`OnValidate` sets it. A prefab produced by scripted tooling that omits it stays at Unity's
default of zero.

The client builds its placement-preview occupancy cells by iterating that field. In the game
client source (`Client/Assets/Scripts/Player/WorldObjectPlacementPreviewer.cs`):

```csharp
var sizeIteratedList = (isItem ? Vector3i.One : worldObj.size).ConvertI().XYZIter().ToList();
```

With `size == (0,0,0)` this list is **empty**. When the object declares a `Down` attach
requirement (via the item's `SideAttachedContext(DirectionAxisFlags.Down, ...)`), the preview
runs `CheckRequiredAttachedSideRequirements`, which calls `GetFurthestPositions(sizeXYZIter,
Down)`; that method does `positions.Min(p => p.y)` on the cell list. `.Min()` on an empty
sequence throws `InvalidOperationException("Sequence contains no elements")`. The exception
aborts placement-preview evaluation, so the `Place` interaction is never produced, nothing is
sent to the server, and no error surfaces — exactly the observed "ghost shows, Place never
completes, no error" signature.

`size = (1,1,1)` yields a single occupancy cell `(0,0,0)`, so `GetFurthestPositions` returns
that cell instead of throwing, the `Down`-attach check reads the block below, and placement
completes.

## Prevention

- For any custom placeable WorldObject whose prefab is **not** produced by the ModKit's own
  `WorldObjectSetup` tool, verify `WorldObject.size` is non-zero and matches the block
  footprint before building the bundle. Zero is the silent-failure default.
- Have scripted prefab tooling set `size` from renderer bounds (as above) so it can't regress.
- Add `size != (0,0,0)` to the review checklist for modded placeable objects, alongside the
  naming triad and `AddOccupancy` registration.
- Distinguish the two placement failure signatures: **no ghost at all** points at the
  item/object/prefab naming triad; **ghost shows but Place never completes with no error**
  points at a client-side preview exception, of which zero `size` is the first suspect.

## Related

- `docs/solutions/conventions/eco-custom-worldobject-placement-requirements.md` — the naming
  triad, `AddOccupancy`, namespace, and occupancy-component requirements; the sibling "object
  crafts but won't place" learning whose fix (naming) precedes this one. That doc's Status
  section records this same live confirmation.
- `docs/solutions/best-practices/eco-013-server-driven-movement.md` — the
  `WorldObjectManager.ForceAdd` spawn path for a modded WorldObject.
