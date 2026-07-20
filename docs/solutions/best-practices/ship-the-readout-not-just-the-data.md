---
title: A feature whose output the player cannot read is not shipped
date: 2026-07-20
category: best-practices
module: EcoServerMod
problem_type: best_practice
component: tooling
severity: high
applies_when:
  - "Building a feature whose value is information it produces (a survey, a report, a scan, a diagnostic)"
  - "The producing side works and is tested, but no user-facing channel renders its output yet"
  - "The only surface currently showing the data is a developer diagnostic or a log line"
tags: [eco-modding, product-thinking, readout, user-facing, survey, scope]
related_components: [EcoServerMod/AdvancedElectronics]
---

# A feature whose output the player cannot read is not shipped

## Context

A survey drone was built to roam a district, sample blocks underground and accumulate
per-ore density. Movement worked, sampling worked, the pure-logic grid was unit-tested, and
the live status line confirmed `Lifecycle: Surveying, sampling=yes`.

The user's assessment was blunt and correct: *"the ore information is not sent to the dock
as required, and it's not being sent anywhere actually. if the user doesn't have access to
it, the drone is effectively not doing anything from the perspective of the user."*

Every component reported success while the feature delivered nothing. The data accumulated
on a component in server memory with no channel a player could read. The requirement had
always said results render on the dock; the build had satisfied every part of the pipeline
except the part the player touches.

## Guidance

**Treat the output channel as part of the feature, not as presentation to add later.** For
an information feature, the readout *is* the deliverable — the sampling that feeds it is
plumbing. A survey nobody can read has the same user value as no survey.

**Ship at least one channel you are certain renders.** Prefer a surface already proven in
the running system over one that is merely implemented. In this project the ordering was:

| Channel | Confidence | Notes |
|---|---|---|
| Chat command | Proven — used repeatedly in live sessions | Server-side, no client assets, no positioning |
| Server-side tooltip | Likely — engine feature, implemented, never observed | Requires the player to find and hover the object |
| Custom client script rendering world-space text | Unverified — depends on modded client scripts executing at all | Never seen working |

The feature shipped its readout as a chat command first, because the goal was for the data
to reach the user at all, not to reach them in the most elegant place. Richer surfaces stay
on the roadmap; the certain one lands first.

**Include the fields that make the information actionable, not just true.** The first
readout reported which ore and how concentrated. The user pointed out that depth was
missing and load-bearing: *"it is important information to how much work will be needed to
mine it"* — the same deposit is a very different job 4 blocks down versus 40, especially for
a player with no mining automation. Density without depth is a fact; density with depth is
a decision. Ask what the reader will *do* with the output and check every input to that
decision is present.

**Model the output on the tool the domain already has.** The game's own prospecting drills
list a column block by block, so depth is conveyed by position in the list. Matching the
established mental model ("how deep is it") mattered more than inventing a cleaner
abstraction, and it also revealed the right progression axis: tiers of the tool differ by
how deep they see.

## Why This Matters

Component-level success is a poor proxy for feature-level success, and information features
are where the two diverge most: every unit can pass while the user experiences nothing.
Because the producing side is the interesting engineering, it absorbs the attention, and the
delivery step — usually trivial — gets deferred until it is invisible.

The cost is asymmetric. Delivery is typically a small amount of work; without it, all the
work behind it is unrealised. In this case a fully functioning survey pipeline was, from the
user's seat, indistinguishable from a drone doing nothing at all.

## When to Apply

- Building anything whose value is information: surveys, scans, reports, analyses,
  diagnostics, recommendations.
- When a plan says results "render on X" and X is not yet verified working — the feature is
  not done, regardless of test coverage on the producing side.
- When the only place data currently appears is a developer diagnostic. That is a debugging
  aid, not a product surface, and it is easy to mistake for delivery because the developer
  can see the data.
- When deciding what to include in an output: enumerate the decision the reader makes, and
  confirm every input to it is present.

## Examples

Before — data reachable only by a developer diagnostic, and missing the effort dimension:

```
/drone status
  Lifecycle: Surveying, sampling=yes, homeDock=set
  Ore data: none sampled yet.
```

After — a player-facing readout, ordered by richness, carrying the dig-effort signal:

```
/drone survey
Survey results for Drone Dock -- district 'District 2'
  IronOre: densest at (26, 31), ~12%, shallowest 29 blocks deep
  CopperOre: densest at (24, 30), ~4%, shallowest 8 blocks deep
  Coverage: 37%
```

The second is not a formatting improvement. It is the difference between a feature that
exists and a feature that is usable.

## Related

- `docs/solutions/runtime-errors/hand-written-walkability-blocks-own-occupancy.md` — the
  movement bug that had to be fixed before the survey could gather anything to report.
- `docs/solutions/workflow-issues/eco-mod-batched-live-testing.md` — batching rule; the
  delivery gap was found in a live session that could have surfaced it earlier had the
  readout been part of the same batch as the sampling.
