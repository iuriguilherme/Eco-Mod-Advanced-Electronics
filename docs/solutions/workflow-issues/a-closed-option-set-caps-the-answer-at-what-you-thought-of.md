---
title: "A closed option set caps the answer at what you already thought of"
date: 2026-08-01
last_updated: 2026-08-10
category: workflow-issues
module: EcoServerMod
problem_type: workflow_issue
component: development_workflow
severity: medium
applies_when:
  - "Asking the maintainer to choose a mechanism, not just a preference"
  - "Building a multiple-choice question whose options you had to invent"
  - "The maintainer knows the game or the domain better than the codebase shows"
  - "Every offered option would have produced a worse result than what came back"
tags: [collaboration, question-design, domain-knowledge, decision-loops, eco-modding, methodology]
related_components: [EcoServerMod/AdvancedElectronics]
---

# A closed option set caps the answer at what you already thought of

## Context

A long planning session ran most of its decisions through multiple-choice questions — a sound default,
since a well-built option set scaffolds an answer instead of demanding an essay. Five times, the
maintainer ignored every option and typed something better. Not a preference the menu had missed: a
*mechanism* the menu could not have contained, because it came from knowledge of the game that the
codebase does not record.

Each of those five answers changed the design. Two of them eliminated entire categories of risk that
the offered options had merely tried to manage. One of them dissolved a race condition rather than
ordering it. The pattern is not that the questions were badly built — most were reasonable. It is
that a closed option set has a ceiling, and the ceiling is the asker's own model.

## Guidance

**Check that the decision is theirs before designing the menu.** If you would carry out either option
yourself and the maintainer would notice no difference in the repository, in something they can see
running, or in what they are committed to, the question is yours to answer and no menu is the right
one — see `a-decision-about-state-you-own-is-not-the-users-to-make.md`. Check too that the costs you
attach to the options are ones you verified rather than ones you assumed — see
`a-remembered-capability-and-a-cited-file-are-claims.md`. Everything below assumes both gates have
been passed.

**Distinguish choosing from proposing.** Two very different questions wear the same interface.

*Choosing* is picking among alternatives that exist independently of you: apply this fix or skip it,
defer to planning or decide now, which of these three files is the right home. You did not invent the
options — you observed them — so an enumeration is complete and the menu is genuinely helpful.

*Proposing* is offering mechanisms you thought up: how should a stranded drone behave, where should
wear live, how should removal be gated. Here the option set is a sample of your imagination, and
listing three makes it look like a survey of the space when it is a survey of your last ten minutes.

**Apply the invention test before building the menu.** Ask where the options came from. If you
observed them — in the code, in the domain, in the user's own words — enumerate freely. *Observed*
means you opened it, this session. A remembered path and a capability inferred from a single absent
grep hit both feel observed and are neither, and a plausible naming convention is the most
convincing form the illusion takes. If you
*invented* them, say so in the question, or ask openly, or offer the options while explicitly inviting
a fourth. The tell is straightforward: writing the option list felt like design work rather than
description.

**Weight it by who knows what.** The asymmetry that matters is not seniority, it is access. On this
project the maintainer plays the game, has watched vehicles wear and modules attach, and knows what
is landing in the next release. None of that is readable from the repository. When a question lands
in territory the other party can observe and you cannot, your options are downstream of a model they
can correct — so make correcting it the easy path, not the escape hatch.

**Read a rejected menu as information about the question, not the answer.** One user answer outside
the options is ordinary. A second is a signal that the questions are being framed inside the wrong
model. Notice it at two, not at five.

**Do not overcorrect into open-ended everything.** Options genuinely reduce effort and prevent
vagueness, and most of this session's questions worked. The fix is narrow: recognise the proposing
case and keep it open.

## Why This Matters

The failure is invisible from the asker's side, which is what makes it worth writing down. A menu
answered from within its options produces a decision that feels collaborative and *is* bounded — you
never learn what the fourth option would have been, because nothing surfaces it. The times it went
well here were the times the maintainer pushed past the interface, and that took deliberate effort on
their part every time.

It also compounds in the wrong direction. Each accepted option becomes a premise for the next
question, so a mechanism chosen from an impoverished set narrows the set the following question is
drawn from. Two of this session's better answers arrived late and invalidated work built on earlier,
narrower ones — not because the earlier decisions were wrong to make, but because they had been made
from a smaller space than existed.

The asymmetry is the argument: an open question costs a sentence more to answer, and a closed one
costs whatever the unlisted option would have been worth.

## When to Apply

- Whenever the options in a draft question are mechanisms you designed rather than alternatives you
  found.
- When the question is about behaviour in a running system the other party can observe and you
  cannot.
- After a second answer arrives outside the options — treat it as a signal about framing.
- When the honest recommendation is "I don't know enough here to enumerate" — say that instead of
  producing three plausible-looking options to fill the slots.

## Examples

Five answers from one session, each outside every option offered:

```text
asked   -> route a recall that cannot path home into the existing retry?
answered-> "it must progressively relax the constraints like maximum height it can
            jump, it needs to start hovering above things or even clipping if
            necessary. in the worst case scenario it just teleportss"
effect  -> removed stranding as a failure mode entirely, rather than retrying into it

asked   -> wear as an installed component, as item durability, or keep it static?
answered-> "Both, like the steam tractor: the steam tractor has it's parts which
            degrades over time like any vehicle. The modules have their own item
            wear (RepairableItem)"
effect  -> eliminated the only unproven code path in the design

asked   -> pin the despawn/uninstall order to fix the race?
answered-> "drones are supposed to go back to the dock before they can be picked up"
effect  -> made the race impossible instead of ordering it

asked   -> promote the cargo component's type to a stated constraint?
answered-> "the storage for items is different from the fuel storage and the drone
            storage we are using... need to research stockpiles and vehicles"
effect  -> corrected a prescription that had generalised from one example

asked   -> defer the fuel rate, since I cannot supply a number?
answered-> "the only thing to decide is how fast fuel burns which can be reasoned
            from looking at a truck object and at a industrial generator"
effect  -> turned an open question into a bounded comparison
```

The shape that would have surfaced these earlier — options offered as a starting point rather than a
ballot:

```text
WRONG (closed, mechanisms invented by the asker):
  How should a recalled drone that cannot path home behave?
    A. Retry under the existing unreachable handling  (recommended)
    B. Defer to implementation
    C. Skip -- existing behaviour is fine

RIGHT (open, because the answer depends on how the game actually plays):
  A recalled drone that cannot path home -- what should it do? I can wire it into
  the existing unreachable retry, but you have watched these move and I have not,
  so if there is a behaviour you already have in mind, that is the one to build.
```

## Related

- `docs/solutions/workflow-issues/a-user-report-carries-evidence-and-a-request.md` — the receiving
  half of the same collaboration. That one is about not discarding what a user's sentence contains;
  this one is about not constraining what their sentence is allowed to contain.
- `docs/solutions/workflow-issues/the-compile-target-decides-what-exists.md` — from the same session,
  and the reason two of the answers above arrived late: the version premise was wrong, so the
  questions built on it were drawn from the wrong space.
- `docs/solutions/workflow-issues/a-decision-about-state-you-own-is-not-the-users-to-make.md` — the
  prior question. That one asks whether the decision is the maintainer's at all; this one assumes it
  is and asks whether the options are wide enough. An answer from outside the menu can mean either,
  so check ownership before widening.
- `docs/solutions/workflow-issues/a-remembered-capability-and-a-cited-file-are-claims.md` — the other
  precondition. That one checks the premise the options rest on; this one checks whether the options
  are wide enough. A menu can clear both gates and still misfire, because the maintainer chose
  correctly from prices that were never verified.
