---
title: "An exception in WorldObject.Initialize() leaves a half-built object — invisible, non-interactable, and silent"
date: 2026-07-31
category: runtime-errors
module: EcoServerMod
problem_type: runtime_error
component: tooling
severity: high
symptoms:
  - "A placed mod world object does not render and cannot be interacted with, but its name label and map marker are present"
  - "The item tooltip lists only some of the object's components (e.g. 'Components: Authorization Component and Status Component')"
  - "No error appears in the server log at the moment the object is placed"
  - "The next server start fails outright with the exception, naming the mod object's Initialize()"
root_cause: config_error
resolution_type: code_fix
tags: [eco-modding, worldobject, initialize, partscomponent, partitem, invisible-object, half-built, server-mod]
related_components: [EcoServerMod/AdvancedElectronics]
---

# An exception in WorldObject.Initialize() leaves a half-built object — invisible, non-interactable, and silent

## Problem

`DroneDockObject.Initialize()` threw partway through. Everything before the throw ran, everything
after it did not, and the object was left half-built: present on the server, invisible and
non-interactable on the client, with nothing in the server log at placement time to say so. The
mod had shipped a `PartsComponent` entry naming an item that is not a part.

## Symptoms

The placed dock rendered no mesh. Its floating name label and its map marker were both there, and
the crafting recipe, skill gating, and registration all behaved normally, so the failure read as a
client/prefab problem for several test cycles.

The one server-side tell was the item tooltip:

```
Drone Dock
Size: 1x1x1
Components: Authorization Component and Status Component
```

Two components, where the working build showed six tabs. That truncated list is exactly the set
that survives an aborted `Initialize()`.

The exception itself only surfaced on the **next** server start, at which point it was fatal and
fully named:

```
System.Exception: PartsComponent can only be used with PartItem.
   at Eco.Gameplay.Components.PartsComponent.Config(Func`1 decayDescription, PartInfo[] partsRequired)
   at Eco.Mods.TechTree.DroneDockObject.Initialize()
   at Eco.Gameplay.Objects.WorldObject.DoInitializationSteps(Boolean fistTimeAdded)
   at Eco.Gameplay.Objects.WorldObjectManager.<>c__DisplayClass23_0.<Initialize>b__0(KeyValuePair`2 pair)
```

## What Didn't Work

- **Blaming the Unity prefab and the asset bundle.** The prefab's occupancy fields were correct
  (`hasOccupancy: 1`, `size: {x:1,y:1,z:1}`, `interactable: 1`), and the client log's
  `The referenced script on this Behaviour (Game Object 'DroneDockObject') is missing!` warnings
  were pre-existing and present in builds where the dock rendered fine. Decisive check: the bundle
  had not been rebuilt between a run where the object rendered and a run where it did not, so the
  cause had to be server-side.
- **A four-point correlation that was confounded.** Every build carrying a temporary
  `UIShowcaseComponent` probe failed to render the dock; the one build without it rendered. That
  looked conclusive and was wrong — `PartsComponent` and its throwing `Config(...)` call had been
  added in the *same* edit as the probe, so both tracked the symptom equally well. Correlation
  across builds cannot separate two changes that always travel together.
- **Dismissing the tooltip.** The truncated component list was noticed early and argued away as
  "probably just how the tooltip summarizes," because the server log was clean. It was the real
  signal.
- **Trusting clean boot verifications.** Several full boots to `Web Server now listening` passed
  while this bug was live. See *Why This Works* — they could not have caught it.

## Solution

Every entry in a `PartsComponent.Config` call must name a type deriving from `PartItem`.
`FramedGlassItem` does not — it is a building material:

```csharp
// .references/Mods/__core__/AutoGen/Block/FramedGlass.cs:112
[Tag("Constructable")]
public partial class FramedGlassItem : ...

// .references/Mods/__core__/AutoGen/Item/SteelPlate.cs:104
public partial class SteelPlateItem : PartItem { ... }
```

```csharp
// before -- throws at Config(), aborting Initialize()
new() { TypeName = nameof(AdvancedCircuitItem), Quantity = 1},
new() { TypeName = nameof(FramedGlassItem),     Quantity = 1},
new() { TypeName = nameof(SteelGearItem),       Quantity = 2},

// after
new() { TypeName = nameof(AdvancedCircuitItem), Quantity = 1},
new() { TypeName = nameof(SteelPlateItem),      Quantity = 1},
new() { TypeName = nameof(SteelGearItem),       Quantity = 2},
```

`SteelPlateItem` was chosen because the dock recipe already consumes it; the constraint is the base
class, not the particular item.

Audit the whole mod in one pass rather than fixing the one that threw — the vanilla `PartItem` list
is enumerable from the reference tree:

```bash
grep -rhoE "class [A-Za-z]+Item[[:space:]]*:[[:space:]]*PartItem" \
  --include=*.cs .references/Mods/__core__/ | sed -E 's/class ([A-Za-z]+Item).*/\1/' | sort -u

grep -rhoE "TypeName = nameof\([A-Za-z]+Item\)" EcoServerMod/AdvancedElectronics/*.cs \
  | sed -E 's/.*nameof\((.*)\)/\1/' | sort -u
```

Every name in the second list must appear in the first. In this mod that is 45 valid part types
against 8 in use.

## Why This Works

Two separate mechanisms combine to make this bug so quiet.

**A throw in `Initialize()` aborts the rest of it.** Component setup in an Eco world object is a
straight-line sequence of `GetComponent<T>().Initialize(...)` / `.Config(...)` calls. An exception
partway through means every later component is attached (via `[RequireComponent]`) but never
configured. The server keeps the object — it has an ID, a position, a name label, a map marker —
but the client cannot construct a view for a half-configured object, so no mesh renders and no
window opens. The item tooltip lists only the components that got past the throw, which is why
"Authorization Component and Status Component" was a precise description of where execution
stopped, not a summary.

**`Initialize()` runs per instance, not at boot.** `WorldObjectManager.Initialize` fans out over the
objects present in the save (visible in the stack trace's `PartitionerForEachWorker` frame). If no
instance of the object exists in the world, the code never executes. That is the whole reason
repeated clean boots to `Web Server now listening` proved nothing here: they ran against a save with
no dock in it. The dock was placed at 17:50; the next load ran its `Initialize` and the server
refused to start.

So a clean boot verifies the *type graph* — skills, recipes, registration, the skill-tree walk — and
says nothing about object initialization. Those are different test surfaces and need different
worlds.

## Prevention

- **Constrain part entries at review time.** `PartInfo.TypeName` is a `string` produced by
  `nameof(...)`, so the compiler cannot check it. Treat every `PartsComponent.Config` entry as
  unverified until checked against the `PartItem` list — the two greps above are a few seconds.
- **Keep a test save containing one of every mod object.** Boot verification against a world with no
  mod objects in it cannot execute any `Initialize()` path. A save with each object placed turns
  every restart into a real check of object-level code.
- **Read a truncated component list as an aborted `Initialize()`.** When an object's tooltip or
  window shows *some* of its components, that is not a rendering quirk — it is a marker for where
  execution stopped. Match the listed components against the order of calls in `Initialize()` to
  find the throwing line.
- **Do not bisect two changes that arrived together.** If two edits landed in the same commit or the
  same working-tree change, no amount of build-level correlation can separate them. Split them, or
  find a signal that names the cause directly — here, one unhandled exception at the next boot was
  worth four rounds of correlation.
- **When an object is invisible, decide client vs server first.** Check whether the asset bundle
  changed between a working and a non-working run. If it did not, stop investigating prefabs.

## Related

- `docs/solutions/runtime-errors/worldobjectcomponent-missing-attributes-empty-window.md` — the other
  way a mod object's window comes up empty (missing `[Serialized]`/`[NoIcon]` on a custom component,
  which fails view encoding at window-open). That doc draws the line "object-level defects break
  placement; component-level attribute defects break the window"; this failure is a third mode that
  breaks **both**, and unlike that one it logs nothing at the time it happens.
- `docs/solutions/ui-bugs/bundled-mod-objects-must-ship-disabled.md` — a third distinct cause of the
  same visible symptom, on the client side. Distinguishing tell: that one resolves when the chunk
  re-streams (walk away and return) and logs `Loaded objects should start as DISABLED`; this one
  never resolves and logs nothing until the next boot.
- `docs/solutions/conventions/eco-custom-worldobject-placement-requirements.md` — the placement
  checklist a hand-written mod object must satisfy; its rule #5 covers the component-attribute
  requirement that the empty-window doc details.
- `docs/solutions/workflow-issues/validate-the-instrument-before-the-hypothesis.md` — the same
  methodological failure one layer out. There a contaminated restore made the culprit follow the
  most recent edit; here a confounded pair made it follow the wrong one of two co-travelling changes.
- `docs/solutions/conventions/auditing-content-derived-from-autogen-templates.md` — the sibling
  silent-residue class, where a wrong `CraftingElement<T>` type-checks and misbehaves rather than
  throwing.
