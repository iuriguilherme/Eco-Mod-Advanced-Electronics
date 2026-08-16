---
title: "Comparing the slotted item by reference reinstalls its components and destroys the open UI"
date: 2026-08-16
category: logic-errors
module: EcoServerMod
problem_type: logic_error
component: tooling
severity: high
symptoms:
  - "The +/- number selectors and the assign buttons on a Drone Dock's Survey and Mining tabs vanish mid-session and never come back, ending the live test"
  - "The server log is clean at the moment the controls vanish - no mod exception, no stack trace"
  - "Client Player.log repeats: Destroyed AutoGenSelector has active subscriptions for: ... SurveyComponentView.MaterialTargets"
  - "Server log shows paired 'Drone Dock: installed ... / uninstalled ...' lines inside a single millisecond, several times a session"
  - "Ordinary inventory actions - consolidate, restack, stack merge - are enough to trigger it; no player action on the dock is needed"
root_cause: logic_error
resolution_type: code_fix
tags: [eco-modding, worldobjectcomponent, component-churn, reference-equality, item-identity, client-log, drone-dock]
related_components: [EcoServerMod/AdvancedElectronics]
---

# Comparing the slotted item by reference reinstalls its components and destroys the open UI

## Problem

A dynamic component installer decided whether to reinstall by comparing the slotted item with
`ReferenceEquals`. Inventories hand back a fresh `Item` instance for unchanged contents, so the
comparison failed constantly and every component was torn off the world object and re-added several
times a session. Each of those cycles destroyed the object's open UI on the client, permanently
removing controls from the running window.

## Symptoms

The `+`/`-` number selectors and the assign buttons on the Drone Dock's Survey and Mining tabs stopped
being drawn partway through a session and never returned. Without an assign button there is no way to
give a drone an area, so each live test ended at that point.

The server log at the moment of the failure: nothing. No exception, no warning.

The server log earlier in the same session, already noticed and mis-filed as merely odd:

```
Drone Dock: uninstalled MiningDroneItem's components.
Drone Dock: installed MiningDroneItem's components.
```

Those lines come from `Uninstall` and `TryInstall`
(`EcoServerMod/AdvancedElectronics/DroneModuleComponent.cs`), and they repeat as pairs inside a single
millisecond.

The client log named the actual damage:

```
Destroyed AutoGenSelector has active subscriptions for: ... GamePickerListView.MarkedUpName, SurveyComponentView.MaterialTargets
Destroyed WorldObjectUI has active subscriptions for: ... MiningComponentView.ForceActiveTab
Destroyed InputFieldControl has active subscriptions for: MiningComponentView.AssignedAreaDisplay, MiningComponentView.StampedCitizenDisplay
```

`AutoGenSelector` is the `+`/`-` control. `InputFieldControl` is the mining tab's text readouts.

## What Didn't Work

- **Six passes searching the server log, across six separate reports.** It was clean every time, and
  the silence was read as "no evidence" rather than "wrong process". The server had nothing to report
  because the server did exactly what it was told; the cost was paid entirely on the client.
- **Four server-side hypotheses, none testable with the instrument in hand.** An exception during the
  autogen UI refresh; exceeding a synced-element budget; `Changed()` called from the dock's background
  tick thread; the object's `Operating` state locking controls. None was confirmable, and none was the
  cause.
- **Treating the install/uninstall churn as an unrelated curiosity.** It was visible server-side and had
  been noticed, but nothing joined it to the UI symptom, because the UI symptom produced no server error
  to join it on.

## Solution

`DroneModuleComponent.Sync()` compared the slotted item by reference:

```csharp
// before
var newSource = this.SlottedSource;
if (ReferenceEquals(this.installedSource, newSource)) return;
```

It now compares by type:

```csharp
// after -- EcoServerMod/AdvancedElectronics/DroneModuleComponent.cs
private static bool SameKind(IWorldObjectComponentSource a, IWorldObjectComponentSource b)
{
    if (a == null || b == null) return a == null && b == null;
    return a.GetType() == b.GetType();
}

if (SameKind(this.installedSource, newSource)) return;
```

What gets installed is a function of which item **class** is slotted: `ComponentsToInstall` is declared
per item class and reads no instance state (`MiningDrone.cs`, `SurveyDrone.cs`, `HarvesterDrone.cs`).
Two instances of the same drone item therefore call for identical components.

The guard stays correct at the edges:

- **Insertion and removal still sync** - `SameKind` is true only for two nulls, false for any
  null/non-null pair, so an empty slot and a filled one are never the same kind.
- **A swap between different drone kinds still reinstalls** - the item classes differ.
- **A same-kind swap now skips the persistent-data capture/restore round trip.** That is safe because
  removal is already refused while the drone's fuel tank holds fuel (each drone declares
  `canUninstall: c => c.Inventory.IsEmpty` on its `FuelSupplyComponent`, which the vanilla
  `ComponentSourceRestriction` added in `AttachTo` honours), so there is no partly-burned unit to carry
  across.

Commit `5b30ea6` on branch `feat/mining-drone`. There is no PR and it is not merged; the causal chain is
a diagnosis backed by the client log plus the code, not by an observed post-fix live session.

Two findings in the same client-log capture were deliberately left alone, being Unity asset gaps rather
than server code: `Cannot find icon with name MiningComponent`, and the same for `SurveyComponent`.

## Why This Works

**A world object's component set is a UI-lifecycle input, not just server state.** The client rebuilds
an object's entire UI when the set changes - correct behaviour, since the set of tabs is derived from
the set of components. Rebuilding destroys the existing view objects, and the destroyed controls still
held property subscriptions, which is what `Destroyed X has active subscriptions for:` reports. A
control destroyed in that state does not come back for the life of the client's view. So one
harmless-looking uninstall/install pair per inventory shuffle was permanently deleting controls from a
running client, one class at a time, until the tab was unusable.

**Type is the invariant the guard actually needed.** The question `Sync` must answer is "does the
installed component set still match what the slot calls for?" The set derives from the item's class, so
the class is the correct key. Reference identity answers a strictly narrower question - "is this the
very same object as last time?" - whose answer changes for reasons unrelated to the thing being
guarded. A guard keyed on something that changes more often than the state it protects will fire
spuriously, and here the spurious firings were destructive.

## Prevention

- **Do not use `ReferenceEquals` on anything a collection may re-instance.** Inventories, identity maps,
  caches, and serializers all hand back fresh instances for unchanged logical content. Key a guard on
  the property that determines the outcome - here the type - not on object identity. This is the part
  of the learning with no precedent in this store.
- **Treat same-millisecond install/uninstall pairs as a churn signal, not routine noise.** A dynamic
  installer that logs a matched pair inside one millisecond is re-firing, and any cost attached to
  installation is being paid repeatedly.
- **When adding or removing components on a live object, remember the client is watching the set.**
  Anything that changes the set during ordinary play will rebuild the window. Prefer a stable set with
  components that disable themselves over a set that installs and uninstalls.
- **The client-log rule was already written down here, twice, and still did not fire.** See
  `client-animation-is-driven-by-name-not-by-mod-code.md` ("A clean server log is not evidence that the
  server is innocent; it is evidence that the server was never involved") and
  `eco-server-only-mod-client-rendering-surfaces.md`. Both scope their trigger to an object that does
  not render *at all*; this object rendered perfectly and only lost controls mid-session, so neither
  trigger matched. A recorded rule that only fires on first-render failures will not catch a
  mid-session one - the trigger, not the rule, is what needed widening.
- **`Destroyed <ClassName> has active subscriptions for:` is the search string.** It names the control
  class that was lost. Grep the client log for `Destroyed` when any control disappears.
- **A symptom that degrades the test rig outranks newer bugs.** This one was deferred six times as "not
  the newest problem" while it silently capped how much any single session could verify. Age is the
  wrong sort order when a defect is taxing every future cycle.
- **The test suite is not evidence about this path.** The only test project,
  `EcoServerMod/AdvancedElectronics.Navigation.Tests`, references only `AdvancedElectronics.Navigation`;
  `DroneModuleComponent` is not reachable from it. Confirmation has to come from a live session.

## Related Issues

- `docs/solutions/architecture-patterns/client-animation-is-driven-by-name-not-by-mod-code.md` - already
  owns the read-the-client-log rule. This case is the recurrence that shows its trigger is scoped too
  tightly to first-render failures.
- `docs/solutions/conventions/eco-server-only-mod-client-rendering-surfaces.md` - owns the client-log
  path and search terms. Its search list does not yet include the destroyed-subscription signature.
- `docs/solutions/conventions/requirecomponent-is-re-enforced-on-every-server-load.md` - the same fact
  (a component set is not inert) on the load-time axis; this doc is the per-tick axis.
- `docs/solutions/runtime-errors/naming-a-component-hides-it-from-its-vanilla-consumer.md` - nearest
  prior art on dynamic component installation through this same drone module system.
- `docs/solutions/runtime-errors/autogen-template-binding-contract.md` - how mod component tabs are
  rendered, and the vocabulary for the widgets that were destroyed.
- `docs/solutions/runtime-errors/worldobjectcomponent-missing-attributes-empty-window.md` - differential:
  a window empty from first open, loud in the server log, versus one that empties during play.
- `docs/solutions/runtime-errors/n-editable-members-cannot-share-one-field.md` - the other mod-tab
  control defect: control present but reverting, versus control gone.
- `docs/solutions/workflow-issues/the-control-under-test-is-not-a-readout-of-it.md` - differential: the
  control is present and lies, versus the control is absent.
- `docs/solutions/workflow-issues/eco-mod-batched-live-testing.md` - why each confirmation costs a
  restart, and the natural owner of the deferred-symptom-caps-throughput point.
