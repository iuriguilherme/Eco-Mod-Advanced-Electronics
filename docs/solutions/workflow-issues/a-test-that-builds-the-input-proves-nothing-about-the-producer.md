---
title: "A test that builds the input proves nothing about the producer"
date: 2026-09-05
category: workflow-issues
module: EcoServerMod
problem_type: workflow_issue
component: development_workflow
severity: high
applies_when:
  - "Adding a member to a type that crosses the Navigation/Eco assembly boundary"
  - "A new constructor parameter is optional, so every existing call site keeps compiling"
  - "Unit tests construct the shared structure themselves instead of obtaining it from its producer"
  - "One side of a boundary has no automated coverage by design"
  - "A green suite is being read as evidence that a feature will appear in the running game"
symptoms:
  - "Unit tests for a new display behaviour all pass and the behaviour never appears in the running game"
  - "Every producer call site keeps compiling unchanged after a new member is added to the shared type"
  - "The new member silently reads its default value everywhere in production"
  - "The gap survives several commits and is found only while implementing a later unit"
root_cause: incomplete_setup
resolution_type: code_fix
related_components: [EcoServerMod/AdvancedElectronics, EcoServerMod/AdvancedElectronics.Navigation, EcoServerMod/AdvancedElectronics.Navigation.Tests]
tags: [testing, test-coverage, false-confidence, assembly-boundary, optional-parameter, default-value, eco-modding, methodology]
---

# A test that builds the input proves nothing about the producer

## Context

The server half of this mod is split into two assemblies on purpose, and the split is what makes any
of it testable at all. `EcoServerMod/AdvancedElectronics.Navigation/` is plain C# with no dependency
on any `Eco.*` namespace, so `dotnet test` can run against it on a machine with no game installed.
`EcoServerMod/AdvancedElectronics/` is the shipped mod, compiled against Eco's reference assemblies,
and it is where world objects, components, drone behaviour and chat commands live.

The test project sits on one side of that line and cannot cross it. Its project file,
`EcoServerMod/AdvancedElectronics.Navigation.Tests/AdvancedElectronics.Navigation.Tests.csproj`,
declares exactly three package references — `Microsoft.NET.Test.Sdk`, `xunit` and
`xunit.runner.visualstudio` (lines 12-14) — and exactly one project reference, at line 18:

```xml
<ProjectReference Include="..\AdvancedElectronics.Navigation\AdvancedElectronics.Navigation.csproj" />
```

That is the whole reference set. Nothing names `AdvancedElectronics.csproj`, and a search across
every source file in the test project for the string `Eco.` returns no file at all. The dependency
arrows both point the same way: the mod assembly references the Navigation assembly (declared in
`EcoServerMod/AdvancedElectronics/AdvancedElectronics.csproj`, in the ItemGroup commented `U2:
consumes GridPathfinder/PathResult/ArrivalDetector (U3) via EcoWorldSampler`), and the test assembly
references the Navigation assembly. Neither one can see the mod. This is deliberate and it is
correct — Eco has no headless mod test harness, so the only way to get a fast automated suite was to
push the logic worth testing into an assembly that does not need the game. The price paid for it is
that **the entire Eco-side half of the mod has no automated coverage of any kind**, and every claim
about it has to be settled by deploying to a running server and looking.

Data crossing that line is carried by types defined in the Navigation assembly and constructed by
the mod assembly. `AreaSnapshot` is one of them: a `public readonly struct` at
`EcoServerMod/AdvancedElectronics.Navigation/DockReadout.cs:57`, described in its own doc comment as
"one survey area reduced to exactly what the dock's panel prints about it". The Navigation side
formats it. The mod side builds one per area from live world-object state, in two places and only
two: `EcoServerMod/AdvancedElectronics/SurveyComponent.cs:652` and
`EcoServerMod/AdvancedElectronics/MiningComponent.cs:491`.

### What happened

Unit U3 of `docs/plans/2026-09-04-1431-fix-ground-change-attribution-plan.md` added a display
behaviour: when an area's survey figures are known to be out of date, the readout prefixes them with
a label rather than recalculating or hiding them. The implementation, in commit `10dc075`
*"feat(readout): say the figures are old rather than changing them (U3)"*, added three things to the
Navigation assembly:

- the constant `OutOfDateLabel` at `DockReadout.cs:162`, whose value is
  `"area changed and needs resurveying. old data:"`;
- a private `OutOfDatePrefix(AreaSnapshot)` at `DockReadout.cs:402-403`, called twice from
  `FormatAreaSummary` at `DockReadout.cs:371-384`;
- a new `bool NeedsResurvey` property on `AreaSnapshot` at `DockReadout.cs:108`, fed by a new
  **optional** constructor parameter `bool needsResurvey = false` at `DockReadout.cs:120`.

It also added five unit tests, at `DockReadoutTests.cs:195`, `:210`, `:224`, `:235` and `:250`. All
five passed. The commit records the suite going from 566 tests to 571, and records that the prefix
was temporarily neutralised to confirm the new tests were sensitive rather than vacuous — two of
them failed when it was, and passed again when it was restored.

The tests were sensitive. The feature still could not appear in the running game, because **nothing
on the Eco side ever set the field**. Both production construction sites still called the old
nine-argument form and silently received `needsResurvey: false`, so `OutOfDatePrefix` returned
`string.Empty` on every real call, forever.

The reason the tests could not see this is visible in one line of the test file. `DockReadoutTests`
builds its snapshots through a local helper at `DockReadoutTests.cs:18-31`, whose signature mirrors
the constructor's — including its defaults — and which commit `10dc075` extended in lockstep with a
`bool needsResurvey = false` parameter of its own at line 28. Four of the five new tests then pass
`needsResurvey: true` themselves, and the fifth is the control that omits it. So the suite proved
the formatter, given the field, produces the label; and it proved nothing whatsoever about
whether anything ever gives the formatter the field.

It was caught four commits later. `138ea68` (U4), `caf9d12` (U5) and `3a05504` (U6) landed in
between, and the fix went out inside `1c3814b` *"feat(area): show digging from the moment a mining
drone is assigned (U7)"*, whose message names it plainly: *"the label was implemented and unit-tested
but nothing on the Eco side ever set the field that triggers it, so it could never have appeared."*
Both producers now pass `needsResurvey: area.AnyPlotNeedsReReading` —
`SurveyComponent.cs:660` and `MiningComponent.cs:512` — reading the property defined at
`EcoServerMod/AdvancedElectronics/SurveyAreaEntry.cs:1185`. (These SHAs, and the others named below, are local to
this repository's `feat/tech-tree-icons` branch, which has no upstream. This repository does not
rewrite history, so they stay valid here; they will not resolve in a clone that has only
`origin/main`.)

## Guidance

**A shared data structure is a test seam only for the side that reads it.** When a test constructs
the structure itself, it has replaced the producer with the test author's intent. Every assertion
downstream of that construction is an assertion about the consumer. This is not a flaw in the test —
`FormatAreaSummary` genuinely needed those five cases and genuinely has them — it is a limit on what
the test can be cited for. The green suite is evidence about `DockReadout`. It is not evidence about
the feature, because the feature is the whole path from world state to rendered text and the suite
touches only its last step.

**The optional parameter is what makes the gap silent.** A required parameter would have made
`10dc075` fail to build until both `SurveyComponent.cs` and `MiningComponent.cs` were updated: the
compiler would have named the two files, and the unit could not have been called done without
visiting them. An optional one asks nothing. The producers keep compiling, keep running, keep
rendering, and quietly supply `false`. The failure mode a default parameter buys you is exactly the
one this repo can least afford, because the side that keeps compiling is the side with no tests.

**A new member on a type that crosses the tested boundary is not done when its tests pass; it is
done when every construction site on the untested side has been found and made to fill it.** The
compiler will not ask, so the search has to be manual and it has to be part of the unit. For
`AreaSnapshot` that search is one grep and returns two files. The cost of doing it is a minute; the
cost of not doing it was four commits of a feature that could not fire.

**Decide the default's meaning before you decide it is convenient.** "Optional and false by default,
so every existing caller reads exactly as it did before" is a real property and `10dc075` states it
as a benefit. It is also the exact sentence that describes the bug: every existing caller did read
exactly as it did before, including the two that were supposed to change. Backward compatibility for
callers you intend to update is not compatibility, it is a postponed edit with no reminder attached.

**When a default is genuinely right, say so at the call site.** The repo already contains the good
version of this. `MiningComponent.cs:315-323` constructs an `AreaProjection` without its two optional
arguments and carries a comment explaining that the omission is intended: *"The claim flag and the
kind are left at their defaults deliberately: this is the area being ASKED about, and nothing reads
either field off the asking side."* A reader auditing producers can clear that site in seconds. A
site with no comment is indistinguishable from a site that was never visited, which is precisely the
state `SurveyComponent.cs` and `MiningComponent.cs` were in for four commits.

**Classify the unit by what it changes, not by which file holds the logic.** U3's plan entry at
`docs/plans/2026-09-04-1431-fix-ground-change-attribution-plan.md:371-388` lists two files, both on
the Navigation side, and its approach step says to add the label "in the Eco-free readout module …
so it is covered by the unit test suite". That reasoning is sound about the label and wrong about the
unit: adding a member to a boundary type is a change to the mod assembly whether or not the diff
touches it. The plan's Verification Contract table (lines 490-495) lists live behaviour as applying
to U2, U4, U5, U6 and U7, with U3 deliberately absent, and U3's own Definition of Done (line 518) —
"the label appears while any plot is marked and disappears when none is" — is satisfiable by the unit
suite alone. Every gate the plan attached to U3 was passed honestly by a feature that could not run.

**Distinguish the whole-plan gate from the per-unit gate.** The plan was not blind to the end-to-end
claim: its global Definition of Done includes "a mining drone that finds the world does not match the
survey records that fact, and the player sees the area's figures labelled as out of date". That
sentence covers this exactly. But it sits four units downstream of the change that could invalidate
it, and it is the plan's closing gate rather than U3's. A gap that only a final gate can catch is a
gap that lives for as long as the plan does.

**Where the test project cannot reach, the reviewer is the only compiler.** In an ordinary codebase
the producer's tests would have caught this. Here there is no such thing, and there is no cheap way
to make one — verification on the Eco side costs a server deploy and a human restart, which is the
scarcest resource in this project (see `docs/solutions/workflow-issues/eco-mod-batched-live-testing.md`).
That makes reading the producers by hand the substitute for the missing suite, not an optional extra
on top of it.

## Why This Matters

The direct cost was small and self-correcting: four commits during which one label could not appear,
caught by the next person to touch the same area. What makes it worth writing down is that the same
failure had already happened once in this repo, on a different member of the same boundary path, and
nobody recognised the shape.

`DroneDockObject.StatusOfArea` at `EcoServerMod/AdvancedElectronics/DroneDock.Mining.cs:337-341`
takes `AreaKind kind = AreaKind.Mining`. When that parameter was introduced, the Mining tab's
producer did not pass it, so every area rendered on that tab derived its status through the mining
ladder regardless of what the area was actually for. The comment that now sits at
`MiningComponent.cs:493-498` is the post-mortem: *"Omitting it here defaulted every line to the
mining ladder, so a farming area listed on this tab rendered a ladder rung instead of `[farm]` — a
hole in an invariant whose whole point is that it is structural."* It was closed in `c611b0e`
*"feat(mining): give the dock network a physical extent (U10)"*, whose message calls it *"a gap the
concurrent unit found and could not reach"*. Two occurrences, two different types, the same
mechanism: an optional parameter added on the tested side, and a producer on the untested side that
went on compiling.

Counting more loosely, it is three. In August a set of drone animation-mode booleans was projected by
the dependency-free navigation assembly, was unit-tested, and was correct — and the feature never
appeared in game, because the booleans are constant and the change-gate that published them fires
only on change, so a client that built the drone later was never sent the standing value (session
history). The mechanism is different — an event gate rather than a defaulted parameter — but the
shape is identical, and it is the shape that matters: a correct, tested value in the Navigation
assembly, and no consumer on the other side that ever received it. That occurrence was found in live
play, not by the suite, and it produced
`docs/solutions/architecture-patterns/client-animation-is-driven-by-name-not-by-mod-code.md`.

The boundary itself was named a month before any of this. During planning on 2026-07-31, a grep
established that `DockReadout` was documented as testable without a running server while in fact
living where no test could reach it, and the session recorded the finding as "the same shape as the
install path nobody executed" (session history). The knowledge that this seam exists has therefore
been available throughout, and did not prevent the next two occurrences. That is the argument for a
mechanical step in the unit rather than an awareness of a hazard.

The compounding cost is the one the green suite creates. 571 passing tests is not a neutral fact
about U3; it is an active reason not to look further, and it arrived attached to a commit whose own
message documented careful evidence discipline — mutation-testing the prefix to confirm the new tests
were not vacuous. The rigour was real and it was spent entirely inside the boundary. Sensitivity
testing answers "would this test notice if the code under test were wrong"; it cannot answer "is the
code under test the code that runs".

There is a second, quieter multiplier in the test fixtures. `DockReadoutTests.cs:18-31` is not the
only local `Area(...)` helper: `MiningReadoutTests.cs:117-129` has another, with the same seven
positional arguments and only two of the three defaults — it does not carry `needsResurvey` at
all. Each such helper is a second copy of the constructor's default list, so a new optional
member propagates its
default through the fixtures as well as through production, and every test that does not name the
member explicitly keeps asserting the pre-change behaviour while looking like coverage.

Finally, the same "the zero value silently becomes the answer" reasoning turned up in this plan as a
persistence question rather than a parameter question. `1c3814b`'s message explains why the new
drone-kind value on a claim is numbered from 1 rather than mirroring `AreaKind`'s ordinals: because
`AreaKind.Mining` is 0, an existing save with no field for it would have loaded every claim as a
mining claim. A defaulted parameter and an unwritten serialized field are the same hazard on
different axes — an absent value that is indistinguishable from a deliberate one.

## When to Apply

- When adding any member to a type defined in `AdvancedElectronics.Navigation` and constructed in
  `AdvancedElectronics`. Before the unit is done, grep the mod assembly for every construction site
  and either pass the new value or leave a comment saying why the default is right there.
- When the new member's constructor parameter is optional. That is the moment the compiler stops
  being your search tool, and the moment to decide whether making it required is affordable — the
  five U7 tests were, per that commit's message, "written first and observed failing to compile
  against the unchanged signature", which is what a required parameter gives you for free.
- When a plan unit's file list contains only Navigation-side files but the change adds or widens a
  boundary type. Adding a member to a shared struct is a mod-assembly change with no mod-assembly
  diff, and the file list will not say so.
- When a plan attaches no live check to a unit because the unit "is Eco-free". Ask what the unit's
  passing gate would look like if the producer had never been written.
- When citing a green suite as evidence that a cross-boundary feature works. State which half it
  covers. In this repo the honest sentence is "the formatter is proved and the producer is unverified",
  not "the tests pass".
- When reviewing a diff whose test changes construct the type under test directly. That is the tell:
  the test is standing in for the producer, so the producer is unexamined by construction.
- When a test fixture has a local factory helper mirroring a production constructor's defaults. The
  helper is a second place the new member has to arrive, and its default silently pins every older
  test to the old behaviour.

## Examples

The constructor as U3 left it. The first seven parameters are required; the tail is not, and
`needsResurvey` joined that tail (`DockReadout.cs:110-120`):

```csharp
public AreaSnapshot(
    int position,
    string name,
    int plotCount,
    float coveragePercent,
    SurveyFinding topVisibleFinding,
    AreaLifecycleStatus status,
    bool isAssigned,
    bool isUnreachable = false,
    bool hasOverlap = false,
    bool needsResurvey = false)
```

The test helper, extended in the same commit so the new tests could reach the new field
(`DockReadoutTests.cs:18-31`, abridged). Note that it mirrors the default as well as the parameter:

```csharp
private static AreaSnapshot Area(
    ...
    bool overlap = false,
    bool needsResurvey = false) =>
    new AreaSnapshot(
        position, name, plotCount, coverage, top ?? SurveyFinding.NotFound, status,
        assigned, unreachable, overlap, needsResurvey);
```

And a test built on it (`DockReadoutTests.cs:195-206`). Everything it observes is downstream of a
value the test itself supplied:

```csharp
[Fact]
public void AreaSummary_WhenAPlotNeedsReReading_LabelsTheFiguresAsOldData()
{
    var current = DockReadout.FormatAreaSummary(
        Area(coverage: 43f, top: Finding("IronOre", 180)));

    var outOfDate = DockReadout.FormatAreaSummary(
        Area(coverage: 43f, top: Finding("IronOre", 180), needsResurvey: true));

    Assert.Equal(DockReadout.OutOfDateLabel + " " + current, outOfDate);
    ...
}
```

The formatter this proves correct (`DockReadout.cs:402-403`). It is correct. It simply never received
a `true` from anything but a test:

```csharp
private static string OutOfDatePrefix(AreaSnapshot area) =>
    area.NeedsResurvey ? OutOfDateLabel + " " : string.Empty;
```

The two producers, as `1c3814b` changed them. Neither diff is more than a few lines, and neither was
reachable by any test — this is the half of the feature that the 571 green tests said nothing about:

```diff
  // MiningComponent.cs, in Snapshot(...)
-                hasOverlap: OverlapsAnything(owner, area, published));
+                hasOverlap: OverlapsAnything(owner, area, published),
+                // R8. Same label as the Survey tab, for the same reason and from the same fact ...
+                needsResurvey: area.AnyPlotNeedsReReading);
```

The fact both sites now read is one property on the Eco-side area
(`SurveyAreaEntry.cs:1185`):

```csharp
public bool AnyPlotNeedsReReading => this.PlotsNeedingReReading.Count > 0;
```

### The good pattern, already in the tree

`MiningComponent.cs:315-323` is what an intentional default looks like. `AreaProjection`'s
constructor (`AreaOverlap.cs:70-84`) has two optional parameters, `bool holdsClaim = false` and
`AreaKind kind = AreaKind.Mining`; three of its four production sites pass both
(`DroneDock.cs:537-539`, `DroneDock.Mining.cs:507-509`, `MiningComponent.cs:285-286`), and the fourth
passes neither, on purpose, and says so:

```csharp
/// <summary>
/// One area as the geometry side of a comparison. The claim flag and the kind are left at
/// their defaults deliberately: this is the area being ASKED about, and nothing reads
/// either field off the asking side -- the answer is about what the OTHER areas hold.
/// </summary>
private static AreaProjection Project(DroneDockObject owner, SurveyAreaEntry area) =>
    owner == null || area == null
        ? null
        : new AreaProjection(area.Id, owner.ObjectID, area.Plots());
```

### The state of the other boundary members today

An audit of the current tree for the same exposure found no second live gap, but it did find that the
shape is common and that the repo depends on discipline rather than on the compiler to keep it
closed:

- `AreaSnapshot` (`DockReadout.cs:110-120`) — three optional parameters. Both production sites
  (`SurveyComponent.cs:652-660`, `MiningComponent.cs:491-512`) now pass all three explicitly.
- `AreaProjection` (`AreaOverlap.cs:70-84`) — two optional parameters, four production sites, three
  passing both and one deliberately defaulting with a comment, as quoted above.
- `AreaLifecycle.DeriveStatus` (`AreaLifecycle.cs:221-227`) — `Func<PlotCoord, bool> needsReReading =
  null` and `bool miningDroneAssigned = false`. One production caller, `DroneDock.Mining.cs:350-367`,
  which passes both.
- `MiningReadout.FormatJobStatus` (`MiningReadout.cs:24-25`) and `MiningReadout.FormatBlockedReason`
  (`MiningReadout.cs:98-99`) — optional `hasAssignment`, `travel` and `assignmentOutOfRange`. Passed
  explicitly by `MiningComponent.cs:411` and `MiningComponent.cs:434-435`.
- `DroneDockObject.StatusOfArea` (`DroneDock.Mining.cs:337-341`) — `AreaKind kind = AreaKind.Mining`,
  the parameter of the earlier occurrence. All six call sites today pass `area.Kind` explicitly
  (`DroneDock.cs:534`, `DroneDock.Mining.cs:471`, `MiningComponent.cs:208`, `:283`, `:499`,
  `SurveyComponent.cs:645`), which means the default is now reached by nobody and carries only risk.
  A default no caller relies on is a default worth deleting.

The counter-example worth noting is `FarmAreaState` (`FarmJob.cs:126-134`), which has five optional
parameters and is **private** — its defaults can only ever be chosen from inside the Navigation
assembly, by code the test suite can reach. Optional parameters are not the hazard on their own;
optional parameters on a constructor the untested assembly calls are.

## Related

- `docs/solutions/workflow-issues/a-gate-that-discovers-nothing-passes-everything.md` — the same
  family, one step earlier. There a check passed because its discovery step examined nothing; here a
  suite passed because its subject was only half of the path. In both cases the reported result is
  correct and useless, which is why it survives.
- `docs/solutions/workflow-issues/the-control-under-test-is-not-a-readout-of-it.md` — "ask what the
  widget is a picture of", applied to a test rather than a screenshot. A test that constructs its own
  input is reading the test author's intent, exactly as a ticked checkbox reads the client's.
- `docs/solutions/workflow-issues/eco-mod-batched-live-testing.md` — why the untested half stays
  untested: the unit of verification cost on the Eco side is a human server restart.
- `docs/solutions/best-practices/ship-the-readout-not-just-the-data.md` — the mirror image. There the
  producing side worked and no surface rendered it; here the rendering side worked and no producer
  fed it. Both are a feature that is complete on one side of a seam and absent to the player.
