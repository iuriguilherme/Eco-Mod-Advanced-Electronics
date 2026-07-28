---
title: "Placed mod objects are invisible until you walk away and come back"
date: 2026-07-28
category: ui-bugs
module: Assets
problem_type: ui_bug
component: tooling
severity: high
symptoms:
  - "A placed world object does not render and cannot be interacted with, but its tooltip lists it as present and gives its distance"
  - "The object appears only after leaving the area and returning, i.e. after the client re-streams the chunk"
  - "Client log repeats: Loaded objects should start as DISABLED, but object X in bundle Y is not"
  - "Spawned objects (drones) and hand-placed objects (a dock) are both affected"
root_cause: configuration
resolution_type: config_change
tags: [eco-modding, modkit, asset-bundle, prefab, world-object, rendering, unity]
related_components: [Assets/Art/AdvancedElectronics, EcoServerMod/AdvancedElectronics]
---

# Placed mod objects are invisible until you walk away and come back

> **Path convention.** Paths beginning `Assets/` or `docs/` are in-repo. Paths beginning `Client/`
> refer to the Eco game source in the local Eco checkout, external to this repo.

## Problem

Mod world objects — a hand-placed Drone Dock and server-spawned Survey Drones — rendered
intermittently. The server knew about them the whole time; only the client's copy was missing, and
re-streaming the chunk fixed it.

## Symptoms

The decisive observation came from live play, not from a log:

> "the phantom drones were not visible at first but when I moved away from that area and came back I
> could see them. the same happened to one drone dock that was already in the world. the tooltip
> shows the drone dock is in that location but I couldn't see or interact with it."

The hover tooltip listed **five Drone Docks in the world, the nearest 4.1 m away**, while nothing was
drawn at that spot. Server state was correct; client rendering was not.

The client had been reporting the cause on every single load, once per bundled object:

```
Loaded objects should start as DISABLED, but object "DroneDock" in bundle
"...AdvancedElectronics.unity3d" is not. This can cause many problems, you should
disable the object by default.
```

Three objects were named: `DroneDock`, `SurveyDrone`, `SurveyDroneItem`.

## What Didn't Work

**Treating the warning as Unity noise.** It appeared in every log read during an unrelated UI
investigation and was filtered out each time as harmless boilerplate, because it names no exception
and nothing visibly fails at load. It was the answer for hours before it was read as one.

**Blaming object lifecycle.** An earlier guess was that a `[RequireComponent]` added and then removed
between builds had orphaned the objects, so they failed to deserialize and were dropped. The user
refuted it directly — the phantom drones predated that change — and the hypothesis was abandoned
rather than patched.

**Assuming the scene objects were at fault.** The obvious reading of "objects should ship disabled"
is that the scene GameObjects are active. They are, but that is not the defect:
`ModKitTools.BuildSceneBundle` already deactivates every scene root before building and restores the
prior state afterwards. Checking that before changing anything narrowed the fix to the **prefab
assets**, which are what actually get bundled.

## Solution

Ship every bundled object's root GameObject **disabled**, in the prefab asset.

Two changes in `Assets/Art/AdvancedElectronics/Editor/AdvancedElectronicsBuildTools.cs`:

**1. Stop the prefab finisher from reintroducing it.** Toggle around the save so the author's scene
object is left as they had it:

```csharp
var wasActive = go.activeSelf;
go.SetActive(false);
var prefab = PrefabUtility.SaveAsPrefabAsset(go, path, out var success);
go.SetActive(wasActive);
```

**2. Fix the assets already on disk** with a one-shot menu command
(`Eco Tools/Advanced Electronics/Disable Mod Object Roots`) that loads each prefab under the art
folder, disables its root, and saves — plus any scene GameObject tagged `ModObject`:

```csharp
var instance = PrefabUtility.LoadPrefabContents(path);
instance.SetActive(false);
PrefabUtility.SaveAsPrefabAsset(instance, path);
PrefabUtility.UnloadPrefabContents(instance);
```

Then rebuild the bundle (`Eco Tools > Mod Kit > Build Current Bundle`) and copy the `.unity3d` to the
server's mod folder. **Both halves are required** — the code change alone does nothing until a bundle
is rebuilt from the corrected prefabs.

Watch for a **second copy at scene root**. Alongside the properly-parented `SurveyDroneItem` under
the disabled `Items` root sat a stray duplicate at scene root, still active and untagged — which is
why it was named in the warning but missed by a tag-based sweep. Item objects are not tagged
`ModObject`, so a `ModObject`-only pass will not catch them.

Verified live by the user: *"the phantom drones and docks are there from the start."*

## Why This Works

The client treats a bundled object as an **inactive template**: it holds the prefab, then
instantiates and enables a copy per world object the server tells it about. Shipping the template
already enabled means the template itself is live, and per-instance activation misbehaves — which is
why the object appears only when the chunk is re-streamed and the instantiation path runs again.

The tooltip listing the object while nothing renders is the signature worth remembering: it splits
the problem cleanly. Tooltip data comes from server state, so a listed-but-invisible object means the
server is right and the fault is entirely in client-side instantiation.

## Prevention

- **Read the "should start as DISABLED" warning as a defect, not noise.** It is the only signal the
  client gives, it names the offending object, and it costs nothing to check.
- **Keep the fix in the tool, not in a checklist.** The finisher now saves disabled, so a future
  prefab cannot reintroduce the bug by being built the normal way.
- **After changing prefabs, rebuild the bundle before testing.** A prefab-only change is invisible to
  the server; see `docs/solutions/workflow-issues/release-scripts-should-refuse-not-warn.md` for the
  matching staleness trap on the packaging side.
- **When an object is present server-side but absent client-side, suspect the bundle, not the
  gameplay code.** Tooltip-says-yes / screen-says-no localises the fault before any code is read.

## Related

- `docs/solutions/runtime-errors/duplicate-asset-bundle-under-mods-aborts-startup.md` — the other
  bundle-level failure that presents as a mod defect rather than an asset defect.
- `docs/solutions/logic-errors/prefab-finisher-writes-to-the-scene-object-name.md` — the same tool,
  and the origin of the stray duplicate objects this fix had to account for.
- `docs/solutions/conventions/eco-server-only-mod-client-rendering-surfaces.md` — what a bundle can
  and cannot contribute to the client.
