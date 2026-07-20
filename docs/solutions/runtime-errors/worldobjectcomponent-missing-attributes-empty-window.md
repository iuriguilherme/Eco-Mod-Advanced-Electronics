---
title: WorldObjectComponent subclasses without [Serialized] and [NoIcon] render the whole object window empty
date: 2026-07-19
category: runtime-errors
module: EcoServerMod
problem_type: runtime_error
component: tooling
severity: high
symptoms:
  - "Opening a modded WorldObject's 'Use' window shows an empty panel (default 'Editable Title', no storage grid, no component tabs) — even for components the object demonstrably runs"
  - "Server log: 'Component X has to explicitly define [HasIcon] or [NoIcon], because it's parent has [DerivedMustDefineIcon].'"
  - "Server log: 'System.AggregateException ... Can't encode instance of type X (do you need to add a generic type to the [SerializeForGenericTypesAttribute] on the class?)'"
root_cause: config_error
resolution_type: code_fix
tags: [eco-modding, worldobjectcomponent, serialization, noicon, object-window, empty-ui, server-mod]
related_components: [EcoServerMod/AdvancedElectronics]
---

# WorldObjectComponent subclasses without [Serialized] and [NoIcon] render the whole object window empty

## Problem

Custom `WorldObjectComponent` subclasses (a drone mover, an ore sensor, a lifecycle
state machine) were declared as bare `public class X : WorldObjectComponent`. The mod
loaded cleanly, the components ran their `Tick()` logic, and crafting/placement worked —
but pressing E on the owning object produced an **empty window**: no storage grid, no
component tabs, default "Editable Title" only. The failure hit sibling components too:
the dock's `PublicStorageComponent` slot grid (a vanilla component) also vanished,
because the whole window payload failed to serialize.

## Symptoms

At window-open time (not at server startup), the server log records both error families:

```
[Error] [Eco] Component DroneMoverComponent has to explicitly define [HasIcon] or [NoIcon], because it's parent has [DerivedMustDefineIcon].
[Error] [Eco] System.AggregateException: One or more errors occurred. (Can't encode instance of type 'Eco.Mods.TechTree.DroneMoverComponent' (do you need to add a generic type to the [SerializeForGenericTypesAttribute] on the class?). Instance: Eco.Mods.TechTree.DroneMoverComponent)
```

The player sees no error — just the blank panel. Because the errors only fire on
interaction, a headless server boot looks perfectly clean.

## What Didn't Work

- **Attributing the empty window to a missing client UI.** The window's content
  (storage grid, component tabs) is server-composed from the object's components; no
  client asset ships it. The blank panel was a server-side encode failure, provable
  from the server log alone.
- **Assuming a component that ticks fine is fully conformant.** Ticking needs no view
  encoding; the window does. A component can run for weeks and still blank every window
  of every object that carries it.

## Solution

Every `WorldObjectComponent` subclass gets `[Serialized]` and `[NoIcon]` (or
`[HasIcon]`), matching every vanilla component:

```csharp
// before
public class DroneMoverComponent : WorldObjectComponent

// after
using Eco.Core.Controller;      // NoIcon lives here, NOT in Eco.Core.Items
using Eco.Shared.Serialization; // Serialized

[Serialized]
[NoIcon]
public class DroneMoverComponent : WorldObjectComponent
```

Applied to all three custom components in one pass. Build green; fix deployed. Live
window confirmation pending as of this writing (batched with other fixes per
`docs/solutions/workflow-issues/eco-mod-batched-live-testing.md`).

## Why This Works

The base class declares both contracts (game source
`Server/Eco.Gameplay/Objects/WorldObjectComponent.cs`):

```csharp
[Serialized]
[IconGroup(nameof(WorldObjectComponent)), DerivedMustDefineIcon]
public abstract class WorldObjectComponent : ...
```

`[DerivedMustDefineIcon]` makes the engine demand an explicit icon attribute on every
descendant (the exact error message is logged from that class). Separately, the BSON
view encoder only encodes types registered via `[Serialized]` — an unregistered
component in the window's component list throws `Can't encode instance`, and the
exception aborts the entire window payload, blanking vanilla siblings along with the
offender. Vanilla components (e.g. `SolarGeneratorComponent`,
`WindGeneratorComponent`) all carry `[Serialized]` + `[NoIcon]`.

Note the misdirection in the engine's own error text: it suggests
`[SerializeForGenericTypesAttribute]`, which applies to generic classes — for a plain
non-generic component the actual missing piece is `[Serialized]`.

## Prevention

- Include `[Serialized]` + `[NoIcon]`/`[HasIcon]` in the mod's component template; a
  bare `class X : WorldObjectComponent` is never correct.
- When conforming mod classes to the vanilla pattern, audit **all** Eco-derived classes
  in one pass (objects, items, recipes, components, commands) — this defect survived
  three earlier fix rounds precisely because only the classes named by the previous
  error were diffed.
- The two failure signatures separate cleanly: object-level defects (naming, size,
  occupancy) break placement; component-level attribute defects break the **window**.
  An empty window with working placement points at component encoding first.
- These errors appear only at window-open, so grep past session logs for
  `has to explicitly define` and `Can't encode instance` — a clean startup proves
  nothing about window health.

## Related

- `docs/solutions/conventions/eco-custom-worldobject-placement-requirements.md` — rule
  #5 records this same attribute requirement inside the broader placement checklist;
  this doc is the dedicated symptom-to-fix entry for the empty-window signature.
- `docs/solutions/workflow-issues/eco-mod-batched-live-testing.md` — the workflow rule
  this bug's discovery cost (one full restart) helped establish.
- `docs/solutions/runtime-errors/worldobject-zero-size-blocks-placement.md` — sibling
  runtime defect in the same debugging arc (placement-side, not window-side).
