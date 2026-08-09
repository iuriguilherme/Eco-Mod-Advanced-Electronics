---
title: "A crashed check and a flagged check are opposite problems"
date: 2026-08-09
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
to run at all, and in this repo a fixed set of flags is always resolved the same way. Conflating the
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

Two docs currently in `docs/solutions/` contain such bytes:
`docs/solutions/architecture-patterns/client-animation-is-driven-by-name-not-by-mod-code.md` (a
box-drawing diagram) and
`docs/solutions/architecture-patterns/persist-derived-data-as-serialized-snapshot-on-its-owner.md`
(an arrow in a code comment). Both crash a default-encoding read and both validate clean with the
variable set. Any future doc with a drawn diagram joins them.

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

Everything outside those three shapes still gets adjudicated case by case against the skill's own
table. A confirmed-intentional list is a shortcut for the known cases, not a licence to stop reading
the output.

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

It is worth being honest that the validators are behaving correctly in all three cases. A 32-character
lowercase hex string genuinely is indistinguishable from an abbreviated commit SHA without knowing it
came out of a `.meta` file — the claims checker matches 7 to 40 hex characters, and 32 sits squarely
in that window. A path checker that resolves citations against the working tree and `origin/main`
cannot know that a path names a member inside a zip that gets built later, and it cannot see a
proprietary engine checkout that is not part of this clone. Those are right answers computed from the
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

**The crash, reproduced.** Running the claims validator against
`docs/solutions/architecture-patterns/client-animation-is-driven-by-name-not-by-mod-code.md` with no
environment variable produces the `UnicodeDecodeError` on byte `0x90` quoted above and checks
nothing. The same command with `PYTHONUTF8=1` reports `checked 4 paths, 0 SHAs, 0 links; 0 flags` and
`OK`. Same file, same script, same repo state; one invocation verified the document and the other
never opened it successfully.

## Related

- `docs/solutions/developer-experience/two-shells-one-repo-windows-toolchain-traps.md` — the same
  genus as the encoding half: a Windows default silently breaking a tool written and tested
  elsewhere. That doc sorts its traps by whether they fail loud or silent, and this one straddles
  the split — the traceback is loud, the loss of coverage is silent.
- `docs/solutions/workflow-issues/a-gate-that-discovers-nothing-passes-everything.md` — the same
  doctrine reached by a different mechanism: a check that reports success while having examined
  nothing. There the discovery step matched no files; here it never opened one.
- `docs/solutions/workflow-issues/release-scripts-should-refuse-not-warn.md` — the other end of the
  same pipeline. That one is about a check that detects a bad state and chooses to warn instead of
  refusing; this one is about a check that never gets far enough to detect anything.
