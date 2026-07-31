---
title: "Validate the instrument before the hypothesis"
date: 2026-07-31
category: workflow-issues
module: EcoServerMod
problem_type: workflow_issue
component: development_workflow
severity: high
applies_when:
  - "Bisecting a crash by rebuilding and re-running between edits"
  - "Restoring files from scratch copies taken partway through an investigation"
  - "Reading a run as PASS without confirming it reached the code under test"
  - "A culprit appears to move to whichever file was edited most recently"
tags: [debugging, bisect, false-negative, scratch-files, stack-overflow, eco-modding, methodology]
related_components: [EcoServerMod/AdvancedElectronics]
---

# Validate the instrument before the hypothesis

## Context

A mod stopped booting: the server died during `Initializing skills` with Windows exception
`0xc00000fd` — a stack overflow, uncatchable by design, so nothing reached any log file. Finding the
cause took roughly six hours and produced four confident, wrong culprits in a row before the real one.

The bug itself was small. What made it expensive was that the measuring apparatus was broken in two
independent ways, and neither announced itself. Every reading looked like a clean result.

## Guidance

**A scratch copy is only valid for the state it was taken in.** Mid-bisect, several files were saved
aside as `<name>.cs.disabled` so they could be removed from the build. Those copies were taken *after*
an earlier experiment had rewritten a type name throughout the tree. Later, "restoring the originals"
copied those files back — silently reintroducing the experimental rewrite into every subsequent build.

The consequence is the tell: **the culprit appeared to move to whatever had just been edited.** Fix
the parts list, the crash blames the parts list; revert that, it blames `base.Initialize()`; revert
that, it blames the battery block. Four innocent files were each "confirmed" in turn, because the
contaminated file moved in and out of the build alongside them.

If a bisect's suspect keeps relocating to your most recent edit, stop bisecting. That pattern means a
variable is changing that is not on your list.

**Restore from version control, never from your own scratch copies.** `git checkout -- <path>` has a
defined meaning; `cp ../tmp/thing.bak` has whatever meaning it had when you made it, which you will
not remember an hour later. Scratch copies are fine for *removing* a file from a build; they are not a
restore path.

**A run that did not reach the code under test is not a negative result.** Two runs in this
investigation were read as "boots — culprit confirmed". Neither had reached the failing phase:

| Run | Lines of output | Reached `Initializing skills`? | Read as |
|---|---|---|---|
| A | 23 | no — exited immediately | "booted, culprit confirmed" |
| B | 43 | no — `AbandonedMutexException`, another host held the save | "booted, confirmed" |
| real boot | 314 | yes, then `Web Server now listening` | — |

Grepping for the failure string and finding nothing is not evidence of success. **Assert the positive
marker** — the phase you are testing must appear in the output, and ideally the run must reach a
known end state. A 23-line log and a 314-line log are not the same outcome, and only one of them
tested anything.

**Two hosts cannot hold the same save.** `AbandonedMutexException` in this stack means another
process already has it. During interactive testing that will be the human's client, which makes it
precisely the moment a background run is most likely to produce a silent false negative.

**When the fault is uncatchable, the trace still exists — on stdout.** A `StackOverflowException`
terminates the process without unwinding, so `try`/`catch` cannot see it and no log writer flushes it.
The runtime still prints the frames to standard output. Running the server from a shell with output
redirected produced ~8,000 frames naming the exact recursing method, which is what eventually made a
real bisect possible. Instrumenting the mod with `try`/`catch` would have produced nothing at all.

**Prefer adding new types over modifying working ones while exploring.** The maintainer's own summary
is the cleanest statement of the fix:

> "I should have followed my own advice and created a new dock and drone objects and items to test
> new things instead of modifying the working ones."

Because the existing world objects were edited in place, "does the new content break it?" and "does
the changed dock break it?" were the same question, with no configuration that isolated one from the
other. New types alongside the old ones keep a known-good path bootable as a control for free.

## Why This Matters

Every wrong conclusion in this session was reached by reading an instrument, not by reasoning badly
about the domain. The domain reasoning was mostly checkable and mostly got checked. The instrument was
never checked, because instruments do not look like claims — they look like results.

That is also why it compounds: a broken instrument does not fail, it *agrees with you*. A restore path
that quietly reverts one file will confirm whichever hypothesis you happen to test next, so
confidence rises while accuracy does not. Six hours of that produces a detailed, entirely fictional
causal story.

The eventual real bisect took six runs and under thirty minutes, once each run was verified to have
reached the failing phase and each restore came from git.

## When to Apply

- Before trusting the first green run in any bisect — deliberately reproduce the failure once, so you
  know the harness can see it.
- When a suspect moves to whichever file you edited most recently.
- When restoring files after an experiment, if the copies were made at any point other than the very
  start.
- When a background run and a human's interactive session might touch the same server, database, or
  save file.
- When the failure kills the process outright (stack overflow, `StackOverflowException`, OOM,
  segfault) — reach for captured stdout, not exception handling.

## Examples

Reading a run as a result, without checking it produced one:

```bash
# WRONG -- absence of the failure string is not presence of success
grep -q "Stack overflow" "$OUT" && echo "CRASHES" || echo "BOOTED"

# RIGHT -- require the positive marker for the phase under test
if ! grep -q "Initializing skills" "$OUT"; then
    echo "INVALID RUN -- never reached the phase under test ($(wc -l < "$OUT") lines)"
elif grep -q "Stack overflow" "$OUT"; then
    echo "CRASHES"
elif grep -q "Web Server now listening" "$OUT"; then
    echo "BOOTED"
else
    echo "INCONCLUSIVE -- reached the phase, no terminal marker"
fi
```

Auditing past runs the same way exposed both false negatives at a glance — the two "successes" were
23 and 43 lines against ~8,100 for a crash and 314 for a real boot.

Capturing an uncatchable fault, which no amount of in-mod instrumentation would have caught:

```bash
./EcoServer.exe > capture.log 2>&1
grep -oE 'at [A-Za-z0-9_.+<>]+' capture.log | sort | uniq -c | sort -rn | head
#    8013 at Eco.Gameplay.Skills.SkillTree.GetParentSet     <- the recursion, named
```

## Related

- `docs/solutions/workflow-issues/a-gate-that-discovers-nothing-passes-everything.md` — the same
  family one layer out: there a validation gate reported PASS because its discovery step matched
  almost nothing. Both are checks that succeed for the wrong reason; that one was about what a gate
  inspects, this one about whether a run happened at all.
- `docs/solutions/workflow-issues/tracing-beats-theorising-on-invariant-failures.md` — the rule this
  session kept violating before the captured trace ended the guessing.
- `docs/solutions/workflow-issues/verify-the-deploy-landed-before-asking-for-a-restart.md` — the same
  instinct applied to deploys: confirm the artifact under test is the one running.
- `docs/solutions/workflow-issues/eco-mod-batched-live-testing.md` — batching restarts, which assumes
  each batch actually tests what it claims.
