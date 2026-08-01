---
title: "A mod tab writes every editable member back at once, so N controls cannot share one field"
date: 2026-07-31
category: runtime-errors
module: EcoServerMod
problem_type: runtime_error
component: tooling
severity: high
symptoms:
  - "A checkbox ticks, then unticks itself a fraction of a second later"
  - "Clicking one control appears to do nothing, with no exception and no log line"
  - "The value a control writes is immediately replaced by the value of a sibling control"
  - "Server-side state ends up cleared after an interaction that should have set it"
root_cause: wrong_api
resolution_type: code_fix
applies_when:
  - "Giving a mod component one editable control per object (per area, per slot, per target)"
  - "Two or more editable members whose setters write the same underlying field"
  - "A control that behaves as though its setter never ran"
tags: [eco-modding, worldobjectcomponent, autogen, editable-members, rpc, shared-state, ui-design]
related_components: [EcoServerMod/AdvancedElectronics]
---

# A mod tab writes every editable member back at once, so N controls cannot share one field

> **Path convention.** Paths beginning `Server/` refer to the **Eco game source in the local Eco
> checkout, external to this repo** — they will not resolve here, by design. Paths beginning
> `EcoServerMod/` or `docs/` are in-repo.

## Problem

Six checkboxes, one per survey area, each deriving from and writing to the dock's single
"assigned area" field. Clicking one ticked it, then unticked it within a second, and nothing was
ever assigned. The writes were not failing — **every checkbox was writing on every click**, and the
last one to arrive said "off".

## Symptoms

The control responds, so the tab looks alive:

- The box ticks immediately (the client renders the toggle locally), then reverts on the next
  server push about a second later.
- No exception, no client log entry, no server log entry.
- The underlying field ends up **cleared**, not merely unchanged — worse than a no-op.

A server-side diagnostic line printed from the dock's refresh tick, after a single click on the
first checkbox:

```text
[diag] setter calls: 6 | last: pos6=False | ready: True | dock assigned id: 0 | box1 reads: False
```

Six setter invocations from one click. The last was position 6 with `false`, and the shared field
finished at `0`.

## What Didn't Work

- **Blaming the attribute.** Three shapes were tried across three restarts —
  `[SyncToView, Autogen, AutoRPC]`, `[Eco(false)]`, and `[Serialized, Eco]` — on the theory that the
  write path was not being generated. Only the third delivers writes at all (see
  `docs/solutions/runtime-errors/autogen-template-binding-contract.md`), but with six sharing one
  field it made no observable difference, because the stomping happens *after* the writes succeed.
- **Blaming an initialization gate.** A `ready` flag added to guard deserialization became a second
  candidate cause in the same build, so a null result no longer had one explanation. It was fine.
- **Making the siblings idempotent.** Comparing the incoming value against the derived state, so a
  sibling writing `false` when it already reads `false` returns early, is the correct instinct and
  the same guard the material picker uses against the refresh tick. It did not fix this. The
  client's model goes stale the moment the server derives a different answer, so a later batch can
  still arrive carrying values that *do* differ from the new truth.
- **Reading the tick mark.** Every round before the diagnostic measured the client's drawing, which
  is optimistic and reports intent rather than effect. See
  `docs/solutions/workflow-issues/the-control-under-test-is-not-a-readout-of-it.md`.

## Solution

Collapse the N controls into **one** member holding the value.

```csharp
// BEFORE -- six members, one shared field. One click writes all six.
[Serialized, Eco, UITypeName("Boolean"), VisibilityParam(nameof(AreaExists1))]
public bool AssignArea1 { get => this.IsAssigned(1); set => this.SetAssigned(1, value); }
// ... AssignArea2 .. AssignArea6, each writing dock.AssignSurveyArea(...)

// AFTER -- one member. Nothing to be stomped by.
[Serialized, Eco, Range(0, MaxSurveyAreas), UITypeName("Int32")]
public int AssignedPosition
{
    get
    {
        if (this.Parent is not DroneDockObject dock) return 0;
        var area = dock.AssignedSurveyArea;
        return area == null ? 0 : dock.SurveyAreas.IndexOf(area) + 1;
    }
    set
    {
        if (!this.ready) return;                       // deserialization, not a player
        if (this.Parent is not DroneDockObject dock) return;
        if (this.AssignedPosition == value) return;    // batch write-back of an unchanged value

        dock.AssignSurveyArea(
            value <= 0 || value > dock.SurveyAreas.Count ? 0 : dock.SurveyAreas[value - 1].Id);
        this.RefreshAll();
    }
}
```

Shipped at `EcoServerMod/AdvancedElectronics/SurveyComponent.cs`. Reserving `0` for "unassigned"
means the same control also clears the field, so no second control is needed for the inverse.

The `AssignedPosition == value` early return stays. It is not what fixed the bug, but a batch still
re-writes this member with its current value on every interaction, and without the guard each one
would run a redundant assignment and refresh.

## Why This Works

An interaction with a mod component tab does not send one property write. The client writes back
**every editable member it holds**, in declaration order. With six members deriving from one field,
one click produces:

```text
pos1 = true    -> AssignSurveyArea(area1.Id)   field := 1
pos2 = false   -> AssignSurveyArea(0)          field := 0
...
pos6 = false   -> AssignSurveyArea(0)          field := 0
```

Every write succeeds. The field simply ends at whatever the last member says, and for a
one-of-N control the last member always says "not me". The more controls, the more reliably the
intended write is destroyed.

One member has no siblings, so the batch contains exactly one write for that field and it is the
player's. This is a property of the design, not a guard that has to hold — which is the difference
between a fix and a mitigation.

It also explains an earlier, unexplained observation: derived checkboxes that "did nothing" in two
prior probe rounds were doing this the whole time.

## Prevention

- **One field, one editable member.** Before adding an editable control, ask what field its setter
  writes and whether anything else writes the same one. If yes, the design is already broken; make
  it a cursor (`Int32`), a picker, or a single commit action instead.
- **Prefer a value control to a control-per-object.** A stepper costs one row whatever the object
  count, so it removes both this failure and the layout pressure that motivates a per-object pool.
  Reserve one end of its range for the "none" state and the inverse action costs nothing.
- **Never conclude a setter did not run from the control's appearance.** Count invocations
  server-side. A counter incremented at the top of the setter, printed by a readout the object's
  tick already refreshes, distinguishes "never invoked", "invoked and refused", and "invoked and
  overwritten" — three states that look identical on screen.
- **Treat "it reverts after a moment" as a write ordering problem, not a binding problem.** A
  binding failure drops the write; this drops the *result*. The revert timing matches the object's
  refresh interval, which is the tell.

## Related

- `docs/solutions/runtime-errors/autogen-template-binding-contract.md` — the single-member binding
  rules: which attribute shapes deliver writes at all. That doc governs whether one control works;
  this one governs whether several can coexist.
- `docs/solutions/workflow-issues/the-control-under-test-is-not-a-readout-of-it.md` — why this took
  several restarts: the client's tick mark is not a readout of server state.
- `docs/solutions/design-patterns/vertical-stack-only-ui-design.md` — the layout consequence. Its
  "one control per object" guidance is corrected there in light of this finding.
