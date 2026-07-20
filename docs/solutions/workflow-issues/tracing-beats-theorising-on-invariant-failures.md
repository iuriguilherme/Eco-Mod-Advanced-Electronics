---
title: Trace the failing call to its first return instead of proposing plausible causes
date: 2026-07-20
category: workflow-issues
module: EcoServerMod
problem_type: workflow_issue
component: tooling
severity: high
applies_when:
  - "A reported failure is invariant -- it does not change with distance, scale, input size, environment or data"
  - "A fix based on a plausible-sounding cause did not change the observed behaviour"
  - "Each diagnosis attempt costs the user real time (a server restart, a deploy, a manual reproduction)"
tags: [debugging, diagnosis, root-cause, workflow, eco-modding, hypothesis-testing]
related_components: [EcoServerMod/AdvancedElectronics]
---

# Trace the failing call to its first return instead of proposing plausible causes

## Context

A modded drone never moved: every dispatch reported `Unreachable`. Three consecutive root
causes were proposed and acted on, each shipped as a fix, each costing the user a full
server restart to disprove:

1. **"Implement it as an animal."** Misread of an instruction to study how animals pathfind.
   Burned a spike proving animals cannot be puppeteered — true and irrelevant.
2. **"The destination search radius is too small."** The finder did cap at 96 units.
   Disproved by placing the dock 20 metres from the target: still `Unreachable`.
3. **"Vegetation reads as solid."** Genuinely one instance of the real defect. Disproved as
   the whole story by testing in bare desert: still `Unreachable`.

The actual cause — the walkability predicate classified the entity's own occupancy block as
solid, so the pathfinder bailed at its first guard before expanding any node — was
statically discoverable from the first report. Every hypothesis was reached by pattern
matching on "what usually breaks pathfinding" rather than by reading the failing call.

## Guidance

**Trace the failing call to its first `return` before proposing a cause.** For a
`no path found` result, that means reading `FindPath` top to bottom and asking what could
make each early exit fire — not brainstorming reasons a route might not exist. The first
guard was:

```csharp
if (_sampler.IsSolidAt(startColumn...) || _sampler.IsSolidAt(goalColumn...))
    return PathResult.NotFound;
```

One question — *what does this return for the column the entity is standing in?* — settles
it. That question was available before any of the three fixes.

**Read symptom invariance as evidence about the layer.** Catalogue what the failure does
*not* depend on:

| The failure is invariant to… | Therefore it is not in… |
|---|---|
| distance to target | routing, search bounds, radius caps |
| terrain and biome | terrain-specific handling |
| scale or data volume | anything load-related |
| which entity/instance | per-instance state |

The drone's failure was invariant to distance *and* terrain from the first report. That
alone excluded hypotheses 2 and 3 before either was written.

**When a fix does not change the symptom, treat the hypothesis as refuted, not incomplete.**
The strong pull is to keep the theory and add an epicycle ("the radius fix was right, it
just also needs…"). A fix that changes nothing is evidence the model is wrong. Return to
the trace.

**Say "I do not know yet" instead of shipping a guess as a diagnosis.** A speculative fix
presented as a root cause spends the user's restart budget and teaches them to distrust the
next explanation. Shipping the 96-unit fix as "the bug" was more costly than saying the
cause was still unknown — the change itself was a genuine improvement, and framing it
honestly as an improvement rather than a diagnosis would have cost nothing.

## Why This Matters

Each wrong hypothesis in this arc cost one restart, and the environment makes restarts
expensive enough that the project already has a rule against per-fix testing loops. The
compounding damage is worse than the time: three confident, wrong explanations in a row
erode the user's ability to act on any explanation, and they end up re-testing things they
have already disproved.

The technique that finally worked — read the engine's own source, then read our failing
function line by line — was available at zero marginal cost the entire time. The bottleneck
was never information; it was reaching for an available explanation instead of a traced one.

## When to Apply

- Any bug where reproduction is expensive for the user (server restart, deploy, manual
  in-game repro, long build).
- Any failure whose description contains "always" or "never", or that survives an
  environment change the leading hypothesis predicts should matter.
- Immediately after a fix fails to move the symptom — before writing the next fix.
- When integrating against an engine or framework whose source is available: the predicate
  or contract being violated is usually readable, which makes tracing strictly cheaper than
  theorising.

## Examples

**The pattern to avoid** — hypothesis reached by association, then shipped as a diagnosis:

> Symptom: never moves, reports Unreachable.
> "Pathfinding failures are usually unreachable destinations → the destination search caps
> at 96 units → that must be it." Fix shipped and described as the root cause. User's next
> test disproves it in one move.

**The pattern that worked** — trace, then check the first thing that can fire:

> Symptom: never moves, in any biome, at any distance.
> Invariance excludes routing and terrain. Read `FindPath` → first guard is an `IsSolidAt`
> check on the start and goal columns. Ask what `IsSolidAt` returns for the entity's own
> column. Read the engine's equivalent predicate. Find that a placed object writes an
> `Occupied` block at its own position and the mod's predicate counts any non-empty block as
> solid. Root cause, no restart consumed.

## Related

- `docs/solutions/runtime-errors/hand-written-walkability-blocks-own-occupancy.md` — the bug
  this diagnosis arc was chasing.
- `docs/solutions/workflow-issues/eco-mod-batched-live-testing.md` — the sibling rule about
  batching deploys; this doc is about not spending those tests on guesses.
