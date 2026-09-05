---
title: "A cached \"nothing to do\" removes the reason to look again"
date: 2026-09-05
category: logic-errors
module: EcoServerMod
problem_type: logic_error
component: tooling
severity: high
symptoms:
  - "A farm drone that has caught up on all its work never starts again - seed arriving in linked storage, a crop ripening, and a newly assigned area all fail to wake it"
  - "No exception, no log line, no stall reason: the drone parks at its dock and the farming tab reads as settled rather than broken"
  - "The farm resumes after a server restart and then latches off again, which reads as intermittent rather than deterministic"
  - "Found by review of the commit that introduced it, not in play - the unit suite cannot reach the file it lives in"
root_cause: logic_error
resolution_type: code_fix
tags: [eco-modding, farm-drone, memoization, cache-invalidation, change-token, silent-no-op, long-lived-object, wake-up]
related_components: [EcoServerMod/AdvancedElectronics, EcoServerMod/AdvancedElectronics.Navigation]
---

# A cached "nothing to do" removes the reason to look again

## Problem

The Farm Drone's job strategy caches the answer to "what should the drone do next", so that one
tick's scan of every assigned area is paid for once instead of twice. It cached the *negative*
answer under the same rule as the positive one: a scan that found no work stored `null` and marked
the cache valid.

Every point that invalidates that cache fires only when the farm **had** work. So a farm that
caught up on everything stored "nothing to do", and then nothing in the class could ever ask the
question again. The optimisation that made idling cheap made idling permanent.

## Symptoms

**These are the predicted symptoms, not observed ones.** The defect was found in review of the
commit that introduced it and fixed before any live session, so nothing below was seen in play.
They are listed because they are what a future reader will be matching against, and because the
shape of the evidence is the useful part: this failure produces no error of any kind.

A farm that has caught up would never restart. `IsComplete` — the property the lifecycle asks each
tick to decide whether there is anything to dispatch for — reads through the cache
(`EcoServerMod/AdvancedElectronics/FarmingStrategy.cs:128`), so a latched `null` tells the
lifecycle the farm has nothing to offer, permanently. Seed arriving in linked storage, a crop
coming ripe, and a citizen assigning a fresh area would all raise the dock's wake token, and none
of them would be looked at.

Nothing would log or throw. The drone parks; the per-area reports keep showing whatever they last
said; the farming tab reads as a settled farm rather than a broken one. There is no stall reason
to display, because from the strategy's point of view nothing is wrong — it was asked whether it
had work, and it answered from a cache it believed was fresh.

It would clear on a server restart and then recur. The cache lives in plain private fields carrying
no serialization attributes (`FarmingStrategy.cs:69-82`), so a rebuilt strategy starts with an empty
cache and the farm works normally until it next catches up. A defect that heals on restart and
returns later is the kind that gets attributed to load, or to the game, rather than to a line of
code.

## What Didn't Work

**The fix that introduced it was itself correcting a real bug.** `IsComplete` originally answered
by calling the consuming path, which had side effects — it recorded a wake, restarted the sweep,
and handed out a plot — purely from being read. The lifecycle asks `IsComplete` and
`TryGetNextTarget` in the same tick, so that parked farms nobody had offered work to and paid for
the whole per-area scan twice; the class comment at `FarmingStrategy.cs:118-127` still records
why. Routing the property through a pure cached peek was the right answer. Caching the *empty*
peek was the overcorrection that rode in with it.

**Reading the cache-read line on its own.** The original was ordinary memoization:

```csharp
if (this.peekValid) return this.peekedTarget;
```

There is nothing to see in that line. It is the textbook shape, it reads correctly in review, and
it is correct for every value of `peekedTarget` except the one that means "no answer". It is only
wrong relative to what `peekValid == true && peekedTarget == null` means in this class.

**Counting the invalidation points instead of asking what triggers them.** There are four, which
looks thorough. But every one of them is downstream of the farm having had something to do: a
target was taken (`FarmingStrategy.cs:161`), a plot was worked (`:250`), an arrival failed after a
target was taken (`:682`), the hold was unloaded (`:697`). The settled state produces none of those
events, by construction — that is what being settled means. The size of the invalidation list was
never the check. What each entry is triggered *by* is.

**Expecting the unit suite to catch it.** `FarmingStrategy` is Eco-coupled — it reads `WorldTime`,
the dock, and Eco world types — so it sits outside `AdvancedElectronics.Navigation`, which is where
the farming logic that *is* covered lives and where all 88 farming tests run. The pure decision
rules were correct throughout. The bug was in what the glue asked them, and how often.

## Solution

Both fixes are in commit `6401370` on branch `feat/tech-tree-icons`. `git branch -a --contains`
reports it on that local branch only — it is not pushed, there is no PR, and nothing below
describes shipped behaviour.

### The cache must not hold an empty answer past the next reason to look

```csharp
// before
if (this.peekValid) return this.peekedTarget;
```

```csharp
// after -- EcoServerMod/AdvancedElectronics/FarmingStrategy.cs:473
if (this.peekValid && !(this.peekedTarget == null && this.ShouldScan()))
    return this.peekedTarget;
```

The class already owned a correct freshness predicate. `ShouldScan()`
(`FarmingStrategy.cs:502-505`) is true when the dock's stamped-citizen credential is still valid
**and** either the dock's wake token has moved since the last fruitless scan or the earliest crop
being waited on has come due. The bug was that the cache sat *in front of* that predicate instead
of behind it. The fix consults it whenever the cache holds nothing, and only then — a cached
target is still handed back without a second thought, so the tick-level saving the cache exists
for is untouched.

### A change token is sampled before the work it guards, not after

The same file carried the ordering half of the same mistake. `lastScanToken` was assigned the
dock's wake token *after* the scan returned, so a wake raised while the scan was running was
recorded as already-seen and dropped.

```csharp
// after -- EcoServerMod/AdvancedElectronics/FarmingStrategy.cs:479
this.tokenAtScanStart = this.homeDock.FarmWakeToken;

this.peekedTarget = this.ShouldScan() ? this.Scan() : null;
this.peekValid = true;
```

The snapshot is taken before `Scan()` and committed to `lastScanToken` afterwards, in
`SettleUntilSomethingChanges()` (`FarmingStrategy.cs:518`). A wake that lands mid-scan now survives
into the next tick.

**Verification state.** The server mod builds clean, `scripts/validate-name-match.sh` passes, and
88 farming unit tests pass — run in an isolated scratch project, because a concurrent session had
the repo's own test project mid-edit and uncompilable. None of that exercises this code path.
Neither fix has been seen in a live game session.

## Why This Works

**A memoized negative result deletes its own trigger.** Memoization is safe when the cached value
is a function of inputs the cache is invalidated on. Here the input that turns `null` into a target
is not an input at all — it is an *external event*: a chest changing, a plant ripening, a citizen
assigning ground. The class detected those events perfectly well. It just could not reach the
detector, because the cache answered first. Any cache whose stored value is "there is no answer" is
in this position: the stored value is precisely the state in which the computation most needs
re-running, and it is the state that suppresses it.

**The harm scales with how long the object lives, not with how wrong the cache is.** The same line
in a strategy that gets rebuilt each dispatch would be a one-tick staleness nobody would ever
notice. The Farm Drone's strategy is deliberately long-lived, and its own comment
(`FarmingStrategy.cs:110-115`) says why: *"A farm is never done. There is no fact about farmland for
a drone to discover that would mean 'nothing more, ever'."* `IsExhausted` is only
`!AssignedFarmAreas.Any()`, so the lifecycle keeps this strategy across idle trips rather than
rebuilding it. That is a good design decision, and it is exactly what converts a stale cache into a
permanent one.

**The sibling strategies are safe for a reason worth naming.** `MiningStrategy.IsComplete` is
`IsExhausted || holdFull` (`MiningStrategy.cs:208-209`) and `SurveyStrategy.IsComplete` is an index
comparison (`SurveyStrategy.cs:43`). Neither runs a scan to answer, because both jobs *do* finish —
a shaft bottoms out, a plot list runs to its end. Farming is the only strategy that has to
distinguish "nothing right now" from "nothing ever", and it is the only one that needs a cache to
answer cheaply. The one strategy with the hazard is the one strategy with the mechanism.

**A change-detection token records what you had seen when you started looking.** Committing the
current value after the work claims you saw everything up to the moment the work *finished*, which
is a claim the work did not earn. The window is narrow, the loss is silent, and — in a system where
the token is the only thing that will ever wake the process — the loss is permanent. Same shape as
the cache, one layer down.

**Nothing anywhere is in an error state.** The lifecycle asks a fair question, the strategy answers
from a cache it believes is fresh, the readout faithfully prints what the strategy last recorded.
Every participant is behaving correctly. That is why the failure mode is silence rather than a
crash, and why review, not logs, is where it had to be caught.

## Prevention

- **For every cache, write down what an empty cached value means, and name what invalidates it in
  that state.** If the answer is "nothing", the cache is a latch. This is one question and it takes
  ten seconds; it is the whole defence against this class.
- **Ask what triggers each invalidation point, not how many there are.** Four call sites read as
  diligence. List them and ask of each: *does this fire in the state where the cache holds nothing?*
  Here all four were downstream of having work, so all four were unreachable from the state that
  needed them.
- **Put the cache behind the freshness predicate, never in front of it.** If a class already has a
  "has anything changed?" test, an empty cache must consult it. A cached *answer* may short-circuit
  that test; a cached *absence* may not.
- **Sample a change token before the work it guards; commit it after.** State the reason in a
  comment at both ends, because the natural edit is to collapse them back together.
- **Weigh a caching change against the object's lifetime.** A cache on a per-request object is a
  micro-optimisation. A cache on an object that outlives every event it depends on is a behaviour
  change, and deserves the review a behaviour change gets.
- **A fix that makes a read pure by adding a cache needs the same scrutiny as the read it
  replaced.** This one was correct about side effects and wrong about staleness, and it arrived
  inside a commit whose stated purpose was correctness. See
  `docs/solutions/logic-errors/a-recovery-path-that-cannot-fire-in-the-state-it-exists-for.md` for
  the same compounding trap on the mining path — a fix that redirects flow into an existing state
  surfaces whatever was latent there.
- **This file is unreachable from the test suite, so review is the entire defence.**
  `EcoServerMod/AdvancedElectronics.Navigation.Tests` references only the Eco-free
  `AdvancedElectronics.Navigation` project. Anything living in the Eco-coupled strategies and
  lifecycle is confirmed live or not at all, and live confirmation costs a restart cycle — see
  `docs/solutions/workflow-issues/eco-mod-batched-live-testing.md`.

## Related Issues

- `docs/solutions/logic-errors/a-recovery-path-that-cannot-fire-in-the-state-it-exists-for.md` —
  the same family on the mining path, and the closest neighbour in this store. There, a *guard* on
  a recovery path tested state the failure had prevented from ever being set; here, an
  *invalidation* is triggered only by events the settled state cannot produce. Both are code that
  exists for a state it cannot be reached from, and both fail as a silent permanent stall rather
  than a crash. Read them as one rule with two mechanisms.
- `docs/solutions/logic-errors/comparing-a-slotted-item-by-reference-destroys-the-open-ui.md` — the
  mirror direction. That guard was keyed on something that changes more often than the state it
  protects, so it fired constantly; this cache was keyed on nothing at all, so it never fired.
- `docs/solutions/workflow-issues/a-gate-that-discovers-nothing-passes-everything.md` — the same
  abstract shape on the tooling axis: a check that cannot fire is a no-op whose silence reads as
  success. There it reads as a passing gate, here as a settled farm.
- `docs/solutions/best-practices/ship-the-readout-not-just-the-data.md` — why this defect has no
  readout to catch it. The farming tab reports each area's own stall reason, and a settled farm has
  none; a latched strategy and a genuinely quiet one print identically. A second readout on the
  strategy's side — when it last scanned, and what it is waiting for — is what would make the two
  distinguishable.
- `docs/solutions/workflow-issues/eco-mod-batched-live-testing.md` — why the Eco-coupled half of
  this feature is reviewed rather than tested, and why a defect found in review is worth more here
  than the restart cycle it saves.
