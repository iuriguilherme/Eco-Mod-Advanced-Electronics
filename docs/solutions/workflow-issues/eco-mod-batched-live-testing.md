---
title: Batch live tests for Eco mod development — variant objects and diagnostics, never restart-per-fix
date: 2026-07-19
category: workflow-issues
module: EcoServerMod
problem_type: workflow_issue
component: tooling
severity: high
applies_when:
  - "Developing an Eco server mod where verifying a change requires quitting the game client, restarting the dedicated/embedded server, and reloading the world"
  - "A defect can only be observed at runtime (placement, window UI, tooltips, component behavior) and the temptation is to fix one thing and ask for a restart"
  - "Uncertain between multiple candidate fixes or approaches for a runtime-only behavior"
tags: [eco-modding, workflow, testing, batching, diagnostics, variant-testing, server-restart]
related_components: [EcoServerMod/AdvancedElectronics, Assets/Art/AdvancedElectronics]
---

# Batch live tests for Eco mod development — variant objects and diagnostics, never restart-per-fix

## Context

An Eco live test is expensive: quit the game, restart the server, reload the world — many
minutes of the developer's time per iteration. During the Drone Dock debugging arc this
project fell into a change-one-thing → restart → observe loop: three consecutive restarts
each exposed exactly one more defect (the `XItem`→`XObject` naming triad, then the prefab
`WorldObject.size` of zero, then missing `[Serialized]`/`[NoIcon]` on component classes).
Every one of those was statically discoverable up front — from the game source, the vanilla
`__core__` sources, or working reference mods — so each restart bought one bit of
information that a desk audit would have produced for free. The developer named the
anti-pattern directly: "changing a line of code and restarting the server is dumbness."

## Guidance

Treat a live test as a scarce resource that must answer *every* open question at once, not
one question per restart.

1. **Audit everything statically before any deploy.** When conforming custom classes to a
   working pattern, diff **all** of them in one pass — objects, items, recipes,
   `WorldObjectComponent` subclasses, chat commands — against vanilla source and complete
   working mods, not just the class the last error message named. One missed attribute
   costs one full restart.

2. **When genuinely uncertain between N approaches, ship all N in one deploy** so a single
   test discriminates. Variants can be parallel code paths on one object or several
   sibling test objects registered side by side. Concrete example from this project: the
   dock's floating status text shipped *both* a custom MonoBehaviour renderer *and* a
   serialized persistent `set_text` listener on the same prefab in the same bundle — one
   session tells which mechanism the game client honors, with the other as live fallback.

3. **Bake diagnostics into the mod so one session yields complete information.** A chat
   command that dumps each layer's internal state in text removes all dependence on UI
   rendering for diagnosis. Example: `/drone status` reports district assignment, pairing,
   spawn state, lifecycle status, mover state, and per-ore survey data — so whether or not
   any client-side surface (world text, tooltip, window) renders, the server-side truth of
   the whole pipeline arrives labeled in chat.

4. **Verify statically everything that can be verified statically:** engine source reads,
   in-editor checks (shader resolution, persistent-listener presence via editor scripting),
   compile + unit tests, and analysis of logs from *past* runs. Only what is fundamentally
   runtime-only goes into the single batched live session.

5. **Frame the live session as the developer playing when they choose** — never as a
   verification chore the workflow assigns them.

## Why This Matters

The restart loop converts developer time into single bits of information at the worst
possible exchange rate, and it makes the developer the verification step of the agent's
iteration loop. The same information is almost always available statically (this project
has the full game source, vanilla generated sources, and several complete working mods on
disk). Batching inverts the economics: N fixes + N variants + diagnostics cost the same one
restart that a single-line fix does.

## When to Apply

- Any Eco mod change whose verification needs a server restart — batch it with everything
  else pending, and check whether the answer is statically derivable first.
- Any "does the engine honor X?" doubt — ship X and its fallback together.
- Any new runtime subsystem — add its diagnostic dump surface (chat command) in the same
  unit, before the first live test, not after a confusing one.

## Examples

The three-restart chain this rule comes from, each statically answerable:

| Restart bought | Was discoverable via |
|---|---|
| "no placement ghost" → naming triad | client source `ItemInfoExtensions.GetWorldObjectName` |
| "ghost but Place does nothing" → prefab `size` (0,0,0) | client source previewer + prefab YAML |
| "window empty" → components lack `[Serialized]`/`[NoIcon]` | vanilla component sources; the engine even logs the exact error |

Counter-example done right, same project: the fourth deploy batched the component
attributes, the shader/material fix, the Canvas reposition, both status-text variants, and
the `/drone status` diagnostic — one pending restart now adjudicates the entire remaining
acceptance list instead of one symptom.

## Related

- `docs/solutions/conventions/eco-custom-worldobject-placement-requirements.md` — the
  static conformance checklist this workflow rule says to run *in full* before deploying.
- `docs/solutions/runtime-errors/worldobject-zero-size-blocks-placement.md` — one of the
  defects a restart paid for that a prefab-YAML audit would have caught.
