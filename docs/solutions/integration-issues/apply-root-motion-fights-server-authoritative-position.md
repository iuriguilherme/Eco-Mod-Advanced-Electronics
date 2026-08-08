---
title: "Two writers on one transform: root motion fought the server for the drone's position"
date: 2026-08-08
category: integration-issues
module: AdvancedElectronics
problem_type: integration_issue
component: tooling
severity: high
symptoms:
  - "The parked drone sinks and clips into the dock instead of resting at its computed park height"
  - "A park-height constant improves the symptom every time it is raised but never converges"
  - "Drift is worst while a long clip plays and the server is deliberately holding still"
  - "Adjusting the prefab's root Y offset appears to do nothing at runtime"
root_cause: config_error
resolution_type: code_fix
related_components:
  - "Assets/Art/AdvancedElectronics/Editor/AdvancedElectronicsBuildTools.cs"
  - "EcoServerMod/AdvancedElectronics/DroneMoverComponent.cs"
tags: [eco-modding, unity, animator, root-motion, drone, server-authoritative, worldobject, editor-tooling]
---

# Two writers on one transform: root motion fought the server for the drone's position

## Problem

The drone is an Eco `WorldObject` whose world position is owned by the server —
`DroneMoverComponent.Tick()` writes `this.Parent.Position` and pushes the result to clients
every tick (`EcoServerMod/AdvancedElectronics/DroneMoverComponent.cs:261` and `:274`). The
client, meanwhile, plays animation clips on the same object's `Animator`. The drone prefabs
shipped with **Apply Root Motion** enabled, which means the animation clip also displaces the
GameObject's transform. Two independent authorities were writing the same value every frame,
and the position you saw was whichever of them happened to write last.

## Symptoms

The tell came before the diagnosis, and it is the part worth recognising again: **a constant
that keeps getting tuned and never quite lands.** The drone's park height above its dock went
`1f` → `1.5f` → `2f` across three commits on the `feat/drone-animation-dock-footprint` branch
(the constant is `StandingHeightAboveDock`, now at `2f`, in
`EcoServerMod/AdvancedElectronics/DroneDock.cs:591`; the history is visible with
`git log -G"StandingHeightAboveDock = " -- EcoServerMod/AdvancedElectronics/DroneDock.cs`).
Each bump made the picture better. None of them made it right. A value that improves
monotonically but never converges is not a value that is wrong — it is a value that something
else is also writing.

The rest of what was observed, over many live-test deploys with screenshots:

- The parked drone sank and clipped into the dock instead of resting at its computed park
  height, even though the server had just written that exact height.
- The drift was worst while a long one-shot clip played and the server was otherwise idle —
  docking, and the mode-select/arm-select lead-in before take-off. Those are precisely the
  windows when the mover deliberately holds still (`DroneMoverComponent.HoldFor` /
  `IsHolding`, `DroneMoverComponent.cs:190-198`), so the animation effectively had the
  transform to itself.
- Adjusting the prefab's root Y offset in Unity appeared to do nothing at all, because the
  server overwrites the root transform at runtime regardless of where the prefab authored it.

## What Didn't Work

**Raising the park-height constant.** Three rounds of it. It could not work: it moved the
starting point of a fight without ending the fight. It did leave a real fix behind — the
current `2f` is independently justified by the chassis's origin sitting at its centre with the
hull reaching about half a block below (`DroneDock.cs:579-591`) — but that justification was
found afterwards, not what the tuning was chasing.

**Adjusting the prefab's root transform offset.** Invisible at runtime. The server writes the
root position outright; nothing authored on the root survives the first tick.

**Unchecking Apply Root Motion by hand in the Editor.** This is the failure worth remembering,
because it looked like it had worked. The user later reported "turns out apply root motion
wasn't carried to the prefabs" — the uncheck landed on the scene *instance* rather than on the
prefab asset. The scene still carries the flag as a per-instance override — serialized as a
`propertyPath: m_ApplyRootMotion` / `value: 0` pair rather than an inline field, at
`Assets/Art/AdvancedElectronics/Scenes/AdvancedElectronicsScene.unity:457`, `:754` and `:985` —
while the superseded prefab copies kept in the tree still carry
`m_ApplyRootMotion: 1` (`Assets/Art/AdvancedElectronics/Prefabs/OldHarvestDroneObject.prefab:883`,
`OldMiningDroneObject.prefab:883`). A manual checkbox is also undone by the next re-import: the
FBX importer turns root motion on by default, so every regenerated drone gets it back.

## Solution

Turn root motion off, and make the tool that builds the prefabs turn it off every time. The
prefab finisher now does this in `AttachAnimatorStates`
(`Assets/Art/AdvancedElectronics/Editor/AdvancedElectronicsBuildTools.cs:689-694`), which runs
for every drone finished by **Eco Tools > Advanced Electronics > Finish All Drone Prefabs**
(`AdvancedElectronicsBuildTools.cs:149`):

```csharp
if (animator.applyRootMotion)
{
    animator.applyRootMotion = false;
    EditorUtility.SetDirty(animator);
    Debug.Log($"[AdvancedElectronics] Turned off Apply Root Motion on '{animator.name}' -- the server drives this transform, not the animation.");
}
```

It reuses the `Animator` the FBX importer already placed on the rigged root rather than adding
a second one (`AdvancedElectronicsBuildTools.cs:680-681`), so the flag it clears is the one that
actually plays. All three live drone prefabs now carry `m_ApplyRootMotion: 0`:
`Assets/Art/AdvancedElectronics/Prefabs/SurveyDroneObject.prefab:181`,
`HarvestDroneObject.prefab:119`, `MiningDroneObject.prefab:975`.

Two pieces of motion polish landed alongside it and are easy to confuse with the fix, so:
the mover now rotates yaw-only, so a climbing drone stays level like a helicopter instead of
pitching nose-up (`DroneMoverComponent.cs:270-272`), and cruise height came down from `4f` to
`2.5f` above ground (`DroneMoverComponent.cs:88`). Both improve how the drone reads. Neither
addresses the contested transform.

## Why This Works

Root motion is not decoration — it is a write. A clip authored with root displacement moves the
GameObject itself, on the client, every frame it plays. The server's authority over
`Parent.Position` does not suppress that; it merely competes with it. Whichever write lands
later in the frame is the one you see, so the visible position is the *interleaving* of two
authorities, not the output of either.

That also explains the shape of the symptom. When the server is actively stepping the drone
along a path it re-asserts its position every tick, so its writes mostly win and the error is
small and jittery. When the server holds still — docked, or waiting out a take-off clip — it
stops re-asserting, the animation's displacement accumulates unopposed, and the drone slides
into the pad. The bug was loudest exactly where the drone was supposed to be most stable, which
is why "it sinks into the dock" read as a park-height problem rather than an ownership problem.

Disabling root motion does not make the server win the fight; it ends the fight. One authority
writes the transform, animation animates the rig underneath it, and the park height is now a
number that means what it says.

## Prevention

**Recognise the signature.** When a constant improves the symptom every time you tune it but
never fixes it, stop tuning and go find the second writer. Monotonic improvement without
convergence is the fingerprint of two authorities on one value — not of a wrong value. Ask
directly: what else writes this, and when? The answer is usually a subsystem that considers the
write its own business (root motion, a physics rigidbody, a layout pass, a reactive binding,
an ORM's dirty-tracking flush).

**Where two systems touch one value, say who owns it in the code.** The mover's ownership is
now stated in a comment at the point where the flag is cleared
(`AdvancedElectronicsBuildTools.cs:683-688`) and reiterated by the log line, so the next person
to open the finisher learns the rule without having to rediscover the bug.

**Enforce ownership in the tool, not in a checkbox.** The manual uncheck failed twice over —
once by landing on a scene instance instead of the prefab asset, and structurally because FBX
re-import restores the importer default. Any setting that must hold across regeneration belongs
in the generator.

**Check the asset, not the Inspector, when verifying.** Unity prefab overrides make a scene look
correct while the asset that ships in the bundle is not. A grep over the serialized YAML is the
honest check:

```
grep -rn "m_ApplyRootMotion" Assets/Art/AdvancedElectronics/Prefabs/
```

Every prefab a server-driven object ships from should read `m_ApplyRootMotion: 0`. The `Old*`
copies still reading `1` are superseded artefacts, and are themselves a useful reminder of what
the pre-fix state looked like.

## Related Issues

- `docs/solutions/best-practices/eco-013-server-driven-movement.md` — establishes the authority
  this bug violated: the server sets `Position`/`Rotation` and syncs, as the one proven path for
  a moving object. Root motion did not contradict that contract so much as quietly opt out of it.
- `docs/solutions/architecture-patterns/client-animation-is-driven-by-name-not-by-mod-code.md` —
  the other half of this object's client/server split. That doc explains how the animation is
  driven; this one explains what the animation must not drive.
- `docs/solutions/conventions/a-fix-does-not-reach-the-copies-already-taken.md` — the general
  form of why the manual uncheck did not hold: a one-time correction does not propagate, and
  here the copies keep being remade.
- `docs/solutions/logic-errors/prefab-finisher-writes-to-the-scene-object-name.md` — same tool,
  same shape of fix. Both are properties the finisher must re-assert on every run rather than
  assume were set once.
