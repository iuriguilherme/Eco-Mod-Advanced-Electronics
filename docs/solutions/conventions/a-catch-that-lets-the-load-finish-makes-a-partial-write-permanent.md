---
title: "A catch that lets the load finish makes a partial write permanent"
date: 2026-09-26
category: conventions
module: EcoServerMod
problem_type: convention
component: serialization
severity: high
root_cause: data_integrity
resolution_type: migration
applies_when:
  - "A migration or backfill writes persisted state item by item inside a loop"
  - "A try/catch lets the process continue after part of a persisted mutation has been applied"
  - "Deciding where to advance an id counter, cursor, or re-application marker"
  - "Adding containment around startup work so one bad record cannot abort the whole load"
tags:
  - eco-modding
  - serialization
  - persistence
  - migration
  - save-compatibility
  - idempotence
  - error-handling
related_components:
  - EcoServerMod/AdvancedElectronics
---

# A catch that lets the load finish makes a partial write permanent

## Context

v0.4.0's second save fold moved a dock's farms from their own `[Serialized]` collection onto the
one area collection. It ran per dock at world load, wrapped in a try/catch so that one malformed
dock could not take the whole server start down with it. That containment was deliberate and it is
the right call: a mod that refuses to load a world because of one bad record is worse than a mod
that loses one dock's migration.

The fold wrote each folded area into `SurveyAreas` and removed its legacy source row **inside** the
loop, and advanced the dock's `nextAreaId` counter **after** the loop
(`EcoServerMod/AdvancedElectronics/DroneDock.Migration.cs`). Reviewed on its own that reads as
correct: the counter the fold hands back is the one it minted from, so nothing a later area takes
can collide with anything this fold placed.

It is only wrong in combination with the catch. Without the catch, a throw part way through would
abort the load and nothing would persist — loud, and recoverable by fixing the dock. With the
catch, the server finishes starting and the half-finished state is saved: the areas already added
are on disk, the rows already consumed are gone, and the counter still points at an id the loop has
already handed out.

The next load then does the *correct* thing and still corrupts the save. The already-folded rows
are skipped, because their markers are set. The remaining rows mint from the stale counter, straight
onto an id an existing area already holds. Two areas share an id, and every id-keyed lookup on that
dock — assignment, deletion, reconciliation — resolves ambiguously from then on, silently, for the
life of the save.

## Guidance

**When a catch lets a partially-applied persisted mutation survive, every piece of state that
guards against re-application must advance with each item written, not once at the end.** Counters,
cursors, high-water marks, and "already done" markers are all that state. The fix here was one line
moved inside the loop:

```csharp
foreach (var folded in fold.Areas)
{
    var entry = ToAreaEntry(folded);
    this.SurveyAreas.Add(entry);

    // Advance with each area, not once when the loop finishes.
    this.nextAreaId = Math.Max(this.nextAreaId, folded.AreaId + 1);
    ...
}
```

**A catch converts "transaction aborted" into "transaction half-committed".** That is the whole
lesson. Containment around startup work is still right, but adding it changes the failure mode of
everything inside it: code that was previously allowed to assume "if I threw, nothing happened" can
no longer assume it. The two changes are usually made by different people at different times, which
is why the combination survives review — each half is defensible alone.

**The discipline this does not cover.** This corpus already carries
`docs/solutions/conventions/a-moved-serialized-member-needs-a-stand-in-under-its-old-name.md`, whose
rule is that *every early return leaves the legacy list untouched*. That is about the paths the
method chooses. This is about the path it does not choose: a throw part way through the commit,
where there is no early return to be careful about. Both are needed.

**Prefer a shape where the guard cannot lag.** Deriving the counter instead of storing it
(`max(existing ids) + 1`) removes the class of bug outright, at the cost of a scan. Where the stored
counter has to stay — because it is itself persisted and other code reads it — advancing it per item
is the smallest correct answer.

## Why This Matters

The failure has no error in it anywhere. The load completes, the log line prints, the dock's farms
are mostly there. What a player meets, days later, is an area that renames or deletes the wrong one
of two areas that share an id — which reads at the table as the mod being confused about its own
data, not as a migration that half ran once.

It is also the second time on this feature that a safety measure produced the damage. The first was
the reverse: a rule was written, tested and rendered, and simply never wired to a producer, so a
protection everyone believed was in force did nothing. A guard that silently does nothing and a
guard that silently converts a crash into corruption are the same family — the code reads as safer
than it is, and only a specific sequence shows otherwise.

## When to Apply

- Whenever a loop writes persisted state item by item and any state outside the loop records how far
  it got. Ask: if this throws on item three, what does the next run believe?
- Whenever containment is added around work that mutates persisted state. Adding the catch is a
  change to every assumption inside it, not only to the error path.
- Whenever a migration is designed to be re-runnable. Idempotence carries only as far as the marker
  that makes it idempotent; if that marker is written on a different schedule from the data it
  guards, the migration is re-runnable only when it succeeds.
- **Not** needed when the mutation is a single atomic write, or when the failure genuinely aborts the
  process before anything is persisted — but confirm the second rather than assuming it, because a
  catch anywhere up the stack is enough to make it untrue.
