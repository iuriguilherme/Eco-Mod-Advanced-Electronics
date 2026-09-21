---
title: "A commit-message rule failed five times and was withdrawn"
date: 2026-07-31
last_updated: 2026-09-21
category: workflow-issues
module: AdvancedElectronics
problem_type: workflow_issue
component: development_workflow
severity: low
applies_when:
  - "Proposing a commit-message convention for this repository"
  - "A rule has been restated and tightened several times and is still not followed"
  - "Deciding whether to escalate an unfollowed rule or withdraw it"
  - "Reading an older commit body and wondering whether it violated a standing rule"
tags: [git, commit-messages, conventions, process, retired-rule, rule-enforcement]
related_components: [docs]
---

# A commit-message rule failed five times and was withdrawn

## Status

**This rule is not enforced. Do not re-propose it.**

The repository has no commit-message convention beyond the conventional-commit subject prefix
(`feat`, `fix`, `docs`, `chore`) and the mechanical constraints in
`docs/solutions/developer-experience/two-shells-one-repo-windows-toolchain-traps.md` — use `-F -`
with a quoted heredoc for a multi-line message, and read it back with `%B`. Those are about the
message surviving the shell intact, not about its content or its length.

What follows is the record of why there is no content rule, kept because the rule reads as
obviously correct and was therefore written five separate times.

## Context

The repo keeps a searchable knowledge store in `docs/solutions/`, so most non-trivial work produces
two artifacts: a commit and a doc. The rule that kept being proposed divided them: the commit lists
what changed, the doc carries the reasoning. Without it, the reasoning ends up in both, and the
commit copy is the one that cannot be maintained.

That argument is sound and was never the problem. The problem is that nothing made it hold.

## What was tried

Five attempts, each one tightening the mechanism the previous one relied on.

1. **"Keep commit messages short."** Self-graded. A body explaining a genuinely interesting root
   cause always feels short enough to its author.

2. **A stated shape: conventional subject, then roughly five lines or a short bullet list.** Gave
   the judgment a number to check against. An entire session of 20+ commits went in at 15-25 lines
   each, every one opening with the symptom, then the root cause, then why the obvious fix was
   wrong.

3. **A per-sentence test: every sentence must answer *what is different in the tree now*.** Moved
   the check from the body as a whole to each sentence, on the theory that the whole-body judgment
   was too coarse to fail cleanly. It did not change the output.

4. **A procedure: draft the subject, commit with the subject alone, add a body only as a separate
   decision.** Attacked the ordering rather than the content, so that a body had to be chosen rather
   than written by momentum.

5. **A mechanical rule with a word list.** Write the subject, then stop. Bullets only, maximum five.
   Count the lines before committing. If the body contains "because", "which is why", "the cause",
   "used to", or "instead of", delete it and commit the subject alone.

Every one of those was recorded as project feedback, and every one was followed by commits that
violated it.

## Why the fifth one got through

The fifth attempt is the interesting one, because it was no longer a judgment call. It was a count
and a word list, and it still did not run.

On 2026-08-22, working on the tech-tree icons, every commit message was written inline in the same
heredoc as the `git commit` call. The message and the commit were one action. There was never a
moment at which "count the lines" could happen, because there was no point between composing the
message and creating the commit — composing it *was* creating it. Seven commits, 10-25 line bodies,
every one restating a code comment written minutes earlier in the same session.

The mechanism assumed a step that the tooling had removed. A rule that requires a checkpoint cannot
be enforced inside an action that has no checkpoint, and the more mechanical the rule got, the more
it depended on exactly that missing step.

## Why this was withdrawn rather than escalated again

The sixth attempt would have been another mechanism. There was one available — a commit-msg hook
would have enforced it without needing a checkpoint at all.

It was not taken, and the reason is a judgment by the repo's owner rather than a technical limit:
the cost of the rule now exceeds what it buys. Each restatement adds text that loads into every
session's context and is not followed, and unfollowed rules in an instruction file are worse than
no rule, because they teach that the instruction file is approximately true.

The underlying concern is real and unchanged — two copies of the same reasoning drift, and the
commit copy always loses, because it cannot be updated when the finding is superseded, it is
invisible to a topic search, and nothing cross-references it. That concern is now handled by
`docs/solutions/` being where reasoning is *looked for*, not by a rule about where it is forbidden
to also appear.

## When to apply

- When you are about to propose a commit-message convention here. Read this instead, then do not.
- When an existing rule has been restated more than twice and is still not followed. Escalating the
  mechanism is the move that failed here; check first whether the rule's checkpoint still exists in
  the workflow that is supposed to run it.
- When reading old commit bodies. They do not violate a standing rule, because there is none.

## Historical examples

Kept from the withdrawn version of this document, because they are the record of what the rule
asked for. They are not instructions.

Before — twelve lines of root cause and rationale, all of it already present in the doc the commit
was adding:

```text
docs(solutions): record that RequireComponent binds at creation only

Editing [RequireComponent] changes what NEW World Objects get and leaves
existing ones alone, in both directions. Both cost something this session.

Adding a probe to SurveyDroneObject produced no tabs, no content and no
log line, because every drone in the test world pre-dated the change and
none had the component. That read as "the drone renders nothing" -- a
much harder problem that was not happening.

[... six more lines of guidance and rationale ...]
```

After — same commit, same information available, one place to maintain it:

```text
docs(solutions): record that RequireComponent binds at creation only

- new: conventions/requirecomponent-binds-at-creation-not-retroactively.md
- CONCEPTS.md: World Object gains component-set-fixed-at-creation
```

Both blocks are quoted verbatim from the real commit and are kept that way, because what they
illustrate is commit-message *form*. Note for anyone reading them for content: **the technical claim
they state was overturned on 2026-08-14** — `[RequireComponent]` is re-enforced on every server load,
the doc they name has been deleted, and the `CONCEPTS.md` entry has been corrected. See
`docs/solutions/conventions/requirecomponent-is-re-enforced-on-every-server-load.md`. That an
exemplar kept circulating a retracted claim is itself the subject of
`docs/solutions/workflow-issues/a-knowledge-store-corroborates-its-own-errors.md`.

## Related

- `docs/solutions/security-issues/machine-local-paths-leaked-into-a-public-repo.md` — the one
  commit-content rule that *is* enforced, and for a different reason: it is a security constraint on
  a public repository, not a style preference. It also carries the forward-only stance both shared —
  never rewrite pushed history to fix a message.
- `docs/solutions/developer-experience/two-shells-one-repo-windows-toolchain-traps.md` — the
  mechanical constraints on a commit message that do still hold: `-F -` with a quoted heredoc, and
  the `%B` read-back.
- `docs/solutions/workflow-issues/a-knowledge-store-corroborates-its-own-errors.md` — the failure
  mode this document exhibited while it was live, and the reason its examples carry a retraction
  note.
