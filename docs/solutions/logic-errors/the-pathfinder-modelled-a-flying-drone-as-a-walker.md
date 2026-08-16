---
title: "A flying entity pathfound as a walking one, and why raising the step height was not the fix"
date: 2026-08-16
category: logic-errors
module: EcoServerMod
problem_type: logic_error
component: tooling
severity: high
symptoms:
  - "A mining drone digs a shaft to the tier depth and then cannot path back into the hole it just dug; the readout shows 'dispatched to area point 302,617' outbound and 'no path from 267,601 to area point 302,617' on the same trip inbound"
  - "A freshly drawn survey area on a slope reads unreachable, while a flat area of the same size dispatches normally"
  - "Non-contiguous plots inside an otherwise valid area are silently skipped as unreachable"
  - "After the step limit is raised, pathing succeeds but the flight becomes terrain-tracing: the drone descends into every pit on its route and climbs out the far side"
  - "The drone appears to clip through the edge of a stockpile while its centre visibly clears it"
root_cause: logic_error
resolution_type: code_fix
tags: [eco-modding, pathfinding, flight-profile, step-height, collider-footprint, drone-navigation, locomotion-model, pure-assembly]
related_components: [EcoServerMod/AdvancedElectronics.Navigation, EcoServerMod/AdvancedElectronics]
---

# A flying entity pathfound as a walking one, and why raising the step height was not the fix

## Problem

The mod's autonomous drones fly. The mod's pathfinder did not know that. Its move predicate
rejected any transition between two adjacent columns whose ground heights differed by more
than `maxStepHeight`, and the value fed into that predicate was one block — a walking
entity's step. Everything downstream inherited a walker's model of what movement is: what a
route may cross, how high it may rise, and what shape the route should take through the air.

The one wrong constant presented as a family of unrelated-looking bugs. The mining drone
would dig a shaft to the mining tier's depth and then be unable to re-enter it, because the
hole it had made was now a step far taller than one block. A survey area drawn across a
slope read unreachable. Non-contiguous plots were skipped. None of these look like the same
bug from the outside — one is a mining regression, one is a survey dispatch failure, one is
a plot-selection quirk — and all three were the same line of arithmetic refusing any terrain
step over one block on behalf of a machine that hovers.

The deeper problem only surfaced after the obvious fix. Raising the step limit unblocked the
pathfinder and immediately made the drone's behaviour *worse*: now willing to descend, it
traced the ground it was given, diving into every pit along the route and climbing back out.
That failure mode is invisible to the constant. No value of `maxStepHeight` expresses "fly
over the pit instead of through it", because the constant governs how far one step may rise,
not what a route is.

## Symptoms

- **The mining drone cannot return to its own excavation.** The live readout showed the same
  trip succeeding one way and failing the other: "dispatched to area point 302,617" outbound,
  then "no path from 267,601 to area point 302,617" on the return leg. The destination was
  reachable until the drone changed the terrain by working it.
- **A freshly drawn survey area on a slope is unreachable**, while an identical area on flat
  ground dispatches normally.
- **Non-contiguous plots are skipped** as unreachable during a job.
- **After the limit is raised: terrain-tracing flight.** The route descends into every
  depression on the way and climbs out again. Nothing errors; the drone simply looks wrong
  and takes far longer than the straight-line distance implies.
- **Hull clipping.** The drone's centre clears a placed object by one column while its body
  visibly passes through the object's edge.

The signature tying the first three together: they all *vary with terrain*. A route that
works on flat ground and fails on a slope, or works before excavation and fails after, is a
step/height constraint, not a predicate fault — a predicate fault is invariant. That
distinction is the differential against the other documented cause of `Unreachable`; see
Related Issues.

## What Didn't Work

**Raising `OrdinaryMaxStepHeight` alone.** This was the obvious fix and it was necessary but
not sufficient — it removed the blockage and introduced a worse behaviour in its place. The
maintainer's verdict on the deployed result, verbatim:

> "that is exactly what I expected from your fix: you're changing the behavior of the drone
> to prevent it from 'being afraid' of radical changes in height, but this is causing the
> behavior of them to be more unpleasant and unrealistic... going down and up at every pit in
> the way is not reasonable. If you have a fix for that, it's not the one currently deployed."

That judgement is the load-bearing part of this entry. The maintainer is the only instrument
that can observe the running game, so "unpleasant and unrealistic" is a requirement, not a
preference, and a fix that satisfies the tests while failing that observation is not done.

**Treating the route as a sequence of ground samples at all.** `BuildWaypoints` originally
emitted one waypoint per column at that column's ground height. That is exactly right for a
walker and exactly wrong for anything airborne, and it is not tunable into correctness — the
defect is in what the function produces, not in a number it consults.

**Routing the drone as a point.** Even with height and profile correct, a body 3.35 blocks
wide routed as a dimensionless point will graze objects its centre clears.

## Solution

Three commits on `feat/mining-drone`, all inside `EcoServerMod/AdvancedElectronics.Navigation`.

### Fix 1 — the constant models flight, and the ladder is re-based on it (`fba66fa`)

`ReturnEscalation.OrdinaryMaxStepHeight` went from `1f` to `16f`. The value was chosen rather
than invented: `16f` was already the Hover rung's height on the return escalation ladder, so
the drone's everyday capability now matches what the ladder already considered reasonable for
this machine, and it clears `MiningTierDepth` (`= 15`) with one block to spare.

This constant is the single source for the drone's ordinary climb height: it is the default
of `DroneMoverComponent.maxStepHeight`, which builds the live pathfinder, and the lifecycle
resets the mover to it explicitly.

Raising it forced the escalation ladder to be re-based. The rungs are now expressed as
multiples of the ordinary value — `HighClimb` at `OrdinaryMaxStepHeight * 2f`, `Hover` at
`* 4f`, then `Clip` and `Teleport` at `float.MaxValue` — because their old absolute values
(4 and 16) had become "escalations" that climbed *less* than the everyday rung. An existing
test, `Climb_heights_increase_monotonically`, caught that inversion immediately.

Three new pathfinder tests encode the live failures offline:
`ShaftDugToFullTierDepth_IsStillReachable_AtTheDronesOwnClimbHeight`,
`ADropOfSeveralBlocks_IsTraversable_SoASlopedAreaIsNotAWall`, and — the one worth copying —
`ShaftDugToFullTierDepth_IsUnreachable_AtAWalkingStep`, which asserts the *old* value still
fails. That makes the constant demonstrably load-bearing: a future tidy-up back to `1f`
breaks a test in CI rather than a drone on a live server two restarts later.

### Fix 2 — a route becomes a flight profile, not a ground trace (`6e30d1a`)

This is the part that matters. `BuildWaypoints` now reconstructs the column route and hands
it to `CruiseProfile`, which emits:

1. the start waypoint at its ground-relative height;
2. a climb waypoint **sharing the start's column** at cruise altitude;
3. one level waypoint per intermediate column, all at cruise altitude;
4. a waypoint above the goal's column at cruise altitude;
5. the goal waypoint back at its own ground-relative height.

Cruise altitude is the highest ground anywhere on the route, plus the standing height offset,
plus `CruiseClearance`. Because the level leg is computed from the route maximum, it passes
over every obstruction between the ends rather than tracing them — the pit problem disappears
structurally rather than being tuned away.

Making the climb and the descent their **own** waypoints, sharing their neighbour's column,
is not cosmetic. It means the drone rises before it travels instead of gaining height along a
diagonal, and that diagonal was precisely what had been reading as clipping into rising
ground — a requirement the maintainer had stated on day one ("ascent before moving forward").
Both vertical legs are conditional: a route already at or above cruise altitude emits no
redundant climb.

Endpoints deliberately keep ground-relative height, so a shaft floor fifteen blocks down is
still landed on rather than hovered over. Only the travel between the ends is lifted.

Three new tests: `APitOnTheRoute_IsFlownOver_NotDescendedInto`,
`TheClimbIsItsOwnLeg_SoTheDroneRisesBeforeItTravels` (which asserts the second waypoint
shares the first's X and Z and is higher, and mirrors that at the far end), and
`TheGoalKeepsItsGroundHeight_SoAShaftFloorIsStillLandedOn`.

One existing test had to be relaxed: `FlatGround_UnobstructedPath_ReturnsDirectWaypointLine`
asserted that X *strictly increases* per waypoint. It now asserts non-decreasing X plus a
count above two. That assertion had quietly encoded walking — under a flight profile two
consecutive waypoints legitimately share a column. A test that has to be loosened is worth
pausing on; here the loosening was the correct direction because the original assertion was a
statement about a walker, not about correctness.

### Fix 3 — route the footprint, not the centre point (`94813b4`)

The drone's Unity box collider is `3.3535576 x 1.0335332 x 2.5491564`
(`Assets/Art/AdvancedElectronics/Prefabs/MiningDroneObject.prefab`; the survey and harvest
drone prefabs carry the identical size). The pathfinder routed a point, so the hull grazed
objects the centre cleared.

`IsWalkable` now sweeps every column within `DroneClearanceRadius` of the candidate and
rejects the candidate if any swept column holds an obstacle. The radius is `1`, derived from
the collider rather than guessed: a ~1.68-block half-extent means a column at distance 1 lies
under the hull and one at distance 2 does not (2.0 > 1.68). Radius 2 would refuse any gap
narrower than five blocks, which most player builds cannot offer.

Two limits are deliberate and each has a test:

- **Clearance applies to obstacles only, never to solidity.** Terrain beside the drone is
  normal. A shaft is a 5x5 hole with solid walls; demanding a clear block on every side would
  stop the drone entering the very thing it digs, or flying along any cliff. Height governs
  terrain, and the cruise profile already lifts the route over it. Test:
  `SolidTerrainBeside_DoesNotBlock_SoAShaftIsStillEnterable`.
- **Exempt columns are skipped in the sweep, not only at the centre.** Every cell of a dock
  pad reports `Occupied`, so without this a drone standing beside its own dock finds its
  footprint overlapping the pad and refuses to move. Test:
  `ExemptColumnsAreSkippedInTheSweep_SoTheDockDoesNotWallItsDroneIn`. The grazing case itself
  is covered by `ARouteDoesNotGraze_APlacedObject`.

### Merge state

As of writing, `fba66fa`, `6e30d1a` and `94813b4` are on the local `feat/mining-drone` branch
only. They are not merged into `main`, and they are not yet on the remote:
`origin/feat/mining-drone` points at an older commit and is many commits behind the local
branch. The branch itself exists on the remote; these three commits have simply not been
pushed to it. Treat the account above as verified against the working tree, not as shipped
behaviour.

## Why This Works

**Fix 1 corrects the model's parameter. Fix 2 corrects the model.** That distinction is the
whole lesson. `maxStepHeight` answers "how much vertical change may one lateral step
include?" — a question that only makes sense for something in contact with the ground. For a
flying body, the honest answer is "that is not the constraint you have", and the honest fix
is a different function, not a different number. Fix 1 was still required (the search must be
*willing* to expand across a tall step before any profile can be built over it), but on its
own it produced the terrain-tracing flight, because it left the walker's route-shaping intact
and merely let the walker climb better.

`CruiseProfile` works because it derives cruise altitude from the whole route rather than
from a per-column sample, so obstruction between the ends is crossed by construction. And it
preserves the one thing per-column height was genuinely good at — landing accurately — by
keeping ground-relative height at the two endpoints only.

Fix 3 works because the radius is derived from a measurable property of the object (the
collider half-extent) rather than picked, and because it is scoped to obstacles. The scoping
is what keeps it compatible with Fix 2: terrain is a *height* problem the cruise profile
already solves, and treating it as a *clearance* problem too would have re-broken shaft entry.

**A locomotion model is not one number.** Permission to move (max step height), the shape of
the movement (waypoint profile), and the size of the mover (clearance) are three independent
questions. A point-mass walker got all three wrong, and fixing one exposed the next — which is
why this arrived as three commits rather than one.

**The leverage point: all three fixes are provable offline.**
`EcoServerMod/AdvancedElectronics.Navigation` is a plain C# library with, by explicit design,
zero dependency on any `Eco.*` namespace. World data enters through the `IWorldSampler`
interface, which tests satisfy with an in-memory fake. So every claim in this document — that
a 15-layer shaft is reachable at 16 and unreachable at 1, that a pit is flown over, that a
climb shares the start's column, that a footprint sweep does not wall the drone into its own
dock — is a `dotnet test` away.

That is what made three fixes possible in one sitting. Other fixes in the same session lived
in the Eco-coupled assembly and cost a live server restart each. Moving the question into the
pure layer changed the iteration cost from one-fix-per-restart to three-fixes-per-sitting.

## Prevention

**Before constraining movement, name what the entity is.** Write down whether it walks,
flies, swims, or climbs, and check every movement constant against that answer. A flying
machine limited by a walker's step height is a category error, and category errors do not
present as one bug — they present as a scatter of symptoms that look like they belong to
different subsystems. If you find yourself investigating three unrelated-looking movement
complaints in one session, suspect one shared constraint that models the wrong kind of thing.

**Mirror the engine for world facts; never for entity facts.** This is the boundary on an
existing rule in this store, and getting it wrong is how this bug happened. Eco's
`PackedPathNode.IsPathable` answers two different kinds of question in one predicate: *what
is this block* (`Is<Solid>`, `Is<Occupied>` — a property of the world, true for any entity)
and *how may a body move through it* (ground beneath it, headroom above it — a property of a
**biped**; the sibling `CalcMovability` adds rise, while footprint and route shape the engine
never models at all, which is why those were ours to invent). Copying the first is right and
is what `hand-written-walkability-blocks-own-occupancy.md` correctly prescribes. Copying the
second imports a walker's locomotion into whatever you are building.

The settlement now in the tree is close to that split: the block classification still mirrors
the engine, while the step height, the cruise profile and the clearance radius all
deliberately override it. The one block-level divergence proves the rule rather than breaking
it — `EcoWorldSampler` refuses the engine's `Constructed` rejection, because that test exists
to keep *animals* off player-built floors, and a drone is a machine that must cross a road to
reach a dock standing on one.

**When a limit blocks you, ask whether the limit or the model is wrong.** Relaxing a
threshold is cheap and often correct, but it can only ever change *how much* of the existing
behaviour you get. If the resulting behaviour is wrong in a way the constant cannot express —
as terrain-tracing flight is — the constant was never the whole defect. The test: can any
value of this number produce the behaviour I want? If no, stop tuning and change what the
function produces.

**Pin the old value with a failing test.** `ShaftDugToFullTierDepth_IsUnreachable_AtAWalkingStep`
asserts that `1f` still refuses the path. That converts "this constant matters" from a comment
into a CI gate, and it is the cheapest defence against a well-meaning future simplification.

**Audit everything derived from a base constant when you change it.** Re-basing
`OrdinaryMaxStepHeight` silently inverted the escalation ladder — two rungs that climbed less
than the everyday value. Only a pre-existing invariant test on the derived values caught it.

**Watch for tests that encode the old model.** `FlatGround_UnobstructedPath_ReturnsDirectWaypointLine`
asserting strictly-increasing X was a walker assumption wearing a correctness costume. When a
model change forces you to loosen a test, check whether the assertion was ever about the
requirement or only about the old implementation.

**Derive body-size constants from the asset, not from intuition.** `DroneClearanceRadius = 1`
is justified by the collider's half-extent and by what radius 2 would cost in navigable gap
width. That reasoning is what will stop the next person "fixing" a grazing report by bumping
it to 2.

**Keep decidable logic out of the engine-coupled assembly.** Anything expressible against an
interface instead of a live game API becomes testable, and testable means the maintainer is
not the iteration loop.

## Related Issues

- `docs/solutions/runtime-errors/hand-written-walkability-blocks-own-occupancy.md` — the other
  cause of `Unreachable`, the discriminator between them, and the doc this one scopes. That
  one is a *predicate* fault and is invariant: it fails identically at 20 metres and 2000
  metres, in grass and in desert, expanding zero nodes. This one is a *locomotion* fault and
  varies with terrain — flat works, sloped does not; before excavation works, after does not;
  and the same trip can succeed outbound and fail inbound. Its prevention rule — "when the host
  engine already implements a predicate, mirror it rather than inventing one" — is correct for
  block classification and would cause this bug if extended to locomotion; see the boundary in
  Prevention above.
- `docs/solutions/logic-errors/a-recovery-path-that-cannot-fire-in-the-state-it-exists-for.md`
  — the lifecycle half of the same live symptom. That doc's second instance names this exact
  case ("a mining drone digs the shaft that makes its own destination unpathable") and fixes
  the lifecycle so an unreachable plot becomes a recorded skip instead of a hang. Both fixes
  are correct and neither replaces the other: this one removes the cause, that one still
  earns its place for genuinely unreachable plots. Note its motivating example is now a fixed
  bug rather than inherent geometry.
- `docs/solutions/conventions/a-defensive-rule-outlives-the-danger-it-answered.md` — owns the
  return-escalation ladder that Fix 1 re-bases, so its worked example's rung values have
  moved. There is a deeper resonance worth its own look: the ladder's lower rungs were partly
  compensating for a base constraint that was itself wrong — a defensive structure standing on
  a defect rather than on a danger.
- `docs/solutions/best-practices/eco-013-server-driven-movement.md` — establishes why the
  movement model was ours to get wrong: Eco offers no lifecycle-free pathfinder, so a
  WorldObject mover means the pathing is yours. Owning the pathfinder means owning the entity
  model, and defaulting it to the engine's biped is the category error above.
- `docs/solutions/integration-issues/apply-root-motion-fights-server-authoritative-position.md`
  — the client-side half of the flight read, including yaw-only rotation so a climbing drone
  stays level. Note there are now two altitude concepts to hold together: the mover's standing
  offset and the pathfinder's cruise clearance layered on top of it.
- `docs/solutions/conventions/consistent-grid-column-quantization.md` — the other geometry
  rule living in the same file; a useful contrast in defect class, being disagreement between
  two correct-in-isolation call sites rather than one self-consistent constant modelling the
  wrong animal.
- `docs/solutions/workflow-issues/tracing-beats-theorising-on-invariant-failures.md` — owns
  the invariance heuristic, and this is the case showing the inference runs one way only. The
  failure varied sharply with terrain, which correctly excluded a predicate fault but did *not*
  clear the pathfinder: variance localises to the code consuming the varying input, narrowing
  within a module rather than pointing away from it.
- `docs/solutions/workflow-issues/eco-mod-batched-live-testing.md` — why the three-stage arc
  cost what it did, and why stage 2 was only visible because stage 1 shipped.
