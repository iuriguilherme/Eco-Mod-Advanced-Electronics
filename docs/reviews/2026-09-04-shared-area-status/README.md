# Code review — shared area status, and the farming drone alongside it

Date: 2026-09-04
Reviewed: `c21be8a..2074716` on `feat/tech-tree-icons` — 58 files, 11,491 insertions.
Roster: 10 lenses, all returned. No degraded coverage.

Nothing here was fixed. The findings are recorded and the tree is unchanged.

The per-lens JSON in this directory carries the full evidence, including the quoted lines.
Paths in those files are repo-relative; engine references are relative to the Eco source
checkout the reference assemblies are built from.

## What this review covered that no earlier one could

The branch carries two independently developed features — this plan's thirteen units and a
farming drone by another author — written by nine workers who could not see each other's code.
Several findings below exist only in the seam between them, and would not appear in a review of
either half alone.

## Confirmed by more than one lens

These carry the most weight: separate reviewers reached them independently.

### F1. The ground-change handler races itself — P0

`SurveyAreaEntry.ResetPlotsToUnsurveyed` does read-filter-replace on `Findings`,
`SurveyedStamps`, `BedrockPlotCoords` and `SweepColumns`, and it runs from the engine's
block-write notification rather than the dock tick.

That notification is invoked from inside `Parallel.ForEach` (`Server/Eco.World/World.cs:367`,
invoking at `:388`), so it runs concurrently on thread-pool threads. Two changed columns in
different plots of the same area reach the reset at once, each builds a filtered copy from a
shared read, and the last writer wins — silently discarding the other's reset.

The result is a plot the mod believes it reset that still carries findings for ground that no
longer exists: the stale-findings fault this subsystem was built to remove, reintroduced through
concurrency. Player-visible as wrong ore counts after any bulk ground event.

Reviewers: reliability (75), adversarial (50, as a suspected race). Promoted on agreement.

The diff already establishes the remedy for its own claim path — one lock. The ground path
never got one.

### F2. The level pass writes ground unattributed — P1

`FarmingStrategy` opens an attribution scope around its action switch but not around the level
pass, which is a separate write path. Flattening ground therefore reads as an outside change,
so any mining area overlapping it is unsurveyed plot by plot as the farm works.

Reviewers: adversarial (100), correctness (75).

### F3. The sweep rewind never reaches the running pass — P1

The ground-change reset rewinds the persisted cursor so a plot returned to unsurveyed is
revisited. The in-flight survey strategy holds its own cursor in memory and writes it back over
the rewound value, so the mechanism is inert for the case it was written for.

Reviewers: correctness (75), adversarial (75).

## Derived — held by no single lens

### F4. The mod's own writes may not be attributable at all — P0 if confirmed

`ModGroundWrite` is `[ThreadStatic]`: a drone opens the scope on its tick thread. F1 establishes
that the notification fires from a parallel loop. If the notification does not run on the thread
that opened the scope, the handler reads `Outside` for the mod's own digging — and every drone
unsurveys the area it is working as it works it.

Whether this bites depends on whether the parallel loop inlines onto the calling thread, which
makes it intermittent rather than absent. R17 and R43 both rest on this attribution.

Neither lens claimed this. Reliability proved the parallel invocation; adversarial flagged the
thread-static dependency as an unverified residual. It is recorded here as a synthesis, not as a
reported finding, and it is the first thing to settle on a live server.

## Single-lens findings

| # | Severity | What | Lens |
|---|---|---|---|
| F5 | P2 (effect P0) | **Real farm areas never enter the claim system.** Projections are built from survey areas only; actual farms are a separate persisted type. R47 — farmland reserved against mining — therefore protects only a survey area manually retyped by command, never the farm a citizen assigned. | maintainability (75), corroborated in reliability's residuals |
| F6 | P1 | **A cleared area can never be reassigned.** R44 refuses to offer it; R45 needs that assignment to lift the exclusion. If the area is cleared because of the asking dock's own refusal, the retry is blocked and the ground stays cleared permanently. | correctness (75) |
| F7 | P1 | **One dock can claim one area twice** — under its survey and its mining assignment — and either release frees it. The claim epoch exists to scope this and is not used in the release. | adversarial (75) |
| F8 | P1 | **The survey tab's area actions run at consumer access** while the farming half of the same mod gates its equivalents at full access. The two halves disagree about who may assign, unassign and edit areas. | security (75, three findings, one root) |
| F9 | P1 | **A serialized member was deleted** — the only deletion in the diff. Whether the engine tolerates a field present in an old save but absent from the class is unverified, and that failure is the silent one. | api-contract (75) |
| F10 | P1 | **The area-resolution failure counter is burned** by several call sites per tick, shortening the grace period it exists to provide. | reliability (75) |
| F11 | P0 | **Ownership-blind claim conflicts.** A stranger can draw a farm across your mine and lock it; because farmland's reservation outlives its assignment, permanently. This is R47 working as written — a product question, not a defect. | security (50) |
| F12 | P2 | Block-write fan-out is O(docks) per dig, unfiltered by distance. | performance (75) |
| F13 | P2 | Mining exclusions grow unbounded and are rescanned per dock per second by both tabs independently. | performance (75) |
| F14 | P2 | `DroneDock.cs` grew to 1292 lines by adding claim and sweep logic inline rather than following the file's own established per-feature partial convention. | maintainability (75) |
| F15 | P2 | One documented fallback branch in area resolution is never exercised by the suite. | testing (75) |

## Unverified residuals worth keeping

- `SetPlots` replaces the plot list wholesale while `Plots()` enumerates it lazily; an overlap
  scan running during an edit can observe a splice of old and new geometry.
- Area identity is (owning dock, area id), but farm and survey areas draw ids from independent
  counters on the same dock, so the pair is not unique across kinds.
- Concentration on a restored findings row recovers a denominator that no longer exists and
  yields zero. Invisible only because nothing renders it.
- Classifying a refusal re-invokes the law manager, applying any effect not routed through the
  pack's change set a second time. Pre-existing, documented in code, not captured in
  `docs/solutions/`.

## What a clean lens found

`project-standards` returned no findings at the actionable bar, having verified rather than
skimmed: every new serialized member carries a setter, every new serialized collection is a
thread-safe list, and the clear path fires only on delete and genuinely new passes — never on
reassignment or resume.

## Coverage

All ten lenses returned: correctness, security, adversarial, project-standards, testing,
maintainability, learnings, performance, reliability, api-contract.

The independent cross-model adversarial pass did **not** run: no second-provider CLI on the
allowlist was installed, and the one that was installed shares this session's model family. The
adversarial lens is therefore a same-family read with the same blind spots as the orchestrator.
No reviewed code left the machine.

The Eco-side half of this work — persistence, component wiring, the event subscription — has no
automated coverage by design, because the test project deliberately depends only on the
Eco-free library. Every finding touching that half rests on reading, not on a failing test.
