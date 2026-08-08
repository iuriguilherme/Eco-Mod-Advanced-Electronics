---
title: "A correct animation was computed and then painted over by an unmasked Override layer"
date: 2026-08-08
category: runtime-errors
module: AdvancedElectronics
problem_type: runtime_error
component: tooling
severity: high
symptoms:
  - "The drone appears permanently stuck in one base-layer pose and never visibly plays its flight states"
  - "A second Animator layer, driven by the same parameters, animates correctly"
  - "Instrumentation confirms every input arriving correctly while the visible result stays wrong"
root_cause: config_error
resolution_type: config_change
related_components:
  - "Assets/Art/AdvancedElectronics/Animators/HRVSTR_Animator_Controller.controller"
  - "Assets/Art/AdvancedElectronics/Sprites/HRVSTR/HRVSTR_BladesMask.mask"
tags: [eco-modding, unity, animator, avatar-mask, animation-layers, override-blending, drone]
---

# A correct animation was computed and then painted over by an unmasked Override layer

## Problem

The HRVSTR drone's Animator Controller (`Assets/Art/AdvancedElectronics/Animators/HRVSTR_Animator_Controller.controller`) drives two layers: a base layer that holds the body and flight states, and a second layer ("Blades Layer") that spins the four propellers. The Blades Layer ran at full weight with Override blending and no Avatar Mask.

In Unity, an Override layer at weight 1 with no Avatar Mask replaces the pose of every bone its clips touch — the mask is the only thing that narrows an Override layer's authority to a subset of the rig. That is general engine behaviour, not something specific to this repo, and it is easy to miss because nothing errors, nothing warns, and the Animator window shows both state machines advancing exactly as designed.

So the base layer was working the entire time. Its state machine was ticking, its transitions were firing, its clips were being evaluated. The result was computed and then discarded before it reached the renderer.

## Symptoms

The drone appeared permanently stuck in one base-layer clip — docked or mode-select — and never visibly played any of its flight states, while the propellers spun correctly and responded to state changes. Every diagnostic pointed the wrong way:

- Server state was changing correctly throughout. A temporary chat-notification diagnostic confirmed the animator booleans flipping exactly as designed.
- The Animator Controller's parameters and transitions were correct.
- The second layer, driven by the same parameters, behaved perfectly.

Every input was right and the output was wrong. That combination is the signature of this whole class of bug.

## What Didn't Work

The first theory was that the `00_Docked` clip is zero-length (0 to 0 frames) and that its outgoing transitions, which use exit time, could therefore never reach their exit threshold — trapping the state machine in the docked state. The proposed fix was to uncheck "Has Exit Time" on those transitions.

That theory was wrong, and it was killed by an observation the user volunteered rather than by any further code reading: **the same custom boolean parameter drove the propeller layer correctly.**

That single fact does a lot of work. Animator parameters are controller-scoped, not layer-scoped — structurally so. In the serialized controller, `m_AnimatorParameters` hangs off the `AnimatorController` object (`HRVSTR_Animator_Controller.controller:605`) while `m_AnimatorLayers` is a sibling list (`:636`); there is no per-layer parameter table for a value to get lost in. A parameter that reaches one layer reaches all of them. In this controller both layers even leave their respective `00_Docked` states on the identical condition — `IsAtHomeDock` is false (`m_ConditionMode: 2`) — on the base layer at `:522-534` and on the Blades Layer at `:367-379`.

So the propellers spinning proved that the parameter plumbing, the server-to-client state relay, and the condition evaluation were all fine. The exit-time theory was addressing a stage of the pipeline that had already been demonstrated to work. The question changed from "why isn't the state machine advancing?" to "the state machine is advancing — what is discarding the result?"

That is the reusable move, and it is worth more than the fix itself: **when one subsystem works and a parallel one doesn't, and both are fed by the same inputs, the working subsystem exonerates everything upstream of the split. The fault has to live downstream of where the two paths diverge.** Here the paths diverge at the layer boundary, which is precisely where the bug was.

## Solution

Author an Avatar Mask that enables only the propeller bones, and assign it to the Blades Layer.

The mask lives at `Assets/Art/AdvancedElectronics/Sprites/HRVSTR/HRVSTR_BladesMask.mask`. It sets `m_Weight: 1` on the root and on the four propeller transforms — `HRVSTR_Armature/Drone_Base/Blades_BL`, `Blades_BR`, `Blades_FL`, `Blades_FR` — and `m_Weight: 0` on every other transform in the rig: the armature root, `Drone_Base`, both arm chains with their collectors and drills, `Storage`, and `HRVSTR_Mesh`.

The controller change is a single reference. The long hex string below is the mask's Unity asset
GUID — it matches `HRVSTR_BladesMask.mask.meta`, and is not a commit hash:

```diff
   - serializedVersion: 5
     m_Name: Blades Layer
     m_StateMachine: {fileID: 8204945164263862138}
-    m_Mask: {fileID: 0}
+    m_Mask: {fileID: 31900000, guid: 9cc93b5b90328d2488dde0f6f86eb651, type: 2}
```

(The rest of that diff is a state node moving 30 pixels in the Animator graph.) The current state of the layer block, at `HRVSTR_Animator_Controller.controller:649-660`, reads `m_Name: Blades Layer` with the mask assigned, `m_BlendingMode: 0` (Override), and `m_DefaultWeight: 1`. The base layer at `:637-648` still carries `m_Mask: {fileID: 0}` and `m_DefaultWeight: 0`, which is correct and expected — a base layer needs no mask because it has nothing above it to defer to, and Unity serializes the base layer's weight as 0 while treating it as implicitly 1.

**As of this writing the fix is uncommitted working-tree state on the `feat/drone-animation-dock-footprint` branch**: the controller shows as modified and the mask asset (with its `.meta`) is still untracked. It is verified working in game but is not yet in any commit, so a fresh clone of the branch does not have it.

A practical speed bump, reported during the session: an Avatar Mask has to exist as an asset before it can be assigned. The layer's mask slot offers nothing to pick from until you create one (Assets > Create > Avatar Mask, or by importing from the model's rig), which reads at first glance as "this Unity version has no mask option here."

Per this session's testing, the user tried three candidate changes and reported that the first made the base layer's animations work, the second changed nothing noticeable, and the third made both layers work properly.

## Why This Works

Unity composites Animator layers in order. Each layer above the base contributes its pose to the running result, and how it contributes is decided by three settings together: blending mode, weight, and mask.

- **Blending mode** decides the operation. Override replaces the accumulated value; Additive adds a delta on top of it.
- **Weight** decides how much of that operation applies. At 1, an Override layer replaces completely.
- **Mask** decides *where* it applies. Without a mask, the answer is "everywhere the layer's clips have curves."

An Override layer at weight 1 with no mask is therefore the maximal case: it wins on every bone its clips animate. Because the Blades Layer's clips are exported from the same rig, they carry curves for the whole skeleton — including the body bones the base layer was animating — so the base layer's contribution was overwritten wholesale, every frame. The state machine kept running; the pose it produced never survived to the renderer.

The mask removes the overreach without touching anything else. The Blades Layer still overrides at full weight, but now only on four transforms, so the base layer's body and flight pose passes through untouched. Nothing about the state machines, parameters, transitions, or server-side logic had to change — because none of it was ever broken.

## Prevention

**Whenever you add a second Animator layer, set its Avatar Mask in the same action.** Treat "new Override layer" and "new mask" as one step, not two. An Override layer that legitimately wants the whole rig is rare enough that it should be the case you justify, not the default you inherit.

**Audit layers by reading the asset, not the Inspector.** The three fields that matter are adjacent in the serialized YAML and take five seconds to check: for each entry in `m_AnimatorLayers`, look at `m_BlendingMode` (0 = Override, 1 = Additive), `m_DefaultWeight`, and `m_Mask` — where `{fileID: 0}` means *no mask*. The combination `m_BlendingMode: 0` plus `m_DefaultWeight: 1` plus `m_Mask: {fileID: 0}` on any non-base layer is a red flag worth confirming deliberately.

**Recognize the "correct input, wrong output" shape and jump downstream immediately.** When instrumentation shows every input arriving correctly and the visible result is still wrong, stop adding instrumentation upstream. The bug is not in producing the value; it is in something later consuming, overwriting, or discarding it. Confirming the same inputs a third time costs a debugging cycle and rules nothing out. In rendering and animation this shape is common: a compositing stage, a blend, a later write, or a higher-priority source painting over a correct result.

**Use a working sibling as a bisector.** If two consumers share a source and one of them behaves, you have a free experiment that has already been run: everything up to the fork is proven good. Ask where the two paths diverge and start there. In this case the divergence point *was* the answer.

**Be suspicious of a theory that no evidence has actually tested.** The zero-length-clip and exit-time theory was mechanically plausible and completely untested — it was assembled from reading the asset, not from an observation that demanded it. The observation that did exist, the propellers spinning, contradicted it outright and had been available the whole time. Before spending a fix on a theory, check it against what has already been seen.

## Related Issues

- `docs/solutions/integration-issues/apply-root-motion-fights-server-authoritative-position.md` — the other silent-overwrite bug on this same rig, found the same night. That one contested the object's transform; this one contested its bone poses. Same shape, different resource, different fix.
- `docs/solutions/workflow-issues/validate-the-instrument-before-the-hypothesis.md` — the general form of what the propeller observation did here. Worth reading first if the symptom is "my instrumentation says everything is fine."
- `docs/solutions/workflow-issues/tracing-beats-theorising-on-invariant-failures.md` — the general case against the move that cost the time here: building a mechanism from reading assets rather than from an observation that demanded it.
- `docs/solutions/architecture-patterns/client-animation-is-driven-by-name-not-by-mod-code.md` — scene-setting only, not a shared cause. It covers how server state reaches these parameters in the first place; this doc starts after that has already worked.
