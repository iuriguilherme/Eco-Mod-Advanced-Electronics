---
title: Use one consistent world-position-to-grid-column quantization function
date: 2026-07-17
last_updated: 2026-08-10
category: conventions
module: EcoServerMod
problem_type: convention
component: tooling
severity: medium
applies_when:
  - "A mod maps a float World position (Vector3.X/Z) to an integer grid column/cell, and more than one code path does that mapping"
  - "Comparing a position computed by one grid-based system (e.g. a pathfinder's waypoint) against a position or membership test owned by a different system (e.g. a district/zone lookup)"
  - "Adding a new consumer of an existing grid-cell abstraction (survey grids, pathfinding grids, chunk/region grids) in a server mod"
tags: [eco-modding, grid, quantization, pathfinding, districts, floating-point, worldposition]
related_components: [EcoServerMod/AdvancedElectronics.Navigation, EcoServerMod/AdvancedElectronics]
---

# Use one consistent world-position-to-grid-column quantization function

## Context

**Status: the inconsistency this documents is resolved. The rule stands and the codebase now follows
it.** The truncating call site was deleted with the district scaffold (`e72108c`), and every
remaining position-to-column mapping rounds. This section is kept as the incident that produced the
rule; the Guidance below is current.

During a multi-reviewer code review of the Advanced Electronics survey-drone mod (branch `feat/drone-feasibility-spike`, PR #1), the adversarial reviewer traced a real, mechanically-verifiable defect: two different pieces of the codebase map the same kind of value — a float world position's X/Z components — to an integer grid column, using two *different* rounding rules.

- `EcoServerMod/AdvancedElectronics/DistrictAssignment.cs:63` (since deleted) truncated:
  ```csharp
  var pos2i = new WorldPosition2i((int)worldPos.X, (int)worldPos.Z);
  ```
- `EcoServerMod/AdvancedElectronics.Navigation/GridPathfinder.cs:162-163` rounds:
  ```csharp
  private static GridColumn ToColumn(Vector3 position) =>
      new GridColumn((int)MathF.Round(position.X), (int)MathF.Round(position.Z));
  ```

Both call sites are quantizing the *same kind of value* (a drone's or a dock's world position) into the *same kind of thing* (an integer grid column), but a position whose fractional part is `>= 0.5` resolves to a *different* column depending on which function processed it. C#'s `(int)` cast on a `float` truncates toward zero; `MathF.Round` rounds to nearest. For `x = 5.7`: `(int)x == 5`, `(int)MathF.Round(x) == 6`.

Two concrete consequences were traced from this single inconsistency:
1. A drone the pathfinder considers to have arrived at its rounded destination column can truncate to the *adjacent* column in `DistrictAssignment.IsPositionInDistrict`, so the drone is reported as outside the very district it was just dispatched to reach — right at cell boundaries.
2. `DroneLifecycle.cs:138`'s return-to-dock arrival check compares the drone's position (which the mover snaps toward a *rounded* grid column) against `DroneDock.Position` directly (an arbitrary, not-necessarily-integer float) — if the dock isn't placed at exact integer coordinates, the two can permanently sit further apart than the default `0.1` arrival tolerance, and the drone never registers arrival.

Neither symptom is a compile error or an obviously-wrong result in isolation — each call site is internally correct. The bug lives entirely in the *inconsistency between* them, which is exactly the class of defect a single-file review or a unit test scoped to one class will not catch.

## Guidance

When more than one code path quantizes a float world position to an integer grid column (or any other single-cell-per-region mapping), **use the exact same quantization function everywhere**, expressed as one named, shared function — not reimplemented per call site, even when the reimplementation "looks" equivalent.

- Prefer `MathF.Round` over a bare `(int)` cast for float-to-int position quantization in this codebase, matching the convention in `GridPathfinder.ToColumn` (`EcoServerMod/AdvancedElectronics.Navigation/GridPathfinder.cs:207`) and `OreSensorComponent`'s column math (`EcoServerMod/AdvancedElectronics/OreSensorComponent.cs`) — a bare `(int)` cast truncates toward zero, which reads as an accidental default (nobody chooses truncation on purpose for spatial quantization) and behaves inconsistently around negative coordinates.
- If two different subsystems each already have their own working quantization function (as here: the `Navigation` project's `GridPathfinder`/`OreSensorComponent` vs. the Eco-glue `DistrictAssignment`), don't assume they agree just because both "round a position to a column." Diff the actual expressions, not just the intent.
- Where a raw, unquantized position genuinely must be compared against a quantized/snapped one (e.g. arrival detection against a placed WorldObject's exact position), either quantize both sides the same way before comparing, or make the tolerance wide enough to absorb the maximum quantization error (up to `0.5` units per axis for round-based quantization) plus any height/axis difference — an unexamined default tolerance (e.g. `ArrivalDetector.DefaultTolerance = 0.1f` in `GridPathfinder.cs:214`) tuned for "close enough" floating-point noise is not automatically wide enough to absorb a full grid-quantization step.

## Why This Matters

This class of bug is invisible to per-unit tests, because each quantization call site is *correct in isolation* — `GridPathfinderTests.cs` and `SurveyGridTests.cs` both pass, and `DistrictAssignment.cs` correctly re-queries `DistrictMap.GetDistrictAtWorldPos` on every call per its own doc comment. The defect only exists in the gap *between* two correct-looking pieces, which is exactly why an adversarial/composition-focused review pass (rather than a per-file correctness pass) is what caught it here. Left unfixed, it manifests as intermittent, boundary-dependent failures that look like flaky game-server behavior rather than a deterministic bug — a drone near a district edge sometimes reports `Unreachable` for no apparent reason, or a returning drone occasionally gets stuck retrying forever depending on exactly where a player placed the dock.

## When to Apply

- Any new Eco mod code (in this repo or a future one) that maps a `Vector3`/float world position to an integer grid cell, chunk, or column.
- Code review of a diff that adds a *second* consumer of an existing "position -> cell" concept — check whether it reuses the existing quantization function or reimplements it.
- Any arrival-detection / "has this moving thing reached this fixed point" comparison where one side of the comparison came from a grid-snapped path and the other is a raw placed-object position.

## Examples

Before (inconsistent — the code as of PR #1):

```csharp
// EcoServerMod/AdvancedElectronics/DistrictAssignment.cs:63   -- truncates
var pos2i = new WorldPosition2i((int)worldPos.X, (int)worldPos.Z);
```
```csharp
// EcoServerMod/AdvancedElectronics.Navigation/GridPathfinder.cs   -- rounds
private static GridColumn ToColumn(Vector3 position) =>
    new GridColumn((int)MathF.Round(position.X), (int)MathF.Round(position.Z));
```

After — the truncating side went away with the district scaffold rather than being converted, and
every surviving call site rounds:

```
GridPathfinder.cs:207          ToColumn      -- MathF.Round
DroneDock.cs:407-408                         -- MathF.Round
DroneLifecycle.cs:526, :787                  -- MathF.Round
OreSensorComponent.cs                        -- MathF.Round
```

The arrival half was answered structurally rather than by tuning a tolerance. Membership now has one
owner: `DroneLifecycle.IsPositionInAssignedRegion`, commented *"the single membership seam every
arrival/roam check goes through"*, delegating to the dock. Collapsing the comparison to one seam is
the durable version of "quantize both sides the same way" — there is no second side left to disagree.
`ArrivalDetector.DefaultTolerance` remains `0.1f` (`GridPathfinder.cs:284`), which is now safe because
nothing compares a rounded column against a raw placed-object position across a subsystem boundary.

## Related

- `docs/solutions/best-practices/eco-013-server-driven-movement.md` — the proven server-driven movement pattern `GridPathfinder`/`DroneMoverComponent` build on; does not itself discuss quantization consistency.
- `docs/solutions/best-practices/eco-013-reading-district-civics-data.md` — the proven district-read pattern `DistrictAssignment.cs` builds on; predates this quantization finding and does not mention it.
- Flagged in the multi-reviewer code review on PR #1 (`feat/drone-feasibility-spike` -> `main`) as two P1 findings (adversarial persona); unmerged/unfixed in the tree as of this writing (2026-07-17). `EcoServerMod/AdvancedElectronics/DroneDock.cs` (arrival-tolerance consumer) and `EcoServerMod/AdvancedElectronics/OreSensorComponent.cs` (a third, consistent rounding call site) are the other files touching this concept.
