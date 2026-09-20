# Handoff — ground change attribution

Written 2026-09-05, at the end of the session that implemented it. Everything below is either verified in that session or explicitly marked as not verified.

## State

- Branch `feat/tech-tree-icons`, **local only — no upstream configured, never pushed.** 89 commits ahead of `origin/main`; 11 of them are this work, the rest are the tech-tree icons, the farming drone, and the earlier shared-area status work.
- Build clean, zero errors: `dotnet build EcoServerMod/AdvancedElectronics`.
- 581 tests passing in `AdvancedElectronics.Navigation.Tests`, up from 566 before this work. `dotnet test EcoServerMod/AdvancedElectronics.Navigation.Tests`.
- Working tree carries four files that are **not this work and must not be committed by anyone picking this up**: both `ProjectSettings/*.asset`, `docs/plans/2026-08-10-001-feat-tech-tree-icons-plan.md` (the icons session's), and the untracked `docs/plans/2026-08-22-0010-feat-farming-drones-plan.md` (the farming session's). They were dirty before this work began and stayed untouched throughout.

## The plan, and the one before it

`docs/plans/2026-09-04-1431-fix-ground-change-attribution-plan.md` is the authority. It is `implementation-ready` and every one of its eight units is implemented.

Two earlier states of that plan exist in history and are worth knowing about before reading the current one:

- Commit `33bd5cd` holds the version that assumed the mod watches the world for outside ground changes. Its commit message lists exactly what rested on that premise. It was kept deliberately so the removed design can be read back if it ever turns out to have been right.
- Commit `c15c2ab` narrowed the premise and explains why, at length. Read that message before questioning why the plan is shaped the way it is.
- Commit `9893af8` renamed the file from `...-ground-change-on-dock-tick-plan.md`. The old name described the removed design.

## What shipped

Eleven commits. Three are documents, eight are the units.

| Unit | Commit | What it does |
|---|---|---|
| U1 | `9422a70` | A ground write this mod did not make is ignored entirely |
| U2 | `4dc4463` | Our own drone changing another area's ground marks its plots instead of deleting their survey |
| U3 | `10dc075` | The readout labels an area's figures as out of date rather than changing them |
| U4 | `138ea68` | A marked plot reaches both places that decide whether a plot counts as read |
| U5 | `caf9d12` | Every read-modify-write on an area's stored data is atomic |
| U6 | `3a05504` | The mining drone records at the work site that the survey no longer holds |
| U7 | `1c3814b` | An area shows `[digging]` from the moment a mining drone is assigned |
| U8 | `e2c8430` | The reasons the narrowing turned upside down are corrected in the code |

The single behavioural sentence: **the mod reacts only to ground its own drones changed, marks rather than destroys when it does react, and learns about everything else in situ from the drone standing on the ground.**

## Verified, and not verified — read this before trusting the green suite

**Verified in session.** The build, and every unit whose logic lives in the Eco-free `AdvancedElectronics.Navigation` assembly: the verdict function, the status derivation, the readout label, the rewind arithmetic. Each behavioural change was observed failing before the production code was changed, except U3, whose evidence is weaker and is described in its own commit message.

**Not verified, and not verifiable without a running server.** Everything in the Eco-dependent `AdvancedElectronics` assembly: the stored lists, the listener wiring, the lock, in situ discovery, and the communication between the two docks. The test project deliberately cannot reference that assembly.

**Why that distinction matters more here than usual.** U3 added the out-of-date label with five passing unit tests, and the label could not have appeared in the running game, because nothing on the Eco side ever set the field that triggers it. The tests passed because they construct the display snapshot directly. It was caught four commits later, during U7, and is fixed. Treat a green suite as evidence about half the change and no evidence at all about the other half.

## Outstanding — live checks, in the order to run them

None of these was run. They need a deployed server.

1. **The load line, first, before anything else.** `grep "Loading AdvancedElectronics" <server>/Logs/<newest>.log` after deploying. U2 and U7 each added a `[Serialized]` member. A stored member the engine rejects fails type registration with a clean build and no log output at all — not even the mod's own load line — so every check below is meaningless until this one passes.
2. **A player digs by hand inside a surveyed area.** Nothing should happen to the area at all: findings unchanged, tag unchanged, no label. This is the narrowing working.
3. **The administrator cache-rebuild command with several surveyed areas present.** No area affected in any way. This is the wipe that used to destroy every survey result on the server.
4. **A mining drone assigned to a surveyed area.** The area shows `[digging]` on the survey tab before the drone removes a single block.
5. **The end-to-end one.** Survey an area, remove some of the surveyed ore by hand so the survey is knowingly wrong, assign a mining drone, and confirm that on reaching the affected plot it marks the plot, logs the discovery, and the readout labels the area's figures as out of date. This is the proof that in situ discovery replaces what the listener no longer does. U6 emits a log line specifically so this check has something to observe.
6. **Two overlapping areas owned by different docks**, one worked by a mining drone: the other area's plots are marked, its findings are still listed, and its tag is unchanged.
7. **A survey drone re-reading a marked plot** produces a genuinely fresh reading rather than serving its earlier samples.

## Settled — do not re-derive these

This session re-derived a settled decision incorrectly, twice, and built a whole plan on the wrong premise before it was caught. The decisions below are the product owner's. A fresh agent that re-derives them from the code will get some of them wrong, because in places the code is what was wrong.

- **The mod does not monitor the world for changes it did not make.** It does not subscribe to outside events to keep its picture current and is deliberately never told when ground has been dug, filled, or made workable again by anyone else. Discovery happens at the work site, by the drone doing the work. This is also why reassignment is the retry for a refusal.
- **The block-change listener stays.** Removing it was proposed and rejected. The listener already distinguishes a write carrying one of our markers from one that does not, so the decision is served by changing what it does with an unattributed write, not by deleting a system.
- **Mark, never destroy.** A reaction marks plots for re-reading and keeps every survey result. Deleting is reserved for an area edit giving up ground, and for deleting an area.
- **A mark changes what the drones do and never the area's status tag.** The area keeps the tag it last earned. What the player gains is a label saying the figures are old.
- **The figures are not recalculated.** Coverage and findings stay exactly as recorded, because they remain an accurate record of what the pass found. Saying they are old is the honest correction; altering them is not.
- **An area holds `[surveyed]` only while untouched and unassigned.** Assignment moves it to `[digging]` before any block comes out; completion moves it to `[mined]`. This was an original requirement of the wider effort, deferred and never written into a plan until now.
- **The mining dock tells the survey dock through the claim**, not through a second channel. Both docks read the same area.
- **A plot is 5 by 5 columns, which is 25 columns.** `PropertyPlotLength` is `Chunk.Size / 2` and `Chunk.Size` is `10`. An earlier document said 8 by 8 and 64; it now carries a correction notice.
- **Specs are written in full explicit prose.** No compression, no invented shorthand, every term defined where first used, repetition preferred over ambiguity. The plan and these documents follow that.

## Known gaps, accepted deliberately

- **In situ discovery detects one direction only.** It finds the survey having promised ore in a plot whose ground is gone. It does not find ground appearing where the survey recorded none, because the survey stores one aggregated row per ore type per plot rather than a position for every block, so there is nothing precise to compare a new block against. Stated in the code at the check itself.
- **The check is scoped to the first layer of a mining pass.** Deeper down, a layer with nothing removable is ordinary, and treating it as a discrepancy would mark half the plots on the server. A counter accumulated over a whole plot would catch more, but the mining strategy does not survive a trip home with a full hold, so such a counter would reset mid-plot and report discrepancies that are not there.
- **Subsurface changes remain invisible.** The engine raises a signal only when the topmost block of a column changes, so a player tunnelling underground reaches nothing. Pre-existing; unchanged by this work; now largely moot, since outside changes are not acted on anyway.
- **The area-data lock is static, not per area.** A per-area lock is the finer instrument but would need a non-serialized instance field surviving deserialization, and a mistake there fails server init silently. The coarse lock was chosen because the risk could not be tested here. Revisit once the load line has been confirmed on a live server.
- **`[digging]` on an upgraded save.** A claim taken before this work has no recorded work kind, so it produces no `[digging]` until the next assignment. That is the safe direction and is deliberate — the alternative encoding would have made every legacy claim read as a mining claim.

## Traps that cost time here

- **A `[Serialized]` member without a setter fails server init silently.** Clean build, no exception, no stack trace; the absent load line is the only evidence. Every `[Serialized]` collection must be a `ThreadSafeList<T>` of flat primitives. See `docs/solutions/conventions/serialized-needs-a-member-to-write-back-into.md`.
- **`AreaKind.Mining` is `0`,** so any new serialized enum-like value that mirrors those ordinals will read as "mining" on every existing save. U7's claim work kind is numbered from 1 for exactly this reason.
- **`DroneDock` is a partial class across four files,** and another author's committed code reaches `HasFullAccess`, `StampedCitizenName` and `StampedCitizenId` through it nine times. Do not move or rename those three. Do not change `SurveyArea`'s constructor or its `EnumeratePlots()`.
- **A worktree cannot build this repo.** `Local.props` carries `EcoRefAssembliesDir` and is git-ignored, so a fresh worktree resolves no reference assemblies. Work in the main checkout.
- **Two places ask whether a plot has been surveyed,** and they do not share a path: the area's status derivation, and mining plot selection, which reads the surveyed timestamp directly. Any change to what "surveyed" means has to reach both.
- Git Bash and PowerShell take different syntax and the wrong one usually fails silently. Never pass a multi-line commit message with `-m`; use `-F -` and a quoted heredoc, and read it back with `%B`.

## If you pick this up

Deploy and run the seven live checks in the order given, starting with the load line. Nothing else in this work is blocked on anything but that.

If a check fails, the plan's unit for that behaviour names the file and the reasoning, and the commit for that unit says what evidence was and was not gathered. Prefer reading the commit message over re-deriving the intent from the code — several of these decisions are ones the code alone would lead you to reverse.
