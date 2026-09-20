---
title: "A review scoped to the document cannot see what the code already settled"
date: 2026-08-30
last_updated: 2026-09-15
category: workflow-issues
module: EcoServerMod
problem_type: workflow_issue
component: development_workflow
severity: medium
applies_when:
  - "Dispatching reviewers over a requirements or plan document for a feature that extends existing code"
  - "A finding faults the document for not specifying a placement, surface, or message -- or proposes adding one to an interface that already exists"
  - "About to ask the maintainer to choose a placement, a wording, or a reporting surface"
  - "Writing a requirement that describes UI behaviour for a component that already renders something"
  - "Authoring a document whose list of items is assembled from another document rather than from the code that declares them"
tags: [ce-doc-review, review-scoping, requirements, grounding, false-gap, methodology, authoring, eco-modding]
related_components: [EcoServerMod/AdvancedElectronics, docs/plans]
---

# A review scoped to the document cannot see what the code already settled

## Context

Four rounds of `ce-doc-review` ran over a requirements plan for a feature extending the drone mod's
existing readouts. Every reviewer was given the document, the decision primer, and the findings
schema. Several were given specific source files to check *feasibility* claims against.

None of them was told the implementation was authoritative over questions of **placement and
presentation**. So they reviewed the document against itself, and reported — correctly, on the
evidence they had — that it failed to specify things.

Four such findings reached the maintainer as questions. All four were already answered in the code:

- **Where a refusal reason goes.** A finding said the requirement demands a reason on the roster line
  while the field-order requirement never says where it sits, leaving three possible layouts. The
  mining readout already has dedicated rows for this — `FormatStopReason`, `FormatBlockedReason`, and
  a composed `FormatSkipLine` that words property, settlement-law, unreachable and obstructed
  refusals by category. Reasons were never on the area line.
- **What surface a detail view uses.** A finding said "detail" was undefined — panel, command, or
  popup — with no cap on a per-plot list. `FormatProgress` carries a comment stating that per-fact
  rows were deliberately folded into one line because they are "a debugging surface, not a player
  one", and naming `/drone state` as where "why was that plot skipped" belongs.
- **Where partial progress is reported.** A finding called a phrase on the roster line duplicative of
  the status word. Both were redundant with a third surface: the progress row already reports
  `total: N plots, worked: X, skipped: Y`.
- **Whether a tab discloses an area's purpose.** A finding wanted a kind field on every roster line.
  The answer needed both halves of the repo: a dock *can* list unrelated areas, so the tab does not
  disclose kind, and at the time kind was internal and not something the player acted on. That
  second half has since been overturned by the code rather than by the document.
  `DockReadout.cs:213` emits `[farm]` for a farm area, `DockReadout.cs:25-29` records that it rides
  the exclusive status slot because it is what an area *is*, and `CONCEPTS.md` now says the bracket
  tag is how a player knows what an area is for before assigning a drone to it. The refusal still
  stands — kind is not a field of its own — and the lesson is sharper for it: the code answered
  this question twice, and neither answer was ever in the document.

The maintainer caught all four. The reviewers could not have.

## Guidance

**Scope a document review by what each finding class needs as authority, not by what the document
contains.**

A requirements document under review has three different kinds of claim in it, and they answer to
different authorities:

| Claim | Authority | Reviewable from the document alone |
|---|---|---|
| *What we will build* — behaviour, scope, product decisions | the maintainer | yes |
| *Whether it can be built* — cost, data shape, access paths | the codebase | only with source access |
| *Where it goes and what it looks like* — surfaces, slots, wording, reporting | **the codebase** | **no** |

The third row is the trap. It reads like product design, so it is natural to review it against the
document. But for any feature extending existing code, the surfaces already exist and already have
conventions — often documented in comments that state the reasoning, which is exactly what a
reviewer needs and cannot see.

**Practical form:**

- When the feature extends an existing component, name that component's rendering or presentation
  code as authoritative in the reviewer's prompt, the same way feasibility claims already get source
  files. A reviewer told "these files decide placement" will read them.
- Before asking the maintainer where something goes, grep for whether it goes somewhere already.
  "The document does not say" and "nobody has decided" are different statements.
- Treat a finding of the form *"the document never says where/what surface/how it is reported"* as
  requiring a code check before it becomes a question. It is the signature of this failure.

**On "duplicate, so drop it".** One of the four findings identified a genuine duplication and
proposed deletion, which was accepted and was wrong. The information was real; it was in the wrong
place. When a fact appears twice, the question is which surface owns it — deleting one instance
answers a different question than the one worth asking.

## Why This Matters

The cost is not the wrong answer — the maintainer corrected every one. The cost is that **four
product decisions were put to a human who did not need to make any of them**, in a review whose
purpose was to reduce what they had to decide.

It also produces a specific kind of bad requirement. Asked "where should this go?", the natural
output is a requirement that *specifies* a placement — inventing a slot, a cap, or an ordering for a
surface that already has one. That requirement then contradicts the implementation, and the
contradiction is discovered during planning or later.

Two of the four findings resolved not by choosing but by **deleting the requirement's placement
language entirely** and pointing at the existing surface. The document got shorter. A review that
had read the code would have proposed that directly.

## When to Apply

Applies whenever a reviewed document describes behaviour for code that exists. It does not apply to
greenfield work, where the document genuinely is the only authority on placement.

**It applies at authoring time too, and there it is worse.** Everything above is written from the
review side, because that is where it was found: a reviewer given only the document treats the
document as the corpus. But the same substitution happens one step earlier, when a document's
content is assembled from another document instead of from the code, and at that point no reviewer
exists to catch it. The review-time version of this failure produces a false gap that a maintainer
can see and correct. The authoring-time version produces a **missing item**, and nobody reviewing a
document can see what is not in it. When the document being written is a list of things — assets,
requirements, surfaces, endpoints — enumerate it from the construct that declares them and treat
any summary as a cross-check rather than as the source. The companion learning
`docs/solutions/workflow-issues/an-audience-model-sets-the-sources-not-just-the-tone.md` is a worked
instance, including why the wrong reader model is what makes a summary look like an adequate
source.

The signal to check for is a finding that faults the document for *silence* rather than for being
wrong. Silence about behaviour is usually a real gap. Silence about surface, slot, wording, or
reporting is usually the document declining to restate something the code has already settled.

## Examples

A finding as filed, and what a code check turned it into:

> **Filed:** "R27 requires a refusal reason on the roster line; R29 fixes the exact field order and
> never mentions the reason text. An implementer must guess whether it consumes one of the two capped
> overlay slots, rides inside the status word, or is a new uncapped segment."

Three plausible layouts, no basis to choose, so it reached the maintainer as a question.

The check that dissolved it:

```bash
grep -n 'reason\|Reason\|skip\|Skip' EcoServerMod/AdvancedElectronics.Navigation/MiningReadout.cs
```

which returns `FormatStopReason`, `FormatBlockedReason` and `FormatSkipLine` — reasons already have
their own rows. The requirement was corrected to point at them, and the roster line kept its
budgeted length. No slot had to be found, because none was ever needed.

The same grep pattern found the diagnostic-surface answer in a doc comment two methods away.

## Related

- `docs/solutions/workflow-issues/an-audience-model-sets-the-sources-not-just-the-tone.md` — the
  author-side half of this rule. Same mechanism, one step earlier: a document treated as its own
  corpus, but at authoring time, where the failure is a silently short list rather than a false gap.
- `docs/solutions/workflow-issues/a-remembered-capability-and-a-cited-file-are-claims.md` — the
  adjacent failure: asserting a capability exists without checking. This one is the inverse, asserting
  a gap exists without checking.
- `docs/solutions/workflow-issues/a-closed-option-set-caps-the-answer-at-what-you-thought-of.md` —
  why a question built from three invented options is worse than no question; here the options were
  invented because the real answer was never looked up.
- `docs/solutions/design-patterns/vertical-stack-only-ui-design.md` — the panel constraints these
  readouts were built against, and the source of the length budget several findings reasoned about
  without reading.
- `docs/ideation/2026-08-21-shared-area-status-review-decisions.md` — the four-round decision record
  this was drawn from.
