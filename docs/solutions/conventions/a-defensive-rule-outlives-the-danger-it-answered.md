---
title: "A defensive rule outlives the danger it answered, and its comment argues for keeping it"
date: 2026-08-01
category: conventions
module: EcoServerMod
problem_type: convention
component: development_workflow
severity: medium
applies_when:
  - "A change removes a failure mode that other code was written to tolerate"
  - "A comment explains why something is permitted or forbidden, citing a risk"
  - "Adding a constraint that an existing comment argues against"
  - "Reviewing a rule whose justification you cannot currently reproduce"
tags: [technical-debt, comments, invariants, design-decisions, eco-modding, planning]
related_components: [EcoServerMod/AdvancedElectronics]
---

# A defensive rule outlives the danger it answered, and its comment argues for keeping it

## Context

The Drone Dock lets a player pull the drone item out at any moment, including while the drone is
away working. That is deliberate, and the reason is recorded at `EcoServerMod/AdvancedElectronics/DroneDock.cs:507`:

> Removing the item is always allowed (never blocked): a drone that is out roaming can glitch,
> strand, or fail to path home, so removal is treated as "reset" rather than "recall".

Good reasoning for the world it was written in. If a drone can get stuck somewhere unreachable, the
player needs an escape hatch, and blocking removal would turn a stuck drone into a permanently
ruined dock.

A later planning session added a requirement that a return leg can never fail — the drone
progressively relaxes its movement constraints and teleports home as a last resort. That change
deletes the premise the escape hatch was built on. In the same session, and independently, a review
finding about a race condition led to a new requirement: a drone cannot be removed unless it is
fully docked.

So the plan now contains a rule and, a few files away, a confident comment explaining why that rule
must not exist. Nothing connected them. The contradiction surfaced only because the review happened
to touch the removal path for an unrelated reason.

## Guidance

**Read a defensive rule as the conditional it actually is.** "Removal is always allowed" is not a
requirement — it is the consequent of *"because a drone can strand, removal must always be allowed."*
The antecedent is the load-bearing half, and it is the half that can quietly become false. When it
does, the rule keeps standing on nothing.

**When you eliminate a failure mode, go looking for its mitigations.** The failure mode's own
vocabulary is the search string — here, `strand`, `stuck`, `unreachable`, `fail to path`. Anything
that turns up is a candidate for removal, relaxation, or at minimum a note. Eliminating a failure
mode feels like pure subtraction, which is exactly why nobody goes looking for what it leaves behind.

**Delete the expired justification; do not merely change the behaviour.** This is the part that bites
later. Code that contradicts a comment gets reconciled by the next reader, and a well-argued comment
usually wins — it explains a danger, and the reader has no evidence the danger is gone. A comment
that says *"this can strand, so we allow removal"* will get the constraint reverted by someone acting
carefully. Replace it with what changed and why, so the next reader inherits the new premise instead
of the old one.

**Treat "I cannot currently reproduce this rule's justification" as a finding.** Not proof the rule
is wrong — the danger may be real and merely rare — but a prompt to find out which. A rule whose
reason no one can restate is either load-bearing and undocumented, or dead weight, and those want
opposite treatment.

**Suspect the shape, not the topic.** Grep-able forms: *"always allowed"*, *"never blocked"*,
*"deliberately not"*, *"we don't X because Y can happen"*, *"safe to skip since"*. Each encodes a
world-state that was true once.

## Why This Matters

Mitigations are invisible to the change that obsoletes them. Removing a failure mode is a local edit
with a clear win, and nothing about it surfaces the code elsewhere that exists only because that
failure mode used to be possible. There is no compiler error, no failing test — the mitigation still
works, it just no longer buys anything, and it may now cost something.

The comment makes it worse rather than better, which is the counterintuitive part. Documentation is
supposed to protect a decision from erosion, and here that is precisely the harm: the better the
rationale reads, the more effectively it defends a constraint whose reason has expired. Undocumented
behaviour gets questioned; well-documented behaviour gets preserved.

In this case the cost was near zero, because the contradiction was caught during planning rather than
after implementation. Had it not been, the likely path is an implementer reading the comment, taking
it at face value, and either dropping the new requirement or implementing both and shipping a dock
that refuses removals while telling itself it never refuses removals.

## When to Apply

- Immediately after any change that removes a failure mode, a race, an ordering hazard, or an error
  path — search for the mitigations that existed for it before moving on.
- When writing a requirement that contradicts an existing comment. The contradiction is the signal;
  resolve it in the plan rather than leaving both for the implementer.
- When reviewing a defensive rule and finding you cannot restate its justification from current
  behaviour.
- When a comment explains a permission or prohibition by naming a risk — that comment has an
  expiry condition, whether or not it says so.

## Examples

The shape to look for, and what it decomposes into:

```text
"Removing the item is always allowed (never blocked): a drone that is out
 roaming can glitch, strand, or fail to path home, so removal is treated
 as 'reset' rather than 'recall'."

  antecedent  -> a drone can fail to get home          <- can expire
  consequent  -> removal must never be blocked         <- survives, groundless
  risk        -> a reader restores the consequent from the comment alone
```

Hunting the mitigations after removing the failure mode — the failure's own words are the query:

```bash
# after adding "a return leg never fails", find what existed because it could
grep -rn "strand\|stuck\|unreachable\|fail to path\|glitch" EcoServerMod/ --include=*.cs
```

What replaces the comment. Not silence, and not just the new rule — the new rule plus the reason the
old one lapsed, so the next reader cannot re-derive the original:

```text
Removal requires the drone to be docked. This reverses an earlier rule that
removal was always allowed, which existed because a roaming drone could
strand; the return-leg escalation (relax climb height, then hover, then clip,
then teleport) means a return can no longer fail, so the escape hatch is
obsolete rather than merely inconvenient.
```

## Related

- `docs/solutions/conventions/requirecomponent-binds-at-creation-not-retroactively.md` — another case
  where what the source says and what reality holds drift apart without any error; there across
  object lifetimes, here across time.
- `docs/solutions/conventions/auditing-content-derived-from-autogen-templates.md` — residue left by a
  change that looked complete. Same family: the edit succeeds, and what it stranded stays behind.
- `docs/solutions/workflow-issues/a-user-report-carries-evidence-and-a-request.md` — the removal
  constraint in this entry came from the maintainer's own domain knowledge during a review, not from
  the finding that prompted the question.
