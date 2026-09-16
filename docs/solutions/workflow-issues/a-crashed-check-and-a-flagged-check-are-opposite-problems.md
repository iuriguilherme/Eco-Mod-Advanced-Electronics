---
title: "A crashed check and a flagged check are opposite problems"
date: 2026-08-09
last_updated: 2026-09-15
category: workflow-issues
module: AdvancedElectronics
problem_type: workflow_issue
component: development_workflow
severity: high
applies_when:
  - "A bundled plugin validator exits nonzero and it is not yet clear whether it ran"
  - "A documentation check reports nothing and the run is treated as verified"
  - "A solution doc cites a Unity asset GUID, an engine-source path, or a release-zip member"
  - "Running a bundled Python script against repo files on a Windows machine"
  - "A single doc draws a dozen or more citation flags, or the flagged citation is the deletion the doc is about"
tags: [ce-compound, validation, windows, encoding, cp1252, unity-guid, documentation, silent-failure]
related_components:
  - "docs/solutions"
---

# A crashed check and a flagged check are opposite problems

## Context

The `ce-compound` skill's grounding phase runs two bundled Python validators against the doc it just
wrote: a frontmatter parser-safety check (`validate-frontmatter.py`) and a mechanical claims check
(`validate-doc-claims.py`), which scans cited paths, commit SHAs, relative links, and leftover
drafting scaffold. Both ship inside the compound-engineering plugin's own skill directory. Neither
lives in this repository, so neither is under this repo's control and both can change when the plugin
updates.

Across a run of `ce-compound` invocations in a single session, two different things happened that both looked,
at a glance, like "the check failed". Once the validator crashed before it had read the document at
all. Repeatedly, it ran to completion and flagged citations that were correct on purpose.

Those are opposite situations, and only the second one is about the document.

A crash means the protection did not run. Zero claims were checked; the doc got no verification
whatsoever, and the nonzero exit says nothing about its content. A flag means the protection ran,
worked, and handed back a question. The skill's own grounding reference is unambiguous about which
kind of thing a flag is: "Neither pass is a hard gate — every flag is adjudicated, because solution
docs legitimately cite deleted paths and pre-fix states," with exactly three resolutions available,
"fix, annotate, or confirm intentional — never an automatic rewrite and never an automatic pass."

That is settled. What this document adds is the local consequence: on this machine the check can fail
to run at all, and in this repo the flags that are correct on purpose are not a small closed set but
the *majority* of the output, for reasons this store's own maintenance rules guarantee. Conflating the
two is how a validator quietly gets dropped from the workflow. Once flags are routinely waved through
as "the checker not understanding the doc", a crash reads as more of the same, and the one failure
mode that actually costs coverage gets the same shrug.

## Guidance

### Run the validators with `PYTHONUTF8=1`, every time, on this machine

Both scripts read the doc with a bare `open(doc_path)` and no `encoding` argument. Python on this box
is 3.11, which still takes its default text encoding from the locale — `cp1252` here. So the doc is
decoded as cp1252 regardless of the fact that it was written as UTF-8.

Set the environment variable on the invocation:

```bash
PYTHONUTF8=1 python "$SKILL_DIR/scripts/validate-doc-claims.py" docs/solutions/<category>/<doc>.md
PYTHONUTF8=1 python "$SKILL_DIR/scripts/validate-frontmatter.py" docs/solutions/<category>/<doc>.md
```

`SKILL_DIR` is the anchor the skill already establishes for itself; the point is only the prefix. In
PowerShell the equivalent is `$env:PYTHONUTF8 = "1"` set before the call, since PowerShell has no
inline environment-variable prefix.

Be precise about what actually breaks, because the failure is narrower than "non-ASCII characters":

- **En dashes and single curly quotes do not crash it.** Reproduced directly: a throwaway doc
  containing an en dash, and another containing a right single quote, both validate `OK` under plain
  cp1252. Their UTF-8 bytes all happen to have cp1252 meanings, so the file decodes into mojibake and
  the check completes against text that is wrong but readable. Quietly checking a corrupted copy of
  the document is its own small problem, but it is not the crash. The *closing double* quote is the
  exception among common typography: U+201D encodes as `E2 80 9D`, and `0x9D` is one of the undefined
  bytes listed below, so a doc using typographic double quotes crashes exactly like a drawn diagram
  does. U+201C (`E2 80 9C`) is safe, which makes the pair asymmetric. Docs in this repo use ASCII
  double quotes, which is why none currently trip it.
- **Characters whose UTF-8 encoding contains a byte cp1252 leaves undefined do crash it.** Those
  bytes are `0x81`, `0x8D`, `0x8F`, `0x90`, and `0x9D`. In practice the offenders in these docs are
  box-drawing and arrow glyphs: U+2510 (box drawings light down and left) encodes as `E2 94 90`, and
  U+2190 (leftwards arrow) as `E2 86 90`. Both end in `0x90`.

The observed failure, verbatim, running the claims validator against a real doc in this repo without
the variable set:

```
UnicodeDecodeError: 'charmap' codec can't decode byte 0x90 in position 2658: character maps to <undefined>
```

With `PYTHONUTF8=1` the same file reports `checked 4 paths, 0 SHAs, 0 links; 0 flags` and `OK`.

**One doc in `docs/solutions/` currently contains such a byte** —
`docs/solutions/architecture-patterns/persist-derived-data-as-serialized-snapshot-on-its-owner.md`,
where a leftwards arrow U+2190 in a code comment puts `0x90` at byte offset 10169 (it was 7675
when this was written; a 2026-09-15 refresh of that doc moved it, which is why the captured runs
below still name the old position). It crashes a
default-encoding read and validates clean with the variable set.

This paragraph previously named a second doc,
`docs/solutions/architecture-patterns/client-animation-is-driven-by-name-not-by-mod-code.md`, for a
box-drawing diagram. Re-checked 2026-09-06: it now contains **zero** bytes from the undefined set and
validates clean with no environment variable at all. The diagram left in ordinary editing, which is
the point worth keeping — this hazard arrives and departs with a doc's punctuation rather than with
anything about the tooling, so the set of affected files is not stable and is not worth maintaining
as a list. Any future doc with a drawn diagram or an arrow joins it.

This document deliberately names those codepoints instead of embedding the glyphs, so that it stays
readable by a cp1252 default read. That is a courtesy, not the fix. The fix is the variable.

### Adjudicate the recurring flags from a list instead of re-deriving them

Three flag shapes recur in this repo and are always resolved as "confirm intentional". They do not
mean the doc is wrong and they do not mean the validator is wrong; they mean the checker is missing
context it has no way to obtain.

| Flag shape | What is actually being cited | Standing resolution |
|---|---|---|
| `FLAG sha <32 hex chars> — does not resolve to a commit in this repository` | A Unity asset GUID copied out of a `.meta` file or a serialized asset reference | Confirm intentional. The GUID *is* the identifier for that asset; there is nothing to replace it with. |
| `FLAG path ... — not found in working tree or origin/main`, where the path is under Eco's server source tree | A file in Strange Loop Games' proprietary engine source, cited to ground the existence of an engine attribute | Confirm intentional, provided the surrounding prose already says the path is in Eco's tree and not in this repository. |
| `FLAG path ... — not found in working tree or origin/main`, where the path is a member name inside the release zip | A file that exists only inside the built release archive | Confirm intentional, provided the prose makes the archive context explicit. |
| `FLAG path ...`, where the target was deleted **and the deletion is the doc's subject** | A learning quoting the superseded doc it replaced, or code retired after the rule outlived its call site | Confirm intentional when the prose already marks it — *"The deleted `X`"*, *"(since deleted)"*, a status note. **Annotate** if it does not. |
| `FLAG path ...`, where the path is under a git-ignored directory | Files present on disk and never in the tracked tree. `.gitignore:119` is the single pattern `.*`, so all of `.references/` is excluded | Confirm intentional. No commit will ever contain them, so no future check will resolve them either. |
| `FLAG path ...`, where the token is a bare URL | A link written without its scheme. The script's guard drops anything containing `://` or starting with `http`, so `github.com/…/file.md` passes it and then passes the path-shape test on its `.md` ending | Confirm intentional, or add the scheme if you want the link clickable. |
| `FLAG sha … — Prefer citing the PR number`, on a long-lived branch | A commit on a branch with no upstream and no pull request | Confirm intentional. The advice assumes a PR number exists to substitute; on this repo's long-lived branches there is often none. |

Everything outside those shapes still gets adjudicated case by case against the skill's own
table. A confirmed-intentional list is a shortcut for the known cases, not a licence to stop reading
the output — it exists so that eighteen predictable flags on one doc do not consume the attention the
nineteenth, unfamiliar one deserves.

### Never let a nonzero exit stand in for a verdict

Read the output before deciding anything. `checked 0 paths, 0 SHAs, 0 links` and a traceback mean the
document is unverified and the run is not done; a flag list with counts means the document was
verified and now needs a decision. Same exit code, opposite meanings.

## Why This Matters

The compounding store is trusted knowledge. Future agents act on these docs without re-verifying
them, which is exactly why the grounding phase exists. A validator that crashed produced no evidence
at all, so a doc that sailed past a crashed check has the same standing as a doc written with the
check disabled — except it feels checked, which is worse than knowing it is not.

The silent-mojibake case matters for the same reason in a smaller way. A cp1252 read of a UTF-8 doc
that happens not to crash still hands the validator a different string than the one on disk. For
parser-safety checks that is mostly harmless, but "mostly harmless" is not a property worth relying
on when a single environment variable removes the question.

On the flag side, the cost is repetition and erosion. The same three flags appeared run after run in
one session. Each one, re-derived from scratch, means re-opening the doc, re-reading the citation,
and re-reasoning about whether a 32-character hex string is a commit. Doing that once per run is waste;
skipping it because "it's always the GUID thing" is how a real flag gets waved through with the
familiar ones.

### The rate is structural, not incidental

The reason these flags keep coming is not three citation habits. It follows from how the store is
maintained. A superseded learning is **deleted outright** — there is no archive directory, and
version history is the archive (`ce-compound-refresh/SKILL.md:50`, restated in that skill's
`references/classify.md:11` and `references/per-action-flows.md:52`). This repository follows it: a
glob of `docs/solutions/**/*.md` returns seventy-one docs and not one under an `_archived/` path. The
convention only works because this project never rewrites git history (auto memory [claude]).

Put those together. A learning whose subject is *a claim that turned out to be wrong* has to name the
document that carried it, and that document was deleted the moment it was superseded. A learning
whose subject is *a defect in code* outlives the code, because the rule was the point and the call
site was only the occasion. In both cases a correct, valuable doc **must** cite something that no
longer resolves. This is not a store that accidentally accumulates dead references; its most useful
documents are the ones that necessarily contain them.

Add the rest of what this repo legitimately cites — a game engine source tree that is a sibling
directory, a `.references/` tree excluded wholesale, URLs, and SHAs on unmerged long-lived branches —
and the checker's premise, *every cited path resolves in this repo now*, is false for a large share
of a **healthy** store's citations. One session put four docs through the checks and got thirty
flags; roughly two were real. Eighteen flags on one doc is not a sign the doc rotted. It is a sign
the doc grounds its claims in the engine source, which is what makes it worth trusting.

### What an auto-fix destroys, and why it looks like maintenance

Adjudicating a legitimate flag costs a minute. "Fixing" a citation that was deliberately historical
destroys evidence, and it destroys it silently.

Work it through on a real case. `a-knowledge-store-corroborates-its-own-errors.md:57-58` cites
`docs/solutions/conventions/requirecomponent-binds-at-creation-not-retroactively.md`, which was
deleted and replaced by `requirecomponent-is-re-enforced-on-every-server-load.md`. An agent trusting
the flag repoints it at the replacement, because that is the obvious fix and the paths are nearly
identical. The sentence now reads *"The deleted `…is-re-enforced-on-every-server-load.md` opened
with …"* — naming a file that is not deleted, and attributing to the **correct** learning the wrong
claim that the **incorrect** one made. The doc's worked example becomes a false accusation against
the doc that fixed the problem. The exit code goes green. The diff looks like tidying.

The same shape applies to a retired call site: strike `DistrictAssignment.cs:63` from
`consistent-grid-column-quantization.md` and the doc still states its rule, but it no longer shows
the two divergent quantization expressions side by side — the only part proving the rule was learned
from a real defect rather than asserted from taste. A learning with its evidence removed reads like
an opinion, and nothing in the resulting file says it ever had more.

That is the sharp point. A destroyed citation does not announce itself, does not fail a check, and
cannot later be distinguished from a doc that was always thin. The destruction looks exactly like
maintenance, which is why the discipline has to sit at the moment of adjudication rather than at
review.

It is worth being honest that the validators are behaving correctly in all these cases. A 32-character
lowercase hex string genuinely is indistinguishable from an abbreviated commit SHA without knowing it
came out of a `.meta` file — the claims checker matches 7 to 40 hex characters, and 32 sits squarely
in that window. A path checker that resolves citations against the working tree and `origin/main`
cannot know that a path names a member inside a zip that gets built later, it cannot see a
proprietary engine checkout that is not part of this clone, it cannot resolve a path the repo's own
`.gitignore` excludes, and it cannot read the sentence next to a citation that says the file was
deleted on purpose. Those are right answers computed from the
information available. The missing piece is context that only the author has, which is precisely why
the resolution is adjudication rather than an automatic pass.

## When to Apply

Every `ce-compound` run in this repo: prefix both validator invocations with `PYTHONUTF8=1`, then
adjudicate against the table above before reasoning from first principles.

More generally, apply the encoding prefix to any bundled plugin Python script run on this machine
that reads repo files. The scripts are vendored inside a plugin cache, so they cannot be patched here
and any local fix would be overwritten on the next plugin update; the environment variable is the
only durable lever on this side.

Apply the crash-versus-flag distinction whenever a documentation check exits nonzero. The first
question is always "did it run?", not "what did it find?".

**That question presumes the status can answer it, and for a delegated worker it cannot.** Everything
above concerns a script the orchestrator runs itself, where a traceback and a flag list are two
distinguishable outputs of one process. A dispatched subagent adds a third case the pair does not
cover: it writes its output to a file and *then* composes its return, so a death in between reports a
truthful failure over work that is complete on disk. There the first question is not "did it run?" —
nothing in the notification answers that usefully — but "is the artifact there?", which is a
directory listing rather than an inference. See
`docs/solutions/workflow-issues/a-failed-agent-may-have-already-written-its-artifact.md`.

Apply the adjudication table with particular care when the flagged doc's own subject is a
**correction, a retirement, or a superseded belief** — those must cite what is gone, and their
evidence is the most expensive thing in the store to lose. The prior runs the other way elsewhere: a
convention doc that says "do it this way, see `X`" has no structural reason to name a missing file,
so read that flag as probably real until the prose says otherwise.

## Examples

Three docs written this session, each carrying one of the recurring citation classes. All three were
re-run with `PYTHONUTF8=1` while writing this up; the output below is the actual result, not a
recollection.

**Unity GUID reads as a SHA.**
`docs/solutions/runtime-errors/override-animator-layer-without-avatar-mask-overwrites-base-layer.md`
quotes the animator controller diff that assigns the blades avatar mask, which necessarily includes
the mask asset's 32-character GUID. Result: `checked 6 paths, 1 SHAs, 0 links; 1 flags`, the flag
being that the hex string does not resolve to a commit. It does not, and it never will. The doc's own
prose already says the string is a GUID matching `HRVSTR_BladesMask.mask.meta` and is not a commit
hash, which is the annotation that makes the flag safe to confirm. Note that the claims validator
scans for SHAs across the whole body including fenced code, so quoting any Unity YAML diff will
trigger this.

**Engine-source path outside this repository.**
`docs/solutions/conventions/unregistering-a-crafting-table-does-not-hide-the-recipe.md` grounds the
claim that `RecipeFamily` carries `[ForceCreateViewAllDerived]` by citing the defining file and line
in Strange Loop Games' Eco 0.14 server source. Result: `checked 10 paths, 0 SHAs, 0 links; 1 flags`,
the flag being that the path is not in the working tree or `origin/main`. Correct, and the citation
is the point: the doc explicitly states the attribute was confirmed against the engine source rather
than inferred, and that the path is in Eco's tree, not this repository. Removing the citation would
downgrade a verified claim to an asserted one, so the resolution is confirm, not fix.

**Release-zip member paths.**
`docs/solutions/conventions/a-licence-notice-travels-with-the-asset-not-the-repo.md` (the learning
behind the licence work that shipped around `v0.2.0`, commit subject "docs(solutions): a licence
notice has to travel with the asset") describes verifying the art licence by reading it back out of
the built archive rather than out of the repo. It therefore names two archive members under the
top-level AdvancedElectronics prefix that the zip creates. Result: `checked 10 paths, 0 SHAs, 0 links; 2
flags`, both "not found in working tree or origin/main". They are not in the tree by design — the
whole point of that doc is that a green `git status` is not evidence and the shipped bytes are. The
prose already frames both as members of the archive, so both are confirmed intentional.

**The crash, reproduced.** Re-run on 2026-09-06 against the doc that still carries the byte.
`python <script> docs/solutions/architecture-patterns/persist-derived-data-as-serialized-snapshot-on-its-owner.md`
with no environment variable produces

```
UnicodeDecodeError: 'charmap' codec can't decode byte 0x90 in position 7675: character maps to <undefined>
```

and checks nothing. The same command with `PYTHONUTF8=1` reports
`checked 5 paths, 0 SHAs, 0 links; 0 flags` and `OK`. Same file, same script, same repo state; one
invocation verified the document and the other never opened it successfully. The machine's premise
still holds as described: Python 3.11.9, `locale.getpreferredencoding(False)` returns `cp1252`, and
`PYTHONUTF8` is unset unless the invocation sets it.

## Related

- `docs/solutions/workflow-issues/a-fixed-defect-in-the-present-tense-passes-every-check.md` — the
  third position in this doc's taxonomy. A crash means nothing was checked; a flag means something was
  checked and questioned; that doc covers the case where the check runs clean, reports nothing, and the
  prose it passed over is false. Its first instance is this doc's own crash reproduction, which stopped
  reproducing.
- `docs/solutions/developer-experience/two-shells-one-repo-windows-toolchain-traps.md` — the same
  genus as the encoding half: a Windows default silently breaking a tool written and tested
  elsewhere. That doc sorts its traps by whether they fail loud or silent, and this one straddles
  the split — the traceback is loud, the loss of coverage is silent.
- `docs/solutions/workflow-issues/a-failed-agent-may-have-already-written-its-artifact.md` — the
  third case in this doc's taxonomy, on the inverted sign. A crash means no work was done and a flag
  means work was done and questioned; a delegated worker can report failure over work that is
  finished and durable, because it writes its artifact before it composes its return.
- `docs/solutions/workflow-issues/a-gate-that-discovers-nothing-passes-everything.md` — the same
  doctrine reached by a different mechanism: a check that reports success while having examined
  nothing. There the discovery step matched no files; here it never opened one.
- `docs/solutions/workflow-issues/release-scripts-should-refuse-not-warn.md` — the case where the
  opposite response is right, and the reason the two are not in conflict. That doc argues a release
  check should refuse rather than warn, on the ground that its false positives are rare and each
  costs one rebuild. Here they are structural and dominant, so the check hands back questions to
  adjudicate instead of gating. The premise is what differs, not the doctrine.
