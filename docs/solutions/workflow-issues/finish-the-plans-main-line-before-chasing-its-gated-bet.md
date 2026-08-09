---
title: "Finish the plan's main line before chasing its gated bet"
date: 2026-07-28
last_updated: 2026-08-09
category: workflow-issues
module: EcoServerMod
problem_type: workflow_issue
component: tooling
severity: high
applies_when:
  - "A plan or ideation doc splits work into low-risk items plus one high-value item gated on an unknown"
  - "An investigation has produced several consecutive results that are all the same null result"
  - "A session resumes after compaction and the original objective is no longer in anyone's working memory"
  - "Deciding whether to spend another expensive live-test cycle on the same open question"
tags: [eco-modding, workflow, planning, live-testing, scope-discipline, stop-conditions]
related_components: [EcoServerMod/AdvancedElectronics]
---

# Finish the plan's main line before chasing its gated bet

## Context

An ideation doc ranked the client's UI template vocabulary into two tiers and named the next step
precisely: **one batched probe covering Tier A (six low-risk candidates) plus a single Tier B list
attempt, then brainstorm on whatever survived.** Tier B carried an explicit warning in the same
document — *"a hypothesis, not a plan"* — because it depended on an unresolved question about how
lists bind.

What actually happened inverted it. The first probe covered part of Tier A, hit the Tier B question,
and then **seven consecutive deployed builds went into Tier B alone**. Each one changed a single
variable and produced the same result: an empty container, no error. Tier A items 1, 2, 3 and 6 were
never resolved, so the brainstorm the whole exercise existed to feed had nothing to read.

The plan was correct, was already written down, and was not consulted again until the user asked
what the objective had been.

## Guidance

**Re-read the authorizing document when an investigation stalls.** The plan named the deliverable
(a screenshot set for a design conversation) and the probe was only a prerequisite. Nothing in the
seven builds was wrong in isolation — each was a reasonable next hypothesis — but none of them moved
the deliverable, and the document that said so was one file away the entire time.

**Treat a gated item as gated.** When a plan flags an item as depending on an unknown, that flag is a
budget, not a caveat. One attempt was authorized. Seven were not.

**Set the stop condition before the first attempt, not after the sixth.** "I will spend N cycles on
this, then park it and finish the main line" is a decision that is cheap up front and nearly
impossible mid-investigation, because each new hypothesis feels like the one that will work.

**Recognise repeated null results as a signal about the model, not the variable.** Five consecutive
blanks meant the mental model of the subsystem was wrong, not that the fifth variable was wrong.
The correct response is to stop varying inputs and go read the mechanism — which, when finally done,
produced more in twenty minutes than the preceding builds had in two hours.

**Do not let a cost/benefit argument arrive only at the moment of failure.** Abandoning the gated
item was the right call, but it was reached after running out of ideas rather than by design. Stating
the same argument *before* attempt two would have been identical in content and honest in timing.

**After a context reset, restate the objective before continuing.** Plan docs exist precisely because
sessions end and memory does not survive them. Resuming into the middle of an investigation without
re-reading the plan is how a prerequisite quietly becomes the whole project.

## Why This Matters

The cost is not the wasted builds. It is that the deliverable **did not exist** at the end of a long
session — the design conversation still could not happen, and the person paying for each restart was
watching a null result repeat with no visible progress. Their assessment was blunt and correct:

> "I already forgot what we are trying to achieve"

There is a second, subtler cost. Each null result is only meaningful if the build under test actually
reached the server; one of the seven turned out to be testing a stale deploy. A long run of
same-shaped negatives is exactly the condition under which a false negative is least likely to be
noticed, because it looks like all the others.

The failure mode is also self-reinforcing. A gated bet is gated because it is the high-value item, so
each failure raises the apparent stakes and makes stopping feel like giving up rather than
sequencing. That is why the stop condition has to be set while the item is still cheap.

## When to Apply

- Before starting any item a plan marks as depending on an unresolved question — name the number of
  attempts it gets.
- When two or more consecutive live tests return the same null result. Stop, and go read the
  mechanism instead of choosing another variable.
- When resuming after compaction or a session break: re-read the plan before writing code.
- When about to argue that a stalled line of work "was not worth it anyway" — check whether that
  argument would have been equally true before the attempts, and if so, that it was avoidance rather
  than analysis.

## Examples

The plan, which was explicit and was not followed:

> "One **batched probe** ... a single throwaway component exposing several Tier A templates at once
> plus one Tier B list attempt, deployed in one restart and screenshotted. ... Then brainstorm on
> whichever of Tier B survives."
> — `docs/ideation/2026-07-27-mod-ui-vocabulary.md`

What returning to it produced, in one build: nineteen templates in a single tab, screenshotted, and a
layout answer the design conversation had been waiting on — most templates render as two-column
rows, not the full-width stack the design doc had assumed.

The user's own framing, given after the fourth null result and worth adopting as the default:

> "if we are going to guess, then it's much more productive to make 10 new tabs at once and see which
> one works. it's more reasonable to switch them off one by one to find what's crashing than play
> whack a mole trying one thing at a time."

## Related

- `docs/solutions/workflow-issues/eco-mod-batched-live-testing.md` — the mechanics of batching a
  restart. This doc is the layer above: batching does not help if the batch is aimed at the wrong
  question.
- `docs/solutions/workflow-issues/verify-the-deploy-landed-before-asking-for-a-restart.md` — why one
  of the null results in this run was not a real result.
- `docs/ideation/2026-07-27-mod-ui-vocabulary.md` — the plan in question, including the Tier A/Tier B
  split and the gating warning.
- `docs/solutions/workflow-issues/a-decision-about-state-you-own-is-not-the-users-to-make.md` — the
  same carried-state failure in a status summary rather than an investigation: an item survives turns
  because nobody re-reads it, not because it is still open.
