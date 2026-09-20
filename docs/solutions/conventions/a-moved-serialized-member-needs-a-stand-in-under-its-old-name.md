---
title: "A moved [Serialized] member needs a stand-in under its old name"
date: 2026-09-20
category: conventions
module: EcoServerMod
problem_type: convention
component: serialization
severity: high
applies_when:
  - "A [Serialized] member is being deleted from the class that declared it, whether or not its data moves elsewhere"
  - "Persisted state is being relocated to the entity the data is really a fact about"
  - "A new [Serialized] member must read sensibly on a save written before it existed"
  - "A migration has to resolve a reference to another world object before it can write"
  - "Deciding where a one-time save fold should be called from"
root_cause: data_integrity
resolution_type: migration
tags:
  - eco-modding
  - serialization
  - persistence
  - migration
  - save-compatibility
  - worldobject
  - bson
related_components:
  - EcoServerMod/AdvancedElectronics
  - EcoServerMod/AdvancedElectronics.Navigation
---

# A moved [Serialized] member needs a stand-in under its old name

## Context

Release prep for v0.4.0 moved a piece of stored data from one class to another. The record of which
plots a mining drone had already dug — a per-plot *mined stamp* — came off the mining dock and went
onto the survey area, because a stamp about digging is a fact about the ground rather than about the
dock that happened to dig it.

In the 0.3.0 shape the record was `DroneDockObject.MinedStamps`, a `ThreadSafeList<long>` of flat
`(x, z, stamp)` triples, one triple per mined plot (`git show
v0.3.0:EcoServerMod/AdvancedElectronics/DroneDock.Mining.cs`, line 47). In 0.4.0 the dock stopped
declaring that member and the record lives on the area instead, as `SurveyAreaEntry.MinedStamps`
(`EcoServerMod/AdvancedElectronics/SurveyAreaEntry.cs:240`), read and written through
`ReadMinedStamps` (`SurveyAreaEntry.cs:1078`) and `SetMinedStamps` (`SurveyAreaEntry.cs:1057`).

That move is not a rename in any sense a compiler recognises, and nothing a compiler checks can
catch it. Eco serializes `[Serialized]` members to BSON — the attribute's own definition says "This
symbol will be serialized to BSON for storage in the world file"
(`Server/Eco.Shared/Serialization/SerializedAttribute.cs:9`, in the Eco 0.14.1.1 source checkout,
outside this repo). A BSON document stores each field under its member name and loads it back by
looking that name up on the class being deserialized. A save written by 0.3.0 holds a `MinedStamps`
field inside its serialized `DroneDockObject` document; a 0.4.0 `DroneDockObject` that declares no
such member gives that field nowhere to land.

What Eco's loader does with a stored field matching no member was **not tested in this session**.
Whether it drops the field silently or complains somewhere is an open question. The fix is built for
the silent case, which is the one nobody notices.

The consequence, if the value is dropped, is not cosmetic. `PlotFreshness.IsMineable` decides whether
a plot may be dug by comparing two stamps that are supposed to describe the same ground:
`IsMineable(long surveyedStamp, long minedStamp) => surveyedStamp > minedStamp`
(`EcoServerMod/AdvancedElectronics.Navigation/PlotFreshness.cs:59`). On a 0.3.0 save with no fold,
every area's mined record reads empty, every finished plot reads as fresh ground, and the drone
re-digs shafts it completed before the update.

**This hazard was raised when the move was made, and left open for sixteen days** (session history).
During the shared-area work of 2026-09-02 to 09-04, an api-contract review lens flagged the removal
of the dock-level member with the distinction the author had not isolated: *"adding a serialized
member and removing one are different operations against an existing save."* It was recorded as a P1
needing load verification rather than as a migration, on the reasoning that a resurvey would rebuild
what was lost — *"no player information is lost that a single pass doesn't restore"*. It was never
verified against a real pre-move save. The counter-argument arrived the next day from the product
owner, and became a requirement in its own right: *"we need to maintain the practice of doing
migrations because in the future we can't ask people to reset a world to update the mod."* A
resurvey is a player action; needing one is the world-reset answer in smaller clothes.

## Guidance

**Declaring the member correctly on its new owner does nothing for data already on disk.** The new
declaration serves new data going forward. The old value needs a place to load into, on the old
class, under the old name, spelled exactly as the old document spells it.

The fix re-declares the legacy member and does nothing else with it
(`EcoServerMod/AdvancedElectronics/DroneDock.Migration.cs:42`):

```csharp
[Serialized] public ThreadSafeList<long> MinedStamps { get; set; } = new();
```

Nothing writes it. A fresh 0.4.0 world never populates it, and a dock that has been folded once
carries an empty list from then on. Its only job is to catch an old save's field before the mod gets
a chance to read anything.

This is the one case where an otherwise-unused `[Serialized]` member is correct rather than a
mistake. `docs/solutions/conventions/serialized-needs-a-member-to-write-back-into.md` establishes
that loading a persisted value is a *write*, so the member must be assignable. Here that fact is used
deliberately, to keep a one-way mailbox open for as long as old saves are still being carried
forward.

**Move the value with a merge, not a replace, and keep the merge free of engine types.** The target
may already hold data of its own, and the fold may run more than once. The merge lives in the
Eco-free `AdvancedElectronics.Navigation` assembly as flat-triple arithmetic
(`EcoServerMod/AdvancedElectronics.Navigation/LegacyMinedStamps.cs:38`):

```csharp
public static PlotStampAccumulator MergeInto(IEnumerable<long> flatTriples, PlotStampAccumulator existing)
{
    var merged = new Dictionary<PlotCoord, long>();
    if (existing != null)
        foreach (var entry in existing.Snapshot())
            merged[entry.Key] = entry.Value;

    // ...walk flatTriples three values at a time...
    if (!merged.TryGetValue(plot, out var current) || stamp > current)
        merged[plot] = stamp;

    return PlotStampAccumulator.FromSnapshot(merged);
}
```

Keeping the higher stamp per plot, rather than letting either side win by position, is what makes the
fold safe to run twice and safe to run after the area has recorded newer digging: a stamp is a
monotonic marker of when a plot was last dug, so the higher value is always the later one and no plot
can move backwards in time. A trailing partial triple is ignored rather than throwing
(`LegacyMinedStamps.cs:32-34`, enforced at `:57`), matching how every other reader of this shape
walks it.

The split is also what makes the fold testable at all. The test project references only the
navigation assembly (`AdvancedElectronics.Navigation.Tests.csproj:18`), so anything holding an Eco
type cannot be unit-tested here. Arithmetic that could have lived inside the dock method is worth
pulling out for that reason alone.

**Call the fold from something that runs repeatedly, not from `Initialize()`.** The mined stamps
belong on the area, and the area is owned by a *different* dock, reached by resolving
`AssignedMiningArea` (`DroneDock.Migration.cs:75`). World-object initialization gives no guarantee
that the survey dock has loaded by the time the mining dock initializes, so resolving at that point
can fail on a save where the area is perfectly intact. The call site is `MiningComponent.RefreshAll()`
(`EcoServerMod/AdvancedElectronics/MiningComponent.cs:356`), which runs on load and on every refresh
after it.

**Make the failure-to-resolve branch keep the data.** A dock whose area has not loaded yet is
indistinguishable, from inside the method, from one whose area is gone for good — and the two call
for opposite handling (`DroneDock.Migration.cs:71-88`):

```csharp
public void MigrateLegacyMinedStamps()
{
    if (this.MinedStamps == null || this.MinedStamps.Count == 0) return;
    if (this.AssignedMiningArea == null) return;
    if (this.AssignedMiningArea.Resolve(out _, out var area) != AreaLookupSignal.Found) return;

    lock (SurveyAreaEntry.AreaDataLock)
    {
        var merged = LegacyMinedStamps.MergeInto(this.MinedStamps, area.ReadMinedStamps());
        area.SetMinedStamps(merged);
    }

    this.MinedStamps = new ThreadSafeList<long>();
}
```

Every early return leaves the legacy list untouched, and the list is emptied only in the branch that
actually merged under the lock. Discarding on a resolution failure would look like the fold ran, log
nothing, and erase a real record because the fold happened to fire one tick early. Refusing to guess
costs one count check per refresh once the fold has succeeded.

The lock is the area's own (`SurveyAreaEntry.cs:296`), taken for the reason every other
read-modify-write on the area's lists takes it: read, merge, store is three steps, and a writer
landing between any two of them loses one of the writes. A one-time migration is not exempt from
that.

### The other direction: a member with no stored field

Removal is one half. The mirror case is a member the code now requires that an old save never wrote,
which loads as the type's default. Two techniques for it are already in the tree, and the choice
between them is about whether the default collides with a real value.

**A sentinel value, when the domain can spare one.** `SurveyAreaEntry.ClaimWorkValue` numbers its
real values from one — `ClaimWorkSurvey = 1`, `ClaimWorkMining = 2` (`SurveyAreaEntry.cs:426-430`) —
so `0` means "no work kind recorded" and an upgraded claim reads as unknown rather than as a mining
claim. The opposite choice is also in the tree and also deliberate: `AreaKind.Mining = 0`
(`EcoServerMod/AdvancedElectronics.Navigation/AreaLifecycle.cs:71`), commented as "the default, and
what every area in an existing save is", because there every pre-existing area genuinely was a mining
area. The technique is not "always reserve zero"; it is to decide what an absent field should mean and
then encode that meaning at zero.

**An explicit shape version, when it cannot.** A review during the shared-area work rejected an
attempt to detect old rows by testing whether a findings row carried no plot, because the plot fields
are plain `int`s inside a flat serialized class: an old row deserializes as plot `(0,0)`, which is a
legitimate plot near the world origin (session history). **You cannot infer "this field was absent"
from a deserialized default that is also a valid domain value.** The answer was a stamped version:
`FindingsVersion.Current` with `IsStale(int storedVersion) => storedVersion < Current`
(`EcoServerMod/AdvancedElectronics.Navigation/SurveyFinding.cs`), where an unstamped save reads `0`
and is stale by construction, and a version at or past current is kept so a downgrade destroys
nothing.

## Why This Matters

The failure mode has no error message in it anywhere. The save loads, the server starts, the mod's
load line prints. What a player sees, days later, is a drone re-digging ground it already finished —
which reads at the table as the mod having lost track of its own work, not as a bug with a stack
trace to chase. That is the same silent-failure family the serialization docs in this corpus keep
circling: a clean build and a quiet log, with the evidence arriving through gameplay instead.

The distance between a silent load and a visible symptom is what the project's standing rule about
persisted data exists for: a change that alters what is on disk ships with an explicit path for the
data already out there. The three pieces here — a stand-in member that exists only to receive an old
value, a pure merge that can be tested because it holds no engine types, and a call site chosen for
when the data it needs is actually available — are the general answer whenever a `[Serialized]`
member moves. A future move either brings the same three, or carries a deliberate decision that old
saves are out of scope for that release, made on purpose rather than discovered from a bug report.

Worth noting how this one was found: not by a test and not by a player, but by writing the release's
migration note and having to say what a 0.3.0 dock would do. The review that first flagged it did not
close it, because "a resurvey rebuilds it" sounded sufficient until someone asked who performs the
resurvey.

## When to Apply

- Whenever a `[Serialized]` member is deleted from the class that declared it, whether the data moves
  to another member, to another class, or nowhere. The question to ask before deleting: does a save
  written before this change still hold a document keyed to this exact name on this exact class, and
  if so, where does that value go now?
- Whenever persisted data is relocated for a design reason. The reason for the move has no bearing on
  the mechanical fact that BSON does not know the move happened.
- Whenever a new `[Serialized]` member will be read on saves that never wrote it — pick the sentinel
  or the shape version above, rather than letting the type default decide by accident.
- Whenever the migration depends on resolving another object that may not have finished loading when
  the obvious call site runs. Prefer a call site invoked repeatedly during normal operation, and make
  the unresolved branch keep the data rather than discard it.
- **Not** needed when a member keeps its name and only its interpretation changes in a way the
  existing shape already tolerates. The field still has somewhere to land; only the meaning of the
  absent case needed choosing.

## Examples

Before: the 0.4.0 `DroneDockObject` mentions `MinedStamps` nowhere, so a 0.3.0 save's field has no
member to deserialize into and the record of every dug plot is stranded in the file.

After: the member exists again, spelled as 0.3.0 spelled it, holding the value only until the fold
reads and clears it — and the fold is exercised entirely at the pure-merge layer. Seven tests in
`EcoServerMod/AdvancedElectronics.Navigation.Tests/LegacyMinedStampsTests.cs` cover it: legacy stamps
reaching an empty record, the existing record surviving, neither direction moving backwards, running
the fold twice, a trailing partial triple, and nothing to fold. All 629 tests pass and the build
reports no errors — the xUnit analyzer warnings it does emit are pre-existing and unrelated — at
commit `6ba9de3` on `main`.

What the tests do **not** establish is the one check that would prove the fix against real data. As
`docs/KNOWN-ISSUES.md` records, the fold has never been run against a real 0.3.0 save, and the release
zip's `README.txt` still tells admins to remove their docks before updating. The unit tests show the
merge arithmetic is correct; they say nothing about how a real save file round-trips through Eco's
deserializer.

## Related

- `docs/solutions/conventions/serialized-needs-a-member-to-write-back-into.md` — the mechanic this
  builds on: loading a persisted value is a write, so it needs an assignable member. That doc argues
  against a `[Serialized]` member with nothing to write into; this one is the deliberate exception,
  where a member with nothing left to write is kept on purpose as a migration mailbox.
- `docs/solutions/conventions/requirecomponent-is-re-enforced-on-every-server-load.md` — the same
  discipline one layer up. There the engine reconciles a placed object's component set against the
  current code on every load, before any migration runs; here it reconciles a document's fields
  against the current members. Both fixes are the same move: keep the old landing pad alive long
  enough for migration code to read it.
- `docs/solutions/architecture-patterns/persist-derived-data-as-serialized-snapshot-on-its-owner.md`
  — the design rule that produced this migration. It says derived data belongs on the entity it is a
  fact about, which is exactly why the stamps moved; it does not cover what happens to saves written
  before such a move.
- `docs/solutions/runtime-errors/initialize-exception-leaves-a-half-built-worldobject.md` — a second,
  independent reason to distrust cross-object lookups at `Initialize()` time.
