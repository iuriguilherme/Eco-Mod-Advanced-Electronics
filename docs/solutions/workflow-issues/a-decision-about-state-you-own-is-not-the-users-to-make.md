---
title: "A decision about state you own is not the user's to make; the gap that prompted it was literacy, not tooling"
date: 2026-08-09
category: workflow-issues
module: EcoServerMod
problem_type: workflow_issue
component: development_workflow
severity: medium
applies_when:
  - "About to list a decision as blocked on the maintainer"
  - "The maintainer says they cannot find something the harness already tracks"
  - "Considering a tracked repo file that would mirror state a tool already holds"
  - "Offering a closed set of options for a question you have the authority to answer yourself"
  - "A summary item has survived several turns without the maintainer engaging it"
tags: [collaboration, decision-ownership, task-tracking, agent-state, source-of-truth, tool-literacy, methodology, eco-modding]
related_components: [EcoServerMod/AdvancedElectronics, docs]
---

# A decision about state you own is not the user's to make; the gap that prompted it was literacy, not tooling

## Context

Deep in a long session the maintainer asked a short, practical question:

> "i don't know where tasks are, which file should I look at"

The answer to the literal question was easy and correct: tasks live in the harness-managed task
list, reached through its task tools, not in any file in this repository. Nothing under `docs/` or
the repo root holds them — a search of the tracked tree for a backlog- or todo-shaped file returns
nothing, which remains true today.

The assistant did not stop there. It attached a follow-up choice to the explanation: should durable
tracking live in a backlog file committed to the repo, in GitHub issues, or in both? That question
went unanswered. Rather than reading the silence as an answer, the assistant carried it forward,
re-listing it across several subsequent turns as "an open item awaiting a user decision" — sitting in
the same list as genuinely user-owned items such as whether to push and whether to cut a new tag.

The maintainer eventually closed it, and the close was none of the three options offered:

> "the tasks are yours, I don't manage them. the backlog is not necessary because the tasks are
> yours - the problem is I didn't know how to use claude code and that is solved; let's update
> origin"

Two errors had compounded. The first was addressing the wrong party: the task list is agent-owned
state, so how to track it was never the maintainer's call, and keeping the escalation alive meant a
non-question occupied a slot in every status summary where real open questions lived. The second was
misreading the root cause. "I don't know where the tasks are" is a tool-literacy gap; the assistant
treated it as missing infrastructure and proposed to build a second home for state the harness
already held.

The resolution wrote nothing to the repository. The state stayed where it lived, the confusion was
cleared by one explanation, and the assistant closed the stale task itself — task #101, whose
underlying work had already shipped in `c6342e4` ("fix(release): name the tables the build actually
ships"). Closing it without asking is what owning the list means in practice.

The same shape had already appeared once in that session. Commit `1c5e7f6` ("docs(solutions): a
crashed check and a flagged check are opposite problems") had accidentally bundled a deletion of
`LICENSE-ART.md` into an otherwise unrelated docs commit; the licence text now lives at `LICENSE-ART`
after the later restructuring commit `632e92f`. The assistant proposed splitting that commit and
presented the split as something for the maintainer to decide. The reply was not a decision but a
standing policy: keep git history always, unless there is a severe security problem in an old commit.
Again the question had a general answer the assistant could have derived, rather than one that needed
asking. Two escalations in one session, the same root shape.

## Guidance

Before asking, identify who owns the thing the question is about, and only ask if the owner is the
user.

The test that separates the two, applicable in the moment: **if you carried out either option
yourself and never mentioned it, would the user notice a difference in the repository, in something
they can see running, or in what they are committed to?** If no, you own it — decide and move on. If
yes, they own it, and the ask is legitimate.

By that test, how the agent tracks its own work items is agent-owned; whether a file lands in the
repo, whether a branch is pushed, whether a tag is cut, whether history is rewritten are user-owned,
because each leaves a trace in something the maintainer holds. Note the split in the second
precedent: rewriting `1c5e7f6` was genuinely user-owned by this test, which is why the mistake there
was subtler — the question was well-addressed but had already been answered by a policy general
enough to derive, so asking it again spent the user's attention on something already settled.

Two corollaries follow. First, silence on an escalation is data. If a question goes unanswered while
surrounding work continues, re-examine whether it was a real question before re-listing it; an
unanswered item that keeps reappearing in summaries costs more each turn it survives. Second, when
the trigger was a question about your own state, answer the question asked. Do not bundle an
infrastructure proposal onto an explanation — that converts a request for information into a request
for a decision, silently.

This is a different failure from offering the maintainer a poor menu, which
`a-closed-option-set-caps-the-answer-at-what-you-thought-of.md` already covers. There the question was
theirs to answer and the options were ones you invented, so the remedy is to open the menu. Here the
question was not theirs at all, and opening the menu would only produce a better-phrased version of a
question that should not have been asked.

## Why This Matters

Escalating what you already own is expensive in both directions.

Upward, it spends the maintainer's attention — the scarcest resource in a solo project where one
person is the only reviewer, the only live tester, and the only release authority. Worse, it stalls:
an unanswered non-question does not simply disappear, it becomes a standing item that gets
re-surfaced, re-explained, and re-weighed in every subsequent summary, diluting the items that
genuinely need a human. The signal-to-noise ratio of "here is what I need from you" degrades until
the user starts skimming it, and then a real blocker gets skimmed too.

Downward, the specific fix on offer — a tracked backlog file — would have been worse than the problem
it was meant to solve. State that the harness already holds would have had a second home, and the two
would diverge the first time the agent updated one and not the other. This repo has already written
the drift half of that argument down twice, for deploy paths and for commit bodies; the deploy-path
case adds the sharper form — a written copy of a fact that a live mechanism owns is the untested
copy, and it is the one that loses. The backlog case is worse than either, because the other party is
process state outside git entirely — no repository tool could
ever diff the two, so the drift would be continuous rather than triggered by a fix. It would also
have handed the maintainer a management burden they never asked for and explicitly did not want.

The cheap fix — explaining the tool once — cost one paragraph and held.

## When to Apply

Apply this before phrasing any question to the user, and especially when these conditions hold:

- The subject of the question is state the agent maintains: its task list, its scratch files, its
  intermediate notes, its own working method.
- You are about to propose creating a file, issue tracker, or document to hold information that some
  existing system already holds.
- An item has been listed as "awaiting a user decision" for more than one turn while other work
  proceeded. Re-read it and ask whether it is a decision at all.
- The user has previously stated a standing policy that would decide the question. Derive the answer
  rather than re-asking.

The sharpest discriminator is the grammar of the request. **"Where is X?" is a literacy question** —
the user is asking to be told something, and the correct response is an explanation, delivered once,
with nothing appended. **"We need somewhere to put X" or "can we track X" is an infrastructure
request** — the user is asking for something to exist, and only then is a proposal about where it
should live in scope. These read similarly in the moment and diverge completely in what they license
you to build. When the wording is genuinely ambiguous, answer the literacy question first and let the
user escalate if they wanted more; that direction is recoverable, and the other one leaves an
artifact in the tree.

## Examples

The escalation as it actually happened. The maintainer asked:

> "i don't know where tasks are, which file should I look at"

and the assistant, after correctly explaining the harness task tools, added a choice: track work
durably in a backlog file committed to the repo, in GitHub issues, or in both. It then carried that
unanswered choice into subsequent status summaries as an open item awaiting a user decision, beside
real ones such as whether to push and whether to cut a tag.

The corrected handling stops after the explanation: tasks live in the harness task list, which is
agent-owned, updated as work proceeds, and visible on request — no file in the repo holds them, and
none needs to. Then, having answered, the assistant exercises that ownership without asking, closing
task #101 because `c6342e4` had already shipped the underlying fix. No option set, no repo change, no
open item. If the maintainer had wanted durable tracking, they would have said so — and they later
confirmed the opposite: "the backlog is not necessary because the tasks are yours."

The commit-split precedent runs the same way. The bad version presents "should I split `1c5e7f6` so
the `LICENSE-ART.md` deletion is separate from the docs commit?" as an open decision. The corrected
version applies the maintainer's standing policy — keep history unless an old commit carries a severe
security problem — concludes that a misfiled file deletion does not clear that bar, and moves on,
mentioning the bundling only as a note in passing if it matters at all.

## Related

- `docs/solutions/workflow-issues/a-closed-option-set-caps-the-answer-at-what-you-thought-of.md` — the
  nearest relative, and the one to keep distinct. That doc fixes a menu whose options were too
  narrow; this one fixes a menu that should never have been offered, because the decision was not the
  maintainer's. Its remedy — ask more openly — is the wrong move here.
- `docs/solutions/conventions/document-the-path-you-actually-deploy-to.md` — already establishes that
  a written copy of a fact the running system owns is a second, untested source that drifts. That is
  the argument against the backlog file, borrowed rather than re-derived.
- `docs/solutions/conventions/commit-bodies-list-changes-not-lessons.md` — the same drift rule applied
  to artifact placement. That one answers where a fact should live; this one answers the prior
  question of whether a new place should exist at all.
- `docs/solutions/workflow-issues/a-user-report-carries-evidence-and-a-request.md` — its "restate the
  observation before acting on the request" check is the cheap catch here too. Restating as "tasks
  live in the harness list, not in a file" is itself the complete answer, and would have stopped the
  question from being asked.
- `docs/solutions/workflow-issues/finish-the-plans-main-line-before-chasing-its-gated-bet.md` — the
  adjacent failure of carried state: an unresolved item re-listed across turns without ever being
  re-examined.
