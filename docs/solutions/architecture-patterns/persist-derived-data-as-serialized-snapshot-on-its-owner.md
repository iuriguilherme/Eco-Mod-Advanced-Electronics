---
title: Persist derived aggregate data as a serialized snapshot on its owning entity
date: 2026-07-26
category: architecture-patterns
module: EcoServerMod
problem_type: architecture_pattern
component: tooling
severity: medium
applies_when:
  - "A mod accumulates derived data (survey findings, statistics, a computed index) that should outlive a session or a transient assignment"
  - "The raw accumulator is a rich in-memory structure that is awkward or expensive to serialize directly"
  - "Deciding what event should reset the data — and discovering that 'reset on reassign' is the wrong trigger"
tags: [eco-modding, serialization, persistence, derived-data, snapshot, worldobject, threadsafe, data-lifetime]
related_components: [EcoServerMod/AdvancedElectronics, EcoServerMod/AdvancedElectronics.Navigation]
---

# Persist derived aggregate data as a serialized snapshot on its owning entity

## Context

The survey drone accumulates ore findings as it roams. Two questions had to be answered
together, and the first wrong answer to each is the tempting one:

1. **Where does the data live, and what serializes it?** The natural first build put the
   accumulator (a `SurveyGrid`, later `SurveyRecord`) on the *drone's sensor component* and
   never persisted it — "the world already stores block data, so a survey is session-scoped."
2. **What event resets the data?** The first build reset the accumulator whenever the drone
   was **reassigned** to a different area ("fresh area → fresh survey").

Both were wrong, and live testing exposed why. Reassigning the drone to area B and back to
area A lost A's findings — the player experienced it as "the drone goes to the new area but no
longer surveys," because the data was tied to the *drone's current assignment*, not to the
*area that produced it*. And because nothing was serialized, a restart lost everything even
though the areas themselves (serialized on the dock) survived.

The design changed to: **findings belong to the survey area, available until that area is
deleted or edited.** This document is the shape that requirement forced.

## Guidance

**Split a non-serialized live accumulator from a serialized snapshot on the owning entity,
and tie the data's lifetime to the owner's lifecycle — not to transient state.**

Three moving parts:

1. **Live accumulator — rich, in-memory, NOT serialized.** Keep the structure that does the
   real math (dedupe by position, per-plot concentration, argmax) as a plain in-memory object,
   keyed by the owning entity's id. It is the running work, not the durable record. In this
   mod that is `SurveyRecord` (`EcoServerMod/AdvancedElectronics.Navigation/SurveyRecord.cs`) —
   an `Eco`-free class holding `Dictionary<int, ...>` keyed by `areaId`, deliberately never
   `[Serialized]`.

2. **Serialized snapshot — flat, minimal, ON the owning entity.** Store a projected, flattened
   copy of the *finished* result on the entity that owns the data, using only serializer-safe
   primitives. Here that is `OreFindingSnapshot` (a plain `[Serialized]` class of `string`/`int`/
   `float`) held in a `ThreadSafeList<OreFindingSnapshot>` on `SurveyAreaEntry`
   (`EcoServerMod/AdvancedElectronics/SurveyAreaEntry.cs`). Because it lives on the already-
   serialized area entry, it persists exactly when the area does and is discarded with it — no
   separate registry, no separate lifetime to manage.

3. **A projection step that folds live → snapshot, guarded against clobber.** On a throttled
   tick, project the live accumulator's current result for the owning entity into that entity's
   snapshot. Guard it so an *empty* accumulator (e.g. right after a restart, before any new work)
   cannot overwrite a previously-persisted snapshot: `DroneDock.PersistAssignedAreaFindings`
   skips the write when coverage is 0.

**Reset on the owner's lifecycle events, never on transient re-targeting.** The data resets when
the area is **edited** (its geometry is redrawn — effectively a new area) or **deleted**, and on
nothing else. Reassigning the drone between areas is *not* a reset — it only changes which entity
new samples are attributed to. `SurveyAreaEntry.SetPlots` clears the snapshot as part of a redraw;
`DroneDock.ClearSurveyData` clears both snapshot and live record on edit and delete; the dispatch
path that used to call `ResetSurvey` on reassign no longer does.

**Read the durable snapshot, not the live accumulator, everywhere the data is displayed.** The
tab text, the world-space readout, the tooltip, and the chat commands all read the entity's
snapshot. This is what makes the data visible even when the drone is between areas or docked, and
what makes it survive a restart — the readout is driven by the persisted copy, not by whether a
producer is currently running.

## Why This Matters

- **The reset trigger is a data-ownership question in disguise.** "Reset on reassign" felt right
  until you notice it binds the data's lifetime to the *consumer's* state (the drone's current
  target) rather than the *owner's* (the area). Once the requirement is "data belongs to the
  area," the correct reset events fall out mechanically: only events on the area itself (edit,
  delete) reset it. Getting this wrong is not a rendering bug — it silently destroys real user
  data on an ordinary action (switching targets).
- **Direct serialization of the accumulator is the trap you avoid.** The live structure here uses
  a `HashSet` of block positions and nested dictionaries for dedupe and concentration — awkward
  and heavy to serialize, and it would drag `Eco.Shared.Serialization` into an intentionally
  `Eco`-free, unit-tested library. Snapshotting the *result* keeps the math library pure and
  serializes a handful of primitives instead of the whole working set.
- **The clobber guard is not optional.** Without "skip persist when the accumulator is empty," the
  first post-restart tick would project an empty live record over the persisted snapshot and erase
  exactly the data you serialized it to preserve, before the producer had a chance to refill it.

## When to Apply

- When accumulated/derived data must **outlive a session or a transient assignment** but the raw
  accumulator is expensive or awkward to serialize.
- When you catch yourself resetting data on a **re-targeting / reassignment / selection-change**
  event — stop and ask whether the data belongs to the thing being retargeted *away from*. If so,
  reset only on that thing's own lifecycle events.
- When a computed result is displayed from **multiple surfaces** (tab, tooltip, world text, chat):
  point them all at one persisted snapshot so the display is consistent and does not depend on a
  producer being live.

Do **not** reach for this when the world (or another authoritative store) already holds the data
and re-deriving it is cheap and immediate — that is the case that justified the *original*
session-scoped decision. The pattern earns its keep only once "available until the owner changes"
is a real requirement.

## Examples

**Wrong — accumulator on the consumer, reset on reassign, never persisted:**

```csharp
// On the drone's sensor: one global grid, reset when the dock's assignment changes.
private SurveyGrid surveyGrid;                 // in-memory, tied to THIS drone
public void ResetSurvey() => this.surveyGrid = new SurveyGrid(...);

// In dispatch: reassigning wipes the previous area's data.
private void DispatchToArea(...) {
    sensor.ResetSurvey();                       // ← destroys area A's findings on switch to B
    ...
}
```

**Right — live accumulator keyed by owner id, snapshot on the owner, reset on owner events:**

```csharp
// Live accumulator: on the OWNER (dock), keyed by area id, NOT serialized.
private SurveyRecord surveyRecord;
public SurveyRecord SurveyRecord => this.surveyRecord ??= new SurveyRecord(PlotUtil.PropertyPlotLength);

// Snapshot: serializer-safe primitives, ON the owning entity, persists with it.
[Serialized] public ThreadSafeList<OreFindingSnapshot> Findings { get; set; } = new();
[Serialized] public float CoveragePercent { get; set; }

// Projection, guarded so an empty accumulator can't clobber a persisted snapshot.
private void PersistAssignedAreaFindings() {
    var entry = this.AssignedSurveyArea;
    if (entry == null || this.surveyRecord == null) return;
    var coverage = this.surveyRecord.Coverage(entry.ToSurveyArea());
    if (coverage <= 0f) return;                 // no new work yet — keep what's persisted
    entry.SetFindings(this.surveyRecord.Findings(entry.Id), coverage * 100f);
}

// Reset ONLY on the owner's lifecycle events.
public void SetPlots(...) { /* redraw geometry */ this.ClearFindings(); }   // edit = new area
public void ClearSurveyData(int id) {                                        // edit + delete
    this.SurveyAreas.FirstOrDefault(a => a.Id == id)?.ClearFindings();
    this.surveyRecord?.ClearArea(id);
}
// Reassignment: no reset — the sensor just attributes new samples to the newly-assigned id.
```

The serializer-safety of the snapshot is itself a constraint: a `[Serialized]` collection must be
a `ThreadSafeList`/`ThreadSafeDictionary`, not a plain `List`, or Eco fails server init with
"Attempting to serialize non-immutable member … Either make immutable or add [ThreadSafe]" — the
same rule that governs the area's own `PlotCoords`.

## Related

- `docs/solutions/best-practices/ship-the-readout-not-just-the-data.md` — the readout is part of
  the feature; this pattern is what makes that readout *durable* and consistent across surfaces.
- `docs/solutions/conventions/eco-server-only-mod-client-rendering-surfaces.md` — the surfaces
  (tab text, tooltip, world text, chat) that all read this one persisted snapshot.
- `docs/solutions/conventions/consistent-grid-column-quantization.md` — the plot/column
  quantization the accumulator and area membership share.
