---
title: "A cross-reference makes two claims, and only the path is checked"
date: 2026-09-05
last_updated: 2026-09-06
category: workflow-issues
module: docs
problem_type: workflow_issue
component: documentation
severity: high
applies_when:
  - "Writing a sentence that summarises another document without currently having it open"
  - "A claims validator passes a cross-reference because the cited path resolved"
  - "Correcting a wrong description of a doc that other docs also cite"
  - "Reporting that a link exists, or that two docs are cross-linked both ways"
  - "Auditing or refreshing the learnings store's cross-references"
symptoms:
  - "A doc describes its target as an instance of the anti-pattern the target argues against"
  - "Two docs carry the same wrong claim about one target in different words, so no string search finds the pair"
  - "Every link resolves and every validator passes while the prose around them is false"
  - "A commit body describes the prior state of a file or a link, and the tree does not record that state"
  - "Other citers of the same target are assumed wrong, or assumed right, without being opened"
root_cause: inadequate_documentation
resolution_type: documentation_update
tags:
  - cross-reference
  - documentation-drift
  - knowledge-store
  - verification
  - false-confidence
  - validation
  - ce-compound
  - eco-modding
related_components:
  - docs/solutions
  - docs
---

# A cross-reference makes two claims, and only the path is checked

Every commit named below is on `feat/tech-tree-icons`, which is unmerged and has no PR. The short
SHAs are therefore branch-local: they are the only way to name this work today, they are cited here
the way two other docs in this store cite branch-local SHAs, and they are not durable references. If
this branch is ever squashed or rebased, look the commits up by their subject lines rather than by
these hashes.

## Context

A cross-reference from one learning doc to another is not one claim. It is two, and they are checked
by very different amounts of machinery.

The **path claim** is the citation itself — `docs/solutions/workflow-issues/eco-mod-batched-live-testing.md`. It asserts
that a file exists at that path. This claim is checked mechanically, on every `ce-compound` run, by
the bundled claims validator.

The **description claim** is the sentence wrapped around the citation — the em-dash clause that tells
the reader what they will find if they follow it. It asserts that the target says a particular thing.
This claim is checked by nobody. And it rots faster than the path does, because of how it gets
written: the author is describing a document they are not currently reading. The path is copied or
completed from the filesystem; the description is produced from memory of a doc that may have been
read weeks ago, or reconstructed from its filename, or inferred from why the author wanted to link to
it in the first place.

Three instances of the description claim being wrong turned up in a single session on 2026-09-05.

### Instance 1 — the crashed-check doc described the release doc as its own opposite

The target is `docs/solutions/workflow-issues/release-scripts-should-refuse-not-warn.md`. Its thesis
is stated as a bolded lead sentence at
`docs/solutions/workflow-issues/release-scripts-should-refuse-not-warn.md:38`:

> **Refuse, do not warn.** A script that prints a warning and then exits 0 has taught the operator to
> ignore warnings.

The doc's own title, at `release-scripts-should-refuse-not-warn.md:2`, says the same thing: *"A
release script should refuse to package a stale artifact, not warn about it."* Warning instead of
refusing is the behaviour the document argues **against**. It is the anti-pattern, not the subject.

Before it was corrected, `docs/solutions/workflow-issues/a-crashed-check-and-a-flagged-check-are-opposite-problems.md`
described that target, in its Related section, like this:

> - `docs/solutions/workflow-issues/release-scripts-should-refuse-not-warn.md` — the other end of the
>   same pipeline. That one is about a check that detects a bad state and chooses to warn instead of
>   refusing; this one is about a check that never gets far enough to detect anything.

"A check that detects a bad state and chooses to warn instead of refusing" is a precise description of
the failure the target was written to eliminate. A reader who trusted that sentence would come away
believing the store contains a doc arguing for warnings, which is the inverse of what it contains.

Corrected in `bada981` — *"docs(solutions): mark refuse-not-warn as a condition, not a slogan"*. The
replacement text now reads, at
`docs/solutions/workflow-issues/a-crashed-check-and-a-flagged-check-are-opposite-problems.md:280-284`:

> - `docs/solutions/workflow-issues/release-scripts-should-refuse-not-warn.md` — the case where the
>   opposite response is right, and the reason the two are not in conflict. That doc argues a release
>   check should refuse rather than warn, on the ground that its false positives are rare and each
>   costs one rebuild.

### Instance 2 — a second doc carried the same wrong claim in different words

`docs/solutions/workflow-issues/a-gate-that-discovers-nothing-passes-everything.md` cited the same
target twice. One citation, in the body, was correct and remains untouched: *"That doc is about what a
gate does once it has detected a bad condition."* The other, in its Related section, carried the same
error as instance 1:

> - `docs/solutions/workflow-issues/release-scripts-should-refuse-not-warn.md` — the adjacent failure at
>   the next step: that doc is about a gate that detects a bad condition and declines to fail on it;
>   this one is about a gate whose detection stage was empty, so its failure logic never ran.

One correction to how this was first reported: the two wrong sentences are **not** word for word. They
are the same wrong claim expressed independently — "chooses to warn instead of refusing" in one,
"declines to fail on it" in the other. That matters for diagnosis, because two identical strings look
like a copy-paste and invite a copy-paste hunt. Two different phrasings of the same wrong claim mean
the error was reconstructed twice from the same faulty memory of the target, which is a mechanism no
string search will find.

Corrected in `87c4056` — *"docs(solutions): over-broad self-corrects only while a false failure is
rare"*. The replacement, at
`docs/solutions/workflow-issues/a-gate-that-discovers-nothing-passes-everything.md:300-303`, now says
the target *"argues a gate which detects a bad condition should refuse rather than warn, and states
the premise that makes refusing right."*

### Instance 3 — a link direction asserted rather than checked

The third instance is real but its shape is not quite what it was first reported to be, and the tree
is what settles it.

The report was that a doc written earlier in the same session had been described as "cross-linked both
ways" with its sibling when only one direction existed. The two docs are
`docs/solutions/workflow-issues/an-audience-model-sets-the-sources-not-just-the-tone.md` and
`docs/solutions/workflow-issues/a-review-scoped-to-the-document-cannot-see-what-the-code-settled.md`.

What history actually shows:

- `717899f` created the audience-model doc as a 314-line new file. Its Related section at that commit
  cites three things, and the review-scoping doc is not among them. Searching that revision of the
  file for the review-scoping doc's filename returns nothing.
- The review-scoping doc, at that same commit, did not cite the audience-model doc either.
- `6304e2a` — *"docs(solutions): widen the review-scoping learning to authoring time"* — added **both
  directions in a single commit**: the review-scoping doc gained a body paragraph and a Related entry
  pointing at the audience-model doc, and the audience-model doc gained a Related entry pointing back.

So no committed tree ever held the one-way state. What the tree does show is a description claim of
the same class, one artifact over: `6304e2a`'s own commit body says

> Also adds the reciprocal link on the companion, which had only pointed one way.

At `6304e2a`'s parent the pair pointed **neither** way. The most generous reading is that "had only
pointed one way" describes an intermediate state inside that same commit — after the first link was
written, before the second — which is a state no reader of the history can ever observe. Either way it
is a sentence about the prior state of two documents, written from memory rather than from `git show`,
and it is not what the tree records. The end state is correct: today the link genuinely runs both
ways, at `an-audience-model-sets-the-sources-not-just-the-tone.md:302` and
`a-review-scoped-to-the-document-cannot-see-what-the-code-settled.md:148`.

The instance is worth keeping because it shows the same failure escaping the doc bodies entirely.
Commit messages carry description claims too, nothing validates them at all, and `git log` is where
future readers go for exactly the kind of "what did this look like before" question the sentence
purports to answer.

### Why the path half was never in doubt

The claims validator bundled with `ce-compound` lives at
`skills/ce-compound/scripts/validate-doc-claims.py` in the plugin cache. Its own docstring, at lines
16-17, states the first check as *"Cited repo-relative paths (backticked, containing at least one
'/') exist in the working tree"*. What it does with each half of a cross-reference is worth stating
exactly, because "nothing checks the description" should be a read finding and not an assumption:

- **A backticked path.** Every backticked span in the body is normalized and tested for existence with
  `os.path.exists` (line 340). If that misses, the path is looked up in git at `HEAD` and at the
  upstream default branch (`head_has_path` / `upstream_has_path`, lines 302-312), so a file deleted by
  the very fix being documented is classified as historical rather than reported as fabricated. Note
  that `normalize_path` (line 214) strips a trailing `:12` or `:12-20` line reference before the check
  — so even a cited **line number** is not verified, only the file it hangs off.
- **A relative markdown link.** `MD_LINK_RE` extracts the target, URL schemes and intra-doc anchors are
  skipped, and the remainder is tested with `os.path.exists` against the doc's own directory (line
  458).

In both cases the check ends at existence. The script never opens the cited file. It has no
representation of what the target says, so it cannot compare that against the sentence around the
citation. The only content-aware check in the whole script is the scaffold regex pass (lines 466-474),
and that examines the **citing** doc's own text for leaked drafting tokens — `{{...}}` and "Learning
N" — not the target's.

That is the structural fact underneath all three instances. The path claim has an owner that runs
every time. The description claim has no owner at all.

### The sweep, and what it found

The correction that matters is not fixing the two wrong sentences. It is asking how far the wrong
sentence had already travelled, rather than assuming it stopped where it was found.

Six documents under `docs/solutions/` cite
`docs/solutions/workflow-issues/release-scripts-should-refuse-not-warn.md`. Two of them are the wrong
copies above. The other four were read and are all correct:

- `docs/solutions/workflow-issues/verify-the-deploy-landed-before-asking-for-a-restart.md:137` — *"the
  same stale-artifact hazard on the packaging side, and the same conclusion: refuse rather than
  warn."*
- `docs/solutions/conventions/document-the-path-you-actually-deploy-to.md:149` — *"the same packaging
  script's other rule, and why its staleness guard fails closed."*
- `docs/solutions/developer-experience/two-shells-one-repo-windows-toolchain-traps.md:316` — *"the same
  instinct applied to release artifacts: when a failure is silent, add a check rather than trusting
  that it ran fine."*
- `docs/solutions/ui-bugs/bundled-mod-objects-must-ship-disabled.md:125` — an inline rather than a
  Related citation: *"A prefab-only change is invisible to the server; see [the target] for the
  matching staleness trap on the packaging side."*

Four of six correct, two of six wrong. That ratio is the reason this class of defect is hard to see.
It is not a universal convention that a reader could learn to distrust, and it is not an obvious typo
that one careful pass would catch. It is a minority error sitting inside a majority of correct
neighbours, each of which makes the wrong one look more plausible by being adjacent to it.

## Guidance

**Open the target and read its thesis sentence before you describe it.** This is the whole practice
and it costs one file read. Learning docs in this store are built so that the thesis is findable in
seconds: the `title` in the frontmatter, the `# ` heading, and the first bolded lead in the Guidance
section all state it. `release-scripts-should-refuse-not-warn.md` states it three times over — in the
title at `:2`, in the heading at `:18`, and as the bolded lead at `:38`. Not one of those had to be
searched for. In both wrong instances the description would have failed against any of the three on
sight.

**Treat a filename as a lead, not as a summary.** Filenames in this store are argumentative — they are
short sentences stating a conclusion — which makes them feel like they can be quoted from. They
cannot. `release-scripts-should-refuse-not-warn` compresses "refuse, do not warn" into a hyphenated
token where the imperative and the thing being rejected sit side by side with no grammar between them.
A reader reconstructing prose from that token has a genuine chance of reversing it, which is very
close to what happened here twice.

**Write the description as a claim you could be shown to be wrong about.** "That doc argues X on the
ground that Y" is checkable by anyone who opens the target. It also forces the author to have opened
the target, because you cannot state a premise you have not read. A vaguer formulation would have
hidden the error rather than fixing it.

**When you find a wrong description, fix it and then sweep every other citer.** Do not assume the
error is local, and do not assume it is universal. Grep for the target's filename across
`docs/solutions/`, open each hit, and read the sentence around each citation against the target's
actual thesis. This is the mechanism already documented in
`docs/solutions/workflow-issues/a-knowledge-store-corroborates-its-own-errors.md` — a wrong claim
recorded once does not stay one claim — applied to descriptions rather than to technical facts. The
difference worth noting is the spread pattern. That doc's four claims had reached *every* artifact
that touched them. Here the wrong description reached two citers out of six, so a sweep that stopped
at the first correct neighbour would have concluded, wrongly, that the problem was isolated. Read all
of them.

**Search for the claim, not for the string.** The two wrong copies here shared no phrase. A grep for
the first doc's wording would have returned nothing in the second. The only search that finds this
class is: list the citers by path, then read each sentence with the target's thesis in hand.

**Fix the description; do not repoint the link.** The temptation when a citation reads oddly is to
change what it points at, because that makes the sentence true again with one token edited. That is
the wrong repair here — the link was always correct, and repointing it would have destroyed a real
relationship between two docs while leaving the store's actual claim about the target unexamined. The
same trap in its sharper form is recorded in `c84bc10`: auto-repointing a deleted-doc citation at its
replacement makes the citing doc accuse the learning that *fixed* a problem of having caused it, with
a green exit code and a diff that looks like tidying.

**Say what you verified when you report a link as reciprocal.** "Cross-linked both ways" is a claim
about two files, and it is checkable in one grep per direction. Instance 3 is what it looks like when
that claim is asserted by the author, minutes after writing one of the two files, without either grep
being run. The same applies in a commit body: a sentence describing the prior state of a file is a
claim about history, and `git show <parent>:<path>` settles it.

**A reciprocal description is two descriptions, and both need reading.** When you add a link back, you
are writing a sentence about the doc you just wrote *and* a sentence about the doc you are pointing
at. `bada981` is the good pattern: it corrected the wrong description in the citing doc and added a
matching, accurate description on the target side at
`release-scripts-should-refuse-not-warn.md:171-174`, so the two docs now describe each other in
compatible terms — the same doctrine, differing premises — rather than each guessing at the other.

## Why This Matters

**A wrong description is worse than a broken link, and the two failures are not on the same scale.**

A broken link stops the reader. They click, or they grep the cited path, and nothing is there. That is
a visible, loud failure with an obvious next step: find the doc, or conclude it was deleted. The
reader is inconvenienced, and the store's credibility takes a small, honest dent. Nothing false is
transmitted.

A confident wrong summary does something worse: it stops the reader from clicking at all. The whole
purpose of the em-dash clause in a Related entry is to let a reader decide whether following the link
is worth their time. When that clause says the target argues for warning instead of refusing, a reader
who does not currently need advice about warnings simply does not follow the link — and they walk away
carrying the exact inverse of the advice the store spent a document establishing. They have been
misinformed *and* diverted from the one thing that would have corrected them. The better the store's
descriptions usually are, the more efficiently a wrong one works: a reader who has learned that these
summaries are reliable is precisely the reader who will not check.

**Nothing in the toolchain is shaped to catch this.** Every validator in this workflow is a liveness
checker. The claims validator asks whether a path resolves, whether a SHA resolves, whether a markdown
link resolves — existence questions, all of them answerable without opening the thing being cited. The
frontmatter validator asks whether the header parses. There is no check anywhere that reads a target
and compares it against what the citing sentence says about it, and it is not obvious that a
mechanical one could exist. This class of defect is structurally unowned, which means the only thing
standing between the store and a slowly inverting map of itself is an author's habit at authoring
time.

**The failure compounds in the direction the store is designed to grow.** Cross-links are what turn a
folder of markdown files into something navigable; this store leans on them heavily, and the more
densely linked it becomes, the more description claims exist and the smaller the fraction of them
anyone has recently verified. Density is a feature. It just means the per-link authoring discipline
has to be real, because the aggregate has no other defence.

**And it is a claim about a claim, which reads as doubly authoritative.** A doc asserting a technical
fact at least invites the reader to test it against the code. A doc asserting what *another doc in the
same store* says reads as internal bookkeeping — the kind of statement that could only be wrong
through carelessness, so it does not attract suspicion. That is the same false-corroboration dynamic
documented in `a-knowledge-store-corroborates-its-own-errors.md`, reached from the other end: there,
two artifacts agreeing looked like independent confirmation; here, one artifact describing another
looks like it must have been checked, because checking it is so cheap.

## When to Apply

Apply this whenever you are about to write, or have just written, a sentence that says what another
document argues, contains, concludes, or covers:

- Writing or editing a `## Related` entry in any doc under `docs/solutions/`. This is where the
  overwhelming majority of description claims in this repo live.
- Writing an inline citation in a body paragraph — "as documented in X" — where the clause around it
  characterises X. Instance 2 shows both forms in one file: the body citation was right and the
  Related citation was wrong, so the presence of a correct citation elsewhere in the same doc is not
  evidence about the one in front of you.
- Adding a reciprocal link between two docs. Both halves are description claims, and the one about the
  doc you *just wrote* is the one most likely to be asserted from memory rather than read.
- Writing a commit body that describes the prior state of a file, a link, or a document. Nothing
  validates a commit message at all, and it is the artifact future readers consult precisely for
  prior-state questions.
- Immediately after finding one wrong description — sweep every other citer of the same target before
  concluding the problem is contained.
- During any refresh or audit of the learnings store. A refresh that only runs the bundled validators
  has checked the path half of every cross-reference and none of the description half.

**Where this does not apply.**

- **A bare relationship makes no checkable claim.** "See also", "the same area of the codebase", "the
  adjacent doc", "background for this" — these assert proximity, not content. There is nothing in them
  that a reading of the target could falsify, so there is nothing to verify. The rule here bites when
  the clause characterises what the target *says*: it argues, it concludes, it is about, it
  recommends, it warns against. Those verbs are the trigger.
- **This is not an argument for fewer cross-links.** The links are what make the store navigable; a
  learning nobody can find from an adjacent learning may as well not exist. The four correct citations
  found in the sweep are each doing useful work, routing a reader from a deploy problem, a path
  convention, a shell trap, and a bundle bug to one shared packaging rule. The response to two wrong
  descriptions is to read the target before describing it, not to describe less or link less.
- **This is not about line numbers drifting.** A cited `:38` that has moved to `:41` is a different and
  much milder problem, and it is caught by an ordinary refresh pass. The failure documented here is a
  citation whose file, and often whose line, is exactly right, and whose sentence is backwards.

## Examples

**Instance 1 — `a-crashed-check-and-a-flagged-check-are-opposite-problems.md`, Related section.**

Before (fixed in `bada981`):

```text
- `docs/solutions/workflow-issues/release-scripts-should-refuse-not-warn.md` — the other end of the
  same pipeline. That one is about a check that detects a bad state and chooses to warn instead of
  refusing; this one is about a check that never gets far enough to detect anything.
```

After (`:280-284`):

```text
- `docs/solutions/workflow-issues/release-scripts-should-refuse-not-warn.md` — the case where the
  opposite response is right, and the reason the two are not in conflict. That doc argues a release
  check should refuse rather than warn, on the ground that its false positives are rare and each
  costs one rebuild. Here they are structural and dominant, so the check hands back questions to
  adjudicate instead of gating. The premise is what differs, not the doctrine.
```

The check that would have caught it, in the time it takes to run:

```bash
sed -n '2p;18p;38p' docs/solutions/workflow-issues/release-scripts-should-refuse-not-warn.md
# title: "A release script should refuse to package a stale artifact, not warn about it"
# # A release script should refuse to package a stale artifact, not warn about it
# **Refuse, do not warn.** A script that prints a warning and then exits 0 has taught the operator to
```

**Instance 2 — `a-gate-that-discovers-nothing-passes-everything.md`, Related section.**

Before (fixed in `87c4056`):

```text
- `docs/solutions/workflow-issues/release-scripts-should-refuse-not-warn.md` — the adjacent failure at
  the next step: that doc is about a gate that detects a bad condition and declines to fail on it;
  this one is about a gate whose detection stage was empty, so its failure logic never ran.
```

After (`:300-303`):

```text
- `docs/solutions/workflow-issues/release-scripts-should-refuse-not-warn.md` — the adjacent question at
  the next step: that doc argues a gate which detects a bad condition should refuse rather than warn,
  and states the premise that makes refusing right — a false failure that is rare and cheap to clear.
  This one is about a gate whose detection stage was empty, so its failure logic never ran at all.
```

Note what did **not** change in that file: a body citation of the same target, which had always been
correct — *"That doc is about what a gate does once it has detected a bad condition."* One doc, two
citations of one target, one right and one wrong.

**The sweep.** The commands, and what they returned:

```bash
grep -rn "release-scripts-should-refuse-not-warn" docs/
# 7 hits across 6 files -- the gate doc cites it twice
```

| Citing doc | Verdict |
| --- | --- |
| `docs/solutions/workflow-issues/a-crashed-check-and-a-flagged-check-are-opposite-problems.md:280` | wrong — fixed in `bada981` |
| `docs/solutions/workflow-issues/a-gate-that-discovers-nothing-passes-everything.md:301` | wrong — fixed in `87c4056` |
| `docs/solutions/workflow-issues/a-gate-that-discovers-nothing-passes-everything.md:67` | correct (body citation) |
| `docs/solutions/workflow-issues/verify-the-deploy-landed-before-asking-for-a-restart.md:137` | correct |
| `docs/solutions/conventions/document-the-path-you-actually-deploy-to.md:149` | correct |
| `docs/solutions/developer-experience/two-shells-one-repo-windows-toolchain-traps.md:316` | correct |
| `docs/solutions/ui-bugs/bundled-mod-objects-must-ship-disabled.md:125` | correct |

**Instance 3 — the reciprocity claim, settled against history.**

```bash
# Did the audience-model doc cite its sibling when it was created?
git show 717899f:docs/solutions/workflow-issues/an-audience-model-sets-the-sources-not-just-the-tone.md \
  | grep -n "review-scoped-to-the-document"
# (no output, exit 1)

# Did the sibling cite it back at that point?
git show 717899f:docs/solutions/workflow-issues/a-review-scoped-to-the-document-cannot-see-what-the-code-settled.md \
  | grep -n "audience-model-sets"
# (no output)

# What changed between them?
git log --oneline 717899f..6304e2a
# 6304e2a docs(solutions): widen the review-scoping learning to authoring time
```

`6304e2a` adds both entries in one diff. Its commit body nonetheless says *"Also adds the reciprocal
link on the companion, which had only pointed one way."* No committed tree ever had it pointing one
way. The links themselves are correct and both directions exist today; the sentence describing their
prior state is the artifact that was written from memory.

**The validator, on the two halves of one Related entry.** Given the line

```text
- `docs/solutions/workflow-issues/release-scripts-should-refuse-not-warn.md` — the case where the
```

`validate-doc-claims.py` extracts the backticked span, normalizes it, and asks `os.path.exists`
(line 340), falling back to `git cat-file -e HEAD:<path>` and the same against the upstream default
branch (lines 302-312). The file is there, so `checked_paths` increments and the run moves on. Every
word after the em dash is outside every check the script performs. The same is true of a markdown-link
form: the target is resolved from the doc's directory with `os.path.exists` (line 458), and the link
text is never examined. A `:38` line suffix would be stripped by `normalize_path` (line 214) before
the existence check, so not even the line number is verified — only the file.

## Related

- `docs/solutions/workflow-issues/a-fixed-defect-in-the-present-tense-passes-every-check.md` — the
  same split aimed at a different target. Here the unchecked half describes another *document*; there
  it describes *code that has since been repaired*, so the doc goes stale without anyone touching it
  and the citation keeps resolving because a fix leaves the file, the symbol and the line exactly
  where they were.
- `docs/solutions/workflow-issues/a-crashed-check-and-a-flagged-check-are-opposite-problems.md` — what
  to do with the flags this doc's validator *does* raise, including the standing categories that are
  correct on purpose. Read together: that one covers the flags you get, this one covers the ones you
  never will.
- `docs/solutions/workflow-issues/a-knowledge-store-corroborates-its-own-errors.md` — what happens
  after a wrong description is written, rather than while it is being written. Its "repointing a link
  is not correcting a sentence" section is this doc's failure caught one artifact later.
