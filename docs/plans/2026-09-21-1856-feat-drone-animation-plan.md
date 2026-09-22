---
title: Drone Animation - Plan Placeholder
type: feat
date: 2026-09-21
topic: drone-animation
artifact_contract: ce-unified-plan/v1
artifact_readiness: not-planned
product_contract_source: none
execution: blocked
---

# Drone Animation - Plan Placeholder

**This is not a plan. It is a marker saying that a plan has to be made, and that the work is
blocked until it is.**

## Why this file exists

Unity work is delayed. While it is delayed, the tree holds a contradiction about how drone
animation is supposed to be driven, and an agent that finds it reads it as a defect and starts
repairing one side against the other. It is not a defect. It is unfinished work whose design was
never settled.

The two sides:

- `Assets/Art/AdvancedElectronics/Editor/AdvancedElectronicsBuildTools.cs`, in the
  `AttachAnimatorStates` docstring, instructs wiring each state's enabled/disabled UnityEvent to
  `Animator.SetTrigger` with a static trigger name, because a custom relay component cannot be
  delivered to the client.
- `docs/solutions/architecture-patterns/client-animation-is-driven-by-name-not-by-mod-code.md`,
  `CONCEPTS.md` say otherwise, and the prefabs follow neither. No prefab contains the string
  `SetTrigger`; three of the five declare animated-state names whose enabled and disabled events
  hold no persistent calls, and the other two declare no states.

## What must happen before any of it is touched

**Research, then brainstorm, then a real plan. In that order.**

The research question is what the client actually does with `SetAnimatedState` and the
`WorldObject` UnityEvent re-broadcast, established from the Eco source checkout and from a live
client, not from the comment and not from the doc. Until that is done, **neither side is known to
be correct** — including the claim that the prefabs are the wrong side. That claim is the current
working assumption, not a finding.

The brainstorm decides what the mod's animation contract should be, given whatever the research
establishes. Only then is there something to plan.

## Do not

- Do not reconcile the docstring and the doc by editing one to match the other. Nobody has
  established which is right.
- Do not wire or strip the prefabs' state events. Their emptiness is evidence of where the work
  stopped.
- Do not treat this file as a scoped plan. It has no requirements, no sequencing, and no product
  contract, because none have been decided.

## Related

- `docs/solutions/architecture-patterns/client-animation-is-driven-by-name-not-by-mod-code.md`
- `docs/solutions/runtime-errors/override-animator-layer-without-avatar-mask-overwrites-base-layer.md`
- `docs/guides/2026-08-unity-working-guide.md`
