---
title: "A recovery path guarded on state that only exists when nothing went wrong"
date: 2026-08-16
category: logic-errors
module: EcoServerMod
problem_type: logic_error
component: tooling
severity: high
symptoms:
  - "A mining drone hovers indefinitely on the working animation - over its dock flickering Idle/Unreachable, or over a plot it never reaches - and never docks or completes"
  - "The mining job readout sits at 'Working, worked 0, skipped 0' forever while the lifecycle readout on the same object reports skipping an unreachable plot"
  - "Unassigning the area does not clear the state; only assigning a DIFFERENT area breaks the loop, and that workaround tears down the UI and ends the live session"
  - "The server log is clean - no exception, no stack trace - and the identical tick repeats forever"
  - "The same hang recurred across several live-test sessions and took three separate commits, each fixing a different instance of one shape"
root_cause: logic_error
resolution_type: code_fix
tags: [eco-modding, drone-lifecycle, state-machine, recovery-path, guard-condition, infinite-loop, unreachable-plot, live-testing]
related_components: [EcoServerMod/AdvancedElectronics, EcoServerMod/AdvancedElectronics.Navigation]
---

# A recovery path guarded on state that only exists when nothing went wrong

## Problem

The autonomous mining drone kept reaching states it could not leave. Over a single working
session it did so in three different places, and each was fixed as its own bug before it became
clear that all three were one shape:

**A recovery path whose guard condition cannot be true in the state that needs recovering.**

Each of the three guards was written by asking "what is true here?" while picturing the happy
path — the drone parked, the drone flying, the drone mid-journey. But the code being guarded
runs precisely when the happy path did **not** happen. In each case the state the guard tested
for is state the failure prevented from ever being set, so the recovery never ran, and the
system fell straight back into the situation that called for recovery. That is a loop with no
exit, and each one of them ended a live test.

## Symptoms

Three distinct observed failures, one underlying shape.

**A drone that loops on a plot it cannot reach.** The job never advanced. `/drone status`
(`EcoServerMod/AdvancedElectronics/DroneCommands.cs`) reported
`Mining job: Working, worked 0, skipped 0`, and the dock panel's `ProgressDisplay`
(`EcoServerMod/AdvancedElectronics/MiningComponent.cs`) agreed with it, while the drone was
plainly flying around doing something.

**A drone that hovers over its own dock indefinitely,** flickering Idle/Unreachable, never
travelling anywhere.

**Unassigning the area did not release it.** Only assigning a *different* area did — a workaround
that itself re-triggered an unrelated UI defect (see
`docs/solutions/logic-errors/comparing-a-slotted-item-by-reference-destroys-the-open-ui.md`) and
so ended the session. Unassigning failing to help is itself diagnostic: it means the loop was
never driven by the assignment in the first place.

**The tell that broke the first case open: two readouts that disagreed.** The lifecycle's own
`LastDispatchNote` (`EcoServerMod/AdvancedElectronics/DroneLifecycle.cs`) said
`skipped unreachable plot 53,121`. The job ledger, in the same `/drone status` output, said
`skipped 0`. Both are printed by the same command in the same breath. One component reported
performing an action; the component that is supposed to *record* that action had no trace of it.
The bug is in the handoff between them, and the disagreement localises it to that handoff without
any further instrumentation.

## What Didn't Work

- **Fixing each one as an isolated defect.** All three were diagnosed, patched, and committed
  separately across several live-test cycles. Each fix was correct; none of them prompted a look
  at the other two recovery paths for the same mistake, which is why the third one was still
  waiting.
- **Reading the guards as they were written rather than as they fire.** Every one of the three
  guards is locally reasonable and reads fine in review. `if (currentShaftPlot is { } plot)`
  looks like ordinary defensive null-checking. `if (mover.IsMoving) { ...check arrival... }`
  looks like a sensible narrowing. They are only wrong relative to the state that reaches them.
- **Trusting one readout.** The job ledger's `skipped 0` is honest — nothing was ever recorded in
  it. Taken alone it says "no skips have happened", which is exactly backwards from the truth. It
  only becomes evidence when placed next to the lifecycle note that contradicts it.
- **Expecting the unit tests to catch it.** `EcoServerMod/AdvancedElectronics.Navigation.Tests`
  references only the pure `AdvancedElectronics.Navigation` project (see its `.csproj`), which is
  where `DroneStateMachine`, `MiningJob`, and `ShaftPlan` live and where they *are* covered.
  All three of these bugs sit in the Eco-coupled glue — `MiningStrategy` (whose own class comment
  records that it is never unit-tested, every collaborator being an Eco type) and `DroneLifecycle`
  (a `WorldObjectComponent`). The pure state machine was correct throughout. The bugs were in what
  the glue asked it.

## Solution

Three commits, all on branch `feat/mining-drone`. **None of them is merged, and none of them is
pushed** — `git branch -a --contains` reports each commit on the local `feat/mining-drone` only.
`origin/feat/mining-drone` does exist but is 26 commits behind and contains none of the three, and
there is no PR. Everything below describes the state of that branch's working tree, not shipped
behaviour.

### Instance 1 — the skip that was never recorded (`42af8dc`, `MiningStrategy.cs`)

`OnArrivalFailed` recorded the skip only when `currentShaftPlot` was set:

```csharp
// before
if (this.currentShaftPlot is { } plot) { this.job.MarkSkipped(plot, SkipCategory.Unreachable); ... }
```

`currentShaftPlot` is assigned inside `TickParkedWork`, in the branch that opens a fresh shaft —
code that runs only once the drone has **actually parked at the plot**. An arrival that *fails*
never reaches `TickParkedWork`, so the field is null in exactly the case `OnArrivalFailed` exists
to handle. Nothing was written to the ledger; the plot stayed `Unworked`; `NextPlot` offered the
same plot again on the next tick, forever.

The fix gives the strategy a memory of what it *offered*, independent of what it reached.
`TryGetNextTarget` records every plot it hands out, and `OnArrivalFailed` falls back to it:

```csharp
// after -- EcoServerMod/AdvancedElectronics/MiningStrategy.cs
public void OnArrivalFailed()
{
    var plot = this.currentShaftPlot ?? this.lastOfferedPlot;
    if (plot == null) return;

    this.job.MarkSkipped(plot.Value, SkipCategory.Unreachable);
    this.currentShaftPlot = null;
    this.lastOfferedPlot = null;
}
```

The new field carries a doc comment explaining why it exists, so the next reader does not
"simplify" it back out.

### Instance 2 — the attempt cap that one caller never charged (`7b09d47`, `DroneLifecycle.cs`)

`MaxPlotArrivalAttempts` is `5`. The counter it caps, `plotArrivalAttempts`, is owned by the
lifecycle by design — the `IJobStrategy` contract states that the strategy records the outcome and
does not count attempts itself.

The cap existed and was correctly applied on the **plot-to-plot hop** inside `TickOnStation`. It
was not applied on the **dispatch** path. `DispatchToArea`'s no-path branch called `HandleNoPath`
and returned without touching the counter, so an outbound path failure was uncounted and the loop
had no exit:

```
dispatch -> no path -> HandleNoPath -> Unreachable
  -> the Unreachable retry attempts the RETURN leg, which succeeds trivially
     because the drone is already standing at its dock -> Idle
  -> the idle-at-dock resume dispatches again -> no path -> ...
```

The fix charges the dispatch failure against the same cap and retires the plot at the limit:

```csharp
// after -- EcoServerMod/AdvancedElectronics/DroneLifecycle.cs
this.plotArrivalAttempts++;
if (this.plotArrivalAttempts > MaxPlotArrivalAttempts)
{
    this.plotArrivalAttempts = 0;
    this.strategy?.OnArrivalFailed();
}

this.HandleNoPath(mover);
return;
```

Note what this does and does not do: it does not make the plot reachable, it makes an unreachable
plot a *recorded skip* instead of a hang. A mining drone hits this where a survey drone never did,
because a mining drone digs the shaft that makes its own destination unpathable — the exact trip
that worked outbound fails on the return.

**That motivating case was itself a bug, since fixed.** The shaft was unpathable because the
pathfinder limited a flying drone to a walker's one-block step, not because excavation makes a
destination inherently unreachable; see
`docs/solutions/logic-errors/the-pathfinder-modelled-a-flying-drone-as-a-walker.md`. The fix here
stands on its own — a cap that retires genuinely unreachable plots is still required, and Instance 3
was surfaced by this fix routing traffic into it — but do not read "a drone cannot re-enter its own
shaft" as a standing constraint of the system.

### Instance 3 — arrival tested only in the branch that assumes travel (`9adaba6`, `DroneLifecycle.cs`)

`TickUnreachableRetry` tested for arrival only inside the moving branch:

```csharp
// before
if (mover.IsMoving) { if (this.IsAtHomeDock()) { ...OnReturnedToDock... } return; }
```

A drone that went `Unreachable` while already standing at its dock is **not** moving. It fell
through to the periodic retry, which re-pathed a zero-length route to where it already was,
reported success, and declared no arrival. Next tick, identical. It hovered forever. The pure
state machine was not the problem: `DroneStateMachine.OnReturnedToDock` explicitly accepts a
transition out of `Unreachable`. Nothing ever called it.

The fix checks home first and unconditionally; the moving branch is demoted to guarding only a
return leg genuinely in flight:

```csharp
// after -- EcoServerMod/AdvancedElectronics/DroneLifecycle.cs
if (this.IsAtHomeDock())
{
    this.stateMachine.OnReturnedToDock();
    this.strategy?.OnArrivedHome();
    this.ResetReturnLadder(mover);
    return;
}

if (mover.IsMoving) return; // a return leg is in flight; let it fly.
```

`IsAtHomeDock()` is a horizontal-proximity test against a fixed arrival radius, so it is
answerable at any moment regardless of whether the drone is moving — which is what makes it a
legitimate first-line guard.

## Why This Works

**The guard on a recovery path must be satisfiable in the failure state.** That is the whole
rule. Recovery code runs in the one situation the rest of the module is written to avoid, so any
precondition it inherits from the normal flow is a precondition the failure may have destroyed.
In all three instances the guarded state was set by a step the failure skipped:

| Guard | State it tested | Who sets that state | Why it is absent on failure |
| --- | --- | --- | --- |
| `OnArrivalFailed` | `currentShaftPlot` | `TickParkedWork` | Only runs after the drone has parked; a failed arrival never parks |
| dispatch no-path | `plotArrivalAttempts` (never incremented here) | the plot-to-plot hop only | The drone never got as far as a plot-to-plot hop |
| `TickUnreachableRetry` | `mover.IsMoving` | a successful path | Going Unreachable at the dock means no path was ever taken |

**The failing state is usually the *emptier* state, not a different one.** Failure tends to mean
"fewer things happened", so the fields that record progress are null, zero, or false exactly when
the recovery handler wants them. Reaching for the most specific piece of state available is the
natural instinct and the wrong one — `currentShaftPlot` is more precise than `lastOfferedPlot`,
and `mover.IsMoving` is more precise than "am I home", and in both cases the precision came from
information the failure destroyed.

**A recovery that no-ops is indistinguishable from one that succeeded — from the inside.**
`OnArrivalFailed` returning without marking anything looks, from the lifecycle's side, exactly
like a plot properly retired. The lifecycle happily reset its counter and moved on. Nothing
anywhere threw or logged. This is why the failure mode is always an infinite loop and never a
crash: every participant believes it did its job.

**The two-readout disagreement is the diagnostic that catches it.** Because nothing errors, the
only visible evidence is that two independent accounts of the same event do not match — the
actor says "I skipped that plot", the ledger says "no skips recorded". Any long-running
autonomous process worth debugging should have at least two views of the same fact (here: the
lifecycle's last-decision note and the job's plot ledger) precisely so this class of silent
no-op becomes visible. Publishing that pair side by side in one command is what turned an
invisible bug into a five-second read.

**Fixing one made the next one more frequent — this is the part worth carrying forward.**
Instance 2's fix routed *more* traffic into instance 3's latent bug. Before it, outbound dispatch
failures were an uncounted dead end; after it, they land squarely in `Unreachable`, and
"Unreachable while standing at the dock" went from a corner case to the common case. A latent
guard bug is a trap primed by whatever later change starts using that path — so a fix that
redirects flow into an existing state should be followed by a read of everything that handles that
state, before the next test run, not after it.

## Prevention

- **Ask "what is true when this fires?", never "what is true when things are going well?"** For
  every guard on an error handler, failure callback, retry, timeout, cleanup, or compensating
  action, name the concrete failure that calls it and walk backwards to find which assignments
  never executed. If the guard reads a field set by a step the failure skipped, the guard is
  dead code.
- **Grep the recovery surface as a set, not one handler at a time.** In this module that surface
  is `OnArrivalFailed`, `HandleNoPath`, `TickUnreachableRetry`, `AttemptReturnLegOnly`,
  `OnEnded`, and `OnUnloadRefused`. When one of them is found to have a guard bug, read the
  others in the same pass. All three of these were live simultaneously and were found one live
  test at a time.
- **Every cap must be charged by every caller that can reach the capped situation.** Instance 2
  was not a missing cap, it was a cap with one uncharged entry point. When adding a counter with
  a limit, enumerate the paths that produce the event and confirm each increments it. A cap that
  one caller bypasses is not a cap; it is a cap plus an infinite loop.
- **Check state first, movement second.** Whenever a state machine can be entered *without*
  performing the action that normally precedes it, the terminal-condition test must come before
  the in-progress test. `IsAtHomeDock()` is a position question and is answerable in any state;
  `mover.IsMoving` is a process question and only makes sense once a process started. Prefer the
  question with fewer preconditions as the outer guard.
- **Give autonomous processes two independent readouts of the same fact, and print them
  together.** One actor-side ("what I last decided") and one ledger-side ("what is recorded").
  A disagreement is a silent no-op somewhere in the handoff between them. This is the extension
  of `docs/solutions/best-practices/ship-the-readout-not-just-the-data.md` that specifically pays
  off in loop diagnosis.
- **"Unassigning does not clear it" localises the bug.** If removing the input does not stop the
  behaviour, the loop is not driven by the input. That single observation rules out the entire
  assignment/change-detection path and points at the internal state machine — worth reaching for
  before the next restart is spent.
- **After a fix that redirects flow into an existing state, audit that state's handler before
  testing.** This is the compounding trap. Newly-common paths surface latent bugs that the old
  traffic pattern kept hidden, and each surfacing costs a full live-test cycle.
- **These three paths remain unreachable from the test suite.** `MiningStrategy` and
  `DroneLifecycle` are Eco-coupled and cannot be exercised by
  `EcoServerMod/AdvancedElectronics.Navigation.Tests`. Confirmation is live-only, so the review
  question above is the entire defence.
- **This code is the crown jewel.** The mover/lifecycle path is the most valuable and most
  fragile code in the repo — commit before touching it, so a failed experiment costs a
  `git checkout` rather than a reconstruction.

## Related Issues

- `docs/solutions/logic-errors/comparing-a-slotted-item-by-reference-destroys-the-open-ui.md` —
  the mirror image of one rule, from the same session and the adjacent file. That doc's guard was
  keyed on something that changes *more* often than the state it protects, so it fired constantly
  and destroyed live UI; these guards are keyed on state the failing path never sets, so they
  never fire at all. Read as one rule with two failure directions. It is also the UI defect that
  the "assign a different area" workaround kept re-triggering, which is what actually ended these
  sessions.
- `docs/solutions/workflow-issues/a-gate-that-discovers-nothing-passes-everything.md` — the same
  abstract shape on the tooling axis: a condition that cannot be true is a silent no-op whose
  silence reads as success. There it reads as a passing gate; here as a successful recovery. That
  doc's family of examples is entirely tooling; this is the first runtime member.
- `docs/solutions/conventions/a-defensive-rule-outlives-the-danger-it-answered.md` — **qualified by
  this learning.** Its worked example retires the dock's removal escape hatch on the grounds that
  the return-leg escalation means a return can no longer fail. Stranding reappeared anyway, one
  layer up: an escalation in the movement layer cannot save a drone whose state machine never
  reaches it. The abstract rule stands; the example needs the caveat.
- `docs/solutions/runtime-errors/hand-written-walkability-blocks-own-occupancy.md` — differential,
  and the likeliest mis-diagnosis, since it is the top hit for "unreachable" in this store. There
  the pathfinder wrongly produces Unreachable; here a legitimate Unreachable is mishandled by the
  lifecycle. The tell: a pathfinder bug fails at any distance from the first dispatch, while this
  one lets some plots work and freezes the job ledger.
- `docs/solutions/workflow-issues/the-control-under-test-is-not-a-readout-of-it.md` — differential
  on reading instrumentation. There a control lies because it renders its own optimistic state;
  here two honest readouts on the same side of the wire disagree, and the disagreement is what
  localises the defect to the write path between them.
- `docs/solutions/workflow-issues/tracing-beats-theorising-on-invariant-failures.md` — the method
  that closes instance 1: follow the reported action to the code that should have recorded it,
  rather than proposing causes.
- `docs/solutions/best-practices/ship-the-readout-not-just-the-data.md` — owns the readout rule;
  this doc adds the corollary that *two* readouts of one fact are what make a silent no-op
  visible.
- `docs/solutions/workflow-issues/eco-mod-batched-live-testing.md` — why three separate diagnoses
  cost three restart cycles, and why the compounding trap above is a batching hazard.
- `docs/solutions/best-practices/eco-013-server-driven-movement.md` — why `DroneLifecycle` is a
  `WorldObjectComponent` with its own `Tick`, which is what makes every one of these loops a
  per-tick loop.
