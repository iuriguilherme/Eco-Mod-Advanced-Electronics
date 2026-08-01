---
title: "[RequireComponent] decides what NEW objects get, so probes and cleanups both miss what already exists"
date: 2026-07-31
category: conventions
module: EcoServerMod
problem_type: convention
component: tooling
severity: medium
applies_when:
  - "Attaching a diagnostic or probe component to an existing WorldObject class"
  - "Removing a temporary component before a release"
  - "A component renders nothing at all AND logs nothing"
  - "Deciding which object type to host a UI probe on"
tags: [eco-modding, worldobject, requirecomponent, probes, release-hygiene, serialization, silent-failure]
related_components: [EcoServerMod/AdvancedElectronics]
---

# `[RequireComponent]` decides what NEW objects get, so probes and cleanups both miss what already exists

## Context

`[RequireComponent(typeof(T))]` on a `WorldObject` class is a **construction** rule, not a shape rule.
It governs what components an instance receives when it is created; the resulting component list is
then serialized on that instance. Editing the attribute changes what future objects get and leaves
every existing object exactly as it was.

Both directions of that cost something in one session:

- **Adding** a probe component to `SurveyDroneObject` produced no tabs, no content, and no log line.
  Every drone in the test world pre-dated the change, so none of them had the component and the
  probe never ran. It read as "the drone renders nothing", which is a different and much harder
  problem. The probe moved to the dock and worked immediately — noted in the source at
  `EcoServerMod/AdvancedElectronics/SurveyDrone.cs`, above the class declaration.
- **Removing** that probe before cutting a release did not strip it from the docks already placed.
  The `UI Showcase` tab stayed on every dock created while the attribute was live, which looked like
  a release built from the wrong commit until the shipped artifact was checked and found clean.

## Guidance

**Host a probe on an object you can cheaply re-place, and place a fresh one to test it.** The
question a probe answers is worthless if the probe was never attached. In this mod the dock is
craftable and placeable in seconds; the drone only exists as a spawn side effect of a dock, and the
world's drones were all pre-existing orphans, which is why it was the wrong host. Pick the host by
"can I make a new one right now", not by which class the question is about.

**Read "renders nothing AND logs nothing" as not-attached before reading it as broken.** A component
that is present but failing usually leaves something — an exception, a blank tab, a partial panel. A
component that was never attached leaves a completely ordinary object. Absolute silence is the
signature of absence.

**"Detach before release" only cleans objects that do not exist yet.** It is necessary and it is not
sufficient. Anything a probe touched carries that component for the rest of its life, so the
guarantee you can actually make is about a fresh world or fresh objects — which is worth stating in
release notes rather than implying.

**Check the shipped artifact, not the running world, when you want to know what a release contains.**
The running server holds objects built under older rules; the artifact holds the rules. These answer
different questions and only one of them is about the release.

## Why This Matters

Both failure modes are silent and both mimic something worse. The attach case cost a deploy and
pointed the investigation at "the drone has no client view", a genuinely hard problem that was not
happening. The detach case briefly looked like a release cut from the wrong commit, which would have
meant re-cutting and re-uploading.

They also compound with anything else that persists per instance. A world accumulates objects built
under every version of the attribute list it has ever seen, so the older the world, the more
component shapes coexist in it. On a test world that has survived a day of iteration, "what
components does a dock have?" has no single answer — it depends on when that dock was placed.

## When to Apply

- Before hosting a probe component on any class, ask whether you can create a fresh instance of it.
- When a component produces no output whatsoever — check attachment before debugging the component.
- Before a release that removes a temporary component, and when writing that release's notes.
- When a world object behaves differently from an identical-looking one placed at another time.

## Examples

The comment that records the attach direction, written after the wasted deploy
(`EcoServerMod/AdvancedElectronics/SurveyDrone.cs`):

```csharp
// v7 put the container probe here to quarantine it. That failed: the drone is not a usable
// probe host. Its window opens with NO tabs and no content at all -- adding a
// [RequireComponent] does not retroactively attach to world objects that were already
// persisted, and every drone in the test world is a pre-existing orphan. The probe
// produced no render and no log line, so it never ran. Moved to DroneDockObject in v8.
```

The detach direction, in `EcoServerMod/AdvancedElectronics/DroneDock.cs`. Commenting the line is
correct and complete for the artifact, and changes nothing about docks already in a world:

```csharp
// DETACHED for the release. The probe answered its questions [...]
// The component stays in the tree for the next binding question; re-attaching is
// uncommenting one line.
// [RequireComponent(typeof(UIShowcaseComponent))]
```

Verifying a release by its artifact rather than by the world it was deployed into:

```bash
# what the release actually contains -- the rules
git show <release-commit>:EcoServerMod/AdvancedElectronics/DroneDock.cs \
  | grep -nE "RequireComponent"

# NOT this: the running world holds objects built under older rules
```

## Related

- `docs/solutions/workflow-issues/the-control-under-test-is-not-a-readout-of-it.md` — the same
  instinct one layer in: confirm the thing you are measuring is the thing under test. Here it is
  whether the component exists at all.
- `docs/solutions/runtime-errors/initialize-exception-leaves-a-half-built-worldobject.md` — the other
  way a WorldObject ends up with fewer components than its class declares. That one throws partway
  through and leaves evidence; this one leaves none.
- `docs/solutions/conventions/eco-custom-worldobject-placement-requirements.md` — the other class of
  thing a mod WorldObject must declare for itself rather than inherit.
