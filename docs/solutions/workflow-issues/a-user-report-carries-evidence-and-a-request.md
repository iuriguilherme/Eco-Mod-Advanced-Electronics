---
title: "A user's sentence usually carries a test result and a request; taking only the request discards the result"
date: 2026-07-31
category: workflow-issues
module: EcoServerMod
problem_type: workflow_issue
component: development_workflow
severity: medium
applies_when:
  - "A maintainer describes what a control does and what they would prefer, in one sentence"
  - "About to hypothesise about behaviour the user may already have observed"
  - "A user correction contradicts a model built by reading code"
  - "Working on a system only the user can run"
tags: [collaboration, evidence, live-testing, eco-modding, methodology, restart-cost]
related_components: [EcoServerMod/AdvancedElectronics]
---

# A user's sentence usually carries a test result and a request; taking only the request discards the result

## Context

In this project the maintainer is the only instrument that can observe the running game. The agent
can build, deploy and read source; it cannot place an object, click a control, or see what the client
draws. Every behavioural fact arrives through a sentence a human types, and each of those sentences
cost a server restart to produce.

Those sentences usually carry two things at once — what was observed, and what is wanted. Parsing
only the second discards the most expensive evidence in the project.

## Guidance

**Split the sentence before acting on it.** A report like *"we were supposed to have a button to
assign the drone instead of auto assigning every time the radial changed"* contains a **preference**
(a commit button) and an **observation** (changing the radial does assign — so the write path works).
Both are payload. Acting on the preference alone left the observation unrecorded, and a later round
was spent hunting a mechanism problem in behaviour that had already been confirmed working — the
agent even reported the control as "untested" to the person who had just tested it.

**Check whether the behaviour has already been reported before hypothesising about it.** The cheapest
possible test is one already run. Before proposing why a control might not work, re-read what the
user said about it; a sentence framed as a complaint is still a result.

**When a correction contradicts a model built from source, ask for the sequence, not the cause.**
Reading code produces plausible mechanisms; the user holds the actual ordering. A proposed cause —
"picking up the dock never despawns the drone" — died immediately against *"first any survey drone in
the storage gets taken, then in the second hammer hit the drone dock is taken"*, a fact no amount of
code-reading would have produced because it is about how a tool is used, not what a method does.

**Restate the observation before acting on the request.** One line — "so the radial does assign on
change; you want a commit step instead" — costs nothing, records the evidence, and surfaces a
misparse immediately rather than a round later.

## Why This Matters

The unit of cost here is a restart, and a restart is paid by a human waiting. An observation thrown
away has to be re-acquired at that price, and it usually is not — instead the agent proposes a
hypothesis about it, the user has to correct the hypothesis, and the correction consumes another
exchange.

It also produces a specific kind of wrong statement: telling the user something is unverified when
they verified it. That erodes the thing the collaboration runs on, which is that the user's reports
land somewhere and stay landed.

The pattern generalises past this project: any system whose behaviour only one party can see makes
that party's prose the instrument, and instruments deserve to be read carefully. That is the same
discipline applied to server-state readouts in
`docs/solutions/workflow-issues/the-control-under-test-is-not-a-readout-of-it.md`, one layer out.

## When to Apply

- Any message describing what a control, object, or command did.
- Any message mixing "it does X" with "I want Y" — treat X as a result.
- Before writing "untested", "unverified", or "unknown" about anything the user has interacted with.
- When a proposed cause conflicts with a user's account of the steps.

## Examples

Two sentences from one session, with what was in them and what was taken:

```text
"we were supposed to have a button to assign the drone to an area instead of
 auto assigning everytime the radial changed"

  observation  -> changing the radial assigns; the write path works
  preference   -> a commit button instead of assign-on-change
  taken        -> the preference only
  cost         -> a round spent looking for a write-path bug that did not exist,
                  plus reporting the control as untested to the person who tested it
```

```text
"when picking up the drone dock first any survey drone in the storage gets taken,
 then in the second hammer hit the drone dock is taken"

  observation  -> removing the item fires first, so the despawn path IS reached
  taken        -> issued after the agent proposed "dock pickup never despawns"
  cost         -> one falsified hypothesis, and the real cause still unfound
```

The cheap correction, applied before acting:

```text
"So the radial does assign on change -- that's the write path confirmed.
 You'd rather it waited for a commit. Taking both."
```

## Related

- `docs/solutions/workflow-issues/the-control-under-test-is-not-a-readout-of-it.md` — the same
  discipline aimed at instruments the agent builds; this one is aimed at the instrument the agent
  is given.
- `docs/solutions/workflow-issues/eco-mod-batched-live-testing.md` — why the restart is the unit of
  cost, and therefore why a discarded observation is expensive.
- `docs/solutions/workflow-issues/tracing-beats-theorising-on-invariant-failures.md` — the
  hypothesise-instead-of-observe habit this is one instance of.
