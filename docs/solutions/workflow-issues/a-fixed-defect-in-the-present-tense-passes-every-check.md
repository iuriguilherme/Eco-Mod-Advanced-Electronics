---
title: "A fixed defect described in the present tense passes every check there is"
date: 2026-09-06
last_updated: 2026-09-15
category: workflow-issues
module: docs
problem_type: workflow_issue
component: documentation
severity: high
applies_when:
  - "Auditing or refreshing the learnings store after the defects its docs describe were fixed"
  - "A doc's citation check passes and nobody has re-run the command the doc quotes"
  - "Writing a learning in the same commit that repairs the defect the learning describes"
  - "A doc's reproduction, residue sweep, or resolution table now returns a clean result"
  - "Deciding whether to delete a learning whose incident no longer reproduces"
symptoms:
  - "A doc's quoted reproduction runs clean, so the hazard it teaches reads as imaginary"
  - "A sweep returns nothing and a reader cannot tell a passing sweep from a broken command"
  - "A doc describes a fixed defect in the present tense, beside a link that was already repaired"
  - "A table names backup files or prefabs that no longer exist anywhere in the tree"
  - "Every path, symbol, line number and markdown link resolves while the prose describes a past state"
root_cause: inadequate_documentation
resolution_type: documentation_update
tags:
  - documentation-drift
  - knowledge-store
  - learnings-audit
  - resolved-incident
  - verification
  - validation
  - ce-compound
  - eco-modding
related_components:
  - docs/solutions
  - docs
  - scripts/validate-learnings.py
---

# A fixed defect described in the present tense passes every check there is

Every commit named below that is *not* reachable from `origin/main` is on `feat/tech-tree-icons`,
which is unmerged and has no pull request. There is therefore no PR number to cite for those, and the
short SHAs are branch-local: they are the only way to name that work today, they are cited here the
way `a-cross-reference-makes-two-claims-and-only-the-path-is-checked.md` and two other docs in this
store cite branch-local SHAs, and they are not durable references. If this branch is ever squashed or
rebased, look those commits up by their subject lines rather than by these hashes. The commits that
closed the four underlying defects — `7f3b526`, `aac18e3`, `186e648`, `96af0af` — are all reachable
from `origin/main` and are stable history.

## Context

Every automated check this store has verifies **existence**. That is a read finding, not an
impression, and it is worth stating check by check because the whole argument below rests on it.

**The repo's own gate, `scripts/validate-learnings.py`,** reads each doc under `docs/solutions/` and
parses only the YAML header between the two `---` delimiters. It checks that the required fields are
present (`title`, `date`, `category`, `module`, `problem_type`, `component`, `severity`, at line 46),
that `problem_type`, `severity` and `resolution_type` hold values from the closed enums, that a
bug-track doc also carries `symptoms`, `root_cause` and `resolution_type`, that `date` and
`last_updated` are `YYYY-MM-DD`, that `applies_when`, `symptoms` and `tags` are within their caps
(line 48), and that the `category` field equals the name of the directory the file sits in (lines
129-132). It reads the file's full text at line 87 solely to locate that header. **Nothing below the
closing `---` is examined at all.** Run today it reports `PASS: 75 learnings conform to the
frontmatter contract`.

**The plugin's claims validator, `validate-doc-claims.py`,** does look at the body, and performs four
checks on it. First, every backticked span is normalized — `normalize_path` at line 214 strips a
trailing `:12` or `:12-20` line reference, so a cited *line number* is never verified, only the file
it hangs off — and then tested for existence with `os.path.exists` at line 340, falling back to
`git cat-file -e HEAD:<path>` and the same against the upstream default branch (`head_has_path` and
`upstream_has_path`, lines 302-312) so that a file deleted by the very fix being documented is
classified as historical rather than reported as fabricated. Second, every hex word of 7 to 40
characters is resolved with `git cat-file -e <sha>^{commit}` and then classified by reachability from
`HEAD` and from the upstream branch (lines 392-443). Third, every relative markdown link target is
tested with `os.path.exists` against the doc's own directory (line 458). Fourth, a regex pass looks
for leaked drafting scaffold — `{{...}}` tokens and "Learning N" numbering — in the *citing* doc's own
masked text (lines 466-474). **The script never opens a cited file.** Its own docstring is explicit
that its output is adjudication input rather than a gate: *"Flags are adjudication input, NOT hard
failures — a doc may legitimately cite a path deleted by the very fix it documents"* (lines 44-46).

**The plugin's third script, `validate-frontmatter.py`,** checks that the header *parses* —
delimiters, an unquoted `#` that would truncate a value, an unquoted `: ` that would read as a nested
mapping. That description is `scripts/validate-learnings.py`'s own docstring at lines 9-15, which
exists precisely to record what the bundled pair does not cover.

So the complete set of questions the toolchain asks is: does this path exist, does this commit exist,
does this link resolve, does this header parse, and does this header's values sit inside the schema.
Every one of them is answerable without knowing anything whatsoever about what the repository
*does*.

Now consider what a repair does to a doc that documents a defect. It preserves every single thing on
that list. The cited file stays where it is. The cited symbol stays in the tree. The cited line stays
inside the file. The link still lands. The commit still resolves. What changes is only the
**behaviour the document describes** — and that is the one property no check in the set can see. A
doc can therefore pass every check in this repository and in the plugin while describing, in the
present tense, a defect that was fixed weeks earlier.

This is the neighbour of a failure this store already documents, and the two should not be conflated.
`docs/solutions/workflow-issues/a-cross-reference-makes-two-claims-and-only-the-path-is-checked.md`
establishes that a cross-reference carries a path claim and a description claim, and that only the
path half has an owner: the sentence describing what another doc *says* is checked by nobody. The
failure documented here is a third thing. The citation is right, the description of the sibling doc
is right, and the *world* moved. Nothing in the document is wrong about the store. It is wrong about
the repository.

Four instances surfaced in one session's audit of three clusters of learnings. **Every one of them
passed the citation check immediately before being caught by a human reading the prose.** All four
have since been repaired, so the tree today carries the fix pattern rather than the defect; what
follows verifies each against the tree and against `git log`, and in two places the tree adds
something the original account did not have.

### Instance 1 — a crash reproduction whose subject stopped crashing

`docs/solutions/workflow-issues/a-crashed-check-and-a-flagged-check-are-opposite-problems.md`
(created in `1c5e7f6`, 2026-08-09) documents that the bundled Python validators read docs with a bare
`open()` and therefore decode them as `cp1252` on this machine, so a document containing a byte
`cp1252` leaves undefined crashes the validator before it has read a single claim. Its reproduction
ran the claims validator against
`docs/solutions/architecture-patterns/client-animation-is-driven-by-name-not-by-mod-code.md` and
expected `UnicodeDecodeError: 'charmap' codec can't decode byte 0x90 in position 2658`.

Scanning that file today for the five undefined bytes — `0x81`, `0x8D`, `0x8F`, `0x90`, `0x9D` —
returns **zero**. It validates clean with no environment variable set at all. The hazard itself is
entirely real and still reproducible one directory over:
`docs/solutions/architecture-patterns/persist-derived-data-as-serialized-snapshot-on-its-owner.md`
contains exactly one such byte, `0x90`, from a leftwards arrow U+2190 in a code comment. It sits at
offset 10169 today; the runs captured below name 7675, the offset before a 2026-09-15 refresh of that
doc moved it.

The commit that staled it is `186e648`, 2026-08-14, *"docs(solutions): record how far the client-code
constraint propagated"*, reachable from `origin/main`. Replaying the byte count over that file's
revisions puts the transition exactly there, and the removed line is visible in the diff: a
box-drawing connector line ending in U+2510, box drawings light down and left. That codepoint encodes
as `E2 94 90`, and the `0x90` is its third byte. (This document names the codepoint rather than
embedding the glyph, following the same courtesy the crashed-check doc extends to itself, so that it
stays readable under a `cp1252` default read.) **This one was not a fix.** It was an unrelated content edit that rewrote a section and
happened to delete the demonstration's subject along the way, which is worth recording because it
shows the drift does not require anyone to be repairing the thing the doc is about.

The doc was repaired in `95c454f`, 2026-09-06 (branch-only), which repointed the reproduction at the
doc that still carries the byte, recorded the re-check date, and deliberately dropped the list of
affected files as unmaintainable — *"this hazard arrives and departs with a doc's punctuation rather
than with anything about the tooling, so the set of affected files is not stable and is not worth
maintaining as a list."*

### Instance 2 — a residue sweep whose residue had already been cleaned

`docs/solutions/conventions/auditing-content-derived-from-autogen-templates.md` (created in
`1281ccd`, 2026-07-31 at 12:23) teaches that mod content copied from a vanilla AutoGen template keeps
references to the template, and that the highest-signal detection is to grep the derived file for the
template's own subject noun. Its worked example greps
`EcoServerMod/AdvancedElectronics/Battery.cs` for leftover `Biodiesel`.

That grep returns nothing today. The file's two crafting elements are
`new CraftingElement<BatteryItem>(1)` at `Battery.cs:54` and `Battery.cs:120`, and the string
`Biodiesel` does not appear anywhere in it.

The residue was real and was cleaned in `7f3b526`, *"fix(server): crafting a Battery produced
Biodiesel"*, reachable from `origin/main`. Its timestamp is **2026-07-30 at 23:06**, which is thirteen
hours *before* the doc that demonstrates it was committed. The demonstration therefore returned
nothing for every reader the document has ever had. `git log -S'Biodiesel' -- Battery.cs` shows only
two commits touching that string: `f1303e8`, which introduced the file, and `7f3b526`, which removed
it.

The doc was repaired in `850dacc`, 2026-09-06 (branch-only). Its commit body states the cost in one
sentence: the fix means *"an empty result there is now a passing sweep rather than a broken
command."*

### Instance 3 — a plan quoted in the present tense, fixed by the citing commit itself

`docs/solutions/workflow-issues/a-knowledge-store-corroborates-its-own-errors.md` was created in
`96af0af`, 2026-08-14, reachable from `origin/main`. Its section *"Repointing a link is not correcting
a sentence"* quotes `docs/plans/2026-08-01-001-feat-dock-owns-drone-components-plan.md` at `:517`,
where an earlier correction pass had repaired a link to a renamed doc and left the gloss beside it
saying the opposite of what the new target says, and at `:418`, where the same overturned belief sat
as a governing assumption rather than as a citation.

Both lines read correctly today. `:517` now says *"why component changes reach objects already
placed, in both directions"*, and `:418` now opens *"**Component changes DO retrofit — corrected
2026-08-14.**"* with the superseded belief kept and labelled beneath it.

Both were repaired in `96af0af` — **the very commit that wrote the learning.** `git show --stat`
on it lists the plan among five changed files, four lines changed, alongside the 358-line addition of
the learning doc itself. So the paragraph describing the defect in the present tense was already
false at the moment it was committed, in the same diff.

The doc was repaired in `e570fb9`, 2026-09-05 (branch-only), which marked the passage historical
rather than deleting it, on the ground that *"the tense slip is itself an instance: prose written from
the pre-fix state, in a commit that also carried the fix."*

### Instance 4 — a binding table naming four prefabs that no longer exist

`docs/solutions/conventions/moving-a-prefab-can-hand-its-guid-to-a-backup-copy.md` (created in
`ec755ba`, 2026-08-07, then under `docs/solutions/logic-errors/`) documents a Unity asset move that
handed three live prefabs' GUIDs to the `Old*` backup copies created in the same operation, so the
scene's `ModkitPrefabContainer` kept shipping the retired versions. Its central artifact is a table of
five container slots: four resolving to `OldDroneDockObject`, `OldSurveyDroneObject`,
`OldHarvestDroneObject` and `OldOldSurveyDroneObject`, and one dangling. Beneath the table sat the
sentence *"`MiningDroneObject` appears nowhere in the list, so it would not ship at all."*

None of those `Old*` prefabs exists. A glob of `Assets/Art/AdvancedElectronics/**/*.prefab` returns
exactly five files, all live: `AdvancedElectronicsAssemblyObject`, `DroneDockObject`,
`SurveyDroneObject`, `MiningDroneObject`, `HarvestDroneObject`. `scripts/validate-name-match.sh`
reports `PASS: every server WorldObject/Item type has a matching-named client asset.`

The commit that closed it is `aac18e3`, 2026-08-08, *"feat(art): re-export the HRVSTR chassis and mask
the propeller layer"*, reachable from `origin/main`, whose body says *"Drop the superseded Old* prefab
copies and the dock's placeholder pad edits that came with the rebuild."* That is **one day** after
the doc was written. This is the place the tree added to the original account: the repair commit
`195797a` recorded a dated re-check but did not name a closing commit. One was nameable in a single
query — `git log --diff-filter=D --name-only -- 'Assets/Art/AdvancedElectronics/**/Old*'` — and
`b9d79d3` has since put it into that doc's status note, which now opens by naming `aac18e3` and
observing that a reader searching history for the deletion would never have found it by its
subject.

The doc was repaired in `195797a`, 2026-09-06 (branch-only), which added a status note and moved the
table into the past tense. Its commit body names the cost precisely: a reader running the
GUID-resolution loop today *"gets a clean result and cannot tell whether the technique works or the
problem is simply gone."*

**And this instance carries the sharpest detail in the set.** On 2026-09-05, one day before the
staleness was caught, `3bd0cf1` — *"docs(solutions): refresh the logic-errors learnings against the
tree"* — edited this exact document. It moved the file from `logic-errors/` to `conventions/`, changed
`problem_type` from `bug` to `convention`, corrected the scene path in `related_components` from
`Assets/DroneScene.unity` to `Assets/Art/AdvancedElectronics/Scenes/AdvancedElectronicsScene.unity`,
and added a parenthetical to the Context paragraph noting that the art folder layout *"has shifted
again since, and `Icons/` now sits under `Sprites/`"*. Every one of those corrections is
existence-shaped: a path that moved, a directory that was renamed, a category field. The table two
paragraphs below, whose four named targets had been deleted four weeks earlier, was not touched. A
refresh pass read the paragraph, fixed what a path check would have flagged, and left the behaviour
claim exactly as it was.

## Guidance

### Detector one: re-run the doc's own quoted command

Nearly every learning in this store demonstrates rather than asserts. That is the store's central
virtue, and it is also the thing that goes stale, because a demonstration is a claim about the
current state of the repository dressed up as a code block. So the first pass over any doc whose
subject is a defect is mechanical: **find every command the doc quotes, run it, and compare what comes
back against what the prose says will come back.**

The comparison that matters is not "did it error" but "does it still demonstrate". A grep that returns
nothing has not failed. It has succeeded at finding nothing, and the entire question is whether the
prose around it expects a hit. In instance 2 the doc said *"every remaining `Biodiesel` is residue"*
over a command that could no longer produce one. In instance 4 the doc gave a GUID-resolution loop and
a table of what it would resolve to, and the loop now resolves everything to live prefabs. In instance
1 the doc gave a `UnicodeDecodeError` verbatim and named the file that produces it, and that file
produces nothing.

Where a doc's demonstration is not a command — instance 3 quotes two lines of a plan rather than
running anything — the equivalent is to open the cited file at the cited line and read it. This costs
one file read, and it is the same discipline the cross-reference doc argues for when describing a
sibling doc: *"Open the target and read its thesis sentence before you describe it."* Here the target
is a file in the repository rather than a doc in the store, and the sentence being checked is the
doc's own rather than a gloss, but the move is identical.

### Detector two: read the tense

A document about a defect, written in the present tense, whose demonstration now produces nothing, is
a fixed defect being described as live. That sentence is the whole detector, and it works without
running anything.

The tense is what turns a harmless historical account into a false claim. *"The scene's container
slots resolve to the `Old*` backups"* asserts something about the repository right now.
*"They resolved to the backups at the time"* asserts something about August. The second is
unfalsifiable by any future state of the tree, which is exactly the property a durable learning wants.
The first is a hostage to the next commit, and it is the form defect docs are naturally written in,
because at the moment of writing the present tense is simply accurate.

This is why the two detectors belong together. The tense read tells you which docs are exposed; the
command re-run tells you which of the exposed ones have actually drifted.

### The fix pattern

**Do not delete the incident.** This is the first and most important part, and it is where the obvious
repair goes wrong. The incident is the evidence that the rule was learned from something real rather
than asserted from taste, and a learning with its evidence removed reads like an opinion, with nothing
in the resulting file to say it ever had more. That argument is already made at length in
`a-crashed-check-and-a-flagged-check-are-opposite-problems.md` about auto-repairing a flagged
citation, and it applies with the same force to auto-repairing a stale demonstration: the tidied
version passes every check, looks like maintenance, and cannot afterwards be distinguished from a doc
that was always thin.

Apply four moves instead:

1. **Add a status note** at the top of the section whose claims went stale, or at the top of Context
   when the whole incident is historical. Lead with the resolution and state that the rule survives
   it.
2. **Name the commit that closed it**, with its subject line, so a reader can go and see the fix
   rather than take the status note's word for it.
3. **Put the incident into the past tense** — the table header, the surrounding prose, the sentence
   introducing the command.
4. **Say what a clean result means now**, explicitly. This is the move that protects the technique
   rather than the fact, and it is the one most easily skipped.

`docs/solutions/conventions/consistent-grid-column-quantization.md` already does all four, and it is
the model to copy. Its Context opens:

> **Status: the inconsistency this documents is resolved. The rule stands and the codebase now follows
> it.** The truncating call site was deleted with the district scaffold (`e72108c`), and every
> remaining position-to-column mapping rounds. This section is kept as the incident that produced the
> rule; the Guidance below is current.

Note what that paragraph does in four sentences: it states the resolution, it asserts that the rule
outlives it, it names the commit, and it tells the reader which part of the document is history and
which part is current. Then, inline at the citation itself, it annotates the specific dead path rather
than removing it:

> - `EcoServerMod/AdvancedElectronics/DistrictAssignment.cs:63` (since deleted) truncated:

Two words in parentheses. The file is gone, the evidence is intact, the reader is not misled for a
second, and a claims validator flagging that path has the annotation sitting right beside it that
makes the flag safe to confirm as intentional.

The status note that `195797a` added to the prefab doc is the same pattern applied to a demonstration
rather than a citation, and its last clause is the part worth copying verbatim into any similar note:
*"a clean result from that loop today is the expected state, not a sign the check is broken."*

### When you cannot name a closing commit

Two of these four named their closing commit easily, because the doc's author already knew it. For the
other two the commit existed and simply had not been looked for. Both were one query away:

```bash
# What deleted these files? (instance 4 -> aac18e3, "Drop the superseded Old* prefab copies")
git log --oneline --diff-filter=D --name-only -- 'Assets/Art/AdvancedElectronics/**/Old*'

# When did this string leave this file? (instance 2 -> 7f3b526)
git log --format='%h %ci %s' -S'Biodiesel' -- EcoServerMod/AdvancedElectronics/Battery.cs
```

For instance 1, where the change was a byte rather than a string, replaying a check over the file's
revisions found it — `git log --format=%h -- <file>` and then a byte count on each
`git show <rev>:<file>` — landing on `186e648`. That is a general technique: whatever check the doc
demonstrates, run it over the revisions of the file the doc names, and the transition is the commit
you want.

When no commit can be found — because the change was an editor's untracked reorganisation, or
happened outside the repo — substitute a **dated re-check plus the command that produced it**. That
is strictly weaker than a commit, because it asks the reader to trust the note, but it is far stronger
than silence, and it gives the next reader a date to reason from.

## Why This Matters

**The store's most valuable documents are structurally guaranteed to describe a past state.**

Work the trajectory through. A learning about a defect gets written because someone hit the defect,
understood it, and fixed it. The fix is what makes the learning worth writing — an unfixed defect
produces a bug report, not a learning. So by the time the doc exists, the code it describes has
already changed, and the same act that made the document valuable is the act that falsified its
present tense. This is not a hazard that accumulates slowly at the edges of the store. It is a
property of the highest-value class of document in it, present from the moment of writing.

The dates measured here make that concrete, and they are worse than "eventually goes stale". Instance
2's demonstration was empty **thirteen hours before the doc was committed**. Instance 3's quoted lines
were repaired **in the commit that wrote the doc**. Instance 4's table named four files that were
deleted **the next day**. Only instance 1 had any meaningful interval, five days, and even that one
was staled by an edit that had nothing to do with the defect. The gap between "this is worth
documenting" and "this describes a past state" is routinely zero or negative.

This is the same shape as `a-defensive-rule-outlives-the-danger-it-answered.md`, one artifact over. A
mitigation survives the danger it answered because nothing about removing a danger surfaces the code
written to tolerate it. A doc's present tense survives the fix it documents because nothing about
committing a fix surfaces the prose written from the pre-fix state. In both cases the thing that
expires is a premise, and in both cases the artifact that carries it keeps reading as authoritative.

**And nothing in the toolchain is shaped to catch it, by construction rather than by oversight.** Go
back through the four checks. A path check answers whether `Battery.cs` exists; it does, and it did
throughout. A SHA check answers whether `7f3b526` resolves; it does, and it is reachable from
`origin/main`, so it does not even draw a flag. A link check answers whether a relative target
resolves. A frontmatter check answers whether the header parses and conforms. The repair that staled
each of these docs changed none of those properties, because a repair is defined by leaving the
structure in place and changing the behaviour. **The drift a repair produces is exactly the drift no
mechanical check will ever report.**

The flag output is not empty, either, which is its own trap. Running the claims validator on the
prefab doc today reports `checked 12 paths, 2 SHAs, 0 links; 7 flags, 5 notes` — bare folder names
like `Models/` and `Sprites/` as flags, the Unity GUIDs as notes, and one branch-local SHA. The AutoGen doc reports
`checked 8 paths, 1 SHAs, 0 links; 2 flags`, both of them engine-source paths outside this repository.
Every one of those is a known, standing "confirm intentional" per the adjudication table in
`a-crashed-check-and-a-flagged-check-are-opposite-problems.md`. So the checker is not silent; it is
talking about something else entirely, at length, while the actual defect sits three lines below a
flag being confirmed as fine.

Instance 4 shows this defeating a human pass as well, which is the part that should end any hope of
catching it incidentally. `3bd0cf1` was a deliberate refresh of these learnings against the tree. It
opened the file, edited its frontmatter, corrected a stale scene path, and updated a folder-layout
parenthetical inside the very paragraph above the stale table. Everything it corrected was
existence-shaped, because existence-shaped drift is what a refresh naturally looks for. The table
survived it untouched.

**The cost is authority, not accuracy, and that is a much worse thing to lose.** An inaccurate doc
misleads a reader about one fact. A doc whose demonstration produces nothing teaches the reader that
the technique does not work. Think about what the reader in front of that terminal concludes. They ran
the grep the doc told them to run, on the file the doc told them to run it on, and got silence. The
available explanations are "the world changed" and "this doc is wrong", and nothing on their screen
distinguishes the two — so the cheap conclusion, and often the right one for any other doc, is that
the store is unreliable. Both repair commits say exactly this in their own words: `850dacc`, that an
empty result is *"now a passing sweep rather than a broken command"*, and `195797a`, that a reader
*"cannot tell whether the technique works or the problem is simply gone."*

That reader then does not merely discard one fact. They discard the residue-sweep technique, or the
GUID-resolution loop — techniques that are entirely current, that cost seconds, and that exist because
each one caught a real defect that shipped. The document loses the thing it was written to transmit
while every fact in it remains individually defensible.

**And the loss compounds in the direction the store is designed to grow.** This store's whole value
proposition is that a future agent acts on these docs without re-deriving them. A doc that is read and
trusted transmits a stale present tense straight into whatever is built next; a doc that is read,
tested, and disbelieved takes a working technique out of circulation. Both outcomes come from the same
unannotated paragraph, and which one you get depends only on whether the reader was diligent enough to
run the command.

## When to Apply

- **Whenever a doc's subject is a defect and you are refreshing, auditing, citing, or about to act on
  it.** The trajectory described above applies to every such doc in the store without exception, so
  the question is never whether it is exposed but whether it has drifted yet.
- **At the moment you fix a defect that a doc documents.** This is the cheapest possible time, and it
  is the time three of these four instances went by unnoticed. The author of `7f3b526`, of `aac18e3`,
  and of `96af0af` was in each case standing next to the document at the moment it went stale. Adding
  a status note in the same commit costs one paragraph; finding it a month later cost an audit.
- **During any refresh pass, run the demonstrations.** Instance 4 proves that reading the doc is not
  enough — a refresh read that paragraph and corrected two existence-shaped facts inside it. Running
  the quoted grep would have taken one second and produced the finding directly.
- **When a doc is moved, recategorised, or edited for an unrelated reason.** Any commit that opens the
  file is an opportunity, and an edit that touches only structure is exactly the kind that walks past
  behavioural drift.
- **When you are about to conclude that a documented technique does not work** because you ran it and
  got nothing. That is the reader-facing symptom of this defect, and the correct next step is to check
  whether the defect was fixed, not to distrust the technique.

**Where this does not apply.**

- **A doc whose subject is a convention has no such trajectory.** A convention doc states a rule that
  is meant to keep holding — use one quantization function, name the prefab after the server class,
  grep the derived file for the template's noun. Its demonstrations demonstrate a practice rather than
  a past state of the code, so they do not expire when anything is fixed; if anything, the codebase
  moves toward them over time. Be careful not to read this off the `category` field, though. Two of
  the four instances here are filed under `conventions/`, and one of them, the prefab GUID doc, was
  filed under `logic-errors/` until the day before it was caught. What decides is whether the
  document's evidence is **an incident that has an end date**, not which directory it sits in.
- **This is not an argument for deleting stale examples.** Deleting the incident makes the doc pass
  every check, shortens it, and looks tidy in the diff — and it removes the only thing proving the
  rule was learned rather than asserted. The four repairs here all kept their incidents; every one
  added prose rather than removing it.
- **This is not about line numbers drifting.** A cited `:63` that has moved to `:71` is a much milder
  problem and is caught by an ordinary refresh. The failure here is a citation whose file, and usually
  whose line, is exactly right, and whose surrounding claim is about a world that no longer exists.
- **A doc that already frames its incident historically needs nothing.** The test is the tense and the
  status note, not the age of the file.

## Examples

The four, with what each demonstrated, what the demonstration produces today, the commit that closed
the underlying defect, and the commit that repaired the doc.

| Doc | Written | Demonstration closed by | Interval | Repaired in |
|---|---|---|---|---|
| `a-crashed-check-and-a-flagged-check-are-opposite-problems.md` | `1c5e7f6`, 2026-08-09 | `186e648`, 2026-08-14 (in `origin/main`) — an unrelated edit removed the U+2510 glyph | +5 days | `95c454f`, 2026-09-06 |
| `auditing-content-derived-from-autogen-templates.md` | `1281ccd`, 2026-07-31 12:23 | `7f3b526`, 2026-07-30 23:06 (in `origin/main`) | **−13 hours** | `850dacc`, 2026-09-06 |
| `a-knowledge-store-corroborates-its-own-errors.md` | `96af0af`, 2026-08-14 | `96af0af` — the same commit | **0** | `e570fb9`, 2026-09-05 |
| `moving-a-prefab-can-hand-its-guid-to-a-backup-copy.md` | `ec755ba`, 2026-08-07 | `aac18e3`, 2026-08-08 (in `origin/main`) | +1 day | `195797a`, 2026-09-06 |

**Instance 1 — the crash reproduction.**

Before, in the Examples section:

```text
**The crash, reproduced.** Running the claims validator against
`docs/solutions/architecture-patterns/client-animation-is-driven-by-name-not-by-mod-code.md` with no
environment variable produces the `UnicodeDecodeError` on byte `0x90` quoted above and checks
nothing.
```

After (`:279-291`), repointed at the doc that still carries the byte, with the re-check dated and the
machine's premise restated:

```text
**The crash, reproduced.** Re-run on 2026-09-06 against the doc that still carries the byte.
`python <script> docs/solutions/architecture-patterns/persist-derived-data-as-serialized-snapshot-on-its-owner.md`
with no environment variable produces

UnicodeDecodeError: 'charmap' codec can't decode byte 0x90 in position 7675: character maps to <undefined>
```

The Guidance section got the matching treatment — the list of affected docs was replaced by one doc
plus an explanation of why the list is not worth keeping. The verification, and the commit that ended
the old reproduction:

```bash
# Which docs still carry a byte cp1252 leaves undefined?
#   client-animation-...md                                   -> 0
#   persist-derived-data-as-serialized-snapshot-on-its-owner  -> 1  (0x90 at offset 7675)

# When did it leave the first one? Replay the check over that file's revisions:
git log --format=%h -- docs/solutions/architecture-patterns/client-animation-is-driven-by-name-not-by-mod-code.md
# 186e648 is the transition. Its diff removes a box-drawing connector line whose
# final glyph is U+2510 (box drawings light down and left) -> E2 94 90.
```

**Instance 2 — the residue sweep.** Before, the command stood alone under a sentence promising residue:

```bash
# Derived Battery.cs from vanilla Biodiesel.cs -- every remaining "Biodiesel" is residue.
grep -noE '[A-Za-z]*Biodiesel[A-Za-z]*' EcoServerMod/AdvancedElectronics/Battery.cs
```

After (`:132-137`), the same command with the status folded into the comment block, so a reader meets
it before running it:

```bash
# Derived Battery.cs from vanilla Biodiesel.cs -- every remaining "Biodiesel" is residue.
# Re-run 2026-09-06: returns nothing. That residue was real and was cleaned in 7f3b526,
# "fix(server): crafting a Battery produced Biodiesel" -- an empty result here is now a
# passing sweep rather than a broken command. The loop below still returns live output.
grep -noE '[A-Za-z]*Biodiesel[A-Za-z]*' EcoServerMod/AdvancedElectronics/Battery.cs
```

The last sentence is the one doing the real work: the block contains a second sweep that still returns
live output, and naming which of the two is which is what keeps the technique credible. The file today
holds `new CraftingElement<BatteryItem>(1)` at `:54` and `:120`, and the residue example immediately
below the sweep still shows both the wrong and the right form side by side.

**Instance 3 — the plan gloss.** Nothing was deleted here. A paragraph was appended directly beneath
the present-tense passage, at `:122-128`:

```text
**Both of those lines are fixed, and the tense above is the point.** `96af0af` — the commit that
recorded *this* learning — repaired them in the same diff: `:517`'s gloss now reads *"why component
changes reach objects already placed, in both directions"*, and `:418` now opens *"**Component
changes DO retrofit — corrected 2026-08-14.**"* with the superseded belief kept and labelled. So the
paragraph above describes a state that ended the moment it was written down. It is left standing
because it is the evidence, and because the tense slip is itself an instance: prose written from the
pre-fix state, in a commit that also carried the fix.
```

Verified against the tree: the plan reads exactly that way at both lines, and `git show 96af0af`
carries both edits in the same diff as the doc's own creation.

**Instance 4 — the binding table.** Before, a present-tense column header over four rows naming files
that had been deleted four weeks earlier:

```text
| Container slot | Intended | Actually resolves to |
|---|---|---|
| `8da7e182…` | `DroneDockObject` | `OldDroneDockObject` |
```

After (`:59-67`), a lead-in sentence plus a past-tense header, and the table otherwise intact:

```text
The bindings as they stood during the incident — every `Old*` target below has since been
deleted:

| Container slot | Intended | Resolved to, at the time |
|---|---|---|
| `8da7e182…` | `DroneDockObject` | `OldDroneDockObject` |
```

Above it, the status note that tells a reader what a clean result means — the whole pattern in one
paragraph, and the model to copy for any similar repair:

```text
**Status: the misbinding this documents is resolved, and the rule stands.** Re-checked
2026-09-06 — no `Old*` prefab remains under `Assets/Art/AdvancedElectronics`, the five live
prefabs are all present (...), and `scripts/validate-name-match.sh` reports `PASS`. In particular
`MiningDroneObject`, which the table below records as shipping nowhere at all, is present and
bound. The incident is kept because it is what produced the rule and because the GUID-resolution
loop under **Guidance** is how you would catch it again; a clean result from that loop today is
the expected state, not a sign the check is broken.
```

That note was missing step 2 of the fix pattern when this was written, and the tree supplied it: the
closing commit is `aac18e3`, 2026-08-08, *"feat(art): re-export the HRVSTR chassis and mask the
propeller layer"*, reachable from `origin/main`, whose body records *"Drop the superseded Old* prefab
copies and the dock's placeholder pad edits that came with the rebuild."* One query finds it:

```bash
git log --oneline --diff-filter=D --name-only -- 'Assets/Art/AdvancedElectronics/**/Old*'
# aac18e3 feat(art): re-export the HRVSTR chassis and mask the propeller layer
#   Assets/Art/AdvancedElectronics/Prefabs/OldDroneDockObject.prefab
#   ... four more, plus their .meta files
```

**What the checks said about all of this.** Run today, with the encoding variable set:

```text
$ PYTHONUTF8=1 python <plugin>/validate-doc-claims.py \
    docs/solutions/conventions/moving-a-prefab-can-hand-its-guid-to-a-backup-copy.md
checked 11 paths, 0 SHAs, 0 links; 6 flags, 5 notes     # bare folder names; Unity GUIDs read as SHAs

$ PYTHONUTF8=1 python <plugin>/validate-doc-claims.py \
    docs/solutions/conventions/auditing-content-derived-from-autogen-templates.md
checked 8 paths, 1 SHAs, 0 links; 2 flags               # both engine-source paths outside this repo

$ python scripts/validate-learnings.py
PASS: 75 learnings conform to the frontmatter contract
```

Every flag in that output is a standing "confirm intentional" from the adjudication table in
`a-crashed-check-and-a-flagged-check-are-opposite-problems.md`. The `7f3b526` in the AutoGen doc
resolves and is reachable from `origin/main`, so it draws nothing at all. `Battery.cs` exists, so it
draws nothing. The frontmatter conforms. Not one line of that output has anything to do with the fact
that, until 2026-09-06, one of those documents demonstrated its central technique with a command that
could not produce a result.

## Related

- `docs/solutions/conventions/moving-a-prefab-can-hand-its-guid-to-a-backup-copy.md` — instance four,
  and the one with dates attached. Written 2026-08-07, closed by `aac18e3` the next day, and still
  describing the misbinding as current thirty days later. Its status note now names the closing commit
  and records that the commit's subject was an unrelated art re-export, so nothing about it announced
  the closure.
- `docs/solutions/conventions/a-document-stored-in-its-own-generator-has-no-past-tense.md` — the same
  tense failure where a past tense is structurally unavailable. There a document lives only as its
  generator's template, so it cannot say "this used to be true"; here the past tense was available and
  simply went unwritten.

- `docs/solutions/workflow-issues/a-cross-reference-makes-two-claims-and-only-the-path-is-checked.md`
  — the neighbouring unowned claim, and the source of this doc's reading of the claims validator. That
  doc is about a sentence describing another *document* being wrong while the path resolves; this one
  is about a sentence describing the *repository* being wrong while everything cited still exists.
  Both are invisible to the same four existence checks, and its Guidance — open the target and read it
  before describing it — is the same move applied one artifact over.
- `docs/solutions/workflow-issues/a-crashed-check-and-a-flagged-check-are-opposite-problems.md` —
  instance 1, and the argument this doc's fix pattern rests on. Its section on what an auto-fix
  destroys establishes that removing a doc's dead citation leaves a learning that reads like an
  opinion, with nothing in the file to say it ever had evidence; the same holds for removing a stale
  demonstration. It also carries the adjudication table that classifies every flag these docs draw.
- `docs/solutions/conventions/consistent-grid-column-quantization.md` — the model. Its Context opens
  with a status note naming the commit that resolved the incident and stating that the rule outlives
  it, and its dead citation carries an inline `(since deleted)` rather than being removed.
- `docs/solutions/conventions/a-defensive-rule-outlives-the-danger-it-answered.md` — the same expiry
  in code rather than in prose. A mitigation survives the danger it answered because removing a danger
  surfaces nothing written to tolerate it; a doc's present tense survives the fix it documents for the
  identical reason, and in both cases the surviving artifact reads as authoritative.
- `docs/solutions/workflow-issues/a-knowledge-store-corroborates-its-own-errors.md` — instance 3, and
  the reason a status note beats a silent edit: the copies of a claim are still out there, and a
  reader arriving from one of them needs to land on a page that recognises what they were told rather
  than one that has quietly changed the subject.
- `docs/solutions/workflow-issues/a-gate-that-discovers-nothing-passes-everything.md` — the same
  ambiguity at the tooling layer. There a check reports clean because its corpus was empty; here a
  demonstration reports clean because its subject was fixed. In both cases an empty result and a
  passing result are the same bytes on the screen, and only context distinguishes them.
