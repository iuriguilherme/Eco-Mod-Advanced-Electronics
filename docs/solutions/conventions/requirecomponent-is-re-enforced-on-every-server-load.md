---
title: "[RequireComponent] is re-enforced on every server load — detaching one deletes it, and its contents, from objects already placed"
date: 2026-08-10
last_updated: 2026-08-21
category: conventions
module: EcoServerMod
problem_type: convention
component: worldobject_lifecycle
severity: high
applies_when:
  - "Removing or commenting out a [RequireComponent] before a release"
  - "Adding a component to a WorldObject class that servers already have instances of"
  - "A component owns an inventory and its declaration is about to change"
  - "A component renders nothing at all AND logs nothing"
  - "Swapping a required component for its base type, its subclass, or a sibling"
  - "Installing components dynamically rather than by attribute"
tags: [eco-modding, worldobject, requirecomponent, validatecomponents, save-data, release-hygiene, serialization, migration, silent-failure, server-startup]
related_components: [EcoServerMod/AdvancedElectronics]
---

# `[RequireComponent]` is re-enforced on every server load — detaching one deletes it, and its contents, from objects already placed

Paths beginning `Server/` and `Mods/__core__/` below are Eco's own trees — the engine source
checkout and the dedicated server's shipped core mod — not files in this repository. Everything else
is repo-relative.

## Context

The intuitive model of `[RequireComponent(typeof(T))]` is that it is a **construction** rule: it
decides what an instance is built with, the resulting component list is serialized on that instance,
and editing the attribute afterwards only changes objects created from then on. Under that model,
detaching a component before a release is a safe no-op for existing worlds, and adding one is
invisible until somebody places a fresh object.

That model is wrong in both directions, and the engine says so plainly. Component validation is not
a construction step — it is a **load** step, re-run against every persisted object on every server
start (`Server/Eco.Gameplay/Objects/WorldObject.cs:536-541`):

```csharp
/// <summary>Perform the steps needed for initialization, which is called after OnCreate, and every server start.</summary>
public void DoInitializationSteps(bool fistTimeAdded = false)
{
    this.SetupOccupancy();          //Cache the occupancy of the object.
    this.SetupSettlement();         //Init things related to settlement, like assigning the settlement for this world object and subscribing to updates..
    this.ValidateComponents();      //ensure all the components that should be there are
```

And every object in the world goes through it, in bulk, at load
(`Server/Eco.Gameplay/Objects/WorldObjectManager.cs:161`):

```csharp
Parallel.ForEach(this.objectsByID, pair => { pair.Value.DoInitializationSteps(); Interlocked.Increment(ref currentDone); timer.LoadPercentage = (float)currentDone / numWork; });
```

So the attribute list in the shipped DLL is not a record of how old objects were built. It is the
authority that every old object is rewritten to match, once per restart.

## Guidance

**Detaching a `[RequireComponent]` deletes that component from every object already placed — and
its contents go with it.** This is the destructive direction and the reason this document exists.
`ValidateComponents` computes the required set from the current type, marks anything outside it
`Unwanted` (`WorldObject.cs:480-483`), folds that into `Superseded` (`:511`), and then simply drops
those instances (`WorldObject.cs:522`):

```csharp
this.Components?.RemoveAll(component => Superseded(component));
```

A removed `WorldObjectComponent` takes whatever state it owned with it. Upstream hit this hard
enough to leave a warning in its own core mod (`Server/Mods/__core__/Objects/Trucks.cs:9-13`):

```csharp
//Trucks lost their built-in storage when they became modular (storage now comes from a slotted flatbed). The old storage was an UNNAMED
//PublicStorageComponent, which ValidateComponents would delete on load together with its contents before any migration can run.
//Whitelisting it here keeps it alive long enough for TruckStorageToFlatbedMigration to rescue the items. The flatbed's own storage is
//name-keyed, so this can't collide with it.
[MayHaveComponent(typeof(PublicStorageComponent))] public partial class TruckObject { }
```

"Before any migration can run" is the important clause. The deletion happens during load, ahead of
any code a mod could write to rescue the data. If a component holds an inventory, removing its
declaration without a `[MayHaveComponent]` bridge is a save-data loss, not a cleanup.

**Adding a `[RequireComponent]` to a shipped class does reach objects that already exist.** The
missing component is constructed and attached during the same pass
(`WorldObject.cs:492-496`):

```csharp
foreach (var entry in componentsSet)
    if (!entry.Type.IsAbstract)
    {
        if (!HasWantedComponent(entry.Type, entry.Name))
            toCreate.Add(this.AddComponent(entry.Type, entry.Name));
    }
```

A probe or feature component added to a class therefore lands on every one of that class's instances
in the world — after a restart. Before the restart it lands on none of them.

**Swapping a required component for its base or subclass is a third case, and it is neither of the
first two.** Add-and-remove happen in one pass with two different notions of type identity, so
reasoning about them separately gives the wrong answer. `HasWantedComponent` matches by
**assignability** and so an existing subclass instance can satisfy a newly required base type
(`WorldObject.cs:485-486`); `Unwanted` and the superseded-key scan match by **exact `(Type, Name)`**
(`:481-483`, `:508-509`). Three guards in the engine exist for this case specifically:

- Additions run **before** removals, "so a component replaced by a newly required derived type finds
  its successor below" (`WorldObject.cs:489`).
- A required entry counts as satisfied only by a component that will survive the removal pass, "so a
  doomed component can't mask a missing one" (`:485-486`).
- **A superseded instance hands its state to the survivor before it is dropped**
  (`WorldObject.cs:513-520`):

```csharp
var survivor = this.Components.FirstOrDefault(x => x != null && x != superseded && !Superseded(x) && superseded.GetType().IsAssignableFrom(x.GetType()));
if (survivor == null) continue;
survivor.AbsorbSuperseded(superseded);
Log.WriteLineLoc($"Removed duplicate {superseded.GetType().Name} from {this.Name} at {this.Position3i}, state merged into {survivor.GetType().Name}.");
```

That is the one path on which a component's state is **not** destroyed, and unlike everything else in
this document it announces itself in the server log. `AbsorbSuperseded` is what decides how much
actually survives, and it is the component's own implementation — do not assume a full transfer.

**A swap is still not risk-free, and the reason is not established.** Changing the Drone Dock from
`[RequireComponent(typeof(DroneDockLinkComponent))]` to the stock base
`[RequireComponent(typeof(SharedLinkComponent))]` aborted server startup on 2026-08-20, with an NRE
from a bare `GetComponent<LinkComponent>()` dereference in the dock's own `Initialize()` — a link
component of any kind should have been present by then, and was not. **The mechanism is unresolved;
do not repeat an explanation for it.** Two facts survive regardless of the cause: `Initialize()` runs
on every saved object at every start, so an exception there aborts the whole server rather than
degrading one object; and `TryGetComponent` costs one line and converts that abort into one degraded
object. Guard every component dereference in `Initialize()` before shipping a swap. See
`docs/solutions/conventions/an-empty-marker-component-is-a-client-ui-feature-flag.md`.

**"After a restart" is the whole subtlety.** Both effects are invisible until the world reloads,
because nothing rewrites a live object's component list mid-session. That is what makes the
construction-time model so plausible: within one session it is indistinguishable from the truth.

**Read "renders nothing AND logs nothing" as not-attached before reading it as broken.** A component
that is present but failing usually leaves something — an exception, a blank tab, a partial panel. A
component that was never attached leaves a completely ordinary object. Absolute silence is the
signature of absence. The remedy is unchanged and is now better explained: the object was loaded
before the declaration existed and has not been through a load since.

**When a component is installed dynamically rather than declared, it must be announced or it is
stray.** `ValidateComponents` also honours a runtime-declared set
(`WorldObject.cs:474-477`), which is exactly why this mod's module driver implements
`IDeclaresMayHaveComponents` — see the comment on `ExpectedComponents` in
`EcoServerMod/AdvancedElectronics/DroneModuleComponent.cs:71-75`: "without it, the fuel tab and its
contents are stripped on the first restart." Same deletion, reached from the other side.

**Check the shipped artifact, not the running world, when you want to know what a release
contains.** The artifact holds the rules; the world holds objects that will be conformed to those
rules at the next load. They answer different questions, and only the artifact answers the one about
the release.

**All of the above is verified by reading the engine, not by restarting a server.** In this project
a restart is paid for by a human quitting the client and waiting — the standing rule in
`docs/solutions/workflow-issues/eco-mod-batched-live-testing.md`. A behaviour that only manifests
across a load boundary is precisely the kind that must not be established by an interactive
try-restart loop. Five files read settle it; a restart loop would have cost an afternoon and still
only sampled one object shape.

## Why This Matters

The superseded version of this note stated the opposite — that editing the attribute "changes what
future objects get and leaves every existing object exactly as it was" — and drew a release
guarantee from it: detaching a probe cleans nothing, so the promise you can make is only about fresh
worlds. That is backwards, and backwards in the dangerous direction. The real guarantee is stronger
(the probe *is* gone from every dock after one restart) and comes with a hazard the old note did not
mention at all (if the probe had owned an inventory, that inventory is gone too).

Both errors have the same shape: they treat a serialized instance as the record of record. In Eco it
is not. The type is the record of record, and the save is reconciled to it at load. Anything a mod
persists inside a component lives exactly as long as that component stays declared — with one
exception, the base/derived swap above, where the engine hands the state to the survivor instead of
dropping it.

The asymmetry is what makes this high severity. Being wrong about the add direction costs a wasted
deploy and a confusing debugging session. Being wrong about the remove direction costs someone
else's items on someone else's server, silently, at the restart after they installed the update —
with no exception, no log line pointing at the mod, and no way to get the contents back.

## When to Apply

- Before removing or commenting out any `[RequireComponent]` on a class that servers already have
  instances of. Ask what state that component owns; if it owns an inventory, plan a
  `[MayHaveComponent]` bridge and a migration before the deletion ships.
- Before adding a component to a shipped class — it will appear on every existing instance, which is
  usually what you want and occasionally a surprise.
- When a component produces no output whatsoever — check attachment, and check whether the world has
  reloaded since the declaration changed.
- When installing components dynamically: declare them via `IDeclaresMayHaveComponents` or they are
  removed as stray on the next load.
- When writing release notes for a version that adds or removes a component, so the effect at
  players' next restart is stated rather than discovered.

## Examples

What a restart does to an object, by direction of the change:

| Change to the class | Live session | After next server load |
|---|---|---|
| `[RequireComponent(T)]` added | nothing | `T` constructed and attached to every instance |
| `[RequireComponent(T)]` removed | nothing | `T` removed from every instance, its state destroyed |
| `T` swapped for a base or subclass of `T` | nothing | survivor kept, superseded instance's state absorbed into it, one log line written |
| `T` swapped for an unrelated sibling type | nothing | no absorption path — treat as remove plus add, state destroyed |
| `T` installed dynamically, undeclared | works | removed as stray, contents included |
| `T` installed dynamically, declared via `IDeclaresMayHaveComponents` / `[MayHaveComponent]` | works | preserved |

The detach in this mod, correct as written because `UIShowcaseComponent` was a probe holding nothing
worth keeping (`EcoServerMod/AdvancedElectronics/DroneDock.cs:89-94`):

```csharp
// DETACHED 2026-07-31 for the 0.0.3 release. The probe answered its questions -- which
// attribute shape delivers writes, that DynamicTitle resolves a label once and never again,
// and that a Changed() inside a setter needs the dock's tick to reach the client. Findings
// are in docs/solutions/runtime-errors/autogen-template-binding-contract.md. The component
// stays in the tree for the next binding question; re-attaching is uncommenting one line.
// [RequireComponent(typeof(UIShowcaseComponent))]
```

Commenting the line is complete for the artifact. What the old note added — that docks already
placed keep the `UI Showcase` tab for the rest of their lives — is false: they lose it at the next
restart, which is the intended outcome. Had the probe owned storage, the same line would have been a
data-loss bug.

Verifying a release by its artifact rather than by the world it was deployed into:

```bash
# what the release actually contains -- the rules every object will be conformed to
git show <release-commit>:EcoServerMod/AdvancedElectronics/DroneDock.cs \
  | grep -nE "RequireComponent"

# NOT this: the running world has not been reconciled to those rules yet
```

## Related

- `docs/solutions/conventions/an-inventory-restriction-governs-one-verb.md` — the other
  "what happens to objects that already exist" question in this mod. There the engine is gentler
  than expected (old fuel burns off normally); here it is harsher (the component and its contents
  are removed outright). Both are decided by engine code, not by the save.
- `docs/solutions/runtime-errors/naming-a-component-hides-it-from-its-vanilla-consumer.md` — the
  `(Type, Name)` pair is a component's identity here too; validation matches on both, so a rename is
  a delete plus an add.
- `docs/solutions/conventions/an-empty-marker-component-is-a-client-ui-feature-flag.md` — the swap
  that produced the startup crash above, and four falsified theories about why a stock tab rendered
  without its controls. Reading component *types* instead of component *sets* is the shared mistake.
- `docs/solutions/runtime-errors/initialize-exception-leaves-a-half-built-worldobject.md` — the other
  way a WorldObject ends up with fewer components than its class declares. That one throws partway
  through and leaves evidence; this one is a deliberate, silent removal.
- `docs/solutions/workflow-issues/eco-mod-batched-live-testing.md` — why a load-boundary behaviour
  gets settled by reading the engine instead of by restarting until it reproduces.
- `docs/solutions/workflow-issues/the-control-under-test-is-not-a-readout-of-it.md` — the same
  instinct one layer in: confirm the thing you are measuring is the thing under test. Here it is
  whether the component exists at all.
- `docs/solutions/conventions/eco-custom-worldobject-placement-requirements.md` — the other class of
  thing a mod WorldObject must declare for itself rather than inherit.
