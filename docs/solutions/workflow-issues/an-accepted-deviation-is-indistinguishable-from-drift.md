---
title: "An accepted deviation is indistinguishable from drift unless the tree records it"
date: 2026-09-21
category: workflow-issues
module: docs
problem_type: workflow_issue
component: development_workflow
severity: medium
applies_when:
  - "Accepting a deviation from a rule this project has written down"
  - "Writing a rule that existing code, assets or docs do not satisfy"
  - "Leaving work unfinished where the tree will hold two descriptions of it"
  - "An audit, refresh or review has flagged something you already knew about"
  - "A document is about to assert that a claim made elsewhere has gone stale"
tags: [audits, refresh, knowledge-store, exceptions, grandfathering, unfinished-work, false-positives]
related_components: [docs, Assets/Art/AdvancedElectronics]
---

# An accepted deviation is indistinguishable from drift unless the tree records it

**Read this one when nothing is wrong.** Its sibling
`docs/solutions/workflow-issues/a-fixed-defect-in-the-present-tense-passes-every-check.md` covers
prose that has gone stale because the tree moved on, and prescribes past-tensing the account and
naming the commit that closed it. The two look identical from outside — a document and a tree that
disagree — and they take opposite repairs. The question that separates them: **can the owner still
state why the deviation is acceptable?** If yes, this document applies and the repair is to write
that reason down. If no, the sibling applies. Applying the sibling's fix here writes a false
history, because an accepted deviation is live and has no closing commit.

## Context

A refresh pass over the 75 learnings the store then held produced three findings that looked like
defects and were not. In each case the reason the deviation was acceptable existed only in the
project owner's head, and the tree offered the auditor nothing to read.

1. **Grandfathered placeholders.** Two flat-colour rows in the icon table at
   `Assets/Art/AdvancedElectronics/Editor/AdvancedElectronicsBuildTools.cs` stood against a "never
   ship a flat-colour placeholder" rule. The rows predate that rule, written on 2026-08-22, and the
   objects they name are hand-built primitives with no real art, so the placeholder icon matches the
   placeholder model. The rule binds new work; authoring a placeholder for something that does not
   have one is the regression.
2. **Unfinished work reading as a contradiction.** The `AttachAnimatorStates` docstring in the same
   file and `docs/solutions/architecture-patterns/client-animation-is-driven-by-name-not-by-mod-code.md`
   describe different mechanisms, and the prefabs follow neither: three of the five declare
   animated-state names whose enabled/disabled events are bound to nothing, and the other two
   declare no states at all. Animation is unfinished and its contract was never settled.
3. **A claim called stale that had never been false.**
   `docs/solutions/conventions/a-licence-notice-travels-with-the-asset-not-the-repo.md` asserted that
   `README.md:111`'s "This repository contains only our own work" had expired the moment contributed
   art arrived, and counted the commits it stayed "wrong". It had not expired. The contributor asked
   for his work to be distributed with the mod, so it is the project's own work under a second
   licence, and what a contribution changes is the licence, not the authorship claim.

Two of those three the refresh did not merely flag. It wrote a removal prescription into the icons
doc, and it wrote the expiry claim into the licensing doc. Both then read as findings to the next
reader.

**A fourth instance is still live and still unrecorded (session history).** On 2026-08-08 an agent
read `m_HasExitTime: 1` on every transition in the HRVSTR animator controller as a defect. The
owner explained that animations must play to completion and that re-entry is the transition graph's
job. The agent conceded and said it would not raise it again. Nothing was written anywhere. The
controller still carries 30 transitions with `m_HasExitTime: 1` and none with `0`, and the design
is still recorded in no artifact, so the next pass over that file starts the same conversation.

## Guidance

**Write the acceptance where the auditor arrives.** That is at the deviation, in the code or asset,
not only in the document that states the rule. An auditor reading the icon table reaches
`AdvancedElectronicsBuildTools.cs:95` long before it reaches a learning in another directory. The
rule's own document gets the note too, because that is where someone checks whether the rule is
absolute — the two notes are not redundant, they catch different readers.

**A rule written after the thing it would forbid must say whether it binds retroactively.** Silence
reads as yes. "Never ship a flat-colour placeholder" said nothing about the rows that already
existed, so every subsequent reading made them outstanding work.

**Give the acceptance a reversal condition, or it becomes dead weight.** This is the lesson of
`docs/solutions/conventions/a-defensive-rule-outlives-the-danger-it-answered.md` pointed the other
way: an unconditional exemption outlives what justified it exactly as an unconditional rule does.
The project already has the mechanised form of this — `scripts/validate-name-match.sh:130` derives
its exemption list from the source rather than hardcoding it, "so the exemption cannot outlive the"
thing that grants it. The prose form is the same move: name the condition that ends the exception.
The two icon rows go when the models do; the withheld drone recipes carried a one-line-to-restore
marker and their restoration said so in place.

**An accepted deviation and an unsettled question take different notes.** Do not write one pattern
for both. Instances 1 and 3 above *add* a claim — this is grandfathered, this contributor's work is
the project's own. Instance 2 *removes* one: neither the docstring nor the doc is known to be
correct, and the belief that the doc leads is recorded at
`client-animation-is-driven-by-name-not-by-mod-code.md:45` as a working assumption rather than a
finding. Writing "accepted" over a genuinely unsettled question does the same damage in the other
direction, because it retires a question nobody answered.

**Do not conclude that a claim expired without checking the premise that made it true.** The
licensing doc's expiry claim followed from an unexamined premise — that a contributor's work is not
the project's own work — which the owner had never held. Asking cost one question; not asking put a
retracted claim in the store with a commit count attached to it.

**The store already has this doctrine for machine flags, and only for machine flags.**
`docs/solutions/workflow-issues/a-crashed-check-and-a-flagged-check-are-opposite-problems.md:125`
prescribes exactly this for a validator flag: *"Confirm intentional when the prose already marks it
… Annotate if it does not."* Its adjudication table names the recurring exception classes so a
later run resolves them in seconds. Nothing equivalent exists for a finding raised by a human or an
agent reading code, which is where all four instances above came from.

## Why This Matters

**The cost is asymmetric, and the expensive branch is the one that looks like diligence.** A flagged
deviation costs one pass, and it recurs — the same doc's line 186 already makes this argument for
citations: adjudicating a legitimate flag costs a minute, while "fixing" something deliberately
historical destroys evidence silently. A *repaired* deviation costs the repair, plus every document
that now carries the wrong prescription, plus the reader who trusts it.

**An audit's false positive enters the store with the same authority as a finding.** That is the
propagation mechanism in
`docs/solutions/workflow-issues/a-knowledge-store-corroborates-its-own-errors.md`: once one pass has
written the removal prescription into a doc, the next pass meets it as corroboration, and the copies
outlive the correction. Instance 1 had already reached that stage.

**Better scoping does not fix this case.** In
`docs/solutions/workflow-issues/a-review-scoped-to-the-document-cannot-see-what-the-code-settled.md`
the settling context existed in the code and the reviewers were simply never pointed at it, so the
repair was to scope them. Here the context is in no artifact at all. Writing it down is the only
available fix.

**Recording the acceptance does not stop the flag; it makes resolving it cheap.** The adjudication
table has never reduced the number of flags. It reduces each one to a lookup. The same is true of an
acceptance note: the next auditor still sees the deviation, and now reads one sentence instead of
re-running the investigation and reaching the wrong conclusion.

## When to Apply

- When you accept a deviation from a rule this project has written down — write it at the deviation
  and at the rule, with its reversal condition, before moving on.
- When you write a rule that existing code, assets or docs do not satisfy — say whether it is
  retroactive, in the rule itself.
- When you leave work unfinished and the tree will carry two descriptions of it — record that
  neither is known to be correct, and label any preference as an assumption.
- When an audit, refresh or review flags something you already knew about — the flag is the signal
  that the acceptance was never written down. Answer it in the tree, not only in the conversation.
- When a document is about to say another claim has gone stale — check the premise first.

## Examples

**Before.** The icon table carried a comment explaining which rows had been *removed* and why, and
nothing about the two that stayed:

```csharp
// RETIRED, deliberately: AdvancedElectronicsSkill, its skill book, its skill scroll and
// AdvancedElectronicsUpgradeItem used to have rows here and no longer do. [...]
private static readonly (string TypeName, Color Fill)[] ItemIcons =
{
    ("DroneDockItem",  new Color(0.40f, 0.45f, 0.50f, 1f)), // steel grey -- shipped without an icon; see below
```

Meanwhile `mod-icons-reference-vanilla-art-by-name.md` told the reader that removing those two rows
"is the remaining half of the assembly's fix and the whole of the dock's" — an audit's inference,
written as a task.

**After** (`49c9a68`). The acceptance sits in both places, with its reversal condition:

```csharp
// GRANDFATHERED, also deliberately: DroneDockItem and AdvancedElectronicsAssemblyItem keep
// their rows even though the assembly declares [HasIcon("Crafting Table")]. Both objects are
// hand-built primitives with no real art, so a render of them is a flat shape either way.
// The never-ship-a-flat-colour rule binds NEW work -- authoring a placeholder for something
// that does not have one. These two predate it and are not a defect to clean up. They go when
// the models do.
```

and the doc's prescription is reversed at `mod-icons-reference-vanilla-art-by-name.md:359`, naming
the date the rule was written so the grandfathering is checkable rather than asserted.

**The unsettled variant** (`cc42283`). No acceptance is claimed, because none is available:

> The working assumption is that this document leads and the prefabs are the side that is wrong.
> **That assumption has not been established.**

with `AdvancedElectronicsBuildTools.cs:945` marked `UNVERIFIED` and both pointed at
`docs/plans/2026-09-21-1856-feat-drone-animation-plan.md`, a plan-shaped file whose entire content
is that a plan has to be made.

**The premise variant** (`9cb8352`). The licensing doc now carries the rule that makes the README
sentence true, at `:152`, instead of the claim that it had expired.

## Related

- `docs/solutions/workflow-issues/a-fixed-defect-in-the-present-tense-passes-every-check.md` — the
  case this one is most often confused with, and the opposite repair. Its tense detector admits two
  outcomes, current or fixed; an accepted deviation is the third.
- `docs/solutions/workflow-issues/a-crashed-check-and-a-flagged-check-are-opposite-problems.md` —
  the same doctrine already written for validator flags, with the standing adjudication table and
  the cost argument. This entry is that doctrine for findings a person or an agent raises.
- `docs/solutions/conventions/a-defensive-rule-outlives-the-danger-it-answered.md` — the mirror
  image: there the justification really had lapsed and the rule was dead weight; here it was never
  written. The separating question is whether the owner can still state it, which is why that
  entry's "treat it as a finding" has to mean investigate rather than delete.
- `docs/solutions/workflow-issues/a-review-scoped-to-the-document-cannot-see-what-the-code-settled.md`
  — the same false finding where the settling context *was* in the tree, so scoping the reviewer
  fixed it.
- `docs/solutions/workflow-issues/a-knowledge-store-corroborates-its-own-errors.md` — why an
  unrecorded acceptance gets expensive rather than merely wasteful.
- `docs/solutions/workflow-issues/a-commit-message-rule-failed-five-times-and-was-withdrawn.md` —
  the store's worked example of the artifact this entry prescribes: a status block kept in the tree
  precisely because the thing it records reads as obviously wrong to anyone arriving fresh.
- `docs/solutions/architecture-patterns/mod-icons-reference-vanilla-art-by-name.md`,
  `docs/solutions/architecture-patterns/client-animation-is-driven-by-name-not-by-mod-code.md`, and
  `docs/solutions/conventions/a-licence-notice-travels-with-the-asset-not-the-repo.md` — the three
  instances, each now carrying its own note.
