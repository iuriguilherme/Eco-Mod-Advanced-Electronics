---
title: "Commit bodies list what changed; the reasoning belongs in docs/solutions/"
date: 2026-07-31
category: conventions
module: EcoServerMod
problem_type: convention
component: development_workflow
severity: low
applies_when:
  - "Writing a commit message for work that also produced a documented learning"
  - "A commit body is growing past a few lines"
  - "Deciding where an investigation's reasoning should live"
tags: [git, commit-messages, documentation, knowledge-store, conventions]
related_components: [EcoServerMod/AdvancedElectronics, docs]
---

# Commit bodies list what changed; the reasoning belongs in `docs/solutions/`

## Context

This repo keeps a searchable knowledge store in `docs/solutions/`, so most non-trivial work produces
two artifacts: a commit and a doc. Without a rule about which gets the reasoning, the reasoning ends
up in both — and the commit copy is the one that cannot be maintained.

The failure is easy to miss because a long commit body reads as diligence. In one session, commit
bodies grew to a dozen-plus lines each carrying measured costs, falsified hypotheses, and design
rationale — every one of which was already written, better, in the doc the same commit was adding.

## Guidance

**Body is a few lines listing what changed.** Conventional subject, then roughly five lines or a
short bullet list. If it needs paragraphs, it is documentation wearing a commit's clothes.

**Test each sentence: does it say what is different in the tree now?** A sentence answering *why
this happened*, *what we learned*, or *what to do next time* belongs in the doc. Cut it and
reference the doc by path when the connection is not obvious.

**Put the reasoning in `docs/solutions/` instead.** That store carries frontmatter (`module`,
`tags`, `problem_type`) so it is searchable by topic, gets refreshed by maintenance passes when it
goes stale, and can be linked from other docs. A commit message does none of this.

**Never rewrite or amend already-pushed commits to correct this.** The history is public. Apply the
rule going forward and leave the record as it is — see
`docs/solutions/security-issues/machine-local-paths-leaked-into-a-public-repo.md` for the same
forward-only stance on a more serious commit-content problem.

## Why This Matters

Two copies of the same reasoning drift, and the commit copy always loses. It cannot be updated when
the finding is superseded, it is invisible to a topic search, and nothing cross-references it. The
doc gets refreshed; the commit body silently becomes a stale second opinion sitting next to it in
`git log`.

A softer version of this rule — "keep commit messages short" — was tried first and did not hold.
"Short" is self-graded, and a body explaining a genuinely interesting root cause always feels short
enough to its author. The line cap and the per-sentence test exist because the judgment call is the
part that fails.

## When to Apply

- Every commit that accompanies or follows a `docs/solutions/` entry.
- Whenever a commit body reaches a second paragraph.
- When summarising an investigation — the summary goes in the doc, not the commit.

## Examples

Before, from this repo's own history — twelve lines of root cause and rationale, all of it already
present in the doc the commit was adding:

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

Both blocks above are quoted verbatim from the real commit and are kept that way, because what
they illustrate is commit-message *form*. Note for anyone reading them for content: **the technical
claim they state was overturned on 2026-08-14** — `[RequireComponent]` is re-enforced on every server
load, the doc they name has been deleted, and the `CONCEPTS.md` entry has been corrected. See
`docs/solutions/conventions/requirecomponent-is-re-enforced-on-every-server-load.md`. That an
exemplar in a conventions doc kept circulating a retracted claim is itself the subject of
`docs/solutions/workflow-issues/a-knowledge-store-corroborates-its-own-errors.md`.

A code commit under the same rule — what changed, not why it was interesting:

```text
feat(server): assign the drone with a stepper, not a control per area

- SurveyComponent: AssignedPosition replaces the six assign checkboxes
- area cap raised to ten, enforced at creation in SurveyAreaPicker
- headers switched to StringTitle

See docs/solutions/runtime-errors/n-editable-members-cannot-share-one-field.md
```

## Related

- `docs/solutions/security-issues/machine-local-paths-leaked-into-a-public-repo.md` — the other
  commit-content rule, and the forward-only stance both share: never rewrite pushed history to fix
  a message.
