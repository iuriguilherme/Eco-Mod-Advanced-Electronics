---
title: "A fix does not reach the copies already taken from the file"
date: 2026-08-03
category: conventions
module: EcoServerMod
problem_type: convention
component: development_workflow
severity: high
applies_when:
  - "Authoring a file by copying a sibling in the same project rather than a vendor template"
  - "Fixing a file that has recently been used as the basis for another"
  - "Reviewing an untracked or long-lived working-tree file before its first commit"
  - "Searching history for when a value changed and finding nothing"
tags: [copy-paste, divergence, code-review, git, pickaxe, eco-modding, untracked]
related_components: [EcoServerMod/AdvancedElectronics]
---

# A fix does not reach the copies already taken from the file

## Context

`HarvestDrone.cs` was written by copying `SurveyDrone.cs`, the mod's existing drone, and adapting it.
That is the right way to author a sibling — the two share a component contract, a recipe shape, and a
set of non-obvious constraints that are expensive to rediscover.

At the time of the copy, `SurveyDrone.cs` declared its fuel tag as `"Liquid Fuel"`, with a comment
explaining that the mod's own Battery was deferred and no item carried an `"Electric Fuel"` tag yet.
Every word of that was true when written.

The Battery then shipped as a craftable item, and the commit *"feat(drone): burn Electric Fuel instead
of liquid fuel"* corrected `SurveyDrone.cs` — changing the tag and rewriting the comment that
justified the old one.

The copy did not receive that fix. `HarvestDrone.cs` was sitting untracked in the working tree,
holding the pre-fix block verbatim. (That it was untracked at the time is necessarily a self-report —
an untracked file leaves no trace to recover. What *is* checkable is the block itself: it is
byte-identical to the origin's state immediately before the fixing commit, which is the only way it
could have got there.)

That the stale value came wrapped in a confident, once-true rationale is the subject of
`docs/solutions/conventions/a-defensive-rule-outlives-the-danger-it-answered.md`, which covers why a
well-argued comment defends an expired premise and how to hunt the premise rather than the value. This
doc is about the other half: **why the fix never arrived at this file in the first place**, and why
none of the usual tools said so.

## Guidance

**Fixing a file does not fix what was copied out of it, and nothing announces that copies exist.**
Version control tracks the history of a path, not the lineage between paths. There is no query that
answers "what else was derived from this?" — the relationship existed only in the author's head at the
moment of the copy, and it leaves no artifact. The comment-rot problem at least confines itself to one
file over time; this one forks. Every copy taken before a fix is an independent chance to ship the
pre-fix behaviour, and the copies do not know they are copies.

The practical consequence: **the moment to think about copies is when you fix the original**, not when
you review the copy. By review time the copy looks like ordinary new code, because it is.

**An untracked file is invisible to every tool that would have shown the divergence.** `git grep`
skips it. `git diff` skips it. Every review of committed work skips it. A copy that has not been
committed is in the worst possible state: real enough to build and ship from, invisible to everything
that inspects the repository. Time spent uncommitted is time the file cannot be reviewed by any means
except someone opening it — and it held a bug that had already been fixed one directory over.

This is a specific argument for committing early rather than a general one. An untracked file is not
merely unsaved; it is *exempt from the safety net*, and the exemption is silent.

**Diff a copy against its origin's current state before committing it.** This is the one mechanical
check that catches the whole class, and it costs one command. Not a diff against the origin as it was
when copied — against the origin *now*, so anything the origin learned in between shows up as a
disagreement to adjudicate. Most of the output will be the intentional adaptations; the interesting
lines are the ones you cannot explain.

**`git log -S` will not find a change that moves a string rather than adding one.** `-S` is a pickaxe:
it reports commits that change the *number of occurrences* of a string. The fixing commit deleted
`"Electric Fuel"` from a comment and added it to a code line — net zero, therefore invisible, even
though its subject line announces the change. `-G` matches added or removed lines regardless of count,
and finds it. When a history search for "when did this value change" comes back empty or implausible,
suspect the search before concluding the change never happened.

## Why This Matters

Nothing in the normal safety net sees this. The build is green — both tag strings are valid. The tests
pass. The name-match gate passes, because naming is untouched. No log line, no exception.

Crucially, the stale value **still works**. Vanilla's Biodiesel and Gasoline both carry the
`"Liquid Fuel"` tag, so a drone left on it is perfectly fuelable — it simply runs on a mid-game
commodity from another tech branch instead of on the mod's own Battery. The two drones would have had
different fuel economies, indefinitely, with nothing to surface it. A stale value that breaks
something gets found; a stale value that merely diverges gets shipped.

It is worth being precise about the asymmetry, because it is easy to state backwards: the *original*
choice was made because declaring `"Electric Fuel"` when nothing carried that tag would have left the
drone unfuelable. That hazard is real, and it is what the old comment correctly described. It does not
run in reverse — the leftover `"Liquid Fuel"` had no such consequence, which is exactly why it could
sit there.

This compounds differently from ordinary copy-paste residue. Residue from a vendor template is wrong
from the first keystroke and can be swept for mechanically — grep the derived file for the template's
subject noun. This class is not detectable that way, because the copy was **correct when taken**. It
became wrong later, without being touched, when its origin moved. No comparison of the copy against
its origin *at the time of copying* would reveal anything; only a comparison against the origin's
current state.

The failure also scales with how well the copy was made. A faithful copy inherits the origin's bugs
faithfully, and a faithful copy is exactly what a careful author produces.

## When to Apply

- Immediately after fixing a file — ask whether anything was recently derived from it, and check those
  files before closing the change. This is the highest-yield moment and the easiest to skip, because
  the fix feels finished.
- Before committing any file authored by copying a sibling: diff it against that sibling's current
  state.
- Before committing anything that has been sitting untracked for more than a session.
- When a history search for a value's change comes back empty or implausible — retry with `-G` before
  concluding the change never happened.

## Examples

The block the copy carried, byte-identical to the origin's state immediately before the fix:

```csharp
// Liquid fuel (biodiesel, gasoline) -- the vanilla tag the AutoGen vehicles use. The
// mod's own Battery would have supplied an "Electric Fuel" tag, but the battery is
// deferred; a fuel tag no item carries leaves the dock unfuelable.
private static readonly string[] fuelTagList = { "Liquid Fuel" };
```

Corrected — and the replacement comment names the event that invalidated the old one, so the next
reader can date the reasoning rather than trusting it:

```csharp
// The mod's own Battery carries this tag. Matches SurveyDroneItem, which switched off
// "Liquid Fuel" once the Battery shipped as a craftable inventory item -- the comment
// here still claimed the battery was deferred, which stopped being true then.
private static readonly string[] fuelTagList = { "Electric Fuel" };
```

The one command that catches the whole class, run before committing a copied file:

```bash
diff <(git show HEAD:EcoServerMod/AdvancedElectronics/SurveyDrone.cs) \
     EcoServerMod/AdvancedElectronics/HarvestDrone.cs
```

The pickaxe blind spot, reproduced on this repository. `-S` omits the very commit whose subject line
announces the change, because that commit moved the string between a comment and a code line.

The hashes below are transcript, not citation — they were branch-local when captured and a squash
merge will rewrite them. Read the shape of the two outputs, not the identifiers; the point survives
any renumbering.

```console
$ git log --format="%h %s" -S '"Electric Fuel"' -- EcoServerMod/AdvancedElectronics/SurveyDrone.cs
842a44a feat(drone): make the drone item a repairable module
a7a6ee4 feat(drone): strip the drone world object to a mover
f1303e8 fix(server): make the new electronics content compile

$ git log --format="%h %s" -G '"Electric Fuel"' -- EcoServerMod/AdvancedElectronics/SurveyDrone.cs
7c3ff83 feat(drone): burn Electric Fuel instead of liquid fuel      <-- the actual fix
842a44a feat(drone): make the drone item a repairable module
a7a6ee4 feat(drone): strip the drone world object to a mover
0bb3095 feat(server): add the upgrade module, move dock and drone onto the mod's table
f1303e8 fix(server): make the new electronics content compile
```

## Related

- `docs/solutions/conventions/a-defensive-rule-outlives-the-danger-it-answered.md` — the closest
  relative, and the one to read alongside this. It covers the *comment* half: why a once-true
  rationale survives review and how to hunt an expired premise. The delta here is lineage and
  tooling — the fix landed on the origin after a copy had been taken, and the copy was untracked, so
  no repository-inspecting tool could show the divergence.
- `docs/solutions/conventions/auditing-content-derived-from-autogen-templates.md` — the adjacent
  failure with the opposite time signature: residue that was wrong from the moment of the copy and can
  be swept for mechanically. This doc covers the copy that was right when taken and rotted in place.
- `docs/solutions/workflow-issues/a-gate-that-discovers-nothing-passes-everything.md` — the same
  "the search was the problem, not the corpus" lesson, applied to a validation gate rather than to a
  history search.
- `docs/solutions/conventions/an-inventory-restriction-governs-one-verb.md` — the companion fact from
  the same fuel migration: what happens to fuel already sitting in a dock when the accepted class
  changes.
