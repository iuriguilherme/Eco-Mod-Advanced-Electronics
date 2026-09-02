<prior-decisions>
Round 1 — applied (12 entries). Every one of these was acted on. Several were applied with a DIFFERENT fix than the reviewer proposed, because the user corrected the reviewer's premise. Do not re-raise a finding on this list unless the current document text shows the fix failed to land or landed ineffectively.

- R15, R16, R5, Dependencies: "R15's world-change reset fires on the mod's own mining drone" (feasibility, adversarial, 100)
  Applied with an amended fix. The user established that a mined area IS unsurveyed — the survey really is stale — so the remedy became attribution, not suppression. R16 was added, and R5 gained the rule that mineability follows from the survey alone.
  Evidence: "R15. When the ground under a surveyed area changes outside the mod's own drones, the area returns to unsurveyed."

- R6, R7, Dependencies, AE4: "R6's cleared test is stronger than the single read it rests on" (feasibility, 75)
  Applied with an amended fix. The user established there is no scattered unmineable rock: the only impenetrable block is bedrock at the bottom of the world. Obstructions block a single column, not the ground, and digging continues beneath them.
  Evidence: "The test is that nothing mineable remains below, not that the surface reached a fixed Y coordinate."

- R9: "Rests-on-floor observations belong to neither clear list" (adversarial, 75)
  Applied as proposed. R9 now clears the per-plot rests-on-bedrock observations.
  Evidence: "R9. Starting a resurvey clears the area's findings and its live sample record before the drone begins sampling."

- Goal Capsule: "Dock-to-dock visibility omitted from stated active scope" (scope-guardian, 75)
  Applied as proposed. Active scope now names three areas.
  Evidence: "Active scope: the shared status model and the resurvey correctness fix."

- R5, R6, R17, R18, Key Decisions: "R5's stamps-only [mined] rule drops the skipped-plot verdict" (feasibility, adversarial, 100)
  Applied with a different mechanism. The user rejected recording a mined stamp for ground the drone never touched. Instead R17 has the mining pass record what it could not mine, and R18 has the survey exclude that from its findings. R6 now defines [cleared] against that.
  Evidence: "R5. An area reads as `[mined]` when its mined stamps are newer than its survey stamps"

- R6, R17, R18: "[cleared] unreachable when mining skips a plot" (product-lens, adversarial, 100)
  Withdrawn as a separate finding, resolved by the R17/R18 exclusion mechanism above. This withdrawal was Apply-triggered, so verify it actually holds in the current text rather than assuming it.
  Evidence: "R6. An area reads as `[cleared]` when every plot in it has been dug to its unmineable floor."

- R19, Dependencies, Summary: "Moving mined stamps onto the area loses them on redraw or delete" (adversarial, product-lens, 100)
  Applied with a redirected fix. The user established that area destruction is already handled — drones detect it and return to dock — so the fix moved to the edit case: R19 preserves state for plots an edit retains. Dependencies gained the deployment shape (one survey dock, several mining docks; multiple survey docks hold independent information).
  Evidence: "R1. Per-plot mined stamps are stored on the survey area, alongside its survey stamps"

- R3, R4, R8, Key Decisions: "[unreachable] has no defined place in the lifecycle" (coherence, design-lens, adversarial, 100)
  Applied as proposed. [unreachable] left the colour ramp and renders uncoloured beside [assigned]; red belongs to [cleared] alone.
  Evidence: "Red serves both `[unreachable]` and `[cleared]`. They share the exclusive slot and cannot appear together"

- R21: "Nothing specifies the outcome of an interrupted resurvey" (product-lens, adversarial, 100)
  Applied with an amended fix. The user rejected a stalled-pass marker: coverage percentage is the existing signal. R21 keeps partial samples so a later pass resumes rather than restarts.
  Evidence: "R9. Starting a resurvey clears the area's findings and its live sample record before the drone begins sampling."

- R15: "R15 resets a whole area for one changed block" (product-lens, 75)
  Applied as proposed. R15 now invalidates only the plots whose ground changed.
  Evidence: "the area does not distinguish who changed it"

- R4: "R4's colour ramp has no text fallback for two of four states" (design-lens, 75)
  Applied as proposed. [surveyed] joined the vocabulary; every state carries a word as well as a colour.
  Evidence: "The lifecycle statuses and their line colours are: unsurveyed uncoloured, surveyed green"

- Problem Frame, R20, Key Decisions, Scope Boundaries: "Dock-network radius narrows existing behaviour with no stated goal" (product-lens, 75)
  Applied as proposed. The user supplied the intent: the same believability constraint bounded storage links exist for, plus a growth axis — raising the radius is a future upgrade-module effect, alongside the drone cap. R20 covers worlds upgrading into the radius.
  Evidence: "R13. A mining dock may only work areas belonging to survey docks inside a dedicated dock-network radius"

Round 1 — rejected (0 entries).

Round 2 — applied (17 entries). All acted on. Six were applied with a DIFFERENT fix than the reviewer proposed, because the user corrected the premise. Do not re-raise any of these unless the current text shows the fix failed to land.

- AE coverage tag: corrected to cite only the world-change requirement, not the area-edit one. (coherence, 100)
- R9 / resume: the clear now fires only on a newly started resurvey; a resuming pass skips it and keeps its coverage. Fixes a round-1 fix that had landed ineffectively. (product-lens, adversarial, 100)
- R17 / refusal granularity: property, settlement-law and pathing refusals are recorded per plot, because the pass abandons the plot at the refused layer. Only classifier-identified ground is per block. (feasibility, 100)
- R21 / upgrade: a drone mid-pass over a now-out-of-range area returns to dock on the vanished-area path. (adversarial, 75)
- R20 / out-of-range reporting: generalised from upgrading worlds to every out-of-range dock pair. (product-lens, 75)
- State diagram: reset edges relabelled per-plot, mod-mining transition added, area-level-only caveat added. (adversarial, 75)
- R4, R25, R26, AE10, AE11: the user split the terminal state in two. `[cleared]` now means only that the drone is finished, which may be because it was refused; `[empty]` (grey) means genuinely nothing left and is what landfill consumes. R26 requires the refusal reason on the line. (product-lens, design-lens, adversarial, 100)
- R17, R18 / exclusion scope: a refusal about the ground binds every dock; a refusal about one dock's permit or route binds only that dock. (product-lens, feasibility, adversarial, 100)
- R18 / lifting: the survey lifts what it suppresses — a pass observing mineable material at an excluded location drops that exclusion, because a `[cleared]` area receives no mining pass. (product-lens, adversarial, 100)
- R16 / mining's effect: mining marks staleness through mined stamps alone; dug plots keep their findings and coverage. (product-lens, adversarial, 100)
- Dependencies / per-plot findings: recorded that per-plot invalidation and edit preservation require the persisted findings to become per-plot rows. Named as the largest implied piece of work. (feasibility, 100)
- R3, R27 / mixed areas: the user separated two concerns — a precedence rule so a heterogeneous area resolves to its dominant status, and a compression rule so the roster shows the gist rather than per-plot detail. Drones read plots; players read areas. (adversarial, 75)
- Dependencies / resume persistence: recorded that the resume requires persisting the sample record and sweep cursor, reversing a documented session-scoped decision, and that R9's clear is what makes it safe. (feasibility, 75)
- R20 / job survival: an edit ends an in-flight mining job only when it removes plots that job still has to work. (feasibility, 75)
- R21 / mid-pass edit: an edit landing during a survey pass keeps the pass alive; removed plots leave its coverage denominator. (adversarial, 75)
- R28, AE13 / tab convergence: both tabs render the same fields in the same order. (design-lens, 75)
- R29 through R33 / overlap: the user replaced the authorship-based exemption with overlap as the governing concept. Docks exchange geometry regardless of the radius; an overlapping draw is never refused but marked; the detail names each overlapping plot's centre block; no drone acts on an overlapping plot; and a drone's write on its own dock's area records that drone's own state. (raised by a peer session, not by this review)

Round 2 — rejected (0 entries).

Round 3 — applied (15 entries), plus a post-round simplification the user directed. Numbering changed repeatedly; cite by text, not by number. Do not re-raise any of these unless the CURRENT text shows the fix failed to land.

- Handover latch added, then REMOVED again (see the simplification below). Two reviewers found that a farm drone's first write on handed-over ground unsurveyed those plots, so the ceding area stopped reading [empty] and both drones stopped. (product-lens, adversarial, 100)
- Lifecycle status derives from every recorded exclusion regardless of which dock recorded it, so one area has one status. The per-dock scoping settled in round 2 governs what a dock is OFFERED, not what the area reads. User's rule: information drones collect is shared; whether a refusal stops YOU is a separate verdict. (feasibility, adversarial, product-lens, 100)
- Changing an area's kind is an explicit owner action, refused while any drone is mid-pass on that area or one overlapping it, and preserves findings, mined stamps and exclusions. (adversarial, product-lens, 100)
- Cross-owner overlap griefing: WITHDRAWN. Settled instead by the internal-channel rule below.
- NEW STATE [mining] (purple), between [surveyed] and [mined]. The user identified that R3 and its acceptance example disagreed because they described different states and one had no name. [surveyed] = not yet re-mined; [mining] = partly dug; [mined] = fully re-mined. A resurvey of partly-dug ground returns it to [surveyed], because mining can only take what a survey found. (adversarial, 75)
- Docks exchange geometry and observations INTERNALLY; player-facing visibility, editing and assignment stay permission-gated exactly as today. When an action would collide with another player's area the mod warns and names the consequence without exposing that area's contents. (user-directed)
- Writes attribute to the area whose kind the write serves, so ground two areas cover records the state of the work actually being done. The "rare case" claim was dropped. (adversarial, 75)
- Roster line: [empty]'s grey carries a stated verification condition against the client's default text; the mixed-plot note has literal wording ("partly worked") citing the phrase that actually exists ("most <ore>"); overlays are ordered [overlap], [unreachable], [assigned] and capped at two; the Mining tab keeps its owning-dock prefix because area names are not unique and the selector commits by position. (design-lens, feasibility, 75-100)
- Contamination is BUILDABLE, not unmodelled: Eco has a pollution layer, the check is cheap, and it belongs to a farming drone. Neither survey nor mining sensing changes. (user-directed, reversing two reviewers who wanted the requirement demoted)
- The coverage-and-material summary is narrowed by the READING dock's filter — the one field convergence deliberately leaves different, because a filter is a display preference. (feasibility, 100)
- Two mechanical fixes: a stale "open question" cross-reference, and a "still to decide" line contradicting a settled requirement. (coherence, 100)

POST-ROUND SIMPLIFICATION, user-directed and the most important thing in this primer. The user identified the assumption every reviewer and the agent had missed: KIND ALREADY PREVENTS MINING FARMED GROUND, because a mining drone does not work farm areas at all. The conditional stop, the [empty]-only release carve-out and the recorded latch were machinery for defining "done" in the wrong place. They were replaced by two rules:

- A drone does not work a plot that also belongs to an IN-USE area of a different kind. In use = assigned to a dock AND still having work to do. Unassigned is not in use; [empty] is not in use; [cleared] IS in use, because its exclusion may lift.
- A dock working an area records ITS KIND'S DATA on that area, and that record is what tells every other dock what the area is now for. A farm writes farming data, so ground stops being mining ground because the area now says it is farmland — not because a status was held or a release recorded. This makes [empty] a waypoint, not a permanent condition. A kind's data is whatever that kind already keeps and no more: per-plot stamps for mining, an area-level marker for farming. The requirement asks for NO new structure.

Also settled: claims attach to ASSIGNMENTS, not areas. An unassigned area holds no ground. Release ends a claim rather than suspending it, so a re-assignment is a new claim entering the ordinary path.

Round 3 — rejected (0 entries).
</prior-decisions>
